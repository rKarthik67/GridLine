using Gridline.Import;
using Xunit;

namespace Gridline.Tests;

public class ImportTemplateTests
{
    static string Template => Path.Combine(AppContext.BaseDirectory, "Assets", "Gridline-Project-Import-Template.xlsx");

    [Fact]
    public void Sample_template_reads_every_sheet()
    {
        using var f = File.OpenRead(Template);
        var m = Gridline.Import.Template.Read(f);
        Assert.Equal("Northgate Logistics Hub", m.P("Project name"));
        Assert.Equal(3, m.Zones.Count);
        Assert.True(m.Trades.Count >= 4);
        Assert.Contains(m.Trades, t => t.Code == "03 30 00" && t.Budget == 6400000m && t.Split.Count == 3);
        Assert.Contains(m.Bidders, b => b.Partner == "Allied Steel Erectors" && b.State == "posted" && b.Base == 8850000m);
        Assert.True(m.Crew.Count >= 3);
        Assert.Equal(0, m.ErrorCount);
    }

    [Fact]
    public void Not_a_workbook_is_an_error()
    {
        using var f = new MemoryStream("not excel"u8.ToArray());
        Assert.True(Gridline.Import.Template.Read(f).ErrorCount > 0);
    }

    [Theory]
    [InlineData("084400", "08 44 00")]
    [InlineData("08-44-00", "08 44 00")]
    [InlineData("08 44 00", "08 44 00")]
    public void Csi_codes_are_normalised(string input, string want) => Assert.Equal(want, Gridline.Import.Template.Csi(input));
}
