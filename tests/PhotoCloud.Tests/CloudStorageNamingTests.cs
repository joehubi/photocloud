using PhotoCloud.Core.Storage;

namespace PhotoCloud.Tests;

public class CloudStorageNamingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photocloud-name-" + Guid.NewGuid().ToString("N"));

    public CloudStorageNamingTests() => Directory.CreateDirectory(Path.Combine(_root, "Bilder"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CloudStorage CreateStorage() => new(_root, ["Bilder"], [".jpg"], 10);

    [Fact]
    public async Task WithoutExif_UsesFallbackDateAsPrefix()
    {
        var result = await CreateStorage().SaveAsync("Bilder", "foto.jpg", new MemoryStream([1, 2, 3]), new DateTime(2024, 7, 5));

        Assert.Equal("20240705_foto.jpg", result.FileName);
        Assert.True(File.Exists(Path.Combine(_root, "Bilder", "20240705_foto.jpg")));
    }

    [Fact]
    public async Task ExistingDatePrefix_IsNotDuplicated()
    {
        var result = await CreateStorage().SaveAsync("Bilder", "20240101_foto.jpg", new MemoryStream([1]), new DateTime(2024, 7, 5));

        Assert.Equal("20240101_foto.jpg", result.FileName);
    }

    [Fact]
    public async Task SameNameTwice_GetsCounterAfterPrefix()
    {
        var storage = CreateStorage();
        var date = new DateTime(2024, 7, 5);
        await storage.SaveAsync("Bilder", "foto.jpg", new MemoryStream([1]), date);
        var second = await storage.SaveAsync("Bilder", "foto.jpg", new MemoryStream([2]), date);

        Assert.Equal("20240705_foto (1).jpg", second.FileName);
    }

    [Fact]
    public async Task WithExifDate_UsesCaptureDateInsteadOfFallback()
    {
        var jpeg = JpegWithExifDate("2023:05:17 10:20:30");

        var result = await CreateStorage().SaveAsync("Bilder", "foto.jpg", new MemoryStream(jpeg), new DateTime(2024, 7, 5));

        Assert.Equal("20230517_foto.jpg", result.FileName);
        Assert.Equal(jpeg, await File.ReadAllBytesAsync(Path.Combine(_root, "Bilder", result.FileName)));
    }

    // Minimales JPEG (SOI, APP1/Exif mit DateTimeOriginal, EOI), big-endian TIFF.
    private static byte[] JpegWithExifDate(string exifDate)
    {
        var date = System.Text.Encoding.ASCII.GetBytes(exifDate + "\0");
        var tiff = new List<byte>
        {
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01, 0x87, 0x69, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x90, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
        };
        tiff.AddRange(date);

        var app1 = new List<byte> { 0xFF, 0xE1, 0x00, (byte)(2 + 6 + tiff.Count) };
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);

        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(app1);
        jpeg.AddRange([0xFF, 0xD9]);
        return jpeg.ToArray();
    }
}
