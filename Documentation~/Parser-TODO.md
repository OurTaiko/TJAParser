# 解析器自身优化 TODO

只涉及解析器本身（正确性、性能、结构），不含谱面 IDE 相关的工作。按优先级排列，做完一项把状态从 ⬜ 改为 ✅，并在「记录」里写上提交或说明；➖ 表示评估后不做，理由写在「记录」里。

- **P0**：安全网与已知 bug，先做。
- **P1**：热路径零风险优化，不改变行为。
- **P2**：结构性优化，改动较大、收益最大。
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
| ⬜ | P3-4 | 警告不带位置 | 各处 `Warn` | 带行号 | 警告文本变化，同步两站 | |
| ⬜ | P3-5 | `ReadCondition`、`Scroll` 依赖正则 | `TjaParser.cs` | 手写解析 | 行为不变（测试 + fuzz） | |

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
