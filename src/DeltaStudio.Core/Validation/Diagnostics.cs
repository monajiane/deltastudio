namespace DeltaStudio.Core.Validation;

/// <summary>Diagnostic severity.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Probably wrong, compiles anyway.</summary>
    Warning,

    /// <summary>Must be fixed before download.</summary>
    Error,
}

/// <summary>One validation finding.</summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    int? RungIndex = null,
    string? ElementPath = null)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{Severity.ToString().ToUpperInvariant()} {Code}: {Message}" +
        (RungIndex is null ? string.Empty : $" [rung {RungIndex + 1}]") +
        (ElementPath is null ? string.Empty : $" ({ElementPath})");
}

/// <summary>Aggregated validation output.</summary>
public sealed class ValidationReport
{
    /// <summary>All diagnostics in emission order.</summary>
    public List<Diagnostic> Diagnostics { get; } = new();

    /// <summary>True when at least one error was emitted.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Adds a diagnostic.</summary>
    public void Add(Diagnostic d) => Diagnostics.Add(d);

    /// <summary>Adds a diagnostic if it matches the predicate severity floor.</summary>
    public void AddIf(bool condition, Diagnostic diagnostic)
    {
        if (condition)
        {
            Diagnostics.Add(diagnostic);
        }
    }

    /// <summary>Renders the report as multi-line text for the output pane / MCP responses.</summary>
    public string ToText() => Diagnostics.Count == 0
        ? "No diagnostics."
        : string.Join(Environment.NewLine, Diagnostics.Select(d => d.ToString()));
}
