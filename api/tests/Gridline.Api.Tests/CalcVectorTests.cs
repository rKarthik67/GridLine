using System.Text.Json;
using Gridline.Calc;
using Xunit;

namespace Gridline.Tests;

/// <summary>The C# rules must give exactly what the browser's @gridline/calc gives (web/packages/calc/test/vectors.json).</summary>
public class CalcVectorTests
{
    static readonly JsonSerializerOptions O = new(JsonSerializerDefaults.Web);
    static readonly JsonElement V = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json"))).RootElement;

    public static IEnumerable<object[]> Trades() => V.GetProperty("trades").EnumerateArray().Select((v, i) => new object[] { i });
    public static IEnumerable<object[]> Summaries() => V.GetProperty("summaries").EnumerateArray().Select((v, i) => new object[] { i });
    public static IEnumerable<object[]> Answers() => V.GetProperty("answers").EnumerateArray().Select(v => new object[] { v[0].GetString()!, v[1].GetString()!, v[2].ValueKind == JsonValueKind.Null ? null! : v[2].GetDecimal() });

    [Theory, MemberData(nameof(Trades))]
    public void Trade_matches_vector(int i)
    {
        var v = V.GetProperty("trades")[i];
        var o = Rules.Trade(v.GetProperty("in").Deserialize<TradeIn>(O)!);
        var want = v.GetProperty("out");
        Assert.Equal(Num(want.GetProperty("lowestValue")), o.LowestValue);
        Assert.Equal(Num(want.GetProperty("carriedValue")), o.CarriedValue);
        Assert.Equal(Num(want.GetProperty("budgetDelta")), o.BudgetDelta);
        Assert.Equal(want.GetProperty("usesPlug").GetBoolean(), o.UsesPlug);
        var bids = want.GetProperty("bids").EnumerateArray().ToList();
        Assert.Equal(bids.Count, o.Bids.Count);
        for (var k = 0; k < bids.Count; k++)
        {
            Assert.Equal(bids[k].GetProperty("bidId").GetInt64(), o.Bids[k].BidId);
            Assert.Equal(bids[k].GetProperty("scopeAdjustments").GetDecimal(), o.Bids[k].ScopeAdjustments);
            Assert.Equal(Num(bids[k].GetProperty("totalAmount")), o.Bids[k].TotalAmount);
            Assert.Equal(bids[k].GetProperty("rank").ValueKind == JsonValueKind.Null ? null : bids[k].GetProperty("rank").GetInt32(), o.Bids[k].Rank);
        }
        foreach (var z in want.GetProperty("zoneAmounts").EnumerateObject()) Assert.Equal(z.Value.GetDecimal(), o.ZoneAmounts[z.Name]);
    }

    [Theory, MemberData(nameof(Summaries))]
    public void Summary_matches_vector(int i)
    {
        var v = V.GetProperty("summaries")[i];
        var lines = Rules.Summary(v.GetProperty("in").Deserialize<SummaryIn>(O)!);
        var want = v.GetProperty("out").EnumerateArray().Select(x => (x[0].GetString()!, x[1].GetDecimal())).ToList();
        Assert.Equal(want, lines.Select(l => (l.Name, l.Amount)).ToList());
        Assert.Equal(100m, lines.Single(l => l.Name == "Contract total").PctOfContract);
    }

    [Theory, MemberData(nameof(Answers))]
    public void Typing_into_a_cell(string input, string kind, decimal? amount)
    {
        var a = Rules.ParseAnswer(input);
        Assert.Equal(kind, a.Kind);
        if (amount is not null) Assert.Equal(amount, a.Amount);
    }

    [Fact]
    public void Totals_hash_matches_the_browser() => Assert.Equal("78d7da8c", Rules.TotalsHash([1m, 2m, null]));

    static decimal? Num(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetDecimal();
}
