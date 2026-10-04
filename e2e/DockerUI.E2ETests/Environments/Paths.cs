namespace DockerUI.E2ETests.Environments;

/// <summary>
/// Resolves the directories the e2e suite needs. The paths are discovered relative to the
/// test assembly location, so the suite works from any output directory.
/// </summary>
public static class Paths
{
    /// <summary>Tag of the DockerUI image the scenarios run against (built from the repo root).</summary>
    public const string DashboardImageTag = "docker-ui-e2e:latest";

    public static string E2eDirectory { get; } = FindDirectoryContaining("scenarios")
        ?? throw new InvalidOperationException("Could not locate the e2e directory (no 'scenarios' folder found).");

    public static string RepoRoot { get; } = FindDirectoryContaining("Dockerfile")
        ?? throw new InvalidOperationException("Could not locate the repository root (no 'Dockerfile' found).");

    /// <summary>Joins segments under the e2e directory and resolves to an absolute path.</summary>
    public static string CombineE2e(params string[] segments) => Path.GetFullPath(Path.Combine([E2eDirectory, .. segments]));

    static string? FindDirectoryContaining(string marker)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker)) ||
                Directory.Exists(Path.Combine(directory.FullName, marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
