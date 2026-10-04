using Microsoft.UI.Xaml;

namespace DeltaStudio.App.Services;

/// <summary>Available editor themes.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows setting.</summary>
    System,

    /// <summary>Light.</summary>
    Light,

    /// <summary>Dark.</summary>
    Dark,
}

/// <summary>Applies the theme root <see cref="ElementTheme"/> to the app's window content.</summary>
public sealed class ThemeService
{
    /// <summary>Current theme.</summary>
    public AppTheme Theme { get; private set; } = AppTheme.System;

    /// <summary>Raised when the theme changes; the MainWindow re-applies it to its root.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Set the active theme.</summary>
    public void SetTheme(AppTheme theme)
    {
        Theme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Translate to the XAML enum for a root element.</summary>
    public ElementTheme ToElementTheme() => Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
