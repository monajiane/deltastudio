using DeltaStudio.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DeltaStudio.App.Views;

/// <summary>
/// Project tree (program rungs, symbols). Built in code because the shape follows the engine's
/// live project; invoking a rung node selects it in the editor.
/// </summary>
public sealed partial class ProjectExplorerView : UserControl
{
    private MainViewModel? _vm;

    /// <summary>Creates the view.</summary>
    public ProjectExplorerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnVmChanged;
            }

            _vm = DataContext as MainViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmChanged;
            }

            Rebuild();
        };
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ProjectTitle))
        {
            Rebuild();
        }
    }

    private sealed record ExplorerNode(string Text, object? Tag)
    {
        public override string ToString() => Text;
    }

    /// <summary>Rebuilds the tree from the view-model.</summary>
    public void Rebuild()
    {
        Root.RootNodes.Clear();
        if (_vm is null)
        {
            return;
        }

        var program = new TreeViewNode { Content = new ExplorerNode($"Program ({_vm.Rungs.Count} rungs)", null), IsExpanded = true };
        foreach (RungViewModel r in _vm.Rungs)
        {
            program.Children.Add(new TreeViewNode { Content = new ExplorerNode($"{r.Number}: {r.Comment ?? "(no comment)"}", r) });
        }

        var project = new TreeViewNode
        {
            Content = new ExplorerNode(_vm.ProjectTitle, null),
            IsExpanded = true,
        };
        project.Children.Add(program);
        project.Children.Add(new TreeViewNode { Content = new ExplorerNode("Target: Delta DVP (capability-checked)", null) });
        project.Children.Add(new TreeViewNode { Content = new ExplorerNode("Symbols", null) });
        Root.RootNodes.Add(project);
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedNode is { Content: ExplorerNode { Tag: RungViewModel rung } } && _vm is not null)
        {
            _vm.SelectedRung = rung;
        }
    }
}
