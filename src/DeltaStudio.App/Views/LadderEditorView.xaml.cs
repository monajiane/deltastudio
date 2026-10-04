using Microsoft.UI.Xaml.Controls;

namespace DeltaStudio.App.Views;

/// <summary>Graphical ladder editor surface: renders the semantic IR as chips; edits go through the engine.</summary>
public sealed partial class LadderEditorView : UserControl
{
    /// <summary>Creates the view; DataContext comes from the parent window.</summary>
    public LadderEditorView() => InitializeComponent();
}
