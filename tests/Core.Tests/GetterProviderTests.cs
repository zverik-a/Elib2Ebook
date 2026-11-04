using System.Reflection;
using Core.Configs;
using Core.Logic.Getters;
using Core.Logic.Getters.LibSocial;
using Core.Misc;

namespace Core.Tests;

public class GetterProviderTests {
    private static readonly PropertyInfo SystemUrlProperty = typeof(GetterBase).GetProperty("SystemUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static List<GetterBase> CreateAllGetters(BookGetterConfig config) {
        return typeof(GetterBase).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(GetterBase)))
            .Select(t => (GetterBase)Activator.CreateInstance(t, config)!)
            .ToList();
    }

    private static bool OverridesIsSameUrl(Type type) {
        return type.GetMethod(nameof(GetterBase.IsSameUrl))!.DeclaringType != typeof(GetterBase);
    }

    [Fact]
    public void GettersSharingAHost_DisambiguateByUrl() {
        using var config = TestHelpers.CreateConfig();

        // Несколько геттеров на один хост допустимо, только если каждый из них переопределяет IsSameUrl
        // (как books.yandex.ru: книги и комиксы различаются по пути). Иначе выбор зависит от порядка рефлексии.
        var ambiguous = CreateAllGetters(config)
            .GroupBy(g => ((Uri)SystemUrlProperty.GetValue(g)!).Host.Replace("www.", ""), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !g.All(x => OverridesIsSameUrl(x.GetType())))
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.GetType().Name))}")
            .ToList();

        Assert.True(ambiguous.Count == 0, "Несколько геттеров на один хост без явного разделения:\n" + string.Join("\n", ambiguous));
    }

    [Fact]
    public void EveryGetter_IsSelectedForItsOwnSystemUrl() {
        using var config = TestHelpers.CreateConfig();

        var mismatches = new List<string>();
        foreach (var getter in CreateAllGetters(config)) {
            var type = getter.GetType();
            var url = (Uri)SystemUrlProperty.GetValue(getter)!;

            Type actual;
            try {
                actual = GetterProvider.Get(config, url).GetType();
            } catch (ArgumentException) when (OverridesIsSameUrl(type)) {
                // Геттеры с собственной логикой (например, по пути) могут не принимать корень сайта
                continue;
            }

            if (actual != type) {
                mismatches.Add($"{url}: ожидался {type.Name}, выбран {actual.Name}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Theory]
    [InlineData("https://books.yandex.ru/books/abc", "BooksYandexBooksGetter")]
    [InlineData("https://books.yandex.ru/comicbooks/abc", "BooksYandexComicsGetter")]
    public void BooksYandex_IsResolvedByPath(string url, string expected) {
        using var config = TestHelpers.CreateConfig();
        Assert.Equal(expected, GetterProvider.Get(config, new Uri(url)).GetType().Name);
    }

    [Fact]
    public void BooksYandexRoot_IsUnsupportedInsteadOfCrashing() {
        using var config = TestHelpers.CreateConfig();
        Assert.Throws<ArgumentException>(() => GetterProvider.Get(config, new Uri("https://books.yandex.ru/")));
    }

    [Theory]
    [InlineData("https://ranobelib.me/ru/book/123--slug", typeof(RanobeLibGetter))]
    [InlineData("https://old.ranobelib.me/ru/book/123--slug", typeof(RanobeLibGetter))]
    [InlineData("https://mangalib.me/ru/manga/1--slug", typeof(MangaLibGetter))]
    [InlineData("https://mangalib.org/ru/manga/1--slug", typeof(MangaLibOrgGetter))]
    [InlineData("https://hentailib.me/ru/manga/1--slug", typeof(HentaiLibGetter))]
    public void LibSocialUrls_ResolveToExpectedGetter(string url, Type expected) {
        using var config = TestHelpers.CreateConfig();
        Assert.IsType(expected, GetterProvider.Get(config, new Uri(url)));
    }

    [Fact]
    public void UnsupportedSite_ThrowsArgumentException() {
        using var config = TestHelpers.CreateConfig();
        Assert.Throws<ArgumentException>(() => GetterProvider.Get(config, new Uri("https://unsupported.invalid/book")));
    }
}