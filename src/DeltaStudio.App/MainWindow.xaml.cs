using DeltaStudio.App.Services;
using DeltaStudio.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;

namespace DeltaStudio.App;

/// <summary>Shell window. Pure composition surface: menus route straight into <see cref="MainViewModel"/>.</summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly LocalizationService _loc;
    private readonly ThemeService _theme;

    /// <summary>Creates the window and wires services.</summary>
    public MainWindow()
    {
        _vm = App.Services.GetRequiredService<MainViewModel>();
        _loc = App.Services.GetRequiredService<LocalizationService>();
        _theme = App.Services.GetRequiredService<ThemeService>();
        InitializeComponent();
        Title = _loc["AppTitle"];
        DataContext = _vm;
        _loc.LanguageChanged += (_, _) => Title = _loc["AppTitle"];
        _theme.ThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Shell.PreviewKeyDown += OnGlobalKeyDown;
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = _theme.ToElementTheme();
        }
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        var shell = (FrameworkElement)sender;
        shell.FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _loc.LanguageChanged += (_, _) =>
            shell.FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (!ctrl)
        {
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.S:
                _ = _vm.SaveProjectCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Z:
                _vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Y:
                _vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---------------- menu handlers ----------------

    private async void OnNewProject(object sender, RoutedEventArgs e)
    {
        if (await NewProjectDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _vm.NewProjectCommand.Execute(null);
        }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        global::WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _vm.OpenDirectory = folder.Path;
            await _vm.OpenProjectCommand.ExecuteAsync(null);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => _ = _vm.SaveProjectCommand.ExecuteAsync(null);

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnUndo(object sender, RoutedEventArgs e) => _vm.UndoCommand.Execute(null);

    private void OnRedo(object sender, RoutedEventArgs e) => _vm.RedoCommand.Execute(null);

    private void OnToggleTheme(object sender, RoutedEventArgs e) => _vm.ToggleThemeCommand.Execute(null);

    private void OnToggleLanguage(object sender, RoutedEventArgs e) => _vm.ToggleLanguageCommand.Execute(null);

    private void OnConnectMock(object sender, RoutedEventArgs e) => _ = _vm.ConnectMockCommand.ExecuteAsync(null);

    private void OnConnectSerial(object sender, RoutedEventArgs e) => _ = _vm.ConnectSerialCommand.ExecuteAsync(null);

    private async void OnDisconnect(object sender, RoutedEventArgs e) => await _vm.DisconnectCommand.ExecuteAsync(null);

    private void OnValidate(object sender, RoutedEventArgs e) => _vm.ValidateCommand.Execute(null);

    private void OnCompile(object sender, RoutedEventArgs e) => _vm.CompileCommand.Execute(null);

    private async void OnAiInfo(object sender, RoutedEventArgs e) => await AiDialog.ShowAsync();
}
