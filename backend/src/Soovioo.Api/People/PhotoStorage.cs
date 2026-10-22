using SkiaSharp;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.People;

public enum PhotoStatus
{
    /// <summary>Uploaded by the pupil, visible only to them and to whoever
    /// may approve it (parents, class teacher, admin).</summary>
    Pending,
    Approved,
}

public enum PhotoSize
{
    /// <summary>Avatars in lists.</summary>
    Thumb,

    /// <summary>The profile itself.</summary>
    Profile,
}

/// <summary>Profile photos (people's and pupils') as files next to the
/// attachments (<see cref="MessageAttachmentStorageService"/>, one folder per
/// school — deleted with a demo school too). An upload is decoded and
/// re-encoded: that drops EXIF (GPS and all), turns it upright, crops it to
/// a square and leaves two fixed WebP sizes. Never public — served only by
/// <see cref="PhotosController"/> after its access check.</summary>
public class PhotoStorage(MessageAttachmentStorageService storage)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    private static readonly Dictionary<PhotoSize, int> Pixels = new()
    {
        [PhotoSize.Thumb] = 128,
        [PhotoSize.Profile] = 512,
    };

    /// <summary>Null when the upload is fine, the error message otherwise.</summary>
    public static string? Validate(IFormFile? file, long maxBytes = MaxUploadBytes)
    {
        if (file is null || file.Length == 0)
        {
            return "Vyberte fotku.";
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
        {
            return "Fotka musí být JPEG, PNG nebo WebP.";
        }
        if (file.Length > maxBytes)
        {
            return $"Fotka může mít nejvýš {maxBytes / 1024 / 1024} MB.";
        }
        return null;
    }

    /// <summary>The new photo's key, or null when the file isn't a readable
    /// image.</summary>
    public async Task<string?> SaveAsync(int schoolId, IFormFile file)
    {
        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory);
        memory.Position = 0;

        using var square = DecodeSquare(memory);
        if (square is null)
        {
            return null;
        }

        var name = $"photo-{Guid.NewGuid():N}";
        foreach (var (size, pixels) in Pixels)
        {
            await storage.SaveBytesAsync(schoolId, FileName(name, size), Encode(square, pixels));
        }
        return $"{schoolId}/{name}";
    }

    public string FullPath(string key, PhotoSize size)
    {
        var (dir, name) = Split(key);
        return storage.GetFullPath(Path.Combine(dir, FileName(name, size)));
    }

    public void Delete(string? key)
    {
        if (key is null)
        {
            return;
        }
        var (dir, name) = Split(key);
        foreach (var size in Pixels.Keys)
        {
            storage.Delete(Path.Combine(dir, FileName(name, size)));
        }
    }

    /// <summary>What clients put into the photo URL — changes with every
    /// upload, so a cached old photo is never shown.</summary>
    public static string? Token(string? key) => key?[(key.LastIndexOf('-') + 1)..];

    private static string FileName(string name, PhotoSize size) => $"{name}-{size.ToString().ToLowerInvariant()}.webp";

    private static (string Dir, string Name) Split(string key)
    {
        var slash = key.IndexOf('/');
        return (key[..slash], key[(slash + 1)..]);
    }

    /// <summary>Upright (EXIF orientation applied) and cropped to the
    /// centred square.</summary>
    private static SKBitmap? DecodeSquare(Stream stream)
    {
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            return null;
        }
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? decoded.Height : decoded.Width;
        var height = swap ? decoded.Width : decoded.Height;
        var side = Math.Min(width, height);

        var square = new SKBitmap(side, side);
        using var canvas = new SKCanvas(square);
        canvas.Translate(-(width - side) / 2f, -(height - side) / 2f);
        canvas.SetMatrix(canvas.TotalMatrix.PreConcat(OrientationMatrix(origin, decoded.Width, decoded.Height)));
        using var image = SKImage.FromBitmap(decoded);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        canvas.Flush();
        return square;
    }

    /// <summary>Maps the stored pixels to the upright image.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static byte[] Encode(SKBitmap square, int pixels)
    {
        using var resized = square.Width <= pixels
            ? square.Copy()
            : square.Resize(new SKImageInfo(pixels, pixels), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 82);
        return data.ToArray();
    }
}
