using Core.Configs;
using Core.Logic.Getters;
using Core.Types.Book;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.Tests;

internal static class TestHelpers {
    public static Options CreateOptions(Action<Options>? configure = null) {
        var options = new Options(["https://example.com/book/1"]) {
            Format = ["epub"],
            Timeout = 10
        };

        configure?.Invoke(options);
        return options;
    }

    public static BookGetterConfig CreateConfig(Action<Options>? configure = null) {
        return BookGetterConfig.GetDefault(CreateOptions(configure), NullLogger.Instance);
    }
}

/// <summary>
/// Геттер-заглушка, открывающий protected методы GetterBase для тестов
/// </summary>
internal class TestGetter(BookGetterConfig config) : GetterBase(config) {
    protected override Uri SystemUrl => new("https://test.invalid/");

    public IEnumerable<T> Slice<T>(ICollection<T> toc, Func<T, string> selector) => SliceToc(toc, selector);

    public override Task<Book> Get(Uri url) => throw new NotSupportedException();
}