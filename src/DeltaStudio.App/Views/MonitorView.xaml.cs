using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace DeltaStudio.App.Views;

/// <summary>Watch-list panel backed by the engine's poller.</summary>
public sealed partial class MonitorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public MonitorView() => InitializeComponent();
}

/// <summary>True→Visible converter (registered as a resource).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
