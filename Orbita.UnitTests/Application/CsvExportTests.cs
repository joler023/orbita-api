using Orbita.Application.Crm;

namespace Orbita.UnitTests.Application;

public sealed class CsvExportTests
{
    [Fact]
    public void Escape_QuotesFieldsWithCommas()
    {
        Assert.Equal("\"Ana, Pérez\"", CsvExport.Escape("Ana, Pérez"));
        Assert.Equal("\"dice \"\"hola\"\"\"", CsvExport.Escape("dice \"hola\""));
        Assert.Equal("simple", CsvExport.Escape("simple"));
    }

    [Fact]
    public void Build_WritesHeaderAndRows()
    {
        var csv = CsvExport.Build(
            ["name", "phone"],
            [
                ["Ana", "+57"],
                ["Luis, A", null],
            ]);

        Assert.Equal(
            """
            name,phone
            Ana,+57
            "Luis, A",

            """.ReplaceLineEndings("\n"),
            csv.ReplaceLineEndings("\n"));
    }
}
