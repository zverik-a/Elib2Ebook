using System.Text.Json;
using Core.Logic.Getters.LibSocial;
using Core.Types.SocialLib;

namespace Core.Tests;

public class LibSocialAnnotationTests {
    private static JsonElement? Summary(string json) {
        return JsonSerializer.Deserialize<RanobeLibBookDetails>("{\"data\":{\"name\":\"x\",\"summary\":" + json + "}}")!.Data.Summary;
    }

    [Fact]
    public void TipTapDocument_BecomesParagraphs() {
        var summary = Summary("""
        {"type":"doc","content":[
            {"type":"paragraph","content":[{"type":"text","text":"Первый "},{"type":"text","text":"абзац","marks":[{"type":"bold"}]}]},
            {"type":"paragraph"},
            {"type":"paragraph","content":[{"type":"text","text":"Второй <абзац>"}]}
        ]}
        """);

        Assert.Equal("<p>Первый абзац</p><p>Второй &lt;абзац&gt;</p>", NewLibSocialGetterBase.GetAnnotationHtml(summary));
    }

    [Fact]
    public void PlainStringSummary_IsReturnedAsIs() {
        Assert.Equal("Просто текст", NewLibSocialGetterBase.GetAnnotationHtml(Summary("\"Просто текст\"")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("123")]
    public void MissingOrUnexpectedSummary_IsEmpty(string json) {
        Assert.Equal(string.Empty, NewLibSocialGetterBase.GetAnnotationHtml(Summary(json)));
    }

    [Fact]
    public void AbsentSummary_IsEmpty() {
        var details = JsonSerializer.Deserialize<RanobeLibBookDetails>("""{"data":{"name":"x"}}""")!;
        Assert.Equal(string.Empty, NewLibSocialGetterBase.GetAnnotationHtml(details.Data.Summary));
    }
}