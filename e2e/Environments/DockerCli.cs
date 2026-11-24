using System.Diagnostics;

namespace E2E.Environments;

/// <summary>
/// Runs Docker CLI commands with captured output. Compose commands are executed from the
/// stack's own directory so the default project name and relative paths resolve as expected.
/// The tool overloads run other container-adjacent CLIs (kind, kubectl) the same way.
/// </summary>
public static class DockerCli
{
    public static Task<DockerResult> RunAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
        => RunToolAsync("docker", workingDirectory, arguments, cancellationToken);

    public static Task<DockerResult> RunToolAsync(
        string tool,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunProcessAsync(tool, workingDirectory, arguments, cancellationToken);
    }

    /// <summary>Runs the command and throws with the captured output if it fails.</summary>
    public static Task<DockerResult> EnsureSucceededAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
        => EnsureSucceededToolAsync("docker", workingDirectory, arguments, cancellationToken);

    /// <summary>Runs the command and throws with the captured output if it fails.</summary>
    public static async Task<DockerResult> EnsureSucceededToolAsync(
        string tool,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunToolAsync(tool, workingDirectory, arguments, cancellationToken);

        return result.Succeeded
            ? result
            : throw new InvalidOperationException(
                $"Command '{tool} {string.Join(' ', arguments)}' (in '{workingDirectory}') failed with exit code {result.ExitCode}." +
                $"\n--- stdout ---\n{result.StandardOutput}" +
                $"\n--- stderr ---\n{result.StandardError}");
    }

    static async Task<DockerResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo =
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new DockerResult(process.ExitCode, await standardOutput, await standardError);
    }
}
