namespace backend.Models;

public sealed class BacktestRun
{
    public Guid Id { get; set; }
    public Guid CbotId { get; set; }
    public Cbot Cbot { get; set; } = null!;
    public string Status { get; set; } = "Running";
    public string Account { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string DataMode { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal Commission { get; set; }
    public decimal Spread { get; set; }
    public string? HtmlReportPath { get; set; }
    public string? JsonReportPath { get; set; }
    public string? HtmlReportUrl { get; set; }
    public string? JsonReportUrl { get; set; }
    public string Output { get; set; } = string.Empty;
    public string PerformanceOverview { get; set; } = string.Empty;
    public string SuggestedImprovements { get; set; } = "[]";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
