namespace Elib2Ebook.DomainServices.Tests.Getters;

public class SliceTocTests
{
    private static readonly string[] Toc = ["Пролог", "Глава 1", "Глава 2", "Глава 3", "Эпилог"];

    private static string[] Slice(int? start = null, int? end = null, string startName = null, string endName = null)
    {
        using var config = TestHelpers.CreateConfig(o =>
        {
            o.Start = start;
            o.End = end;
            o.StartName = startName;
            o.EndName = endName;
        });

        // Геттер не диспозим отдельно: он диспозит тот же config
        var getter = new TestGetter(config);
        return getter.Slice(Toc, s => s).ToArray();
    }

    [Fact]
    public void NoRange_ReturnsWholeToc()
    {
        Assert.Equal(Toc, Slice());
    }

    [Theory]
    [InlineData(2, null, new[] { "Глава 1", "Глава 2", "Глава 3", "Эпилог" })]
    [InlineData(null, 2, new[] { "Пролог", "Глава 1" })]
    [InlineData(2, 4, new[] { "Глава 1", "Глава 2", "Глава 3" })]
    [InlineData(3, 3, new[] { "Глава 2" })]
    public void PositiveIndexes_AreOneBasedAndInclusive(int? start, int? end, string[] expected)
    {
        Assert.Equal(expected, Slice(start, end));
    }

    [Fact]
    public void NegativeStart_TakesLastChapters()
    {
        // README: --start -3 → 3 последних главы
        Assert.Equal(["Глава 2", "Глава 3", "Эпилог"], Slice(start: -3));
    }

    [Fact]
    public void NegativeStartAndEnd_SelectsFromTheEnd()
    {
        // README: --start -2 --end -1 → только предпоследняя глава
        Assert.Equal(["Глава 3"], Slice(start: -2, end: -1));
    }

    [Fact]
    public void ByName_IsCaseInsensitiveAndTrimmed()
    {
        Assert.Equal(["Глава 1", "Глава 2"], Slice(startName: " глава 1 ", endName: "ГЛАВА 2"));
    }

    [Fact]
    public void UnknownName_Throws()
    {
        var ex = Assert.Throws<Exception>(() => Slice(startName: "Нет такой"));
        Assert.Contains("Нет такой", ex.Message);
    }
}
