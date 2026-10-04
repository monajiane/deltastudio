using System.Text.Json.Nodes;
using DeltaStudio.Compiler.Il;
using DeltaStudio.Core.Project;
using DeltaStudio.Delta.Models;
using DeltaStudio.Infrastructure;
using DeltaStudio.Mcp;

namespace DeltaStudio.Cli;

/// <summary>
/// Headless entry point proving the architecture: the MCP server (and project work) run fully
/// WITHOUT the WinUI app. Usage:
///   deltastudio mcp-stdio              run the MCP server over stdin/stdout
///   deltastudio new &lt;dir&gt; &lt;name&gt; &lt;model&gt;  create a project
///   deltastudio validate &lt;dir&gt;         validate + report
///   deltastudio compile &lt;dir&gt;          validate + symbolic listing
///   deltastudio info &lt;dir&gt;             metadata summary
///   deltastudio il &lt;dir&gt;               render IL text
///   deltastudio models                 list supported CPUs
/// </summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return args[0] switch
            {
                "mcp-stdio" => await RunMcpAsync(cts.Token),
                "new" => await NewAsync(args),
                "validate" => await ValidateAsync(args),
                "compile" => await CompileAsync(args),
                "info" => await InfoAsync(args),
                "il" => await IlAsync(args),
                "models" => ListModels(),
                "--help" or "-h" => Usage(),
                _ => Usage(),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    private static async Task<int> RunMcpAsync(CancellationToken ct)
    {
        await using var engine = new DeltaStudioEngine();
        var server = new McpServer(engine);
        // logs to stderr so stdout stays a clean JSON-RPC channel
        Console.Error.WriteLine("deltastudio-mcp: stdio transport ready; engine headless.");
        await server.RunStdioAsync(ct);
        return 0;
    }

    private static async Task<int> NewAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: deltastudio new <dir> <name> <modelId>");
            return 2;
        }

        if (!DvpModelCatalog.TryGet(args[3], out _) )
        {
            Console.Error.WriteLine($"unknown model '{args[3]}'; see 'deltastudio models'");
            return 2;
        }

        await ProjectFileIO.CreateAsync(args[1], args[2], args[3]);
        Console.WriteLine($"created {args[1]} (model {args[3]})");
        return 0;
    }

    private static async Task<int> ValidateAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: deltastudio validate <dir>");
            return 2;
        }

        await using var engine = new DeltaStudioEngine();
        await engine.Workspace.OpenAsync(args[1]);
        var report = engine.Validate();
        Console.WriteLine(report.ToText());
        Console.WriteLine(report.HasErrors ? "RESULT: BLOCKED (errors)" : "RESULT: OK");
        return report.HasErrors ? 1 : 0;
    }

    private static async Task<int> CompileAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: deltastudio compile <dir>");
            return 2;
        }

        await using var engine = new DeltaStudioEngine();
        await engine.Workspace.OpenAsync(args[1]);
        var result = engine.Compile();
        Console.WriteLine(result.Listing);
        Console.WriteLine(result.Report.ToText());
        Console.WriteLine("binaryEmittable=" + result.BinaryEmittable);
        if (!result.BinaryEmittable)
        {
            Console.WriteLine("blocked: " + result.BinaryBlockedReason);
        }

        return result.Report.HasErrors ? 1 : 0;
    }

    private static async Task<int> InfoAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: deltastudio info <dir>");
            return 2;
        }

        PlcProject p = await ProjectFileIO.OpenAsync(args[1]);
        Console.WriteLine($"name        : {p.Metadata.Name}");
        Console.WriteLine($"model       : {p.Target.ModelId}");
        Console.WriteLine($"rungs       : {p.Program.Main.Count}");
        Console.WriteLine($"symbols     : {p.Symbols.Count}");
        Console.WriteLine($"createdUtc  : {p.Metadata.CreatedUtc:O}");
        Console.WriteLine($"modifiedUtc : {p.Metadata.ModifiedUtc:O}");
        Console.WriteLine($"description : {p.Metadata.Description}");
        return 0;
    }

    private static async Task<int> IlAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: deltastudio il <dir>");
            return 2;
        }

        PlcProject p = await ProjectFileIO.OpenAsync(args[1]);
        Console.Write(new IlFormatter().Format(p.Program));
        return 0;
    }

    private static int ListModels()
    {
        foreach (var m in DvpModelCatalog.All.Values.OrderBy(m => m.Family).ThenBy(m => m.Id))
        {
            Console.WriteLine($"{m.Id,-16} {m.Family,-8} steps={m.ProgramCapacitySteps,-6} outputs={m.OutputType}");
        }

        return 0;
    }

    private static int Usage()
    {
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            deltastudio — headless Delta DVP engineering tool + MCP server
              mcp-stdio                 serve MCP over stdin/stdout (for AI agents)
              new <dir> <name> <model>  create project
              validate <dir>            run the validator
              compile <dir>             validation + symbolic listing
              info <dir> / il <dir>     inspect a project
              models                    supported CPU catalog
            """);
    }
}
