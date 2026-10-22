using System.Collections.Concurrent;
using SkiaSharp;
using Soovioo.Api.People;

namespace Soovioo.Api.Demo;

/// <summary>Illustrated "photos" for demo schools: a head-and-shoulders
/// figure in flat colours (skin, hair, clothes, background) — clearly a
/// drawing, never mistaken for a real person. A fixed set of variants per
/// adult/child × woman/man, drawn and encoded once per process and then
/// only copied into each demo, so generating a demo stays fast.</summary>
public static class DemoAvatars
{
    public const int VariantsPerKind = 12;

    private const int Size = 512;

    private static readonly ConcurrentDictionary<(bool Female, bool Adult, int Variant), IReadOnlyDictionary<PhotoSize, byte[]>> Cache = new();

    private static readonly SKColor[] Backgrounds =
        Colors("#dbeafe", "#e0e7ff", "#fce7f3", "#dcfce7", "#fef3c7", "#ede9fe", "#cffafe", "#ffe4e6", "#e2e8f0", "#fae8ff");

    private static readonly SKColor[] SkinTones =
        Colors("#f8d5c2", "#f1c3a5", "#e0ac8a", "#c68863", "#a8704f", "#7d4f35");

    private static readonly SKColor[] HairColors =
        Colors("#2b1d14", "#4a3020", "#6b4226", "#a0652f", "#d4a55a", "#e8c77d", "#8a3b1e", "#1f1f1f", "#9ca3af");

    private static readonly SKColor[] Clothes =
        Colors("#4f46e5", "#0f766e", "#b91c1c", "#1d4ed8", "#7c3aed", "#c2410c", "#334155", "#15803d", "#be185d", "#0369a1");

    /// <summary>Both sizes of variant <paramref name="variant"/> (any int —
    /// taken modulo <see cref="VariantsPerKind"/>), ready for
    /// <see cref="PhotoStorage.SaveProcessedAsync"/>.</summary>
    public static IReadOnlyDictionary<PhotoSize, byte[]> Get(bool female, bool adult, int variant) =>
        Cache.GetOrAdd((female, adult, Math.Abs(variant % VariantsPerKind)), key =>
        {
            using var stream = new MemoryStream(Render(key.Female, key.Adult, key.Variant));
            return PhotoStorage.Process(stream)!;
        });

    private static byte[] Render(bool female, bool adult, int variant)
    {
        // Seeded per kind+variant: the same variant always looks the same.
        // Spread apart: neighbouring seeds start with near-identical values.
        var rng = new Random(unchecked((variant * 4 + (female ? 2 : 0) + (adult ? 1 : 0)) * 2_654_435_761u).GetHashCode());
        var skin = SkinTones[rng.Next(SkinTones.Length)];
        var hair = HairColors[rng.Next(adult ? HairColors.Length : HairColors.Length - 1)]; // grey only for adults
        var longHair = female ? rng.NextDouble() < 0.75 : rng.NextDouble() < 0.08;
        var bald = adult && !female && rng.NextDouble() < 0.12;
        var glasses = adult && rng.NextDouble() < 0.3;

        using var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Backgrounds[rng.Next(Backgrounds.Length)]);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        var headY = adult ? 225f : 240f;
        var headR = adult ? 98f : 104f;
        var shoulderRx = adult ? 190f : 150f;

        if (longHair)
        {
            paint.Color = hair;
            canvas.DrawRoundRect(256 - headR - 22, headY - 40, (headR + 22) * 2, adult ? 250 : 220, 90, 90, paint);
        }

        // Shoulders and neck.
        paint.Color = Clothes[rng.Next(Clothes.Length)];
        canvas.DrawOval(256, 560, shoulderRx, 175, paint);
        paint.Color = Darken(skin, 0.92f);
        canvas.DrawRect(256 - 30, headY + headR - 30, 60, 70, paint);

        // Head.
        paint.Color = skin;
        canvas.DrawCircle(256, headY, headR, paint);

        // Hair on top: the upper part of a slightly larger circle.
        if (!bald)
        {
            paint.Color = hair;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, Size, headY - headR * (longHair ? 0.15f : 0.35f)));
            canvas.DrawCircle(256, headY - 6, headR + 8, paint);
            canvas.Restore();
        }

        // Face: eyes and a smile.
        paint.Color = SKColor.Parse("#1f2937");
        canvas.DrawCircle(256 - 34, headY + 12, 8, paint);
        canvas.DrawCircle(256 + 34, headY + 12, 8, paint);
        using var line = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6, StrokeCap = SKStrokeCap.Round,
            Color = SKColor.Parse("#1f2937"),
        };
        canvas.DrawArc(new SKRect(256 - 30, headY + 18, 256 + 30, headY + 62), 25, 130, false, line);
        if (glasses)
        {
            line.StrokeWidth = 5;
            canvas.DrawCircle(256 - 34, headY + 12, 22, line);
            canvas.DrawCircle(256 + 34, headY + 12, 22, line);
            canvas.DrawLine(256 - 12, headY + 12, 256 + 12, headY + 12, line);
        }

        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor[] Colors(params string[] hex) => hex.Select(SKColor.Parse).ToArray();

    private static SKColor Darken(SKColor color, float factor) =>
        new((byte)(color.Red * factor), (byte)(color.Green * factor), (byte)(color.Blue * factor));
}
