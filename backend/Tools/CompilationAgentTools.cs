using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using backend.Dtos;
using backend.Services;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace backend.Tools;

/// <summary>Builds cTrader cBots in a disposable .NET SDK container and saves generated artifacts.</summary>
public sealed class CompilationAgentTools(
    IConfiguration configuration,
    CbotsService cbots,
    ILogger<CompilationAgentTools> logger)
{
    public const string ToolGroupName = "CompilationAgentTools";
    public const string ToolName = "compile_ctrader_cbot";
    public const string ToolDescription = "Compiles cTrader Automate cBot source in the .NET 10 SDK Docker image and saves the source and .algo artifacts.";

    private const string DefaultSdkImage = "mcr.microsoft.com/dotnet/sdk:10.0-alpine";
    private const string DefaultBuildRoot = "/home/dn/ctrader-builds";
    private const int DefaultTimeoutMinutes = 5;

    [Description(ToolDescription)]
    public async Task<CompileCbotToolResponse> CompileAsync(
        CompileCbotToolRequest request,
        CancellationToken cancellationToken = default,
        Func<string, Task>? onProgress = null)
    {
        var elapsed = Stopwatch.StartNew();
        var botName = SanitizeBotName(request.BotName);
        logger.LogInformation("[Compilation] Starting request for bot {BotName}. Source length: {SourceLength} characters.",
            botName, request.SourceCode?.Length ?? 0);
        if (botName.Length == 0)
        {
            logger.LogWarning("[Compilation] Rejected request because the bot name is empty or invalid.");
            return new CompileCbotToolResponse
            {
                Succeeded = false,
                Output = "The bot name must contain at least one letter or number.",
                BotId = request.BotId
            };
        }

        if (string.IsNullOrWhiteSpace(request.SourceCode))
        {
            logger.LogWarning("[Compilation] Rejected request for bot {BotName} because source code is empty.", botName);
            return new CompileCbotToolResponse
            {
                Succeeded = false,
                Output = "The cBot source code cannot be empty.",
                BotId = request.BotId
            };
        }

        var buildRoot = Path.GetFullPath(configuration["CTrader:BuildRoot"] ?? DefaultBuildRoot);
        var tempRoot = Path.Combine(buildRoot, Guid.NewGuid().ToString("N"));
        var solutionDirectory = Path.Combine(tempRoot, botName);
        var projectDirectory = Path.Combine(solutionDirectory, botName);
        var projectFile = Path.Combine(projectDirectory, $"{botName}.csproj");
        var solutionFile = Path.Combine(solutionDirectory, $"{botName}.slnx");
        var sourceFile = Path.Combine(projectDirectory, $"{botName}.cs");
        var containerId = string.Empty;
        DockerClient? dockerClient = null;
        var timeoutMinutes = int.TryParse(configuration["CTrader:BuildTimeoutMinutes"], out var configuredTimeout)
            ? Math.Clamp(configuredTimeout, 1, 60)
            : DefaultTimeoutMinutes;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation(
                "[Compilation] Preparing temporary project. Solution: {SolutionFile}; project: {ProjectFile}; build root: {BuildRoot}.",
                solutionFile, projectFile, buildRoot);
            if (onProgress is not null)
                await onProgress("Creating the temporary cTrader compilation project…");

            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(sourceFile, request.SourceCode, cancellationToken);
            await File.WriteAllTextAsync(projectFile, CreateProjectFile(botName), cancellationToken);
            await File.WriteAllTextAsync(solutionFile, CreateSolutionFile(botName), cancellationToken);
            logger.LogInformation("[Compilation] Wrote source, project, and solution files for {BotName}.", botName);

            if (onProgress is not null)
                await onProgress("Project created. Preparing the .NET 10 Docker compiler…");

            var image = configuration["CTrader:DockerImage"] ?? DefaultSdkImage;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

            var endpoint = configuration["CTrader:DockerEndpoint"];
            var dockerUri = string.IsNullOrWhiteSpace(endpoint)
                ? new Uri("unix:///var/run/docker.sock")
                : new Uri(endpoint);
            logger.LogInformation("[Compilation] Initializing Docker client at {DockerEndpoint}.", dockerUri);
            dockerClient = new DockerClientConfiguration(dockerUri).CreateClient();
            logger.LogInformation("[Compilation] Docker client initialized for bot {BotName}.", botName);

            cancellationToken.ThrowIfCancellationRequested();
            if (onProgress is not null)
                await onProgress("Starting the .NET 10 compilation container using the local SDK image…");

            logger.LogInformation("[Compilation] Creating container from local image {Image} for {BotName}.", image, botName);
            var containerUser = OperatingSystem.IsLinux() ? $"{GetEffectiveUserId()}:{GetEffectiveGroupId()}" : null;
            var container = await dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = image,
                User = containerUser,
                WorkingDir = $"/workspace/{botName}",
                Env = [
                    "HOME=/workspace/.home",
                    "DOTNET_CLI_HOME=/workspace/.home",
                    "NUGET_PACKAGES=/workspace/.nuget/packages"
                ],
                Cmd = ["dotnet", "build", "-c", "Release", "--verbosity", "minimal", "--nologo"],
                HostConfig = new HostConfig
                {
                    Binds = [$"{tempRoot}:/workspace"]
                }
            }, timeout.Token);
            containerId = container?.ID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(containerId))
                throw new InvalidOperationException("Docker created the compilation container without returning a container ID.");

            logger.LogInformation("[Compilation] Created container {ContainerId}; attaching output stream.", containerId);
            using var outputStream = await dockerClient.Containers.AttachContainerAsync(
                containerId,
                tty: false,
                new ContainerAttachParameters { Stream = true, Stdout = true, Stderr = true },
                timeout.Token);

            logger.LogInformation("[Compilation] Starting container {ContainerId}.", containerId);
            await dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), timeout.Token);
            logger.LogInformation("[Compilation] Container {ContainerId} started; waiting for build output.", containerId);
            if (onProgress is not null)
                await onProgress("Compilation is running in the Docker container…");

            var (stdout, stderr) = await outputStream.ReadOutputToEndAsync(timeout.Token);
            var buildOutput = JoinOutput(stdout, stderr);
            var waitResponse = await dockerClient.Containers.WaitContainerAsync(containerId, timeout.Token);
            if (waitResponse is null)
                throw new InvalidOperationException("Docker stopped the compilation container without returning its exit status.");
            logger.LogInformation("[Compilation] Container {ContainerId} exited with code {ExitCode} after {ElapsedMilliseconds} ms.",
                containerId, waitResponse.StatusCode, elapsed.ElapsedMilliseconds);

            if (!string.IsNullOrWhiteSpace(buildOutput))
                logger.LogInformation("[Compilation] Compiler output for {BotName}:{NewLine}{BuildOutput}", botName, Environment.NewLine, buildOutput);

            if (waitResponse.StatusCode != 0)
            {
                logger.LogWarning("[Compilation] Build failed for {BotName} with exit code {ExitCode}.", botName, waitResponse.StatusCode);
                return await BuildFailedAsync(request,
                    $"cTrader compilation failed (exit code {waitResponse.StatusCode}).{Environment.NewLine}{buildOutput}",
                    cancellationToken, onProgress);
            }

            if (onProgress is not null)
                await onProgress("Build succeeded. Searching for the generated .algo file…");
            var expectedAlgoFile = $"{botName}.algo";
            var algoFile = Directory.EnumerateFiles(tempRoot, "*.algo", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), expectedAlgoFile, StringComparison.OrdinalIgnoreCase));
            if (algoFile is null)
            {
                logger.LogWarning("[Compilation] Build exited successfully, but expected artifact {ExpectedAlgoFile} was not found.", expectedAlgoFile);
                return await BuildFailedAsync(request,
                    $"The .NET build succeeded, but the expected {expectedAlgoFile} artifact was not generated. Check the cTrader build targets and project configuration.{Environment.NewLine}{buildOutput}",
                    cancellationToken, onProgress);
            }
            logger.LogInformation("[Compilation] Found expected artifact {AlgoFile}.", algoFile);

            if (onProgress is not null)
                await onProgress("The .algo file was generated. Saving compilation artifacts…");
            var result = await cbots.SaveSuccessfulCompilationAsync(new CompileCbotToolRequest
            {
                BotId = request.BotId,
                UserId = request.UserId,
                ConversationId = request.ConversationId,
                InitialPrompt = request.InitialPrompt,
                Description = request.Description,
                BotName = botName,
                SourceCode = request.SourceCode
            }, sourceFile, algoFile, buildOutput, cancellationToken);

            if (result.Succeeded && onProgress is not null)
                await onProgress("Compilation completed successfully. The source and .algo files have been saved.");
            if (result.Succeeded)
                logger.LogInformation("[Compilation] Saved source and .algo for {BotName}. BotId: {BotId}; total time: {ElapsedMilliseconds} ms.",
                    botName, result.BotId, elapsed.ElapsedMilliseconds);
            else
                logger.LogWarning("[Compilation] Build succeeded for {BotName}, but backend artifact persistence failed: {Output}", botName, result.Output);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Compilation] Build for {BotName} timed out after the configured {TimeoutMinutes} minute limit.", botName, timeoutMinutes);
            var (timedOutOutput, logError) = await TryReadContainerLogsAsync(dockerClient, containerId);
            if (!string.IsNullOrWhiteSpace(logError))
                timedOutOutput = JoinOutput(timedOutOutput, $"Compiler output could not be read from Docker: {logError}");
            var output = $"Docker compilation exceeded the {timeoutMinutes}-minute timeout.";
            if (!string.IsNullOrWhiteSpace(timedOutOutput)) output += Environment.NewLine + timedOutOutput;
            return await BuildFailedAsync(request, output, cancellationToken, onProgress);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[Compilation] Build for {BotName} was cancelled by the caller.", botName);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[Compilation] Unexpected failure while compiling {BotName}.", botName);
            return await BuildFailedAsync(request, exception.ToString(), cancellationToken, onProgress);
        }
        finally
        {
            if (dockerClient is not null && containerId.Length > 0)
            {
                try
                {
                    logger.LogInformation("[Compilation] Removing Docker container {ContainerId}.", containerId);
                    await dockerClient.Containers.RemoveContainerAsync(containerId,
                        new ContainerRemoveParameters { Force = true }, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "[Compilation] Failed to remove Docker container {ContainerId}.", containerId);
                }
            }
            dockerClient?.Dispose();
            logger.LogInformation("[Compilation] Docker client disposed for {BotName}.", botName);

            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
                logger.LogInformation("[Compilation] Temporary build directory cleanup completed for {BotName}.", botName);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "[Compilation] Could not remove temporary build directory {TempRoot}.", tempRoot);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "[Compilation] Permission denied while removing temporary build directory {TempRoot}.", tempRoot);
            }
        }
    }

    private static async Task<(string Output, string? Error)> TryReadContainerLogsAsync(DockerClient? client, string containerId)
    {
        if (client is null || string.IsNullOrWhiteSpace(containerId))
            return (string.Empty, "Docker client or container ID was unavailable.");
        try
        {
            using var logs = await client.Containers.GetContainerLogsAsync(containerId, tty: false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, CancellationToken.None);
            if (logs is null)
                return (string.Empty, "Docker returned an empty log stream.");
            var (stdout, stderr) = await logs.ReadOutputToEndAsync(CancellationToken.None);
            return (JoinOutput(stdout, stderr), null);
        }
        catch (Exception exception)
        {
            // A failed log stream must not hide the container's exit code or prevent
            // the caller from seeing a useful Docker/compiler diagnostic.
            return (string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string JoinOutput(params string?[] values) =>
        string.Join(Environment.NewLine, values.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

    private static string CreateProjectFile(string botName)
    {
        var escapedBotName = System.Security.SecurityElement.Escape(botName);
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>{escapedBotName}</AssemblyName>
                <AlgoName>{escapedBotName}</AlgoName>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="cTrader.Automate" Version="*" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string CreateSolutionFile(string botName) => $"""
        <Solution>
          <Project Path="{botName}/{botName}.csproj" />
        </Solution>
        """;

    private static string SanitizeBotName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sanitized = new string(name.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return sanitized.Trim('_', '-');
    }

    private static uint GetEffectiveUserId() => geteuid();

    private static uint GetEffectiveGroupId() => getegid();

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();

    [DllImport("libc", EntryPoint = "getegid")]
    private static extern uint getegid();

    private async Task<CompileCbotToolResponse> BuildFailedAsync(
        CompileCbotToolRequest request,
        string output,
        CancellationToken cancellationToken,
        Func<string, Task>? onProgress)
    {
        logger.LogWarning("[Compilation] Recording failed compilation for {BotName}.", request.BotName);
        await cbots.SaveFailedCompilationAsync(request, output, cancellationToken);
        if (onProgress is not null)
            await onProgress("Compilation failed. Compiler diagnostics have been saved.");
        return new CompileCbotToolResponse { Succeeded = false, Output = output, BotId = request.BotId };
    }
}
