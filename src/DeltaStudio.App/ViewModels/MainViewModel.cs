using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeltaStudio.App.Services;
using DeltaStudio.Compiler.Il;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Project;
using DeltaStudio.Core.Program;
using DeltaStudio.Infrastructure;
using DeltaStudio.Infrastructure.Monitoring;
using DeltaStudio.Protocols.Transport;
using Microsoft.Extensions.Logging;

namespace DeltaStudio.App.ViewModels;

/// <summary>
/// Single view-model for the shell. Every action delegates to <see cref="DeltaStudioEngine"/> —
/// the exact same surface the MCP server drives; the GUI adds no logic of its own.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DeltaStudioEngine _engine;
    private readonly LocalizationService _loc;
    private readonly ThemeService _theme;
    private readonly UiLogService _log;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IlFormatter _formatter = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    /// <summary>ViewModel-backed rungs of the open program.</summary>
    public ObservableCollection<RungViewModel> Rungs { get; } = new();

    /// <summary>Monitor watch-list rows.</summary>
    public ObservableCollection<MonitorRow> MonitorRows { get; } = new();

    /// <summary>Available model ids for the New-project dialog.</summary>
    public IReadOnlyList<string> ModelIds => _engine.Models.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Log lines for the Output pane.</summary>
    public ObservableCollection<string> LogLines => _log.Lines;

    [ObservableProperty] private string _projectTitle = "no project";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _diagnosticsText = string.Empty;
    [ObservableProperty] private string _listingText = string.Empty;
    [ObservableProperty] private string _connectionText = "Offline";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isMockSession;
    [ObservableProperty] private bool _isProjectOpen;
    [ObservableProperty] private RungViewModel? _selectedRung;
    [ObservableProperty] private string _newProjectName = "Project1";
    [ObservableProperty] private string _newProjectModel = "DVP14SS2T";
    [ObservableProperty] private string _openDirectory = string.Empty;
    [ObservableProperty] private string _serialPort = "COM3";
    [ObservableProperty] private string _deviceDialogText = "X0";
    [ObservableProperty] private string _monitorDevicesText = "X0, X1, Y0, M1000, D0";
    [ObservableProperty] private string _languageName = "English";
    [ObservableProperty] private string _themeName = "Dark";
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

    /// <summary>App services for view code-behind (dialogs).</summary>
    public MainViewModel(
        DeltaStudioEngine engine,
        LocalizationService loc,
        ThemeService theme,
        UiLogService log,
        ILogger<MainViewModel> logger)
    {
        _engine = engine;
        _loc = loc;
        _theme = theme;
        _log = log;
        _logger = logger;
        _engine.ConnectionChanged += OnConnectionChanged;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Rebuild();
    }

    /// <summary>Marks the current snapshot for the next mutation so Undo can restore it.</summary>
    private string Snapshot() => PlcProjectJson.SerializeProgram(_engine.Workspace.Current!.Program);

    private void PushUndo()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
        CanUndo = true;
        CanRedo = false;
    }

    /// <summary>Refresh rung VMs + title from the engine workspace.</summary>
    public void Rebuild()
    {
        if (_engine.Workspace.Current is null)
        {
            Rungs.Clear();
            IsProjectOpen = false;
            ProjectTitle = "no project";
            return;
        }

        PlcProject p = _engine.Workspace.Current!;
        IsProjectOpen = true;
        ProjectTitle = $"{p.Metadata.Name} — {p.Target.ModelId}{(_engine.Workspace.IsDirty ? " •" : string.Empty)}";
        var selected = SelectedRung?.Index;
        Rungs.Clear();
        for (int i = 0; i < p.Program.Main.Count; i++)
        {
            Rung rung = p.Program.Main[i];
            var vm = new RungViewModel { Index = i, Comment = rung.Comment };
            vm.LoadFrom(rung);
            vm.IlPreview = _formatter.Format(rung).Replace(Environment.NewLine, "  ");
            Rungs.Add(vm);
        }

        if (selected is not null && selected < Rungs.Count)
        {
            SelectedRung = Rungs[selected.Value];
        }
    }

    private void OnConnectionChanged(object? sender, string text)
    {
        ConnectionText = text;
        IsConnected = _engine.Active is not null;
        IsMockSession = _engine.Active?.IsMock == true;
        StatusText = text;
        if (!IsConnected)
        {
            MonitorRows.Clear();
        }
    }

    // ---------------- project commands ----------------

    private bool RequireProject()
    {
        if (_engine.Workspace.Current is null)
        {
            StatusText = "No project — use File ▸ New or File ▸ Open first.";
            return false;
        }

        return true;
    }

    /// <summary>Create a project (name + model from the bindable fields).</summary>
    [RelayCommand]
    private void NewProject()
    {
        _engine.Workspace.NewProject(NewProjectName.Trim().Length == 0 ? "Project1" : NewProjectName.Trim(), NewProjectModel);
        _undo.Clear();
        _redo.Clear();
        CanUndo = CanRedo = false;
        StatusText = $"Project '{NewProjectName}' created ({NewProjectModel})";
        _logger.LogInformation("GUI created project {Name} ({Model})", NewProjectName, NewProjectModel);
        Rebuild();
    }

    /// <summary>Open a project directory (path typed in the File dialog flyout or passed by the view).</summary>
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(OpenDirectory))
        {
            StatusText = "Enter a project directory first.";
            return;
        }

        try
        {
            await _engine.Workspace.OpenAsync(OpenDirectory.Trim());
            Rebuild();
            StatusText = $"Opened {OpenDirectory.Trim()}";
        }
        catch (Exception e)
        {
            StatusText = $"Open failed: {e.Message}";
        }
    }

    /// <summary>Save (null target keeps the current directory).</summary>
    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        try
        {
            await _engine.Workspace.SaveAsync(_engine.Workspace.Directory);
            StatusText = "Saved.";
            Rebuild();
        }
        catch (Exception e)
        {
            StatusText = $"Save failed: {e.Message}";
        }
    }

    /// <summary>Validate and show the report in the Diagnostics tab.</summary>
    [RelayCommand]
    private void Validate()
    {
        var report = _engine.Validate();
        DiagnosticsText = report.ToText();
        StatusText = $"{report.Diagnostics.Count(d => d.Severity == DeltaStudio.Core.Validation.DiagnosticSeverity.Error)} error(s), " +
                     $"{report.Diagnostics.Count(d => d.Severity != DeltaStudio.Core.Validation.DiagnosticSeverity.Error)} warning(s)";
    }

    /// <summary>Compile (IL + Delta symbolic listing; binary emission is NOT_IMPLEMENTED by design).</summary>
    [RelayCommand]
    private void Compile()
    {
        var r = _engine.Compile();
        ListingText = r.Listing;
        DiagnosticsText = r.Report.ToText();
        StatusText = r.BinaryEmittable
            ? "compiled"
            : "compiled (listing only — object-code encoder is NOT_IMPLEMENTED; see docs/communication.md)";
    }

    // ---------------- editing commands ----------------

    /// <summary>Append an empty rung.</summary>
    [RelayCommand]
    private void AddRung()
    {
        PushUndo();
        _engine.Workspace.Mutate(p => p.Program.AddRung("new rung"), "ui_add_rung");
        Rebuild();
    }

    /// <summary>Delete the selected rung.</summary>
    [RelayCommand]
    private void DeleteRung()
    {
        if (SelectedRung is null)
        {
            return;
        }

        PushUndo();
        int idx = SelectedRung.Index;
        _engine.Workspace.Mutate(p => p.Program.RemoveRung(idx), "ui_delete_rung");
        Rebuild();
    }

    /// <summary>Move selected rung up (-1) or down (+1).</summary>
    [RelayCommand]
    private void MoveRung(object? directionObj)
    {
        if (!RequireProject() || SelectedRung is null)
        {
            return;
        }

        int dir = directionObj is int i ? i : (string.Equals(directionObj?.ToString(), "up", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
        int from = SelectedRung.Index;
        int to = from + dir;
        if (to < 0 || to >= Rungs.Count)
        {
            return;
        }

        PushUndo();
        _engine.Workspace.Mutate(p => p.Program.MoveRung(from, to), "ui_move_rung");
        Rebuild();
        SelectedRung = to < Rungs.Count ? Rungs[to] : null;
    }

    /// <summary>Add a contact to the selected rung (DeviceDialogText = address, polarity from dialog combo).</summary>
    [RelayCommand]
    private void AddContact(object? polarityObj)
    {
        if (!RequireProject() || SelectedRung is null)
        {
            return;
        }

        if (!DeviceAddress.TryParse(DeviceDialogText.Trim(), out DeviceAddress addr, out string? err))
        {
            StatusText = err ?? "bad address";
            return;
        }

        ContactPolarity pol = polarityObj?.ToString() switch
        {
            "NC" => ContactPolarity.NormallyClosed,
            "UP" => ContactPolarity.RisingEdge,
            "DOWN" => ContactPolarity.FallingEdge,
            _ => ContactPolarity.NormallyOpen,
        };

        PushUndo();
        int idx = SelectedRung.Index;
        _engine.Workspace.Mutate(p => AppendContact(p.Program.Main[idx].Logic, addr, pol), "ui_add_contact");
        Rebuild();
    }

    /// <summary>Add a coil to the selected rung.</summary>
    [RelayCommand]
    private void AddCoil(object? actionObj)
    {
        if (!RequireProject() || SelectedRung is null)
        {
            return;
        }

        if (!DeviceAddress.TryParse(DeviceDialogText.Trim(), out DeviceAddress addr, out string? err))
        {
            StatusText = err ?? "bad address";
            return;
        }

        CoilAction action = actionObj?.ToString() switch
        {
            "SET" => CoilAction.Set,
            "RST" => CoilAction.Reset,
            _ => CoilAction.Output,
        };

        PushUndo();
        int idx = SelectedRung.Index;
        _engine.Workspace.Mutate(p => p.Program.Main[idx].Logic.Elements.Add(new CoilNode { Device = addr, Action = action }), "ui_add_coil");
        Rebuild();
    }

    private static void AppendContact(SeriesNetwork logic, DeviceAddress addr, ContactPolarity pol)
    {
        // parallel insertion is handled by the MCP toolset; GUI appends in series for now (documented as PARTIAL)
        logic.Elements.Add(new ContactNode { Device = addr, Polarity = pol });
    }

    /// <summary>Undo the last UI mutation (snapshot based).</summary>
    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Snapshot());
        Restore(_undo.Pop());
    }

    /// <summary>Redo.</summary>
    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Snapshot());
        Restore(_redo.Pop());
    }

    private void Restore(string programJson)
    {
        PlcProgram program = PlcProjectJson.DeserializeProgram(programJson);
        _engine.Workspace.Mutate(p =>
        {
            p.Program.Main.Clear();
            foreach (Rung r in program.Main)
            {
                p.Program.Main.Add(r);
            }
        }, "ui_undo_redo");
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
        Rebuild();
    }

    // ---------------- connection + monitor ----------------

    /// <summary>Connect to the in-process mock PLC (clearly labelled as emulation).</summary>
    [RelayCommand]
    private async Task ConnectMockAsync()
    {
        var plc = _engine.CreateMockPlc();
        await _engine.ConnectMockAsync(plc);
        StatusText = "connected to MOCK (emulated devices — not hardware)";
    }

    /// <summary>Connect via RS-232/RS-485 serial (Modbus RTU framing against DVP link layer).</summary>
    [RelayCommand]
    private async Task ConnectSerialAsync()
    {
        try
        {
            var transport = new SerialPortTransport(new SerialTransportOptions { PortName = SerialPort.Trim(), BaudRate = 9600, DataBits = 7, Parity = System.IO.Ports.Parity.Even, StopBits = System.IO.Ports.StopBits.One });
            await _engine.ConnectAsync(transport, station: 1, useAscii: false);
            StatusText = $"connected via {transport.Description}";
        }
        catch (Exception e)
        {
            StatusText = $"connect failed: {e.Message}";
        }
    }

    /// <summary>Disconnect.</summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _engine.DisconnectAsync();
        StatusText = "disconnected";
    }

    /// <summary>Start the monitor for the device list in MonitorDevicesText.</summary>
    [RelayCommand]
    private void StartMonitor()
    {
        if (_engine.Monitor is null)
        {
            StatusText = "connect to a PLC first";
            return;
        }

        var devices = MonitorDevicesText
            .Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => DeviceAddress.TryParse(t.Trim(), out DeviceAddress d, out _) ? d : (DeviceAddress?)null)
            .OfType<DeviceAddress>()
            .ToList();
        if (devices.Count == 0)
        {
            StatusText = "no valid devices to watch";
            return;
        }

        MonitorRows.Clear();
        foreach (DeviceAddress d in devices)
        {
            MonitorRows.Add(new MonitorRow(d.ToString()));
        }

        _engine.Monitor.SamplesReady += OnSamples;
        _engine.Monitor.Start(devices, TimeSpan.FromMilliseconds(500));
        StatusText = $"monitoring {devices.Count} device(s) @ 2 Hz";
    }

    /// <summary>Stop polling.</summary>
    [RelayCommand]
    private void StopMonitor()
    {
        if (_engine.Monitor is not null)
        {
            _engine.Monitor.SamplesReady -= OnSamples;
            _engine.Monitor.Stop();
        }

        StatusText = "monitor stopped";
    }

    private void OnSamples(object? sender, IReadOnlyList<MonitorSample> samples)
    {
        // poller runs on the thread pool → marshal into the UI dispatcher
        void Apply()
        {
            foreach (MonitorSample s in samples)
            {
                string text = s.Error is not null
                    ? $"ERR {s.Error}"
                    : s.Value.IsWord ? $"{s.Value.Word} (0x{s.Value.Word:X4}, {s.Value.SignedWord})" : s.Value.Bit ? "ON" : "OFF";
                foreach (MonitorRow row in MonitorRows.Where(r => r.Device == s.Address.ToString()).ToList())
                {
                    row.Value = text;
                }
            }
        }

        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            _dispatcher.TryEnqueue(() => Apply());
        }
    }

    // ---------------- shell ----------------

    /// <summary>Toggle light/dark.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        AppTheme next = ThemeName == "Dark" ? AppTheme.Light : AppTheme.Dark;
        _theme.SetTheme(next);
        ThemeName = next.ToString();
    }

    /// <summary>Toggle English/Persian (shell RTL for Persian; ladder stays LTR).</summary>
    [RelayCommand]
    private void ToggleLanguage()
    {
        UiLanguage next = LanguageName == "English" ? UiLanguage.Persian : UiLanguage.English;
        _loc.SetLanguage(next);
        LanguageName = next.ToString();
    }

    /// <summary>Text shown in the AI menu: how to attach agents while this window is open.</summary>
    public string AiHint =>
        "The same engine is served over stdio by:  deltastudio mcp-stdio --dir <datadir>\n" +
        "Point any MCP client at it — project, ladder, validation, compile and monitored reads all work while this GUI is open.";

    /// <summary>Live monitor display row.</summary>
    public sealed class MonitorRow(string device)
    {
        private string _value = "…";

        /// <summary>Watched device.</summary>
        public string Device { get; } = device;

        /// <summary>Latest value.</summary>
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value)));
            }
        }

        /// <inheritdoc cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
