using System;
using Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDatabaseContext))]
[Migration("20261122101500_AddBacktestRuns")]
public sealed class AddBacktestRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BacktestRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CbotId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Account = table.Column<string>(type: "text", nullable: false),
                Symbol = table.Column<string>(type: "text", nullable: false),
                Period = table.Column<string>(type: "text", nullable: false),
                Start = table.Column<DateOnly>(type: "date", nullable: false),
                End = table.Column<DateOnly>(type: "date", nullable: false),
                DataMode = table.Column<string>(type: "text", nullable: false),
                Balance = table.Column<decimal>(type: "numeric", nullable: false),
                Commission = table.Column<decimal>(type: "numeric", nullable: false),
                Spread = table.Column<decimal>(type: "numeric", nullable: false),
                HtmlReportPath = table.Column<string>(type: "text", nullable: true),
                JsonReportPath = table.Column<string>(type: "text", nullable: true),
                HtmlReportUrl = table.Column<string>(type: "text", nullable: true),
                JsonReportUrl = table.Column<string>(type: "text", nullable: true),
                Output = table.Column<string>(type: "text", nullable: false),
                PerformanceOverview = table.Column<string>(type: "text", nullable: false),
                SuggestedImprovements = table.Column<string>(type: "text", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BacktestRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_BacktestRuns_Cbots_CbotId",
                    column: x => x.CbotId,
                    principalTable: "Cbots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BacktestRuns_CbotId_StartedAt",
            table: "BacktestRuns",
            columns: new[] { "CbotId", "StartedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "BacktestRuns");
}
