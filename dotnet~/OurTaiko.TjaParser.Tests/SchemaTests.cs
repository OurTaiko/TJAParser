using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using OurTaiko;
using Xunit;

namespace OurTaiko.Tests;

// I1-3 (Documentation~/Parser-TODO.md): TjaSchema is the one list of commands and headers. The
// parser dispatches by it (checked when the parser starts), and the specification's command
// table is generated from it:
//
//   TJA_UPDATE_SPEC=1 dotnet test --filter SchemaTests
public class SchemaTests
{
    static string SpecPath([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", "..", "Documentation~", "TJA-Specification.md"));

    const string Begin = "<!-- schema:commands -->", End = "<!-- /schema:commands -->";

    static string Scope(TjaScope scope) => scope switch
    {
        TjaScope.Body => "块内", TjaScope.Outside => "块外", TjaScope.Outside | TjaScope.Body => "块外或块内", _ => "头部",
    };

    static string Origin(TjaOrigin origin) => origin switch { TjaOrigin.Tja => "TJA", TjaOrigin.OpenTaiko => "OpenTaiko", _ => "本库扩展" };

    static string OnError(TjaOnError e) => e switch
    {
        TjaOnError.FailsBlock => "块解析失败", TjaOnError.Dropped => "警告并忽略", TjaOnError.Default => "用默认值", _ => "—",
    };

    static string Parameter(TjaParameter p)
    {
        string text = p.Values.Count > 0 ? string.Join("｜", p.Values) : p.Name;
        if (p.Unit != null) text += "（" + p.Unit + "）";
        return p.Optional ? "[" + text + "]" : text;
    }

    internal static string CommandTable()
    {
        var sb = new StringBuilder();
        sb.Append("| 指令 | 参数 | 写在 | 来源 | 进哈希 | 读不出时 | 见 |\n|---|---|---|---|---|---|---|\n");
        foreach (var k in TjaSchema.Commands)
            sb.Append($"| `{k.Name}` | {(k.Parameters.Count == 0 ? "—" : string.Join(" ", k.Parameters.Select(p => "`" + Parameter(p) + "`")))} | {Scope(k.Scope)} | {Origin(k.Origin)} | {(k.InHash ? "是" : "否")} | {OnError(k.OnError)} | {k.Section} |\n");
        return sb.ToString();
    }

    [Fact]
    public void TheSpecificationsCommandTableIsTheSchema()
    {
        string spec = File.ReadAllText(SpecPath());
        int begin = spec.IndexOf(Begin, StringComparison.Ordinal) + Begin.Length, end = spec.IndexOf(End, StringComparison.Ordinal);
        string table = "\n" + CommandTable();
        if (Environment.GetEnvironmentVariable("TJA_UPDATE_SPEC") == "1")
        {
            File.WriteAllText(SpecPath(), spec.Substring(0, begin) + table + spec.Substring(end));
            return;
        }
        Assert.Equal(table, spec.Substring(begin, end - begin));
    }

    [Fact]
    public void TheSchemaAndTheSpecificationNameTheSameThings()
    {
        string spec = File.ReadAllText(SpecPath());
        // Every section the schema points to is there.
        foreach (var k in TjaSchema.Commands.Concat(TjaSchema.Headers))
            Assert.Matches(new Regex("(?m)^### " + Regex.Escape(k.Section) + " "), spec);
        // Every command a heading of section 6 names is in the schema, under that section.
        foreach (Match heading in Regex.Matches(spec, @"(?m)^### (6\.\d+) (.*)$"))
            foreach (Match name in Regex.Matches(heading.Groups[2].Value, @"#[A-Z_]+"))
            {
                var k = TjaSchema.Find(name.Value);
                Assert.True(k != null, name.Value + " is not in TjaSchema");
                if (k.Name.StartsWith("#BRANCH") || k.Name is "#N" or "#E" or "#M") continue; // section 6.8 points on to 8
                Assert.Equal(heading.Groups[1].Value, k.Section);
            }
        // The keys of the header table (2.2) are the schema's headers.
        string table = spec.Substring(spec.IndexOf("### 2.2", StringComparison.Ordinal));
        table = table.Substring(0, table.IndexOf("### 2.3", StringComparison.Ordinal));
        var written = Regex.Matches(table, @"(?m)^\| ([^|]*) \|").Select(m => m.Groups[1].Value)
            .SelectMany(cell => Regex.Matches(cell, "`([A-Z][A-Z0-9_-]*)`").Select(m => m.Groups[1].Value)).ToHashSet();
        var headers = TjaSchema.Headers.Select(h => h.Name).ToHashSet();
        Assert.Empty(written.Where(w => !headers.Contains(w)));
        // SUBTITLE's languages are written as "SUBTITLE + the same suffixes", NOTESDESIGNER1–3 as a range.
        Assert.Empty(headers.Where(h => !written.Contains(h) && !h.StartsWith("SUBTITLE") && !Regex.IsMatch(h, "^NOTESDESIGNER[1-3]$")));
    }

    [Fact]
    public void FindIgnoresCase()
    {
        Assert.Equal("#BPMCHANGE", TjaSchema.Find("#bpmchange").Name);
        Assert.Equal("BALLOONEXP", TjaSchema.Find("balloonexp").Name);
        Assert.Null(TjaSchema.Find("#FOO"));
    }
}
