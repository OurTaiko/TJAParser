# 编辑器接口说明

写 TJA 编辑器（IDE）时用到的库接口。命名空间都是 `OurTaiko`，源码在 `Runtime/`（netstandard2.1、C# 9）。游戏与服务器用的 `TjaParser.Parse`、`SongInfo`、`ChartHash` 不受这些接口影响。计划见 [`IDE-Plan.md`](IDE-Plan.md)。

## 约定

- **位置**：`TextSpan { Start, Length, End }`，是传入文本的 UTF-16 偏移（BOM 也算一个字符）。
- **行列**：`TjaSourceText`（`TjaDocument.Lines`）。行从 1 起，列从 0 起；换行规则与解析器相同：CRLF、单独的 CR、LF。
- **快照**：`TjaDocument`、`TjaAnalysis` 一旦生成就不再变化，可以多线程同时读。约定读取方也不改 `TaikoChart`。
- **取消**：耗时的接口都接受 `CancellationToken`，取消时抛 `OperationCanceledException`，不会留下半成品。
- **块**：一个 `#START … #END`。用 `(course, side)` 指定：`course` 是 COURSE 的原文（`TjaDocumentBlock.Course`），`side` 为 `single`／`p1`／`p2`。

## 最小示例

```csharp
using OurTaiko;

var doc = new TjaDocument(File.ReadAllText(path), version: 0);

// 用户输入：在 offset 处把 length 个字符替换成 text，得到下一版
doc = doc.WithChange(offset, length, text, version: doc.Version + 1);

// 后台线程：整份诊断（各块合并，含编辑器检查）
var diagnostics = doc.Diagnostics(cancel);
foreach (var d in diagnostics)
{
    var (line, column) = doc.Lines.Position(d.Span.Start);
    Console.WriteLine($"{d.Severity} {d.Code} {line}:{column} {d.MessageIn("zh")}");
}

// 某一块的分析：谱面、源码映射、小节、某处状态
var block = doc.BlockAt(caretOffset);
if (block != null)
{
    var analysis = doc.Analyze(block.Course, block.Side, cancel);
    double time = analysis.StateAt(caretOffset)?.Time ?? 0;          // 光标 → 预览时刻
    var note = analysis.NoteNear(clickTime, BranchRoute.Master);      // 预览点击 → 音符
    if (note != null && analysis.SourceMap.TryGetSpan(note, out var span)) { /* 选中 span */ }
}
```

## 文档与分析

| 类型／成员 | 说明 |
|---|---|
| `new TjaDocument(text, version)` | 一份文本的快照 |
| `WithText(text, version)`、`WithChange(start, length, text, version)` | 得到下一版。改动没碰到的块会复用上一版的分析（只挪位置，结果与重读相同） |
| `Blocks`、`BlockAt(offset)` | 每个 `#START`（`TjaDocumentBlock`：`Course`、`Side`、`Line`、`StartSpan`、`EndSpan`、`Contains`）；某位置所在的块 |
| `Analyze(course, side, cancel)` | 某块的 `TjaAnalysis`，按需计算并缓存 |
| `Diagnostics(cancel)` | 全文诊断：各块的诊断去重合并，加上编辑器检查 TJA3xxx |
| `Syntax`、`Lines` | 无损语法 token、行列表 |
| `TjaParser.Analyze(text, course, side, cancel)` | 不经文档、直接分析一块 |

`TjaAnalysis` 的成员：

- `Chart`：谱面（`TaikoChart`）。出错时按恢复规则继续读；没有该块时为 null。
- `Diagnostics`、`HasErrors`、`Version`、`StartSpan`／`EndSpan`。
- `SourceMap`（`ChartSourceMap`）：
  - `TryGetSpan(obj, out span)`：音符、小节线（得到整个小节）、`ChartTempo`、`ChartGogo`、`ChartSection`、`ChartJudgeMove`、`ChartLaneAlpha`、`ChartBranch` 在源码中的位置；
  - `TryGetTailSpan(note, out span)`：连打尾的位置；
  - `NoteAt(offset, out tail)`：某位置上的音符。
- `Measures`：小节表（`ChartMeasure`：`Index`、`Span`、`StartTime`、`EndTime`、`Bpm`、`Length`、`Slots`、`BranchId`、`Route`、`Bar`）。`Played(route)` 给出某条路线实际经过的小节。
- `StateAt(offset, cancel)`：该处生效的状态（`TjaPositionState`）：
  - `Time`、`Beat`、`Bpm`、`MeasureLength`、`ScrollX`／`ScrollY`／`ScrollMode`；
  - `Barline`、`Gogo`、`SuddenShow`／`SuddenMove`；
  - `Measure`、`Slot`／`Slots`、`BranchId`、`Route`、`OpenRoll`；
  - 下一个气球：`BalloonList`、`BalloonIndex`、`NextBalloonHits`、`BalloonListed`。
  
  每次调用约等于重读一次该块。
- `NoteNear(time, route)`：某路线上离该时刻最近的音符。

谱面对象见 `Runtime/TaikoChart.cs`。`ChartNote.Time` 是判定时刻（秒，已含 OFFSET），`Kind`、`EndTime`、`BalloonHits`、`BranchId`／`Route`、`Moji` 等都在上面。

## 诊断

- `TjaDiagnostic` 的字段：`Code`、`Severity`（`Error`／`Warning`／`Information`）、`Message`（英文）、`MessageIn("zh" | "ja" | 其它=英文)`、`Args`、`Line`、`Span`、`Kind`。
- 所有种类在 `TjaDiagnosticKind.All`，按编号分三段：
  - TJA1xxx：解析器的错误；
  - TJA2xxx：解析器的警告；
  - TJA3xxx：编辑器检查，只出现在 `TjaDocument.Diagnostics` 里。
  
  `FromParser` 区分前两段与 TJA3xxx。

## 语言功能

| 功能 | 接口 | 返回 |
|---|---|---|
| 语法高亮 | `TjaSyntax.Tokenize(text)` 或 `doc.Syntax` | `TjaSyntaxToken { Kind, Span, Note }`；所有 token 拼起来就是原文 |
| 补全 | `TjaCompletion.At(doc, offset, cancel)` | `TjaCompletionItem { Label, Kind, Detail, Insert, Replace }`：用 `Insert` 替换 `Replace` 范围 |
| 悬停 | `TjaHover.At(doc, offset, cancel)` | `TjaHoverInfo { Span, Markdown, Note, Tail, Measure, Slot, Keyword, … }`；无内容时为 null |
| 参数提示 | `TjaSignatureHelp.At(doc, offset)` | `TjaSignature { Keyword, Label, Parameters (Start, Length), Active }` |
| 折叠 | `TjaFolding.Ranges(doc)` | `TjaFoldingRange { StartLine, EndLine, Kind }`（Block／Branch／Route／Comment） |
| 行内提示 | `TjaSummary.Hints(doc, within, cancel)` | `TjaInlayHint { Offset, Label }`：小节号、时刻、BPM 变化 |
| 块摘要 | `TjaSummary.Blocks(doc, cancel)` | `TjaBlockSummary { Block, Hash, Duration, Notes, Combo, PeakPerSecond, Title }`，按路线给出 |
| 快速修复 | `TjaQuickFix.For(doc, diagnostic)`、`TjaQuickFix.InlineBalloons(doc, course, side)` | `TjaCodeAction { Title, Edits, Fixes }` |
| 格式化 | `TjaFormat.Edits(doc, resolution = 0)` | `List<TjaTextEdit>`；保证每块哈希不变 |
| 拆分／合并小节 | `TjaRefactor.Split(doc, course, side, offset)`、`TjaRefactor.Join(...)` | `TjaCodeAction`；不能保证除小节线外完全不变时返回 null |
| 指令与头部资料 | `TjaSchema.Commands`／`Headers`／`Find(name)` | `TjaKeyword { Name, Scope, Parameters, Origin, InHash, OnError, Section, Summary }` |

修改都以 `TjaTextEdit { Span, NewText }` 返回。`TjaTextEdit.Apply(text, edits)` 会套用一组互不重叠的修改。编辑器应把它们作为一次可撤销的操作，再用 `WithChange` 或 `WithText` 生成新版本。

## 语言服务（另一种接法）

不用 C# 的编辑器可以通过 LSP 接入 `tja-language-server`（`dotnet~/OurTaiko.TjaParser.LanguageServer`，标准输入输出）。

- 支持的 LSP 方法：诊断、`completion`、`hover`、`signatureHelp`、`foldingRange`、`semanticTokens/full`、`inlayHint`、`codeLens`、`codeAction`、`formatting`（`options.tjaResolution`）。
- 自定义请求：
  - `tja/timeAt`：参数 `textDocument`、`position`，返回该处所在的块、时刻、小节与路线；
  - `tja/noteAt`：参数 `textDocument`、`course`、`side`、`time`、`route`，返回最近音符的范围。
- `initialize` 的 `locale` 决定诊断用哪种语言；`initializationOptions.debounceMs` 设定防抖时间。

## 性能参考

大型生成谱（约 90 KB，5 个难度）上的实测：

- 每次按键重读一块：约 4 ms、5 MB；
- `StateAt`：约 2 ms；
- 全文语法 token：约 5 ms。

详见 `Parser-TODO.md` 的「编辑器性能预算」。分配预算由测试 `BudgetTests` 把关。
