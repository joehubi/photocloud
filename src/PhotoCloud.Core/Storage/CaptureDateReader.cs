using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoCloud.Core.Storage;

public static class CaptureDateReader
{
    /// <summary>Liest das Aufnahmedatum (EXIF DateTimeOriginal, sonst DateTimeDigitized) aus dem Dateianfang; null, wenn keines gefunden wird.</summary>
    public static DateTime? TryRead(ReadOnlyMemory<byte> header)
    {
        try
        {
            using var stream = new MemoryStream(header.ToArray(), writable: false);
            var directories = ImageMetadataReader.ReadMetadata(stream);
            foreach (var exif in directories.OfType<ExifSubIfdDirectory>())
            {
                if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
                {
                    return original;
                }

                if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
                {
                    return digitized;
                }
            }
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or MetadataException)
        {
        }

        return null;
    }
}
