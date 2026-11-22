using System.ComponentModel.DataAnnotations;

namespace backend.Dtos;

public sealed class BacktestParameters
{
    [Required]
    public Guid BotId { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public required string CTraderId { get; init; }

    [Required, StringLength(512, MinimumLength = 1)]
    public required string Password { get; init; }

    [Required, RegularExpression(@"^\d{1,20}$")]
    public required string Account { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public required string Symbol { get; init; }

    [Required, StringLength(32, MinimumLength = 1)]
    public required string Period { get; init; }

    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")]
    public required string Start { get; init; }

    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")]
    public required string End { get; init; }

    [Required, RegularExpression(@"^(m1|ticks|m1-csv|tick-csv|open)$", MatchTimeoutInMilliseconds = 100)]
    public required string DataMode { get; init; }

    [Range(typeof(decimal), "0.01", "1000000000000")]
    public decimal Balance { get; init; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal Commission { get; init; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal Spread { get; init; }
}

public sealed record BacktestResult
{
    public required bool Succeeded { get; init; }
    public required string Output { get; init; }
    public Guid? RunId { get; init; }
    public string? HtmlReportPath { get; init; }
    public string? JsonReportPath { get; init; }
    public string? HtmlReportUrl { get; init; }
    public string? JsonReportUrl { get; init; }
    public string? AnalysisData { get; init; }
    public string? PerformanceOverview { get; init; }
    public IReadOnlyList<string> SuggestedImprovements { get; init; } = [];
}

public sealed class BacktestRunDto
{
    public required Guid Id { get; init; }
    public required Guid BotId { get; init; }
    public required string BotName { get; init; }
    public required string Status { get; init; }
    public required string Account { get; init; }
    public required string Symbol { get; init; }
    public required string Period { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
    public required string DataMode { get; init; }
    public required decimal Balance { get; init; }
    public required decimal Commission { get; init; }
    public required decimal Spread { get; init; }
    public required string Output { get; init; }
    public required string PerformanceOverview { get; init; }
    public required IReadOnlyList<string> SuggestedImprovements { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? HtmlReportUrl { get; init; }
    public string? JsonReportUrl { get; init; }
}
