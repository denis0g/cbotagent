using System.Globalization;
using backend.Dtos;
using backend.Services;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace backend.Tools;

/// <summary>Runs one cTrader CLI backtest in its own short-lived Docker container.</summary>
public sealed class BacktestingDockerTool(
    IConfiguration configuration,
    CbotsService cbots,
    ILogger<BacktestingDockerTool> logger)
{
    private const string DefaultImage = "ghcr.io/spotware/ctrader-console:latest";

    public async Task<BacktestResult> RunAsync(
        Guid botId,
        Guid userId,
        BacktestParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(parameters.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(parameters.End, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end)
            || start > end)
            return Failure("Choose valid backtest dates with the start date on or before the end date.");

        if (HasOptionUnsafeText(parameters.CTraderId) || HasOptionUnsafeText(parameters.Symbol)
            || HasOptionUnsafeText(parameters.Period) || string.IsNullOrWhiteSpace(parameters.Password)
            || parameters.Password.Any(char.IsControl))
            return Failure("One or more cTrader backtest settings are invalid.");

        var runId = await cbots.StartBacktestRunAsync(botId, userId, parameters, cancellationToken);
        if (runId is null)
            return Failure("The cBot or backtest settings could not be found.");
        string? algoPath = null;

        var buildRoot = Path.GetFullPath(configuration["CTrader:BuildRoot"]
            ?? Path.Combine(Path.GetTempPath(), "ctrader-builds"));
        var tempRoot = Path.Combine(buildRoot, "backtest-" + Guid.NewGuid().ToString("N"));
        var passwordRoot = Path.Combine(buildRoot, "backtest-secret-" + Guid.NewGuid().ToString("N"));
        var containerId = string.Empty;
        DockerClient? docker = null;

        try
        {
            algoPath = await cbots.GetArtifactPathAsync(botId, userId, algo: true, cancellationToken);
            if (algoPath is null)
            {
                const string message = "This cBot has no compiled .algo file. Compile it before starting a backtest.";
                await cbots.MarkBacktestFailedAsync(runId.Value, userId, message, cancellationToken);
                return Failure(message, runId);
            }

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(passwordRoot);
            var algoFileName = Path.GetFileName(algoPath);
            var containerAlgoPath = $"/mnt/ctrader/{algoFileName}";
            var stagedAlgoPath = Path.Combine(tempRoot, algoFileName);
            var passwordFilePath = Path.Combine(passwordRoot, "ctrader-cli.pwd");
            var htmlReportPath = Path.Combine(tempRoot, "report.html");
            var jsonReportPath = Path.Combine(tempRoot, "report.json");
            File.Copy(algoPath, stagedAlgoPath, overwrite: true);
            await File.WriteAllTextAsync(passwordFilePath, parameters.Password, new System.Text.UTF8Encoding(false), cancellationToken);

            var endpoint = configuration["CTrader:DockerEndpoint"];
            var dockerUri = string.IsNullOrWhiteSpace(endpoint)
                ? new Uri("unix:///var/run/docker.sock")
                : new Uri(endpoint);
            docker = new DockerClientConfiguration(dockerUri).CreateClient();

            var args = new List<string>
            {
                "backtest", containerAlgoPath,
                $"--ctid={parameters.CTraderId.Trim()}",
                "--pwd-file=/run/secrets/ctrader-cli.pwd",
                $"--account={parameters.Account}",
                $"--symbol={parameters.Symbol.Trim()}",
                $"--period={parameters.Period.Trim()}",
                $"--start={start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                $"--end={end.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                $"--data-mode={parameters.DataMode}",
                $"--balance={parameters.Balance.ToString(CultureInfo.InvariantCulture)}",
                $"--commission={parameters.Commission.ToString(CultureInfo.InvariantCulture)}",
                $"--spread={parameters.Spread.ToString(CultureInfo.InvariantCulture)}",
                "--report=/mnt/ctrader/report.html",
                "--report-json=/mnt/ctrader/report.json",
                "--exit-on-stop"
            };

            var container = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = configuration["CTrader:BacktestImage"] ?? DefaultImage,
                WorkingDir = "/mnt/ctrader",
                Cmd = args,
                HostConfig = new HostConfig
                {
                    Binds = [$"{tempRoot}:/mnt/ctrader", $"{passwordRoot}:/run/secrets:ro"]
                }
            }, cancellationToken);
            containerId = container.ID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(containerId))
            {
                const string message = "Docker did not return a container ID for the backtest.";
                await cbots.MarkBacktestFailedAsync(runId.Value, userId, message, cancellationToken);
                return Failure(message, runId);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutMinutes = int.TryParse(configuration["CTrader:BacktestTimeoutMinutes"], out var configuredTimeout)
                ? Math.Clamp(configuredTimeout, 1, 180)
                : 60;
            timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));
            using var outputStream = await docker.Containers.AttachContainerAsync(containerId, tty: false,
                new ContainerAttachParameters { Stream = true, Stdout = true, Stderr = true }, timeout.Token);
            await docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), timeout.Token);
            var (stdout, stderr) = await outputStream.ReadOutputToEndAsync(timeout.Token);
            var output = string.Join(Environment.NewLine,
                new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            var exit = await docker.Containers.WaitContainerAsync(containerId, timeout.Token);
            if (exit is null || exit.StatusCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(output)
                    ? "cTrader CLI reported that the backtest failed."
                    : output;
                await cbots.MarkBacktestFailedAsync(runId.Value, userId, message, cancellationToken);
                return Failure(message, runId);
            }

            var analysisData = CreateAnalysisData(jsonReportPath);
            var saved = await cbots.CompleteBacktestRunAsync(
                runId.Value, userId, htmlReportPath, jsonReportPath, output, cancellationToken);
            if (!saved)
                return Failure("The cBot was removed before its backtest reports could be saved.", runId);

            logger.LogInformation("Backtest completed for bot {BotId}. HTML and JSON reports were saved.", botId);
            return new BacktestResult
            {
                Succeeded = true,
                Output = string.IsNullOrWhiteSpace(output) ? "Backtest completed successfully." : output,
                RunId = runId,
                HtmlReportUrl = $"/api/backtests/runs/{runId}/report/html",
                JsonReportUrl = $"/api/backtests/runs/{runId}/report/json",
                AnalysisData = analysisData
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "The cTrader backtest exceeded its configured time limit.";
            await cbots.MarkBacktestFailedAsync(runId.Value, userId, message, CancellationToken.None);
            return Failure(message, runId);
        }
        catch (OperationCanceledException)
        {
            await cbots.MarkBacktestFailedAsync(runId.Value, userId, "Backtest cancelled by the user.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Backtest failed for bot {BotId}.", botId);
            var message = $"Backtest failed: {exception.GetBaseException().Message}";
            await cbots.MarkBacktestFailedAsync(runId.Value, userId, message, CancellationToken.None);
            return Failure(message, runId);
        }
        finally
        {
            if (docker is not null && !string.IsNullOrWhiteSpace(containerId))
            {
                try
                {
                    await docker.Containers.RemoveContainerAsync(containerId,
                        new ContainerRemoveParameters { Force = true }, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Could not remove backtest container {ContainerId}.", containerId);
                }
            }
            docker?.Dispose();
            foreach (var workspace in new[] { tempRoot, passwordRoot })
            {
                try
                {
                    if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Could not clean temporary backtest workspace {TempRoot}.", workspace);
                }
            }
        }
    }

    private static bool HasOptionUnsafeText(string value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal)
        || value.Any(char.IsControl);

    private static string CreateAnalysisData(string reportPath)
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath));
        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return root.GetRawText();
            var selected = new Dictionary<string, System.Text.Json.JsonElement>();
            foreach (var property in root.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (name.Contains("trade") || name.Contains("equity") || name.Contains("balance")
                    || name.Contains("profit") || name.Contains("drawdown") || name is "main" or "symbol" or "period")
                    selected[property.Name] = property.Value.Clone();
            }
            var summary = selected.Count > 0 ? selected : root.EnumerateObject()
                .Take(20).ToDictionary(property => property.Name, property => property.Value.Clone());
            return System.Text.Json.JsonSerializer.Serialize(summary);
        }
    }

    private static BacktestResult Failure(string output, Guid? runId = null) => new()
    {
        Succeeded = false,
        Output = output,
        RunId = runId
    };
}
