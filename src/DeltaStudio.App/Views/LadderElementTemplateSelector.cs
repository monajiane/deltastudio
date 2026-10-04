using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaStudio.App.Views;

/// <summary>Maps ladder element view-models to their chip templates.</summary>
public sealed class LadderElementTemplateSelector : DataTemplateSelector
{
    /// <summary>Normally-open/closed contact chip.</summary>
    public DataTemplate? ContactTemplate { get; set; }

    /// <summary>Coil chip.</summary>
    public DataTemplate? CoilTemplate { get; set; }

    /// <summary>Instruction block.</summary>
    public DataTemplate? BlockTemplate { get; set; }

    /// <summary>Parallel branch group.</summary>
    public DataTemplate? ParallelTemplate { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => Pick(item) ?? base.SelectTemplateCore(item, container);

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item) => Pick(item) ?? base.SelectTemplateCore(item);

    private DataTemplate? Pick(object item) => item switch
    {
        App.ViewModels.ContactViewModel => ContactTemplate,
        App.ViewModels.CoilViewModel => CoilTemplate,
        App.ViewModels.BlockViewModel => BlockTemplate,
        App.ViewModels.ParallelViewModel => ParallelTemplate,
        _ => null,
    };
}
