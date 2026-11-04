using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Elib2Ebook.Domain.Book;
using Elib2Ebook.Domain.Common;
using Elib2Ebook.DomainServices.Configs;
using Elib2Ebook.DomainServices.Extensions;
using EpubSharp;
using EpubSharp.Format;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Elib2Ebook.DomainServices.Builders;

public partial class EpubBuilder(Options options, ILogger logger) : BuilderBase(options, logger)
{
    private const string WEBP_EXTENSION = ".webp";

    protected override string Extension => "epub";

    private readonly EpubWriter _writer = new();

    /// <summary>
    /// Файлы картинок, которые попадут в epub: имя внутри epub -> путь к файлу на диске
    /// </summary>
    private readonly List<FileMeta> _images = [];

    /// <summary>
    /// Картинки, успешно сконвертированные в WebP: исходное имя -> сконвертированный файл
    /// </summary>
    private readonly ConcurrentDictionary<string, TempFile> _converted = new();

    [GeneratedRegex(@"(<item\b[^>]*\bhref=""[^""]+\.webp""[^>]*\bmedia-type="")[^""]*("")", RegexOptions.IgnoreCase)]
    private static partial Regex WebpManifestItemMediaType();

    [GeneratedRegex(@"(<item\b[^>]*\bmedia-type="")[^""]*(""[^>]*\bhref=""[^""]+\.webp"")", RegexOptions.IgnoreCase)]
    private static partial Regex WebpManifestItemMediaTypeReversed();

    /// <summary>
    /// Создание Xhtml документа из кода части
    /// </summary>
    /// <param name="pattern">Шаблон части</param>
    /// <param name="title">Заголовок части</param>
    /// <param name="decodeText">Раскодированный текст</param>
    /// <returns></returns>
    private static string ApplyPattern(string pattern, string title, string decodeText)
    {
        return pattern.Replace("{title}", title.CleanInvalidXmlChars().ReplaceNewLine()).Replace("{body}", decodeText.HtmlDecode().CleanInvalidXmlChars()).AsXHtmlDoc().AsString();
    }

    /// <summary>
    /// Добавление внешних файлов
    /// </summary>
    /// <param name="directory">Путь к директории с файлами</param>
    /// <param name="searchPattern">Шаблон поиска файлов</param>
    /// <returns></returns>
    private void WithFiles(string directory, string searchPattern)
    {
        foreach (var file in FileProvider.Instance.GetFiles(directory, searchPattern))
        {
            var fileName = Path.GetFileName(file.Name);
            var type = file.Name.EndsWith(".ttf") ? EpubContentType.FontTruetype :
                file.Name.EndsWith(".css") ? EpubContentType.Css : throw new Exception($"Неизвестный тип файла {fileName}");

            _writer.AddFile(fileName, file.ReadAllBytes(), type);
        }
    }

    private static ImageFormat GetImageFormat(string path)
    {
        if (path.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase))
        {
            return ImageFormat.Jpeg;
        }

        if (path.EndsWith(".gif", StringComparison.InvariantCultureIgnoreCase))
        {
            return ImageFormat.Gif;
        }

        if (path.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase))
        {
            return ImageFormat.Png;
        }

        return path.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase) ? ImageFormat.Svg : ImageFormat.Jpeg;
    }

    /// <summary>
    /// Конвертация картинки в WebP в отдельный временный файл. Исходный файл не изменяется,
    /// так как он может использоваться другими форматами (cbz, fb2) в этом же запуске.
    /// </summary>
    /// <returns>null, если картинку конвертировать не нужно или не удалось</returns>
    private async Task<TempFile> ConvertToWebp(TempFile source)
    {
        var extension = source.Extension?.ToLowerInvariant();
        if (extension is ".svg" or WEBP_EXTENSION)
        {
            return null;
        }

        try
        {
            using var image = await Image.LoadAsync(source.FilePath);
            if (image.Frames.Count > 1)
            {
                // Анимированные картинки оставляем как есть
                return null;
            }

            var maxSize = Options.MaxImageSize;
            if (maxSize > 0 && (image.Width > maxSize || image.Height > maxSize))
            {
                image.Mutate(x => x.Resize(image.Width >= image.Height ? new Size(maxSize, 0) : new Size(0, maxSize)));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = Math.Clamp(Options.WebpQuality, 1, 100) });

            return await TempFile.Create(source.Url, source.Directory, source.Name + "_webp", WEBP_EXTENSION, output.ToArray());
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Не удалось сконвертировать картинку {source.FullName} в WebP, оставляю исходную. {ex.Message}");
            return null;
        }
    }

    private async Task ConvertImages(IEnumerable<TempFile> images)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        await Parallel.ForEachAsync(images.DistinctBy(i => i.FullName), options, async (image, _) =>
        {
            var converted = await ConvertToWebp(image);
            if (converted != null)
            {
                _converted[image.FullName] = converted;
            }
        });
    }

    private string ReplaceConvertedImages(string content, IEnumerable<TempFile> images)
    {
        foreach (var image in images)
        {
            if (_converted.TryGetValue(image.FullName, out var converted))
            {
                // Имена картинок уникальны (guid), поэтому заменяем только значение атрибута src
                content = content.Replace($"\"{image.FullName}\"", $"\"{converted.FullName}\"");
            }
        }

        return content;
    }

    private void Write(Book book)
    {
        _writer.AddAuthor(book.Author.Name);

        foreach (var coAuthor in book.CoAuthors)
        {
            _writer.AddAuthor(coAuthor.Name);
        }

        _writer.SetTitle(book.Title);

        if (book.Cover != null)
        {
            // Обложку не конвертируем: превью в читалках часто не поддерживают WebP
            _writer.SetCover(book.Cover.Content, GetImageFormat(book.Cover.FullName));
        }

        if (!string.IsNullOrWhiteSpace(book.Annotation))
        {
            _writer.AddDescription(book.Annotation.AsHtmlDoc().DocumentNode.GetText());
        }

        if (book.Seria != null)
        {
            _writer.AddCollection(book.Seria.Name, book.Seria.Number);
        }

        var pattern = FileProvider.Instance.ReadAllText($"{Options.ResourcesPath}/ChapterPattern.xhtml");

        var validChapters = book.Chapters.Where(c => c.IsValid).ToList();
        var skipped = book.Chapters.Count() - validChapters.Count;
        if (skipped > 0)
        {
            Logger.LogWarning($"Пропущено пустых глав: {skipped}");
        }

        foreach (var chapter in validChapters)
        {
            foreach (var image in chapter.Images)
            {
                if (_converted.TryGetValue(image.FullName, out var converted))
                {
                    // EpubSharp не знает тип image/webp — тип исправляется после записи в FixWebpMediaTypes
                    _writer.AddFile(converted.FullName, [], EpubContentType.ImagePng);
                    _images.Add(new FileMeta(converted.FullName, converted.FilePath));
                }
                else
                {
                    _writer.AddFile(image.FullName, [], GetImageFormat(image.FullName).ToEpubContentType());
                    _images.Add(new FileMeta(image.FullName, image.FilePath));
                }
            }

            _writer.AddChapter(chapter.Title.HtmlDecode().ReplaceNewLine().RemoveInvalidChars(), ApplyPattern(pattern, chapter.Title, ReplaceConvertedImages(chapter.Content, chapter.Images)));
        }

        WithFiles(Options.ResourcesPath, "*.ttf");
        WithFiles(Options.ResourcesPath, "*.css");
    }

    /// <summary>
    /// Проставляет media-type="image/webp" для webp файлов в манифесте .opf
    /// </summary>
    public static void FixWebpMediaTypes(string epubPath)
    {
        using var archive = ZipFile.Open(epubPath, ZipArchiveMode.Update);
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            string opf;
            using (var reader = new StreamReader(entry.Open()))
            {
                opf = reader.ReadToEnd();
            }

            var fixedOpf = WebpManifestItemMediaType().Replace(opf, "$1image/webp$2");
            fixedOpf = WebpManifestItemMediaTypeReversed().Replace(fixedOpf, "$1image/webp$2");
            if (fixedOpf == opf)
            {
                continue;
            }

            using var stream = entry.Open();
            stream.SetLength(0);
            using var writer = new StreamWriter(stream);
            writer.Write(fixedOpf);
        }
    }

    protected override async Task BuildInternal(Book book, string fileName)
    {
        if (Options.Webp)
        {
            await ConvertImages(book.Chapters.Where(c => c.IsValid).SelectMany(c => c.Images));
        }

        try
        {
            Write(book);
            await _writer.Write(fileName, _images);

            if (!_converted.IsEmpty)
            {
                FixWebpMediaTypes(fileName);
            }
        }
        finally
        {
            foreach (var converted in _converted.Values)
            {
                converted.Dispose();
            }
        }
    }
}
