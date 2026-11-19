using System.ComponentModel;
using backend.Dtos;
using backend.Services;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace backend.Tools;

/// <summary>Builds cTrader cBots in a disposable .NET SDK container and saves generated artifacts.</summary>
public sealed class CompilationAgentTools(IConfiguration configuration, CbotsService cbots)
{
    public const string ToolGroupName = "CompilationAgentTools";
    public const string ToolName = "compile_ctrader_cbot";
    public const string ToolDescription = "Compiles cTrader Automate cBot source in the .NET 10 SDK Docker image and saves the source and .algo artifacts.";

    private const string DefaultSdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";
    private const string DefaultBuildRoot = "/srv/ctrader-builds";
    private const string NuGetCacheVolume = "ctrader-nuget-cache";
    private const int DefaultTimeoutMinutes = 5;

    [Description(ToolDescription)]
    public async Task<CompileCbotToolResponse> CompileAsync(
        CompileCbotToolRequest request,
        CancellationToken cancellationToken = default,
        Func<string, Task>? onProgress = null)
    {
        var botName = SanitizeBotName(request.BotName);
        if (botName.Length == 0)
            return new CompileCbotToolResponse
            {
                Succeeded = false,
                Output = "The bot name must contain at least one letter or number.",
                BotId = request.BotId
            };

        if (string.IsNullOrWhiteSpace(request.SourceCode))
            return new CompileCbotToolResponse
            {
                Succeeded = false,
                Output = "The cBot source code cannot be empty.",
                BotId = request.BotId
            };

        var buildRoot = Path.GetFullPath(configuration["CTrader:BuildRoot"] ?? DefaultBuildRoot);
        var tempRoot = Path.Combine(buildRoot, Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(tempRoot, "temp_proj");
        var projectFile = Path.Combine(projectDirectory, "temp_proj.csproj");
        var sourceFile = Path.Combine(projectDirectory, $"{botName}.cs");
        var containerId = string.Empty;
        DockerClient? dockerClient = null;
        var timeoutMinutes = int.TryParse(configuration["CTrader:BuildTimeoutMinutes"], out var configuredTimeout)
            ? Math.Clamp(configuredTimeout, 1, 60)
            : DefaultTimeoutMinutes;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (onProgress is not null)
                await onProgress("Creating the temporary cTrader compilation project…");

            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(sourceFile, request.SourceCode, cancellationToken);
            await File.WriteAllTextAsync(projectFile, CreateProjectFile(botName), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "temp.slnx"), CreateSolutionFile(), cancellationToken);

            if (onProgress is not null)
                await onProgress("Project created. Preparing the .NET 10 Docker compiler…");

            var image = configuration["CTrader:DockerImage"] ?? DefaultSdkImage;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

            var endpoint = configuration["CTrader:DockerEndpoint"];
            dockerClient = string.IsNullOrWhiteSpace(endpoint)
                ? new DockerClientConfiguration().CreateClient()
                : new DockerClientConfiguration(new Uri(endpoint)).CreateClient();

            if (onProgress is not null)
                await onProgress("Connecting to Docker and preparing the SDK image…");
            var (imageName, imageTag) = SplitImageReference(image);
            await dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = imageName, Tag = imageTag },
                authConfig: null,
                progress: null,
                cancellationToken: timeout.Token);

            cancellationToken.ThrowIfCancellationRequested();
            if (onProgress is not null)
                await onProgress("Starting the .NET 10 compilation container…");

            var container = await dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = image,
                WorkingDir = "/workspace",
                Cmd = ["dotnet", "build", "temp.slnx", "--configuration", "Release", "--verbosity", "minimal", "--nologo"],
                HostConfig = new HostConfig
                {
                    Binds =
                    [
                        $"{tempRoot}:/workspace",
                        $"{NuGetCacheVolume}:/root/.nuget/packages"
                    ]
                }
            }, timeout.Token);
            containerId = container?.ID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(containerId))
                throw new InvalidOperationException("Docker created the compilation container without returning a container ID.");

            await dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), timeout.Token);
            if (onProgress is not null)
                await onProgress("Compilation is running in the Docker container…");

            var waitResponse = await dockerClient.Containers.WaitContainerAsync(containerId, timeout.Token);
            if (waitResponse is null)
                throw new InvalidOperationException("Docker stopped the compilation container without returning its exit status.");

            var (buildOutput, logError) = await TryReadContainerLogsAsync(dockerClient, containerId);
            if (!string.IsNullOrWhiteSpace(logError))
                buildOutput = JoinOutput(buildOutput, $"Compiler output could not be read from Docker: {logError}");

            if (waitResponse.StatusCode != 0)
                return await BuildFailedAsync(request,
                    $"cTrader compilation failed (exit code {waitResponse.StatusCode}).{Environment.NewLine}{buildOutput}",
                    cancellationToken, onProgress);

            if (onProgress is not null)
                await onProgress("Build succeeded. Searching for the generated .algo file…");
            var algoFile = Directory.EnumerateFiles(tempRoot, "*.algo", SearchOption.AllDirectories).FirstOrDefault();
            if (algoFile is null)
                return await BuildFailedAsync(request,
                    $"The .NET build succeeded, but no .algo file was generated. Check the cTrader build targets and project configuration.{Environment.NewLine}{buildOutput}",
                    cancellationToken, onProgress);

            if (onProgress is not null)
                await onProgress("The .algo file was generated. Saving compilation artifacts…");
            var result = await cbots.SaveSuccessfulCompilationAsync(new CompileCbotToolRequest
            {
                BotId = request.BotId,
                UserId = request.UserId,
                ConversationId = request.ConversationId,
                InitialPrompt = request.InitialPrompt,
                BotName = botName,
                SourceCode = request.SourceCode
            }, sourceFile, algoFile, buildOutput, cancellationToken);

            if (result.Succeeded && onProgress is not null)
                await onProgress("Compilation completed successfully. The source and .algo files have been saved.");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var (timedOutOutput, logError) = await TryReadContainerLogsAsync(dockerClient, containerId);
            if (!string.IsNullOrWhiteSpace(logError))
                timedOutOutput = JoinOutput(timedOutOutput, $"Compiler output could not be read from Docker: {logError}");
            var output = $"Docker compilation exceeded the {timeoutMinutes}-minute timeout.";
            if (!string.IsNullOrWhiteSpace(timedOutOutput)) output += Environment.NewLine + timedOutOutput;
            return await BuildFailedAsync(request, output, cancellationToken, onProgress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await BuildFailedAsync(request, exception.ToString(), cancellationToken, onProgress);
        }
        finally
        {
            if (dockerClient is not null && containerId.Length > 0)
            {
                try
                {
                    await dockerClient.Containers.RemoveContainerAsync(containerId,
                        new ContainerRemoveParameters { Force = true }, CancellationToken.None);
                }
                catch (Exception) { }
            }
            dockerClient?.Dispose();

            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static (string ImageName, string Tag) SplitImageReference(string image)
    {
        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        return lastColon > lastSlash
            ? (image[..lastColon], image[(lastColon + 1)..])
            : (image, "latest");
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
                <OutputType>Library</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>{escapedBotName}</AssemblyName>
                <AlgoName>{escapedBotName}</AlgoName>
                <AlgoBuild>true</AlgoBuild>
                <AlgoPublish>false</AlgoPublish>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="cTrader.Automate" Version="1.0.21" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string CreateSolutionFile() => """
        <Solution>
          <Project Path="temp_proj/temp_proj.csproj" />
        </Solution>
        """;

    private static string SanitizeBotName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sanitized = new string(name.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return sanitized.Trim('_', '-');
    }

    private async Task<CompileCbotToolResponse> BuildFailedAsync(
        CompileCbotToolRequest request,
        string output,
        CancellationToken cancellationToken,
        Func<string, Task>? onProgress)
    {
        await cbots.SaveFailedCompilationAsync(request, output, cancellationToken);
        if (onProgress is not null)
            await onProgress("Compilation failed. Compiler diagnostics have been saved.");
        return new CompileCbotToolResponse { Succeeded = false, Output = output, BotId = request.BotId };
    }
}
