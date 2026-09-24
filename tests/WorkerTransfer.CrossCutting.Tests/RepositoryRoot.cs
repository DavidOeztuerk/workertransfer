namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>Finds the repository root.</summary>
internal static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "WorkerTransfer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Every service in the tree, by its folder: <c>src/&lt;name&gt;-service</c>.</summary>
    /// <remarks>
    /// Lists of services in these suites are compared with THIS, not with a
    /// number. A pinned count only proves that nobody changed the count:
    /// assessment-service stood in neither QueryCacheTests nor WireContractTests
    /// for weeks, and both stayed green at "thirteen".
    /// </remarks>
    public static IReadOnlyList<string> Services() =>
        [.. Directory.GetDirectories(Path.Combine(Find(), "src"), "*-service")
            .Select(path => Path.GetFileName(path)[..^"-service".Length])
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The services whose own <c>*.Contracts</c> project holds at least one source
    /// file — an empty project has nothing on the wire to check.
    /// </summary>
    public static IReadOnlyList<string> ServicesWithContracts() =>
        [.. Services().Where(service => Directory
            .GetDirectories(Path.Combine(Find(), "src", $"{service}-service"), "*.Contracts")
            .Any(project => Directory
                .EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                .Any(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                 StringComparison.Ordinal)
                             && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                 StringComparison.Ordinal))))];
}
