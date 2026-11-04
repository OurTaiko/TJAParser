# 解析器自身优化 TODO

只涉及解析器本身（正确性、性能、结构），不含谱面 IDE 相关的工作。按优先级排列，做完一项把状态从 ⬜ 改为 ✅，并在「记录」里写上提交或说明。

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
| ⬜ | P1-6 | 每小节 2–3 次 LINQ `Any` / `Count` | `Read`、`FlushCommands`、`Flush` | `pending` 入队时累计音符数 | |
| ⬜ | P1-7 | 块内每行跑一次 `CourseLine` 正则 | `TjaLines.Read` | 字符比较 | |
| ⬜ | P1-8 | 6 次 `OrderBy().ToList()` 再拷回 | `ChartReader.Read` 末尾 | 通用稳定插入排序（列表几乎有序） | |

## P2：结构性优化

| 状态 | 编号 | 事项 | 位置 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ⬜ | P2-1 | 读行时整文件复制 2–3 次，每行 `ToString` + `Trim` | `TjaLines.Uncommented` | 原文上扫描换行，按需生成子串 | 分配明显下降 | |
| ⬜ | P2-2 | `OfFile` 对每块从头重读，O(块数 × 文件长度) | `ChartHash.OfFile`、`TjaParser.Parse` | 一次读完全部行、切好块，每块带生效的头部上下文；`OfFile` 与 `Parse` 共用 | 多块文件耗时近似线性 | |
| ⬜ | P2-3 | `SongInfo` 与 `TjaParser` 各自解释头部 | `SongInfo.Read`、`TjaParser.Parse` | 抽出共用的头部读取（与 P0-7、P2-2 一起） | 口径一致 | |
| ⬜ | P2-4 | token 是 `List<string>` | `NoteTokens` / `ChartReader` | struct token，音符不生成字符串，命令 key 只求一次 | 解析阶段无按音符分配 | |
| ⬜ | P2-5 | `Command` 大 switch，参数解析分散 | `ChartReader.Command` | 按命令拆成小方法，统一「读不出则丢弃并警告」 | 纯重构 | |

## P3：小问题与整洁

| 状态 | 编号 | 事项 | 位置 | 做法 | 验收 | 记录 |
|---|---|---|---|---|---|---|
| ⬜ | P3-1 | `HasNotes` 按「有数字」判断：全 `0` 算有、只有 `G`/`C`/`F` 算没有 | `SongInfo.Read` | 按真正的音符字符判断 | `hasNotes` 可能变化，通知两站 | |
| ⬜ | P3-2 | `#STARTxxx` 被当成块开始 | `TjaLines.Read` | `#START` 后须是空白或行尾 | 有测试 | |
| ⬜ | P3-3 | 气球用完列表时静默取 5 | `NextBalloon` | 加警告 | 只多警告，哈希不变 | |
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
