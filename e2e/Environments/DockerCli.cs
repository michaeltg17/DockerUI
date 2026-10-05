namespace E2E.Environments;

/// <summary>
/// Runs Docker CLI commands with captured output. Compose commands are executed from the
/// stack's own directory so the default project name and relative paths resolve as expected.
/// </summary>
public static class DockerCli
{
    public static async Task<DockerResult> RunAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = new System.Diagnostics.Process
        {
            StartInfo =
            {
                FileName = "docker",
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

    /// <summary>Runs the command and throws with the captured output if it fails.</summary>
    public static async Task<DockerResult> EnsureSucceededAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken);

        return result.Succeeded
            ? result
            : throw new InvalidOperationException(
                $"Command 'docker {string.Join(' ', arguments)}' (in '{workingDirectory}') failed with exit code {result.ExitCode}." +
                $"\n--- stdout ---\n{result.StandardOutput}" +
                $"\n--- stderr ---\n{result.StandardError}");
    }
}
