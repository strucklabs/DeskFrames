using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskFrames.Imaging;

public enum ImageStatus { Ok, NoPath, Missing, Unreadable }

/// <summary>Ergebnis des Ladens. <see cref="Bitmap"/> ist eingefroren und darf in jedem Thread benutzt werden.</summary>
public sealed record LoadedImage(ImageStatus Status, BitmapSource? Bitmap = null, double AspectRatio = 0,
    int FullLongSide = 0, int DecodedLongSide = 0);

/// <summary>
/// Lädt lokale Bilddateien für einen Rahmen.
/// <list type="bullet">
/// <item>Die Datei wird komplett gelesen und sofort geschlossen – sie bleibt nie gesperrt und wird nie verändert.</item>
/// <item>Die EXIF-Drehung (Handyfotos) wird angewendet; das Seitenverhältnis ist das des angezeigten Bildes.</item>
/// <item>Dekodiert wird nur so groß wie nötig (<c>maxLongSide</c>), damit große Fotos wenig Speicher brauchen.</item>
/// </list>
/// </summary>
public static class ImageLoader
{
    /// <summary>Dateiendungen für den Öffnen-Dialog (was Windows über WIC lesen kann).</summary>
    public const string DialogFilter =
        "Images|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.dib;*.gif;*.tif;*.tiff;*.ico;*.jxr;*.wdp;*.webp;*.heic;*.heif;*.avif|All files|*.*";

    public static LoadedImage Load(string? path, int maxLongSide)
    {
        if (string.IsNullOrWhiteSpace(path)) return new LoadedImage(ImageStatus.NoPath);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new LoadedImage(ImageStatus.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // z. B. Laufwerk nicht bereit, keine Rechte, ungültiger Pfad
            ErrorLog.Write($"Image not readable: {path}: {ex.Message}");
            return new LoadedImage(ImageStatus.Missing);
        }

        try
        {
            return Decode(bytes, maxLongSide);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
                                       or InvalidOperationException or OverflowException or COMException or IOException)
        {
            ErrorLog.Write($"Image not decodable: {path}: {ex.Message}");
            return new LoadedImage(ImageStatus.Unreadable);
        }
    }

    private static LoadedImage Decode(byte[] bytes, int maxLongSide)
    {
        // Erst nur die Kopfdaten: Größe und Drehung
        int pixelWidth, pixelHeight, orientation;
        using (var probe = new MemoryStream(bytes, writable: false))
        {
            var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            pixelWidth = frame.PixelWidth;
            pixelHeight = frame.PixelHeight;
            orientation = ReadOrientation(frame);
        }
        if (pixelWidth <= 0 || pixelHeight <= 0) throw new FileFormatException("Image has no size.");

        int longSide = Math.Max(pixelWidth, pixelHeight);
        maxLongSide = Math.Max(1, maxLongSide);

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes, writable: false);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (longSide > maxLongSide)
        {
            // Nur eine Seite vorgeben – die andere folgt im Seitenverhältnis
            if (pixelWidth >= pixelHeight) bmp.DecodePixelWidth = maxLongSide;
            else bmp.DecodePixelHeight = maxLongSide;
        }
        bmp.EndInit();

        BitmapSource result = ApplyOrientation(bmp, orientation);
        result.Freeze();

        bool swap = orientation is >= 5 and <= 8;
        double aspect = swap ? (double)pixelHeight / pixelWidth : (double)pixelWidth / pixelHeight;
        return new LoadedImage(ImageStatus.Ok, result, aspect, longSide, Math.Min(longSide, maxLongSide));
    }

    /// <summary>EXIF-Ausrichtung 1–8; 1 (normal), wenn das Format keine Metadaten hat.</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("System.Photo.Orientation")
                && meta.GetQuery("System.Photo.Orientation") is ushort o && o is >= 1 and <= 8)
                return o;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or COMException)
        {
            // Formate ohne Metadaten-Unterstützung (BMP, manche GIF): keine Drehung
        }
        return 1;
    }

    /// <summary>Dreht/spiegelt nach EXIF (2 = gespiegelt, 3 = 180°, 6 = 90° im Uhrzeigersinn, 8 = 270° …).</summary>
    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? t = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return t == null ? source : new TransformedBitmap(source, t);
    }
}
