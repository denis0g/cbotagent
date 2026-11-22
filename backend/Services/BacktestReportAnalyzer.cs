using System.Globalization;
using System.Text.Json;

namespace backend.Services;

public sealed record BacktestAnalysis(string Overview, IReadOnlyList<string> SuggestedImprovements);

/// <summary>Builds a conservative review from report metrics locally, without sending report data to an external service.</summary>
public sealed class BacktestReportAnalyzer
{
    private static readonly Dictionary<string, string[]> MetricAliases = new(StringComparer.Ordinal)
    {
        ["net profit"] = ["netprofit", "netprofitmoney"],
        ["profit factor"] = ["profitfactor"],
        ["maximum equity drawdown (%)"] = ["maxequitydrawdownpercentages", "maxequitydrawdownpercentage", "equitydrawdownpercent"],
        ["maximum equity drawdown"] = ["maxequitydrawdown", "maxequitydrawdownmoney"],
        ["total trades"] = ["totaltrades", "totaltradecount", "tradescount"],
        ["winning trades"] = ["winningtrades", "winningtradecount"],
        ["losing trades"] = ["losingtrades", "losingtradecount"],
        ["win rate (%)"] = ["winrate", "winratepercent", "winningtradespercentage"]
    };

    public BacktestAnalysis Analyze(string json, string symbol, string period)
    {
        using var document = JsonDocument.Parse(json);
        var metrics = new Dictionary<string, decimal>(StringComparer.Ordinal);
        CollectMetrics(document.RootElement, metrics, 0);

        var facts = new List<string>();
        foreach (var (label, aliases) in MetricAliases)
            if (aliases.Select(Normalize).FirstOrDefault(metrics.ContainsKey) is { } key)
                facts.Add($"{label}: {Format(metrics[key])}");

        var overview = facts.Count > 0
            ? $"The {symbol} {period} backtest completed. Report metrics: {string.Join("; ", facts)}. These figures describe only the selected historical period and assumptions."
            : $"The {symbol} {period} backtest completed, but the JSON report did not expose recognized summary metrics. Review the HTML and JSON reports for the detailed results.";

        var suggestions = new List<string>();
        if (TryMetric(metrics, ["profitfactor"], out var profitFactor) && profitFactor < 1)
            suggestions.Add("Review the entry and exit rules and retest across separate in-sample and out-of-sample periods; the reported profit factor is below 1.");
        if (TryMetric(metrics, ["netprofit", "netprofitmoney"], out var netProfit) && netProfit < 0)
            suggestions.Add("Inspect losing periods and trading costs, then compare any rule changes against the same dates and assumptions.");
        if (TryMetric(metrics, ["maxequitydrawdownpercentages", "maxequitydrawdownpercentage", "equitydrawdownpercent"], out var drawdown)
            && drawdown >= 20)
            suggestions.Add("Investigate position sizing and drawdown controls; the report shows at least 20% maximum equity drawdown.");
        if (TryMetric(metrics, ["totaltrades", "totaltradecount", "tradescount"], out var trades) && trades < 30)
            suggestions.Add("Treat the result cautiously because fewer than 30 trades were reported; consider a longer period and additional market conditions.");
        if (suggestions.Count == 0)
            suggestions.Add("Compare performance across additional date ranges and data modes, and verify that the strategy remains robust after realistic costs.");

        return new BacktestAnalysis(overview, suggestions);
    }

    private static void CollectMetrics(JsonElement element, Dictionary<string, decimal> metrics, int depth)
    {
        if (depth > 12) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TryDecimal(property.Value, out var value))
                    metrics.TryAdd(Normalize(property.Name), value);
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    CollectMetrics(property.Value, metrics, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().Take(100))
                if (item.ValueKind == JsonValueKind.Object) CollectMetrics(item, metrics, depth + 1);
        }
    }

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDecimal(out result);
        if (value.ValueKind == JsonValueKind.String)
            return decimal.TryParse(value.GetString()?.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        result = 0;
        return false;
    }

    private static bool TryMetric(Dictionary<string, decimal> metrics, string[] aliases, out decimal value)
    {
        foreach (var alias in aliases)
            if (metrics.TryGetValue(Normalize(alias), out value)) return true;
        value = 0;
        return false;
    }

    private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
