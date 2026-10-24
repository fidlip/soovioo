using System.Collections.Concurrent;
using SkiaSharp;
using Soovioo.Api.People;

namespace Soovioo.Api.Demo;

public enum DemoAvatarSet
{
    Boys,
    Girls,
    Men,
    Women,
}

/// <summary>Illustrated "photos" for demo schools — clearly drawings, never
/// mistaken for a real person. One picture per set in
/// <c>Demo/Avatars</c> (boys.png, girls.png, men.png, women.png), each a
/// grid of <see cref="Columns"/>×<see cref="Rows"/> square portraits; a set
/// can be replaced by drawing a new sheet of the same layout. Cut out and
/// encoded once per process, then only copied into each demo, so generating
/// a demo stays fast.</summary>
public static class DemoAvatars
{
    public const int Columns = 4;
    public const int Rows = 3;
    public const int VariantsPerSet = Columns * Rows;

    private static readonly ConcurrentDictionary<(DemoAvatarSet Set, int Variant), IReadOnlyDictionary<PhotoSize, byte[]>> Cache = new();

    public static DemoAvatarSet SetFor(bool female, bool adult) =>
        (female, adult) switch
        {
            (false, false) => DemoAvatarSet.Boys,
            (true, false) => DemoAvatarSet.Girls,
            (false, true) => DemoAvatarSet.Men,
            (true, true) => DemoAvatarSet.Women,
        };

    /// <summary>Both sizes of portrait <paramref name="variant"/> (any int —
    /// taken modulo <see cref="VariantsPerSet"/>), ready for
    /// <see cref="PhotoStorage.SaveProcessedAsync"/>.</summary>
    public static IReadOnlyDictionary<PhotoSize, byte[]> Get(DemoAvatarSet set, int variant) =>
        Cache.GetOrAdd((set, Math.Abs(variant % VariantsPerSet)), key =>
        {
            using var sheet = SKBitmap.Decode(SheetPath(key.Set))
                ?? throw new InvalidOperationException($"Demo avatar sheet {SheetPath(key.Set)} is missing or unreadable.");
            var size = sheet.Width / Columns;
            using var tile = new SKBitmap(size, size);
            sheet.ExtractSubset(tile, SKRectI.Create(key.Variant % Columns * size, key.Variant / Columns * size, size, size));
            using var image = SKImage.FromBitmap(tile);
            using var stream = image.Encode(SKEncodedImageFormat.Png, 100).AsStream();
            return PhotoStorage.Process(stream)!;
        });

    private static string SheetPath(DemoAvatarSet set) =>
        Path.Combine(AppContext.BaseDirectory, "Demo", "Avatars", $"{set.ToString().ToLowerInvariant()}.png");
}
