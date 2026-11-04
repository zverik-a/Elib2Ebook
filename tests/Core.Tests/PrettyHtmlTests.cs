using Core.Extensions;

namespace Core.Tests;

/// <summary>
/// PrettyHtml (AngleSharp) используется в TxtBuilder: фиксируем вывод, чтобы обновления AngleSharp не меняли txt незаметно
/// </summary>
public class PrettyHtmlTests {
    private const string Html = "<p>Первый &amp; абзац</p><p>Второй <b>жирный</b><br>перенос</p><ul><li>один</li><li>два</li></ul>";

    [Fact]
    public void Snapshot() {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pretty.html"));
        Assert.Equal(expected.ReplaceLineEndings("\n"), Html.PrettyHtml().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TxtText_KeepsParagraphsOnSeparateLines() {
        // Так же, как TxtBuilder
        var text = Html.PrettyHtml().AsHtmlDoc().DocumentNode.GetText();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains("Первый & абзац", lines);
        Assert.Contains("один", lines);
        Assert.Contains("два", lines);
    }
}