using System.IO.Compression;
using System.Text.RegularExpressions;
using Core.Logic.Builders;
using Core.Types.Book;
using Core.Types.Common;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Core.Tests;

public class EpubBuilderTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory("elib2ebook-tests").FullName;

    public void Dispose() {
        Directory.Delete(_dir, true);
    }

    private static byte[] Png(int width, int height) {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private async Task<(Book Book, TempFile Png, TempFile Svg)> CreateBook() {
        var png = await TempFile.Create(null, _dir, Guid.NewGuid() + ".png", Png(2000, 1000));
        var svg = await TempFile.Create(null, _dir, Guid.NewGuid() + ".svg", "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"u8.ToArray());
        var cover = await TempFile.Create(null, _dir, "cover.png", Png(100, 150));

        var book = new Book(new Uri("https://example.com/book")) {
            Title = "Тест",
            Author = new Author("Автор"),
            Cover = cover,
            Chapters = [
                new Chapter {
                    Title = "Глава 1",
                    Content = $"<p>Файл photo.jpg упомянут в тексте</p><img src=\"{png.FullName}\"/><img src=\"{svg.FullName}\"/>",
                    Images = [png, svg]
                }
            ]
        };

        return (book, png, svg);
    }

    private async Task<ZipArchive> Build(Book book, bool webp) {
        var options = TestHelpers.CreateOptions(o => {
            o.SavePath = Path.Combine(_dir, webp ? "webp" : "plain");
            o.Webp = webp;
            o.MaxImageSize = 500;
        });

        await new EpubBuilder(options, NullLogger.Instance).Build(book);
        return ZipFile.OpenRead(Directory.GetFiles(options.SavePath, "*.epub").Single());
    }

    private static string Read(ZipArchive zip, Func<string, bool> predicate) {
        using var reader = new StreamReader(zip.Entries.First(e => predicate(e.FullName)).Open());
        return reader.ReadToEnd();
    }

    private static string ManifestMediaType(string opf, string href) {
        var item = Regex.Match(opf, $"<item[^>]*href=\"{Regex.Escape(href)}\"[^>]*>").Value;
        return Regex.Match(item, "media-type=\"([^\"]+)\"").Groups[1].Value;
    }

    private static string ChapterHtml(ZipArchive zip) {
        return zip.Entries
            .Where(e => e.FullName.EndsWith(".html"))
            .Select(e => new StreamReader(e.Open()).ReadToEnd())
            .First(html => html.Contains("<img"));
    }

    [Fact]
    public async Task WithoutWebp_KeepsOriginalImagesAndTypes() {
        var (book, png, svg) = await CreateBook();
        using var zip = await Build(book, webp: false);

        var opf = Read(zip, n => n.EndsWith(".opf"));
        Assert.Equal("image/png", ManifestMediaType(opf, png.FullName));
        Assert.Equal("image/svg+xml", ManifestMediaType(opf, svg.FullName));
        Assert.DoesNotContain(".webp", opf);
    }

    [Fact]
    public async Task WithWebp_ConvertsRasterImagesOnly_AndFixesManifest() {
        var (book, png, svg) = await CreateBook();
        using var zip = await Build(book, webp: true);

        var webpName = Path.GetFileNameWithoutExtension(png.FullName) + "_webp.webp";
        var opf = Read(zip, n => n.EndsWith(".opf"));

        Assert.Equal("image/webp", ManifestMediaType(opf, webpName));
        Assert.Equal("image/svg+xml", ManifestMediaType(opf, svg.FullName));
        Assert.Equal("image/png", ManifestMediaType(opf, "cover.png"));

        var html = ChapterHtml(zip);
        Assert.Contains($"src=\"{webpName}\"", html);
        Assert.Contains($"src=\"{svg.FullName}\"", html);
        Assert.Contains("photo.jpg", html);

        var webpEntry = zip.Entries.Single(e => e.Name == webpName);
        using var webpStream = new MemoryStream();
        await using (var entryStream = webpEntry.Open()) {
            await entryStream.CopyToAsync(webpStream);
        }

        webpStream.Position = 0;
        var info = Image.Identify(webpStream);
        Assert.Equal(500, Math.Max(info.Width, info.Height));
    }

    [Fact]
    public async Task WithWebp_DoesNotModifyOriginalTempFiles() {
        var (book, png, _) = await CreateBook();
        var before = await File.ReadAllBytesAsync(png.FilePath);

        using (await Build(book, webp: true)) { }

        Assert.Equal(before, await File.ReadAllBytesAsync(png.FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*_webp.webp"));
    }
}