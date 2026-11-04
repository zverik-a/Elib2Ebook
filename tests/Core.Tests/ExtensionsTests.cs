using Core.Extensions;
using Core.Logic;
using Core.Types.Book;

namespace Core.Tests;

public class UriExtensionTests {
    [Theory]
    [InlineData("https://www.author.today/work/1", "https://author.today/", true)]
    [InlineData("https://AUTHOR.today/work/1", "https://author.today/", true)]
    [InlineData("https://old.ranobelib.me/", "https://ranobelib.me/", false)]
    [InlineData("https://кц.рф/", "https://xn--j1ay.xn--p1ai/", true)]
    public void IsSameHost(string a, string b, bool expected) {
        Assert.Equal(expected, new Uri(a).IsSameHost(new Uri(b)));
    }

    [Fact]
    public void GetSegment_CollapsesDuplicateSlashes() {
        var uri = new Uri("https://ranobelib.me//ru/book/123--slug");
        Assert.Equal("ru", uri.GetSegment(1));
        Assert.Equal("book", uri.GetSegment(2));
        Assert.Equal("123--slug", uri.GetSegment(3));
    }

    [Fact]
    public void AppendQueryParameter_KeepsExistingAndRepeatsKeys() {
        var uri = new Uri("https://api.example/api/manga/1?a=1")
            .AppendQueryParameter("fields[]", "authors")
            .AppendQueryParameter("fields[]", "summary");

        Assert.Equal("1", uri.GetQueryParameter("a"));
        Assert.Equal("authors,summary", uri.GetQueryParameter("fields[]"));
    }

    [Fact]
    public void AppendSegment_HandlesSlashes() {
        Assert.Equal("https://api.example/api/manga/1", new Uri("https://api.example/").AppendSegment("/api/manga/1").ToString());
    }

    [Fact]
    public void GetFileName_ReturnsLastSegment() {
        Assert.Equal("image.png", new Uri("https://cdn.example/a/b/image.png?x=1").GetFileName());
    }
}

public class StringExtensionsTests {
    [Fact]
    public void HtmlDecode_DecodesRepeatedlyAndDropsAngleEntities() {
        Assert.Equal("a & b", "a &amp;amp; b".HtmlDecode());
        Assert.Equal("ab", "a&lt;b".HtmlDecode());
    }

    [Fact]
    public void ReplaceNewLine_CollapsesWhitespace() {
        Assert.Equal("Глава 1 Начало", "Глава 1\n\t  Начало ".ReplaceNewLine());
    }

    [Fact]
    public void CleanInvalidXmlChars_RemovesControlChars() {
        Assert.Equal("ab", "a\u0001b".CleanInvalidXmlChars());
    }

    [Fact]
    public void Crop_TruncatesLongStrings() {
        Assert.Equal("abc", "abcdef".Crop(3));
        Assert.Equal("ab", "ab".Crop(3));
    }

    [Fact]
    public void RemoveInvalidChars_ReplacesQuotesAndPathSeparators() {
        Assert.Equal("a  b   c", "a\"/b \"/c.".RemoveInvalidChars());
    }
}

public class BookNameBuilderTests {
    private static Book CreateBook(Seria? seria = null) => new(new Uri("https://example.com/book")) {
        Title = "Книга",
        Author = new Author("Автор"),
        Seria = seria
    };

    [Fact]
    public void DefaultPattern() {
        Assert.Equal("Автор - Книга", BookNameBuilder.Build("{Author.Name} - {Book.Title}", CreateBook()));
    }

    [Fact]
    public void SeriaTokens_AreAvailableWhenSeriaIsSet() {
        var book = CreateBook(new Seria { Name = "Цикл", Number = "2" });
        Assert.Equal("Цикл 2 - Книга", BookNameBuilder.Build("{Seria.Name} {Seria.Number} - {Book.Title}", book));
    }
}