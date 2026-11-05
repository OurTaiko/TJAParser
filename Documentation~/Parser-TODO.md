# TJAParser TODO

两部分：**一、谱面 IDE 支持**（待做，按优先级排列）；**二、解析器自身优化**（已完成，留作记录）。做完一项把状态从 ⬜ 改为 ✅，并在「记录」里写上提交或说明；➖ 表示评估后不做，理由写在「记录」里。

# 一、谱面 IDE 支持

目标：让解析器能支撑一个谱面 IDE——带位置的诊断、出错后继续解析、源码与谱面对象互查、补全与悬停所需的上下文，再在其上做 LSP 服务。

- **I0**：地基。后面所有功能都依赖它，先做。
- **I1**：IDE 能实时用起来所需的结构与性能。
- **I2**：IntelliSense 功能（LSP）。
- **I3**：编辑能力与长期优化。

**通用约束**

- 游戏与两站用的旧 API（`TjaParser.Parse`、`SongInfo.Read`、`ChartHash`、CLI）行为不变：golden（`GoldenTests`）与全量谱库 diff（`dotnet~/tools/diff-corpus.sh`）0 差异；新增的位置、诊断、小节表等**不进规范文本**，`ChartCanonical.Version` 不变。
- `Runtime/` 仍是 netstandard2.1、C# 9、不引用 UnityEngine；IDE 才需要的东西（位置表、快照）按需开启，游戏加载谱面时不为它付出分配。
- 每项都用基准工程（`dotnet~/OurTaiko.TjaParser.Benchmarks`）确认旧路径的耗时与分配没有回退。

**已有基础**：警告已带行号（P3-4）；读行不再复制全文（P2-1）；行只读一次并在各块间共用（P2-2）；每条指令一个方法（P2-5）；golden、fuzz、等价测试与基准都已就位（P0-1～P0-3）。

## I0：地基

| 状态 | 编号 | 事项 | 依赖 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | I0-1 | 列位置：token 只带行号，不知道在行内哪一列 | — | `LineReader` 给每行记下它在原文中的起点；无注释的行（快速路径）列号直接可得，有注释的行记下保留片段到原文的偏移映射；token 带 `(行, 列, 长度)` | 随机文本上每个 token 的位置切回原文正好是该 token（含注释、BOM、CRLF、Tab）；旧路径分配不增加 | `TextSpan`（原文 UTF-16 偏移）与 `TjaSourceText`（行从 1、列从 0，换行规则同 TjaLines）；`TjaLines.Read(text, positions)` 只在要位置时记下每行在原文中的起点，有注释的行另记片段映射；解析器合并头部与读谱两遍为 `ChartParser`，待处理小节改存 token 下标，要位置时每个 token 带 span。`TokenPositionTests`：fixture、生成谱、变异谱与 1500 份随机拼接文本（注释、CRLF/CR、BOM、Tab、全角空格），每个 token 的 span 切回原文即该 token，且带位置与否 token 完全相同；故意把映射错一位时测试失败。golden 不变；游戏路径分配不增（Parse Large 1888→1886 KB） |
| ✅ | I0-2 | 结构化诊断：警告是字符串、错误是异常，都没有编号和范围 | I0-1 | `TjaDiagnostic {Code, Severity, Span, MessageKey, Args}`；现有每种警告与错误各配一个稳定编号；`TaikoChart.Warnings` 的文字（含 P3-4 的行号格式）与异常由诊断派生 | golden 0 差异；每个诊断编号都有测试；诊断的 Span 落在出问题的 token 上 | `TjaDiagnosticKind`：16 种错误（TJA1001–1016）与 16 种警告（TJA2001–2016），各带英文消息模板；`TjaDiagnostic` 带种类、消息、参数、行与 span。解析器所有警告与错误都经 `Warn(kind,…)`／`Error(kind,…)`：`Warnings` 的文字与抛出的异常由同一模板生成（golden 不变）；分析时另记每一次出现的位置（`Warnings` 仍每种文字一条）。头部值的问题落在值上，块尾的问题落在 `#END` 或结束块的那一行。`DiagnosticTests` 每个编号一例：span 切出的正是出错的 token，旧 API 给出同样的消息与异常类型，并检查目录里每个编号都有用例。此时分析遇到错误仍会停下，恢复在 I0-3 |
| ✅ | I0-3 | 出错后继续解析：第一个错误就让整块失败 | I0-2 | 新入口（如 `TjaParser.Analyze`）：Error 级问题丢弃出错的 token／指令并记诊断，继续读完，返回「部分谱面 + 全部诊断」；逐类定恢复策略（未闭合 `[`、不支持的音符、非法 BPMCHANGE/MEASURE、分歧结构错误、分歧边界前缺逗号）。旧 API 遇到第一个 Error 照旧抛同样的异常 | fuzz：新入口对任意输入不抛异常；旧 API golden 0 差异；同一错误在新旧入口给出相同的消息 | 公开 `TjaParser.Analyze(text, course, side)` → `TjaAnalysis`（`Chart`、`Diagnostics`、`HasErrors`、`Lines`）。分析时 `Error` 只记诊断不抛异常，各类错误的恢复：头部值读不出用默认（BPM 120、OFFSET 0、LEVEL 0），BPM 非正改 120；不认识的音符当空白占格；非气球上的打数或读不出的打数忽略；坏的 `#BPMCHANGE`／`#MEASURE` 丢弃；未闭合 `[` 丢掉该行余下部分，没有音符的 `[…]` 丢掉；分歧边界前缺逗号就在此结束小节；分歧外或重复的路线标记忽略；没有该块或文本为空时 `Chart` 为 null。旧 API 照旧抛同样的异常（golden 不变）。`RecoveryTests`：golden 语料与 2000 份变异谱 × 5 个难度，`Analyze` 从不抛异常；`Parse` 成功时哈希与警告一致，失败时第一个错误诊断与异常的消息、类型一致（两边各 2000 例以上）；每种恢复各一个用例 |
| ✅ | I0-4 | SourceMap：谱面对象不知道自己来自源码哪里 | I0-1 | 音符、小节线、指令事件（BPM、SCROLL、GOGO、分歧…）记录源 token 的 Span（旁表，或 `ChartNote` 上一个不进规范文本的索引）；提供 `NoteAt(position)`、`SpanOf(note)` | 双向查找在全部 fixture 与生成谱上互逆；哈希不变 | `TjaAnalysis.SourceMap`（`ChartSourceMap`）：音符→其字符（气球连内联打数）、连打尾→结束它的 8（或结束它的音符、`#END`）、小节线→其小节（首个音符到逗号）、`ChartTempo`／`ChartGogo`／`ChartSection`／`ChartJudgeMove`／`ChartLaneAlpha`／`ChartBranch`→其指令行；`TryGetSpan`、`TryGetTailSpan`、`NoteAt(offset, out tail)`（同一位置上音符优先于它结束的连打尾）。只在分析时记录，不进规范文本。`SourceMapTests`：golden 语料与 300 份变异谱的每个难度，每个音符的 span 切出的正是其种类的字符且 `NoteAt` 互逆，连打尾、小节、各类事件都指向对的位置（5 万个以上音符）。顺带发现并单独修复 P3-4 的问题：分歧指令前有待执行的指令时，分歧指令的警告带的是前一条指令的行号（提交 1ce777a） |
| ✅ | I0-5 | 小节表：解析过程中有小节概念但不对外暴露 | I0-4 | `ChartMeasure {Index, Span, StartTime, EndTime, Bpm, Measure, Slots, BranchId, Route}` | 小节数、时刻与 `Bars` 一致；每条路线各自成表 | `TjaAnalysis.Measures`（`ChartMeasure`：`Index`、`Span`、`StartTime`、`EndTime`、`Bpm`、`Length`、`Slots`、`BranchId`、`Route`、`Bar`，按书写顺序）与 `Played(route)`：某条路线实际经过的小节（共通段加各分歧经 `ResolveRoute` 选出的路线），按时间排序。`MeasureTests`：golden 语料与 200 份变异谱的每个难度，小节与小节线一一对应、时刻与 span 一致；三条路线各自经过的小节恰是该路线 NoteLists 里的小节线；每个音符落在同一分歧路线的小节里（1 万个以上小节） |
| ✅ | I0-6 | 位置上的解析状态：补全与悬停需要「此处的 BPM、SCROLL、拍号、所在分歧与路线、开着的连打、下一个气球序号」 | I0-5 | 每小节开头存一份 `TimingState` 快照（只在 IDE 入口开启）；`StateAt(position)` 从最近快照向后推进到该位置 | 任意位置的状态与从头解析到该处一致；游戏路径不分配快照 | `TjaAnalysis.StateAt(offset)` → `TjaPositionState`（时刻、拍、BPM、拍长、SCROLL、滚动方式、小节线开关、GOGO、SUDDEN、小节与格、分歧与路线、开着的连打、下一个气球的列表/序号/打数）；`StartSpan`／`EndSpan` 给出块的范围，主体外返回 null。**做法与计划不同**：不存每小节快照，而是重读该块到该位置的第一个 token 前截下状态——按定义与从头解析一致，游戏路径不分配任何东西；代价是每次约一次块解析（Large 最后一块：Analyze 3.6 ms、StateAt 4.6 ms、Parse 1.9 ms，预热后），若 I1 的预算需要再加快照。`PositionStateTests`：golden 语料与 100 份变异谱中抽样 5000 个以上音符，其位置上的状态与该音符读到的时刻、拍、BPM、SCROLL、滚动方式、SUDDEN、分歧路线完全相同；另有 GOGO、连打、气球、格数、分歧与范围外的用例。I0 全部完成后游戏路径基准：Parse Large 1889 KB（I0 前 1888 KB），golden 不变 |

## I1：实时可用的结构与性能

| 状态 | 编号 | 事项 | 依赖 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | I1-1 | 文档模型与按块重算：每次按键都全量重算整个文件 | I0-3 | `TjaDocument`（文本 + 版本号）一次切好块，每块带生效的头部上下文；改动只落在某块主体内时只重算该块，头部改动重算全部块 | 增量结果与全量重解析逐对象一致（随机编辑序列测试） | `TjaDocument`：不可变快照（文本 + 版本），`WithText`／`WithChange` 得到下一版，`Analyze(course, side)` 按需分析并缓存，`Blocks` 列出每个 `#START`。复用不是按「改动落在哪个块」推断，而是逐行核对：块读到的每一行（其它块的主体除外）在新文本里逐字相同、读出的文字相同、且随改动整体挪动，才把旧分析的位置与行号整体平移（span 起止分别平移，多行小节能吸收中间空行的改动），警告由诊断重建；否则重新分析。另加保险：改动处（含前后一字）有 `/` 或 `*` 时不复用（故意去掉这两道保险测试仍通过，说明逐行核对已覆盖注释边界，保留作余量）。`DocumentTests`：40 份文档各 25 次随机编辑（多为改音符行，也有插入换行、`#END`、`COURSE:`、注释符、删除），每版每块与全新 `TjaParser.Analyze` 的结果逐项一致（图表、警告、诊断、小节、源码映射每个 span、块范围）；只在音符行里打字时每版恰好只重读被改的那一块（5–6 块的文档） |
| ✅ | I1-2 | 性能预算 | I1-1 | 基准加「改一个字符后重算」：Large 的单块重算目标 < 5 ms、分配 < 2 MB（数值先实测再定） | 预算写入本文并由基准跟踪 | 新增 `EditorBenchmarks`（Analyze、TypeInBlock、TypeAndReadAll、StateAt）与 `BudgetTests`（分配预算，超出即失败）；预算表见本部分末尾。实测后先做了三处优化：改动落在块自身范围内时直接重读、不再逐行核对；文档每版只读一次全部行与行首表、各块共用；分析只保留与本块有关的行（不再存其它块的主体）。Large：TypeInBlock 4.7 ms／5.5 MB → 4.1 ms／4.9 MB，TypeAndReadAll 11.6 ms／13.8 MB → 8.4 ms／10.6 MB，Analyze 3.9 → 3.5 MB。时间目标（每次按键 < 5 ms）达成；分配目标 < 2 MB 未采用，理由见预算表下 |
| ✅ | I1-3 | 指令与头部 Schema：指令知识散在 `Command` 的 switch、`SongInfo` 与规范文档里 | I0-2 | 一张表：名称、作用域（头部／块前／块内）、参数（类型、范围、可选、单位）、来源（TJA／OpenTaiko／本库扩展）、是否进哈希、文档 key；解析器的分发与参数校验改为查表（P2-5 已拆成按指令的方法） | golden 0 差异；规范文档的指令表由它生成并与现有文档一致 | `TjaSchema`（`Runtime/TjaSchema.cs`）：23 条指令与 44 个头部（17 个基本键、`NOTESDESIGNER0–4`，以及 TITLE/SUBTITLE 各 11 种语言后缀），每条有作用域、参数（类型、可选、单位、可选值）、来源（TJA／OpenTaiko／本库扩展）、是否进哈希、读不出时的结果、规范小节与一句说明；`TjaSchema.Find` 不分大小写。解析器的块内指令改为按名查表分发，表在首次使用时与 Schema 双向核对（缺一个或多一个都直接报错）。**与计划不同**：参数校验仍在各指令自己的方法里——SCROLL 的复数、JPOSSCROLL 的 inf、LANE_ALPHA 的 %、SUDDEN 的阈值都有 OpenTaiko 兼容细节，改成通用校验会改变行为；表只描述参数，供 IDE 与文档使用。规范 §6 新增由 Schema 生成的指令总表（`SchemaTests` 保证一致，`TJA_UPDATE_SPEC=1` 重新生成），并核对 Schema 引用的每个小节存在、§6 标题里的指令都在 Schema 中、§2.2 头部表的键与 Schema 一致。golden 不变 |
| ✅ | I1-4 | 后台解析与取消 | I1-1 | 解析接受 `CancellationToken`；结果是带版本号的只读快照，约定 `TaikoChart` 解析完成后不再修改 | 取消不留下半成品状态；并发读快照的测试 | `TjaParser.Analyze`、`TjaAnalysis.StateAt`、`TjaDocument.Analyze` 接受 `CancellationToken`（每 256 行／token 检查一次），取消时抛 `OperationCanceledException`、不返回半成品。`TjaDocument.Analyze` 在锁外计算、只把完整结果存入缓存，同一块被两个线程同时请求时各算一次、保留先存入的那份给双方；文档共用的行表用 `LazyInitializer`。`TjaAnalysis.Version` 标明所属文档版本；分析做完时源码映射即排好序（`Seal`），读取不再改动任何东西；约定 `Chart` 生成后不再修改。`ConcurrencyTests`：预先取消的读取在两个入口都抛异常且文档不缓存任何东西，之后的读取完整且与全新分析一致；在随机时刻（0–30 ms）取消 30 次，取消与完成两种结局都出现（探针：7 次取消、23 次完成），之后文档里都是完整的分析；8 个线程同时读同一文档的不同块（含 `NoteAt`、`StateAt`），每块只有一份分析、与顺序分析一致；旧版本在生成 8 个新版本后完全不变。连续运行 5 次均通过 |
| ✅ | I1-5 | 无损语法层：注释与空白在读行时就丢掉了 | I0-1 | 语法高亮、折叠与格式化用的 token 流保留注释、空白与换行（只在 IDE 入口生成） | 全部 token 拼回去与原文逐字节相同 | `TjaSyntax.Tokenize(text)` 与 `TjaDocument.Syntax`（每版一次，只在编辑器入口生成）：全文每个字符属于且只属于一个 token——BOM、空白、换行（CRLF 算一个）、注释（跨行的块注释每行一段）、头部键／冒号／值、指令名与参数（含 `#START`、`#END`）、音符（带 `NoteKind`）、0、8、内联打数、逗号、音符行里的未知字符、块外其它文字。注释与换行按 LineReader 的同一规则识别，每行的种类直接取自 `TjaLines`，所以与解析器读到的一致。`SyntaxTests`：golden 语料与 3000 份随机拼接文本（BOM、CRLF/CR、行内与跨行注释、全角空格、内联打数）拼回即原文、token 连续且非空；1200 份文本上解析器的每个 token（10 万个以上）与语法 token 的分类一致（唯一差别：音符行中间的 `#` 解析器当成单独的指令 token，语法上记为该行的未知字符），每个头部的键与值位置正确；另有一行逐 token 的用例 |

## I2：IntelliSense（LSP 服务）

| 状态 | 编号 | 事项 | 依赖 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | I2-1 | LSP 服务骨架 | I1-1、I1-4 | 新工程 `dotnet~/OurTaiko.TjaParser.LanguageServer`：文档同步、按版本产出快照、取消；VS Code 与自研编辑器（Monaco + monaco-languageclient）共用 | 在 VS Code 打开 .tja 能连上并收到诊断 | 新工程 `dotnet~/OurTaiko.TjaParser.LanguageServer`（`tja-language-server`，不依赖第三方 LSP 库，按 `Content-Length` 分帧的 JSON-RPC 自己实现）：生命周期（initialize／shutdown／exit 退出码）、文档同步（全量与增量，UTF-16 位置）、每个 URI 一份 `TjaDocument` 版本链、改动后在后台读诊断并按版本发布（防抖默认 150 ms，新改动取消旧的读取）、请求在后台执行并支持 `$/cancelRequest`、未知方法回 -32601。库里新增 `TjaDocument.Diagnostics`（各块诊断合并，多块共有的只报一次）。VS Code 客户端在 `Editors~/vscode`（package.json、extension.js、注释配置）。**验收方式与计划不同**：这里没有 VS Code，改为启动真实服务进程经标准输入输出走完整流程——打开文档收到带正确行列的诊断、两次增量修改后收到新版本的诊断、shutdown＋exit 退出码 0；另用独立的 Python 客户端对发布出的服务做了一次握手。扩展只验证了 JS 语法、JSON 与依赖包的接口（`npm install` 成功，`LanguageClient`／`start`／`stop` 存在），**未在 VS Code 中实际运行** |
| ✅ | I2-2 | 诊断推送，并补 IDE 需要的新诊断 | I0-2、I2-1 | 推送 I0-2 的诊断；新增：`LEVEL` 不在 1–10、未知指令提示相近的指令名、小节格数不是常见分音、分歧各路线长度不一致 | 每个新诊断有测试；旧 API 的警告不因此改变（新诊断只出现在 IDE 入口，或单独评估是否进 `Warnings`） | `TjaDocument.Diagnostics` 推送各块的 TJA1xxx／2xxx，另加编辑器自己的检查 `TjaLint`（TJA3xxx，新增严重度 `Information`）：TJA3004 `LEVEL` 缺失或不在 1–10；TJA3005 未知指令与已知指令相差不超过两个字符时提示「Did you mean …」；TJA3006 小节格数不是 2 与 3 的组合——**按「格数 ÷ 拍长」判断**（31/16 小节里的 31 格是十六分音符），且只看含有可打音符的小节（只放连打与连打尾的小节不提示）；TJA3007 同一分歧各路线结束时刻不同。另补了计划外的三项 SongInfo 拒收整个文件的原因（TJA3001 不支持的 COURSE、TJA3002 同一难度同一侧写了两次、TJA3003 `#START` 后不是 P1/P2），单块分析看不到、但编辑器必须显示。这些检查不进 `Warnings`、不影响 `Parse`，游戏与两站都不受影响。`LintTests` 每种一例（位置、消息）并检查 `Parse` 不报它们；第一版的格数规则在 Donkama2000 与 IrohaUta 上误报 25 条，修正后只剩 IrohaUta 里一个确实少见的 1/40 小节 |
| ✅ | I2-3 | 补全 | I0-6、I1-3 | 块外补头部名与取值（COURSE、STYLE、LEVEL…）；块内 `#` 后按上下文补指令（`#N/#E/#M` 只在分歧内，`#BRANCHEND` 只在分歧打开时）；`#BRANCHSTART` 后补条件类型；音符字符 | 每种上下文有测试 | `TjaCompletion.At(document, offset)`（库里，不依赖 LSP）与服务的 `textDocument/completion`（触发字符 `#`、`:`、空格）。按光标所在行之前的文字与位置判断：块外补头部名（插入 `NAME:`）、`COURSE:`／`STYLE:`／`LEVEL:` 的取值、块外可写的指令（`#START` 与三种滚动方式）、`#START` 后的 P1/P2；块内 `#` 后按 Schema 补块内指令，`#N`／`#E`／`#M`／`#BRANCHEND` 只在分歧里出现（取光标处的 `StateAt`）；`#BRANCHSTART` 后、第一个逗号前补条件类型及含义；音符行补音符字符与名称，空行同时补音符与指令；注释里不补。每项带替换范围（已输入的部分）。文档块新增 `EndSpan`、`Contains` 与 `TjaDocument.BlockAt`。`CompletionTests` 覆盖以上每种情况、应用补全后的文本，以及经服务往返的 LSP 结果 |
| ✅ | I2-4 | 悬停 | I0-4、I0-6、I1-3 | 音符：时刻、小节:拍、BPM×SCROLL、音符文字、气球打数及其来自 BALLOON 的第几项、所在路线；指令与头部：Schema 中的文档 | fixture 上抽查结果与解析结果一致 | `TjaHover.At` 与服务的 `textDocument/hover`（Markdown）。音符：名称与音符文字、时刻（连打到结束）、第几小节第几格（拍位）、速度 BPM × SCROLL（含纵向与 BM/HB）、气球打数及来源——列表第几项、写在音符上，或「BALLOONEXP 没有第 5 项，所以取 5」（`TjaPositionState` 新增 `BalloonListed`）、所在分歧路线；连打尾显示所属连打与起止；指令与头部（光标在参数或值上也可）显示 Schema 的签名、说明、来源、是否影响游玩与规范小节。`TjaHoverInfo` 同时给出结构化结果供测试与其它编辑器使用。`HoverTests`：两份真实谱与两个示例中抽 157 个音符，悬停所得音符、时刻、所在小节与解析一致，格号与「源码里该音符之前的音符字符数」（由语法 token 独立数出）一致；气球五种来源情况；连打尾、指令参数、头部、空白与注释；服务往返。测试中发现预期错误：共通段的气球会让三条路线的序号一起前进（与 OpenTaiko 相同），所以路线里的气球可能落在列表之外——悬停据此如实说明 |
| ✅ | I2-5 | 参数提示（Signature help） | I1-3 | `#JPOSSCROLL`、`#SUDDEN`、`#BRANCHSTART`、`#LANE_ALPHA` 等多参数指令 | 光标在第 n 个参数上时高亮第 n 个 | `TjaSignatureHelp.At` 与服务的 `textDocument/signatureHelp`（空格、逗号触发）：光标在指令行、指令名与空白之后时给出 Schema 签名、每个参数在签名中的位置与当前参数。参数按空白分隔（已写完的词数），`#BRANCHSTART` 按逗号，只有一个参数的指令（如复数 `#SCROLL 1 + 2i`）始终是该参数，多写的参数停在最后一个。`SignatureHelpTests`：14 种光标位置（含 JPOSSCROLL 三个参数、BRANCHSTART 逗号、LANE_ALPHA 可选参数、小写指令名、多余参数）高亮正确；无参数指令、未打空格、音符行、未知指令、注释中不给提示；Schema 中每条带参数的指令的每个参数都能在签名里找到；服务往返 |
| ✅ | I2-6 | 折叠与语义着色 | I1-5 | 折叠 `#START…#END`、分歧、各路线、`/* */`；咚、咔、大音符、连打、指令、参数分别着色 | 语义 token 覆盖全部字符且不重叠 | `TjaFolding.Ranges`（库）与服务的 `textDocument/foldingRange`：块（`#START` 到其结束，隐式结束时到结束它那一行之前）、分歧（`#BRANCHSTART` 到 `#BRANCHEND`，缺 `#BRANCHEND` 时随块结束）、各路线（到下一个路线标记或分歧结束之前）、跨行的 `/* */`（未闭合则到文末）。语义着色 `textDocument/semanticTokens/full` 由无损语法 token 生成：标准类型用于注释、头部键与值、指令、参数、内联打数、逗号；音符各有自定义类型（don、ka、bigDon、bigKa、roll、balloon、kadon、mine、adlib、blank、rollEnd、unknownNote），VS Code 扩展里用 `semanticTokenScopes` 与默认颜色规则（咚红、咔蓝、大音符加粗、连打黄……）对应——**颜色未在 VS Code 中看过**。`FoldingAndHighlightTests`：golden 语料 150 份与 2000 份随机文本，解码后的语义 token 按序、不重叠、每个都是一个完整的语法 token，未覆盖的字符只有空白、换行与 BOM；音符类型；两份文档的折叠范围逐项相符；服务往返 |
| ✅ | I2-7 | 预览与源码双向定位 | I0-4、I0-5 | 点预览中的音符跳到源码；光标移动时预览跟到对应时刻 | 往返定位回到同一对象 | 库：`TjaAnalysis.NoteNear(time, route)`——某路线玩家实际打的音符（共通段加各分歧经 `ResolveRoute` 的路线）中离该时刻最近的一个；光标处的时刻用 I0-6 的 `StateAt`。服务新增两个自定义请求：`tja/timeAt`（位置 → 所在块、时刻、小节、分歧与路线，供预览跟随光标）与 `tja/noteAt`（块 + 时刻 + 路线 → 最近音符在源码中的范围，供点击预览跳转）。`PreviewTests`：两份真实谱、分歧示例与 20 份生成谱中抽样 300 个以上音符，从源码位置到时刻再回到音符都回到同一个对象（同一时刻同一路线上有两个音符的情况跳过，二者本就在同一处）；路线间的选择；服务往返 |
| ✅ | I2-8 | Inlay hints 与 Code lens | I0-5 | 行首显示小节号、时刻、BPM 变化；每块显示音符数、最大连击、时长、密度峰值、哈希 | 与 `inspect` 输出一致 | `TjaSummary.Hints`／`TjaSummary.Blocks`（库）与服务的 `textDocument/inlayHint`、`textDocument/codeLens`。Inlay hint 在每个小节开头标「#小节号 时刻」，BPM 变化处另标「♩BPM」，只返回请求范围内的；Code lens 在每块 `#START` 行显示音符数（咚咔 1–4，与 inspect 同口径）、最大连击（另含 Kadon）、时长、1 秒内最多音符数、哈希，有分歧的块按 普通/玄人/达人 分列。`SummaryTests`：两份真实谱、两个示例与 20 份生成谱共 30 个以上的块，哈希与各路线音符数和真实 CLI `inspect` 的输出逐块一致（经入口点调用，与 `CliTests` 放进同一测试集合以免并行抢控制台）；小谱上的音符数、连击、峰值、时长与提示逐项核对；服务往返。全套测试连续三次通过 |

## I3：编辑能力与长期优化

| 状态 | 编号 | 事项 | 依赖 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | I3-1 | 快速修复 | I2-2 | 补逗号、补 `#END`、修正指令拼写、把 BALLOON 列表改成内联打数 | 修复后对应诊断消失，哈希符合预期 | `TjaQuickFix`（库，`TjaTextEdit`／`TjaCodeAction`）与服务的 `textDocument/codeAction`：TJA2003 最后一小节补逗号、TJA1007 分歧前补一行逗号、TJA2001 补 `#END`（文末无换行时先换行）、TJA3005 把指令名改成建议的名字；另有块级动作「把气球打数写到气球上」（`7` → `7[n]`，n 取当前打数）。`QuickFixTests`：前三种修复后诊断消失且谱面哈希与修复前（解析器容错读出的）相同；拼写修复后无诊断；气球改写后哈希不变、再次请求不再提供；服务往返 |
| ✅ | I3-2 | 格式化 | I1-5 | 指令统一大写；可选把每小节统一成固定分辨率 | 格式化前后哈希不变（规范文本保证这一点） | `TjaFormat.Edits(document, resolution)` 与服务的 `textDocument/formatting`（FormattingOptions 可带 `tjaResolution`）：指令名与头部键改大写；给出分辨率（每全音符格数）时，把「独占一行、只有音符与逗号」的小节改写为该分辨率下的格数（按拍长换算，如 3/4 拍 16 分辨率为 12 格），能放大就补 0、能缩小（音符都在粗网格上）才缩小，其余不动。每块的哈希都会复核，改写后哈希变了的块保留原样（不同格数累加浮点时可能差 1 ms）。`FormatTests`：golden 语料 300 份 × 分辨率 0／16／48，每份每个难度格式化前后哈希（或报错）相同、再格式化一次不再变化（100 次以上确有改动）；大小写与放大缩小的逐字用例 |
| ✅ | I3-3 | 重构 | I0-4、I1-5 | 插入／修改 `#BPMCHANGE` 并保持后续时刻不变；拆分、合并小节 | 重构前后除目标改动外哈希不变 | `TjaRefactor.Split`／`TjaRefactor.Join`，作为 `refactor.rewrite` 代码动作提供：在光标处的格把一个独占一行的音符小节拆成两个（各配 `#MEASURE`，之后恢复原拍长），或与下一个相邻、同路线的小节合并（两者的格按分数取最大公约格对齐，配一条 `#MEASURE` 再恢复下一个的拍长）。每个动作都先在结果上复核：规范文本除小节线及引用它的部分外完全相同、小节线恰好多一条或少一条，否则不提供（BM/HB 滚动下拍钟的浮点累加可能差末位，此时不提供）。**「插入／修改 #BPMCHANGE 并保持后续时刻不变」未做**：要连带改写其后直到下一次变速的每个小节的 `#MEASURE` 与 SCROLL（保持 BPM×SCROLL），BM/HB 下还牵涉拍钟，成本与风险高，留待需要时。`RefactorTests`：30 份生成谱每块抽小节拆分与合并，规范文本除小节线外不变；两个逐字用例（含 3/4 拍里八分与附点二分合并到八分网格）与不提供的情况 |
| ➖ | I3-4 | 行级增量词法 | I1-1 | 只重切改动的行，向后传播到行尾状态（是否在 `/* */` 内、是否在块内）一致为止 | 与全量结果一致（随机编辑序列） | ➖ 评估后不做。实测整份重扫 Large（90 KB、6.4 万个 token）5.3 ms、分配约 5 MB；它每个版本最多做一次，且只在编辑器要语义 token、悬停、补全时才做（`TjaDocument.Syntax` 惰性生成），在每次按键 5 ms 的量级内。行级增量要跨行维护注释状态、块归属与每行的种类（它们都依赖前面的行），复杂度与收益不相称；若以后需要，可先降分配（种类数组、token 列表），或在服务端加 `semanticTokens/full/delta` |
| ✅ | I3-5 | 诊断消息本地化 | I0-2 | 中／日／英；Schema 文档同步多语言 | 旧 API 的英文警告文字不变 | `TjaMessages`：全部 39 种诊断（TJA1xxx／2xxx／3xxx）的中文与日文模板，占位符与英文一致；`TjaDiagnostic.MessageIn(language)`（zh-*、ja，其余为英文；缺少 LEVEL 单独措辞）。服务按 initialize 的 `locale` 发布对应语言的诊断。`Parse` 的异常与 `Warnings` 仍是英文（golden 不变）。`MessageTests`：每种都有两种译文且占位符集合相同、按语言取文字、服务以 ja 发布。**Schema 文档（悬停、补全说明）未做多语言**，仍为英文，以节省用量；需要时可按同样方式补一张表 |
| ⬜ | I3-6 | Web 版 IDE 的运行方式 | I2-1 | 验证 netstandard2.1 库在 .NET WASM 中运行（若 IDE 做成网页），或经 LSP 远程服务 | 浏览器中解析 Large 的耗时记录在本文 | |

## 编辑器性能预算（I1-2）

`dotnet~/OurTaiko.TjaParser.Benchmarks` 的 `EditorBenchmarks`（时间）与测试 `BudgetTests`（分配，超出即失败）。Large 是生成谱（5 个难度 × 400 小节，约 90 KB），操作都针对最后一块；`TypeInBlock` 是在该块音符行里插一个字符后重读该块，`TypeAndReadAll` 是同样的编辑后取全部块（其余块被移动而非重读）。

| 操作 | Large 实测（耗时／分配） | 预算 | Donkama2000 实测 |
|---|---|---|---|
| `Analyze`（一块全新分析） | 3.0 ms ／ 3.4 MB | ≤ 4 MB | 0.25 ms ／ 0.46 MB |
| `TypeInBlock`（每次按键） | 4.1 ms ／ 4.8 MB | ≤ 5 ms、≤ 6 MB | 0.40 ms ／ 0.79 MB |
| `TypeAndReadAll` | 8.4 ms ／ 10.3 MB | ≤ 13 MB | 0.70 ms ／ 1.32 MB |
| `StateAt`（最后一个音符处） | 1.8 ms ／ 2.3 MB | ≤ 3 MB | 0.17 ms ／ 0.33 MB |

原计划的「分配 < 2 MB」没有采用：一块的全新分析本身就要约 3.4 MB（`Parse` 1.9 MB，另有位置、源码映射与小节表）。要再降，需要在块内做增量（只重读改动所在的小节之后），或把移动其余块时的源码映射改为按需平移；留待需要时再做。

# 二、解析器自身优化（已完成）

只涉及解析器本身（正确性、性能、结构）。

- **P0**：安全网与已知 bug。
- **P1**：热路径零风险优化，不改变行为。
- **P2**：结构性优化。
- **P3**：小问题与整洁。

**通用验收**：`KnownVectorTests`、`HashTests` 全部通过；差分测试（P0-2）0 差异，即哈希、警告与错误都不变（表中另有说明的除外）。

**版本规则**：下表没有一项改变已有块的规范文本，都不升 `ChartCanonical.Version`。P0-7、P3-1、P3-4 会改变部分块是否报错、`hasNotes` 或警告文本，需同步两站。

## P0：安全网与已知 bug

| 状态 | 编号 | 事项 | 位置 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | P0-1 | 基准测试工程 | 新建 `dotnet~/OurTaiko.TjaParser.Benchmarks` | BenchmarkDotNet 测 `SongInfo.Read`、`TjaParser.Parse`、`ChartHash.OfFile`；fixture 加大型分歧谱 | 输出耗时与分配字节数，基线记入本文末 | `cd dotnet~/OurTaiko.TjaParser.Benchmarks && dotnet run -c Release`；大型谱由 `dotnet~/Corpus/ChartCorpus.cs` 生成 |
| ✅ | P0-2 | 差分测试 | 测试工程 | 新旧版本对同一批谱面比较 `hash`、`warnings`、`error`；仓库内用 fixture 与生成谱，两站全量谱库用 CLI `inspect` 输出逐字比对 | 之后每项优化都跑 | `GoldenTests`：644 份谱（fixture、生成谱、变异谱）的完整解析结果摘要钉在 `Fixtures/corpus.golden`，有意改变行为时用 `TJA_UPDATE_GOLDEN=1` 重新生成；全量谱库用 `dotnet~/tools/diff-corpus.sh <基线ref> <谱面目录>` |
| ✅ | P0-3 | Fuzz 测试 | 测试工程 | 随机输入与随机变异谱面，断言只抛 `FormatException` / `NotSupportedException` | 能复现 P0-4 | `FuzzTests`：3000 份变异谱；修复前只发现 P0-4 的 `IndexOutOfRangeException`，修复后通过 |
| ✅ | P0-4 | `#MEASURE 4` 抛 `IndexOutOfRangeException` | `TjaParser.cs` `#MEASURE` | 缺 `/` 时抛 `FormatException` 并说明 | 有测试；规范 §14.2 删去 | 消息 `Invalid MEASURE (a/b): …`；golden 中只有 3 份含 `#MEASURE 4` 的变异谱改变 |
| ✅ | P0-5 | CLI `hash` 遇到被拒绝的文件时崩溃（退出码 134） | `Program.cs` `hash` | 与 `inspect` 一样捕获，返回 4 | 有测试；规范 §14.3 删去 | `CliTests` 经入口点调用命令；README 退出码说明同步 |
| ✅ | P0-6 | `Parse` 的 COURSE 匹配不把 `Ura` 视为 `Edit` | `TjaParser.cs` `Course` / `SameCourse` | 两边都经 `SongInfo.DifficultyOf` 归一再比较 | 传 `"Edit"` 能读到 `COURSE:Ura` 的块；`SongInfo.Blocks` 路径结果不变 | golden 中只有 5 份被 SongInfo 拒绝、含 `COURSE:Ura` 的变异谱改变（测试对它们直接按难度名解析） |
| ✅ | P0-7 | 别的难度里读不出的 `LEVEL`／`BPM`／`OFFSET` 让当前块失败 | `TjaParser.cs` 头部 switch | 与 `SongInfo` 统一：只有当前块生效的值读不出才报错 | 原本失败的块得到新哈希，已有哈希不变 | golden 中只有 1 份变异谱改变（第一个 COURSE 前的 `LEVEL:x` 不再使各块失败）；规范 §2.2、§14.1 同步 |
| ✅ | P0-8 | README「每个难度每个侧别只取第一块」与代码不符 | `README.md` | 改为「重复的难度与侧别拒绝整个文件」 | 规范 §14.5 删去 | README 同时更正了「按难度排序」「未知 COURSE 的块不列出」两处（实际为文件顺序、整个文件被拒绝）；`ChartHash.OfFile` 注释同步 |

## P1：热路径零风险优化

| 状态 | 编号 | 事项 | 位置 | 做法 | 记录 |
|---|---|---|---|---|---|
| ✅ | P1-1 | 每个音符调用 `Enum.IsDefined`（反射、装箱） | `TjaParser.cs` `Flush` | 静态 `bool[36]` 查表 | 静态表 `IsNoteKind`；golden 不变 |
| ✅ | P1-2 | 每个非空音符分配 2 个迭代器和 1 个 `HashSet` | `OpenRolls()` / `Routes()` | 直接对 3 条路线循环，局部去重 | 复用 3 格数组 `openHeads`，先取快照再逐个收尾（与原惰性迭代等价：收尾只清空同一音符的路线）；golden 不变 |
| ✅ | P1-3 | 每个 token（含音符）都 `Split` + `ToUpperInvariant` | `ChartReader.Read`、`Command` | 只对 `#` 开头的 token 求 key | `CommandKey` 只用于 `#` 开头的 token；分隔符数组改为静态 `Blanks`；golden 不变 |
| ✅ | P1-4 | 每个音符字符 `c.ToString()` | `NoteTokens` | 128 项单字符串缓存 | `AsciiStrings` 缓存；内联打数仍拼接成新串，不改缓存；golden 不变 |
| ✅ | P1-5 | 警告去重 `List.Contains`，O(W²) | `Warn` 及各处 `chart.Warnings.Contains` | 统一走 `Warn`，内部配 `HashSet` | `ChartReader` 内统一走 `Warn`（`HashSet` 以已有警告为初值）；头部 BALLOON 的少量警告仍用 `Contains`；golden 不变 |
| ✅ | P1-6 | 每小节 2–3 次 LINQ `Any` / `Count` | `Read`、`FlushCommands`、`Flush` | `pending` 入队时累计音符数 | `pendingNotes` 计数；golden 不变 |
| ✅ | P1-7 | 块内每行跑一次 `CourseLine` 正则 | `TjaLines.Read` | 字符比较 | 首字符不是 `c`/`C` 的行不跑正则（正则本身保留，语义完全不变）；golden 不变 |
| ✅ | P1-8 | 6 次 `OrderBy().ToList()` 再拷回 | `ChartReader.Read` 末尾 | 通用稳定插入排序（列表几乎有序） | `SortStable`：已有序（无分歧时的常见情况）则不动，否则仍用稳定的 `OrderBy`，避免手写排序在病态谱上退化；golden 不变 |

## P2：结构性优化

| 状态 | 编号 | 事项 | 位置 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | P2-1 | 读行时整文件复制 2–3 次，每行 `ToString` + `Trim` | `TjaLines.Uncommented` | 原文上扫描换行，按需生成子串 | 分配明显下降 | `LineReader` 在原文上找换行，只为保留的内容生成一个已 trim 的子串；`LineReaderTests` 用旧实现作参照，在 2 万份随机文本上逐行一致；SongInfoRead 分配 Donkama −22%（401→313 KB）、Large −22%（1877→1462 KB），剩余主要是每行的字符串与 `SongInfo` 的 `ToList`（留给 P2-2/P2-3）；golden 不变 |
| ✅ | P2-2 | `OfFile` 对每块从头重读，O(块数 × 文件长度) | `ChartHash.OfFile`、`TjaParser.Parse` | 一次读完全部行、切好块，每块带生效的头部上下文；`OfFile` 与 `Parse` 共用 | 多块文件耗时近似线性 | `OfFile` 只读一次行列表，交给 `SongInfo.Read` 与各块的 `Parse`（内部重载，公开 API 不变）；每块仍从头遍历行列表取头部，但不再生成字符串。新测试 `OfFileGivesWhatEachBlockGivesAlone` 证明与逐块 `OfBlock` 一致。OfFile：Donkama 3.38→2.67 MB、Large 40.1→35.2 MB（56→44 ms）；剩余大头是规范文本（P2-6） |
| ➖ | P2-3 | `SongInfo` 与 `TjaParser` 各自解释头部 | `SongInfo.Read`、`TjaParser.Parse` | 抽出共用的头部读取（与 P0-7、P2-2 一起） | 口径一致 | ➖ 评估后不做。真正的口径差异已在别处统一：选块按难度（P0-6）、只读生效的 BPM/OFFSET/LEVEL（P0-7）、行只读一次并共用（P2-2）。剩下的差异是有意的：`SongInfo` 是整首歌的元数据（取文件里最后一个可读值，读不出用默认值），`Parse` 是某一块（取块结束前的值，读不出则块失败，见规范 §14.1）。再抽一层公共头部读取只是搬代码 |
| ➖ | P2-4 | token 是 `List<string>` | `NoteTokens` / `ChartReader` | struct token，音符不生成字符串，命令 key 只求一次 | 解析阶段无按音符分配 | ➖ 评估后不做。P1-3、P1-4 之后音符已不再生成字符串（单字符串来自缓存，命令 token 就是行文本本身），token 列表只剩引用。实测 Large 最后一块 `Parse` 约 1.7 MB：`ChartNote` 输出本身约 0.64 MB、行字符串约 0.35 MB，token 列表（含扩容）约 0.3 MB。改成逐行喂给 `ChartReader` 最多省约 15%，还要额外一遍预扫描才能保持现有的报错顺序，收益与风险不相称 |
| ✅ | P2-5 | `Command` 大 switch，参数解析分散 | `ChartReader.Command` | 按命令拆成小方法，统一「读不出则丢弃并警告」 | 纯重构 | `Command` 只做分发；`BpmChange`、`Measure`、`Scroll`、`Delay`、`LevelHold`、`JposScroll`、`Sudden`、`LaneAlphaCommand` 各成方法，可丢弃的指令返回 false、由 `Command` 统一 `Warn(command)`；golden 不变 |
| ✅ | P2-6 | 规范文本每个字段一个字符串（`N()`、`params string[]`）：Large 每块写规范文本分配约 4 MB，是解析本身的 2 倍（P1 后实测补入） | `ChartCanonical.Write`、`ChartHash.OfCanonical` | 数字用 `TryFormat("R")` 直接写入缓冲；SHA-256 按块增量计算，不整串转 UTF-8 | 规范文本逐字节不变，`OfFile` 分配大幅下降 | `Writer` 把数字用 `TryFormat` 直接写进 `StringBuilder`（不预设容量，避免大对象堆）；`OfCanonical` 用 `IncrementalHash` 按 8 KB 缓冲编码。golden 的全部哈希不变即规范文本逐字节不变；`CanonicalHashTests` 覆盖跨缓冲的代理对。OfFile：Donkama 2.67→1.79 MB、Large 35.2→18.4 MB（44→34 ms） |

## P3：小问题与整洁

| 状态 | 编号 | 事项 | 位置 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ✅ | P3-1 | `HasNotes` 按「有数字」判断：全 `0` 算有、只有 `G`/`C`/`F` 算没有 | `SongInfo.Read` | 按真正的音符字符判断 | `hasNotes` 可能变化，通知两站 | `TjaParser.HasNote`：1–7、9、`C`、`F`、`G` 算，`0`、`8`、`E` 与 `[…]` 内的打数不算；golden 无变化（语料中没有只含空白或只含字母音符的块），新增单元测试；规范 §3.2 同步。**两站**：只写 0 的块 `hasNotes` 由真变假，只写 `G`/`C`/`F` 的块由假变真 |
| ✅ | P3-2 | `#STARTxxx` 被当成块开始 | `TjaLines.Read` | `#START` 后须是空白或行尾 | 有测试 | 按 OpenTaiko 的指令名规则（`#` 加大写字母，见其 `CommandAndArgumentRegex`）：`#START` 后紧跟字母（不分大小写）就不是块开始；`#START1` 等仍是（之后由 SongInfo 拒绝非 P1/P2）。golden 中只有 1 份含 `#STARTE` 的变异谱改变；规范 §1.4 同步 |
| ✅ | P3-3 | 气球用完列表时静默取 5 | `NextBalloon` | 加警告 | 只多警告，哈希不变 | 警告 `A balloon has no count in BALLOON|BALLOONEXP|BALLOONMAS; it takes 5 hits (measure n)`，有内联打数的不警告；golden 有 456 份摘要变化，已核对：去掉这条警告后 644 份全部与之前一致；规范 §7.2 同步 |
| ✅ | P3-4 | 警告不带位置 | 各处 `Warn` | 带行号 | 警告文本变化，同步两站 | 每条警告带首次出现的行号：`… (measure n)` → `… (measure n, line l)`，其余加 ` (line l)`；去重仍按原文字，所以警告的条数与顺序不变（已核对：去掉行号后 644 份摘要全部与之前一致）。token 带行号，块尾相关的警告用 `#END`（或隐式结束）所在行。规范 §0.1 同步。**两站**：警告文字多了行号 |
| ➖ | P3-5 | `ReadCondition`、`Scroll` 依赖正则 | `TjaParser.cs` | 手写解析 | 行为不变（测试 + fuzz） | ➖ 评估后不做。两处正则都是每条指令（`#SCROLL`、`#JPOSSCROLL`、`#BRANCHSTART`）跑一次，不在逐音符的热路径上；IL2CPP 下 Regex 可用。`Scroll` 的正则语义细节多（零宽切分、不分大小写排除 `e`/`E`、`inf`/`infinity` 替换），手写版本只增加行为差异的风险而没有收益 |
| ✅ | P3-6 | 三处 `EndsWith("i")`／`EndsWith("%")` 按当前文化比较：游戏用玩家的文化，CLI 是 InvariantGlobalization（序数比较），以可忽略字符（如 U+00AD、U+200B）结尾时两边结果不同（实施中发现补入） | `TjaParser.Scroll`、`LaneAlphaCommand` | 改为序数的 `EndsWith(char)` | 无可构造的实际分歧（这些值随后都读不出）；golden 不变 | 改为 `EndsWith('i')`、`EndsWith('%')`；已验证 ja-JP 文化下 `"2i\\u00AD".EndsWith("i")` 为真而序数为假，但三处的值随后都读不出，所以结果本就一致，改动只是去掉隐患；golden 不变 |

## 基准记录

`dotnet~/OurTaiko.TjaParser.Benchmarks`，.NET 10.0.114，云端容器（噪声较大，看量级与分配即可）。Donkama2000 是真实谱（4 个难度，18 KB）；Large 是生成谱（5 个难度 × 400 小节，含分歧，约 90 KB）。`Parse` 解析文件里最后一块。

### 基线（优化前，提交 e5d8102）

| 方法 | 谱面 | 耗时 | 分配 |
|---|---|---:|---:|
| SongInfoRead | Donkama2000 | 249 µs | 401 KB |
| Parse | Donkama2000 | 344 µs | 494 KB |
| OfFile | Donkama2000 | 3,135 µs | 4,468 KB |
| SongInfoRead | Large | 1,597 µs | 1,877 KB |
| Parse | Large | 3,843 µs | 3,715 KB |
| OfFile | Large | 64,091 µs | 49,477 KB |

### P1 完成后（提交 0119b35）

| 方法 | 谱面 | 耗时 | 分配 | 相对基线 |
|---|---|---:|---:|---|
| SongInfoRead | Donkama2000 | 194 µs | 401 KB | 分配不变（P1 未涉及） |
| Parse | Donkama2000 | 188 µs | 316 KB | 耗时 −45%，分配 −36% |
| OfFile | Donkama2000 | 2,408 µs | 3,381 KB | 耗时 −23%，分配 −24% |
| SongInfoRead | Large | 1,319 µs | 1,877 KB | 分配不变 |
| Parse | Large | 2,183 µs | 2,135 KB | 耗时 −43%，分配 −43% |
| OfFile | Large | 56,391 µs | 40,139 KB | 耗时 −12%，分配 −19% |

`OfFile` 的余量：Large 每块解析约 2 MB、写规范文本约 4 MB、SHA 约 0.35 MB，SongInfo 1.9 MB；规范文本补为 P2-6。

### 全部完成后（提交 5951fdf）

| 方法 | 谱面 | 耗时 | 分配 | 相对基线 |
|---|---|---:|---:|---|
| SongInfoRead | Donkama2000 | 120 µs | 237 KB | 耗时 −52%，分配 −41% |
| Parse | Donkama2000 | 154 µs | 246 KB | 耗时 −55%，分配 −50% |
| OfFile | Donkama2000 | 1,762 µs | 1,843 KB | 耗时 −44%，分配 −59% |
| SongInfoRead | Large | 1,029 µs | 996 KB | 耗时 −36%，分配 −47% |
| Parse | Large | 1,714 µs | 1,888 KB | 耗时 −55%，分配 −49% |
| OfFile | Large | 34,922 µs | 19,378 KB | 耗时 −46%，分配 −61% |

`SongInfoRead` 在 P2-1 之后又因 P3-1 下降（`HasNotes` 不再对每个主体行跑 LINQ `Any`）。

## 需要同步两站的行为变化

| 编号 | 变化 |
|---|---|
| P0-5 | CLI `hash` 遇到被拒绝的文件返回退出码 4（原来进程崩溃） |
| P0-7 | 别的难度里读不出的 `BPM`／`OFFSET`／`LEVEL` 不再使块失败：原来报错的块现在有哈希；已有哈希不变 |
| P3-1 | `hasNotes`：只写 0 的块由真变假，只写 `G`/`C`/`F` 的块由假变真 |
| P3-2 | `#START` 紧跟字母（如 `#STARTP1`）不再是块开始，与 OpenTaiko 相同 |
| P3-3 | 新警告：气球没有对应的打数、取 5 |
| P3-4 | 每条警告末尾带行号 |

没有任何改动影响已有块的规范文本与哈希，`ChartCanonical.Version` 仍为 1。

