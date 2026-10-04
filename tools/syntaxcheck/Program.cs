using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

int bad = 0, files = 0;
foreach (string dir in args)
{
    foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
        if (file.Contains("/obj/") || file.Contains("/bin/")) continue;
        files++;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.CSharp12));
        foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            bad++;
            Console.WriteLine($"{Path.GetRelativePath(dir, file)}: {d}");
        }
    }
}
Console.WriteLine($"parsed {files} files, {bad} syntax errors");
return bad == 0 ? 0 : 1;
