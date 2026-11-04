using System.Reflection;
using Elib2Ebook.DomainServices.Configs;
using Elib2Ebook.DomainServices.Getters;
using Elib2Ebook.DomainServices.Misc;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elib2Ebook.ExternalServices.Tests;

public class GetterFactoryTests
{
    private static readonly PropertyInfo SystemUrlProperty = typeof(GetterBase).GetProperty("SystemUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// В тестах entry assembly — testhost, поэтому GetterFactory не найдет сборки сайтов сам.
    /// Загружаем их заранее: GetterFactory также просматривает уже загруженные сборки.
    /// </summary>
    private static readonly List<Type> GetterTypes = DependencyContext.Default!.RuntimeLibraries
        .Where(l => l.Name.StartsWith("Elib2Ebook.ExternalServices.") && l.Name != typeof(GetterFactoryTests).Assembly.GetName().Name)
        .Select(l => Assembly.Load(new AssemblyName(l.Name)))
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(GetterBase)))
        .ToList();

    private static BookGetterConfig CreateConfig()
    {
        return BookGetterConfig.GetDefault(new Options(["https://example.com/"]) { Format = ["epub"], Timeout = 10 }, NullLogger.Instance);
    }

    private static Uri GetSystemUrl(GetterBase getter) => (Uri)SystemUrlProperty.GetValue(getter)!;

    private static bool OverridesIsSameUrl(Type type)
    {
        return type.GetMethod(nameof(GetterBase.IsSameUrl))!.DeclaringType != typeof(GetterBase);
    }

    [Fact]
    public void AllSiteAssemblies_AreLoaded()
    {
        Assert.True(GetterTypes.Count > 50, $"Найдено геттеров: {GetterTypes.Count}");
    }

    [Fact]
    public void GettersSharingAHost_DisambiguateByUrl()
    {
        using var config = CreateConfig();

        // Несколько геттеров на один хост допустимо, только если каждый из них переопределяет IsSameUrl
        // (как books.yandex.ru: книги и комиксы различаются по пути). Иначе выбор зависит от порядка рефлексии.
        var ambiguous = GetterTypes
            .Select(t => (GetterBase)Activator.CreateInstance(t, config)!)
            .GroupBy(g => GetSystemUrl(g).Host.Replace("www.", ""), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !g.All(x => OverridesIsSameUrl(x.GetType())))
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.GetType().Name))}")
            .ToList();

        Assert.True(ambiguous.Count == 0, "Несколько геттеров на один хост без явного разделения:\n" + string.Join("\n", ambiguous));
    }

    [Fact]
    public void EveryGetter_IsSelectedForItsOwnSystemUrl()
    {
        using var config = CreateConfig();

        var mismatches = new List<string>();
        foreach (var type in GetterTypes)
        {
            var url = GetSystemUrl((GetterBase)Activator.CreateInstance(type, config)!);

            Type actual;
            try
            {
                actual = GetterFactory.Get(config, url).GetType();
            }
            catch (ArgumentException) when (OverridesIsSameUrl(type))
            {
                // Геттеры с собственной логикой (например, по пути) могут не принимать корень сайта
                continue;
            }

            if (actual != type)
            {
                mismatches.Add($"{url}: ожидался {type.Name}, выбран {actual.Name}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Theory]
    [InlineData("https://ranobelib.me/ru/book/123--slug", "RanobeLibGetter")]
    [InlineData("https://old.ranobelib.me/ru/book/123--slug", "RanobeLibGetter")]
    [InlineData("https://mangalib.me/ru/manga/1--slug", "MangaLibGetter")]
    [InlineData("https://mangalib.org/ru/manga/1--slug", "MangaLibOrgGetter")]
    [InlineData("https://hentailib.me/ru/manga/1--slug", "HentaiLibGetter")]
    [InlineData("https://books.yandex.ru/books/abc", "BooksYandexBooksGetter")]
    [InlineData("https://books.yandex.ru/comicbooks/abc", "BooksYandexComicsGetter")]
    public void Urls_ResolveToExpectedGetter(string url, string expected)
    {
        Assert.NotEmpty(GetterTypes);
        using var config = CreateConfig();
        Assert.Equal(expected, GetterFactory.Get(config, new Uri(url)).GetType().Name);
    }

    [Theory]
    [InlineData("https://unsupported.invalid/book")]
    [InlineData("https://books.yandex.ru/")]
    public void UnsupportedUrls_ThrowArgumentException(string url)
    {
        Assert.NotEmpty(GetterTypes);
        using var config = CreateConfig();
        Assert.Throws<ArgumentException>(() => GetterFactory.Get(config, new Uri(url)));
    }
}
