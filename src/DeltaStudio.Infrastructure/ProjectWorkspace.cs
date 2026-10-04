using DeltaStudio.Core.Project;
using DeltaStudio.Core.Model;
using DeltaStudio.Delta.Models;

namespace DeltaStudio.Infrastructure;

/// <summary>
/// The one and only holder of the currently open project for an engine instance.
/// WinUI and MCP both mutate through here — the same code path, no duplicated editor logic.
/// Events let the UI repaint while a headless MCP session keeps working without a UI at all.
/// </summary>
public sealed class ProjectWorkspace
{
    private readonly object _sync = new();

    /// <summary>Raised after any mutation (content, model selection, symbols…).</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary>Currently open project (null = no project).</summary>
    public PlcProject? Current { get; private set; }

    /// <summary>Directory of the open project (null when unsaved).</summary>
    public string? Directory { get; private set; }

    /// <summary>True when there are unsaved modifications.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Creates a fresh project in memory (unsaved).</summary>
    public PlcProject NewProject(string name, string modelId)
    {
        lock (_sync)
        {
            Current = new PlcProject
            {
                Metadata = new PlcProjectMetadata { Name = name },
                Target = new PlcTarget { ModelId = modelId },
            };
            Directory = null;
            IsDirty = true;
        }

        Raise("new");
        return Current;
    }

    /// <summary>Opens an existing directory.</summary>
    public async Task<PlcProject> OpenAsync(string directory, CancellationToken ct = default)
    {
        PlcProject project = await ProjectFileIO.OpenAsync(directory, ct).ConfigureAwait(false);
        lock (_sync)
        {
            Current = project;
            Directory = directory;
            IsDirty = false;
        }

        Raise("open");
        return project;
    }

    /// <summary>Saves (to the current directory, or the supplied one for the first save).</summary>
    public async Task SaveAsync(string? targetDirectory = null, CancellationToken ct = default)
    {
        PlcProject project = Require();
        string dir = targetDirectory ?? Directory ?? throw new InvalidOperationException("Project has no directory; supply one for the first save.");
        await ProjectFileIO.SaveAsync(dir, project, ct).ConfigureAwait(false);
        lock (_sync)
        {
            Directory = dir;
            IsDirty = false;
        }

        Raise("save");
    }

    /// <summary>Applies a mutation on the current project on the engine's single code path.</summary>
    public T Mutate<T>(Func<PlcProject, T> action, string reason)
    {
        PlcProject project = Require();
        T result = action(project);
        lock (_sync)
        {
            IsDirty = true;
            project.Touch();
        }

        Raise(reason);
        return result;
    }

    /// <summary>Void mutation convenience.</summary>
    public void Mutate(Action<PlcProject> action, string reason) => Mutate<object?>(p => { action(p); return null; }, reason);

    /// <summary>Closes the project.</summary>
    public void Close()
    {
        lock (_sync)
        {
            Current = null;
            Directory = null;
            IsDirty = false;
        }

        Raise("close");
    }

    /// <summary>Selects the target CPU model (validated against the catalog).</summary>
    public void SelectModel(string modelId)
    {
        if (!DvpModelCatalog.TryGet(modelId, out PlcModelDefinition? m) || m is null)
        {
            throw new ArgumentException($"Unknown PLC model '{modelId}'. Use plc_get_available_models.");
        }

        Mutate(p => p.Target.ModelId = m.Id, "model");
    }

    /// <summary>The resolved model definition of the current project.</summary>
    public PlcModelDefinition CurrentModel => ResolveModel(Require().Target.ModelId);

    /// <summary>Resolves a model id (throws with guidance when unknown).</summary>
    public static PlcModelDefinition ResolveModel(string modelId)
    {
        if (!DvpModelCatalog.TryGet(modelId, out PlcModelDefinition? m) || m is null)
        {
            throw new ArgumentException($"Unknown PLC model '{modelId}'. Known: {string.Join(", ", DvpModelCatalog.ModelIds)}.");
        }

        return m;
    }

    private PlcProject Require() =>
        Current ?? throw new InvalidOperationException("No project is open. Call project_create or project_open first.");

    private void Raise(string reason) => Changed?.Invoke(this, new ProjectChangedEventArgs(reason, Current, IsDirty));
}

/// <summary>Workspace change notification.</summary>
public sealed class ProjectChangedEventArgs(string reason, PlcProject? project, bool isDirty) : EventArgs
{
    /// <summary>Why the change happened (e.g. "rung_add", "save").</summary>
    public string Reason { get; } = reason;

    /// <summary>The project after the change (null when closed).</summary>
    public PlcProject? Project { get; } = project;

    /// <summary>Dirty flag after the change.</summary>
    public bool IsDirty { get; } = isDirty;
}
