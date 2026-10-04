using DeltaStudio.Core.Program;

namespace DeltaStudio.Core.Project;

/// <summary>
/// Directory-based project format:
/// MyMachine/ ├── project.json ├── program/main.json └── symbols.json
/// (diagnostics are runtime output and intentionally not persisted; see docs/project-format.md).
/// </summary>
public static class ProjectFileIO
{
    /// <summary>Project manifest file name.</summary>
    public const string ManifestFile = "project.json";

    /// <summary>Program file name (relative).</summary>
    public const string ProgramFile = "program/main.json";

    /// <summary>Symbols file name (relative).</summary>
    public const string SymbolsFile = "symbols.json";

    /// <summary>True when the directory holds a DeltaStudio project.</summary>
    public static bool IsProjectDirectory(string root) =>
        File.Exists(Path.Combine(root, ManifestFile));

    /// <summary>Creates a directory with a fresh project; throws when the directory already holds a project.</summary>
    public static async Task<PlcProject> CreateAsync(
        string root, string name, string modelId, CancellationToken ct = default)
    {
        var project = new PlcProject
        {
            Metadata = new PlcProjectMetadata { Name = name, CreatedUtc = DateTimeOffset.UtcNow },
            Target = new PlcTarget { ModelId = modelId },
            Program = new PlcProgram(),
        };

        await SaveAsync(root, project, ct).ConfigureAwait(false);
        return project;
    }

    /// <summary>Persists the project split across manifest, program and symbols files.</summary>
    public static async Task SaveAsync(string root, PlcProject project, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "program"));
        project.Touch();

        await File.WriteAllTextAsync(
            Path.Combine(root, ManifestFile), PlcProjectJson.SerializeProject(project), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(root, ProgramFile), PlcProjectJson.SerializeProgram(project.Program), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(root, SymbolsFile), PlcProjectJson.SerializeSymbols(project.Symbols), ct).ConfigureAwait(false);
    }

    /// <summary>Loads a project directory.</summary>
    public static async Task<PlcProject> OpenAsync(string root, CancellationToken ct = default)
    {
        string manifestPath = Path.Combine(root, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            throw new DirectoryNotFoundException($"{root} is not a DeltaStudio project (no {ManifestFile}).");
        }

        var project = new PlcProject();
        PlcProjectJson.ApplyProjectJson(project, await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));

        string programPath = Path.Combine(root, ProgramFile);
        if (File.Exists(programPath))
        {
            project.Program = PlcProjectJson.DeserializeProgram(await File.ReadAllTextAsync(programPath, ct).ConfigureAwait(false));
        }

        string symbolsPath = Path.Combine(root, SymbolsFile);
        if (File.Exists(symbolsPath))
        {
            project.Symbols = PlcProjectJson.DeserializeSymbols(await File.ReadAllTextAsync(symbolsPath, ct).ConfigureAwait(false));
        }

        return project;
    }
}
