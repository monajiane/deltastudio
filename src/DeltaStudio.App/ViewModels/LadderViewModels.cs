using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Program;

namespace DeltaStudio.App.ViewModels;

/// <summary>Base for the visual model of one ladder element (rendered by a DataTemplateSelector).</summary>
public abstract class LadderElementViewModel : ObservableObject
{
    /// <summary>Short text used by previews and accessibility.</summary>
    public abstract string Text { get; }
}

/// <summary>A contact chip.</summary>
public sealed partial class ContactViewModel : LadderElementViewModel
{
    /// <summary>Address text (X0, Y10, M1000 …).</summary>
    [ObservableProperty]
    private string _device = string.Empty;

    /// <summary>Polarity keyword for display (NO / NC / UP / DOWN).</summary>
    [ObservableProperty]
    private string _polarity = "NO";

    /// <summary>Monitored state, when a monitor session is running.</summary>
    [ObservableProperty]
    private bool? _liveState;

    /// <inheritdoc/>
    public override string Text => $"{Polarity} {Device}";

    /// <summary>Glyph pair mimicking ladder contact art; live state brightens it via the template.</summary>
    public string Art => Polarity == "NC" ? "─┤/├─" : "─├┤─";
}

/// <summary>A coil chip (OUT / SET / RST).</summary>
public sealed partial class CoilViewModel : LadderElementViewModel
{
    /// <summary>Device text.</summary>
    [ObservableProperty]
    private string _device = string.Empty;

    /// <summary>OUT / SET / RST.</summary>
    [ObservableProperty]
    private string _action = "OUT";

    /// <summary>Monitored state.</summary>
    [ObservableProperty]
    private bool? _liveState;

    /// <inheritdoc/>
    public override string Text => $"{Action} {Device}";

    /// <summary>Coil art.</summary>
    public string Art => Action switch
    {
        "SET" => "─(S)─",
        "RST" => "─(R)─",
        _ => "─( )─",
    };
}

/// <summary>An application instruction block (TMR, MOV, …).</summary>
public sealed partial class BlockViewModel : LadderElementViewModel
{
    /// <summary>Mnemonic.</summary>
    [ObservableProperty]
    private string _mnemonic = string.Empty;

    /// <summary>Operand display text.</summary>
    [ObservableProperty]
    private string _operands = string.Empty;

    /// <inheritdoc/>
    public override string Text => string.IsNullOrEmpty(Operands) ? Mnemonic : $"{Mnemonic} {Operands}";
}

/// <summary>A parallel element: branches stacked vertically, each a horizontal chain.</summary>
public sealed class ParallelViewModel : LadderElementViewModel
{
    /// <summary>Branch chains (each rendered as a horizontal strip of chips).</summary>
    public ObservableCollection<ObservableCollection<LadderElementViewModel>> Branches { get; } = new();

    /// <inheritdoc/>
    public override string Text => $"PARALLEL ({Branches.Count} branches)";
}

/// <summary>One rung: header plus a top-to-bottom element chain.</summary>
public sealed partial class RungViewModel : ObservableObject
{
    /// <summary>Zero-based index in the program.</summary>
    public required int Index { get; init; }

    /// <summary>Displayed rung number (1-based).</summary>
    public string Number => $"Rung {Index + 1}";

    /// <summary>Rung comment.</summary>
    [ObservableProperty]
    private string? _comment;

    /// <summary>IL preview under the rung (the same formatter the compiler uses).</summary>
    [ObservableProperty]
    private string _ilPreview = string.Empty;

    /// <summary>Elements, in evaluation order.</summary>
    public ObservableCollection<LadderElementViewModel> Elements { get; } = new();

    /// <summary>Rebuild the visual chain from the IR rung.</summary>
    public void LoadFrom(Rung rung)
    {
        Elements.Clear();
        foreach (var el in BuildChain(rung.Logic))
        {
            Elements.Add(el);
        }
    }

    private static ObservableCollection<LadderElementViewModel> BuildChain(SeriesNetwork chain)
    {
        var result = new ObservableCollection<LadderElementViewModel>();
        foreach (var node in chain.Elements)
        {
            switch (node)
            {
                case ContactNode c:
                    result.Add(new ContactViewModel
                    {
                        Device = c.Device.ToString(),
                        Polarity = c.Polarity switch
                        {
                            ContactPolarity.NormallyClosed => "NC",
                            ContactPolarity.RisingEdge => "UP",
                            ContactPolarity.FallingEdge => "DOWN",
                            _ => "NO",
                        },
                    });
                    break;
                case CoilNode k:
                    result.Add(new CoilViewModel
                    {
                        Device = k.Device.ToString(),
                        Action = k.Action switch
                        {
                            CoilAction.Set => "SET",
                            CoilAction.Reset => "RST",
                            _ => "OUT",
                        },
                    });
                    break;
                case InstructionNode ins:
                    result.Add(new BlockViewModel
                    {
                        Mnemonic = ins.Call.Mnemonic,
                        Operands = string.Join(", ", ins.Call.Operands.Select(o => o.ToText())),
                    });
                    break;
                case ParallelNetwork par:
                    var vm = new ParallelViewModel();
                    foreach (var br in par.Branches)
                    {
                        vm.Branches.Add(BuildChain(br));
                    }

                    result.Add(vm);
                    break;
            }
        }

        return result;
    }
}
