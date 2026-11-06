# OurTaiko TJA 语法规范

本文是 TJAParser（`OurTaiko/TJAParser`，命名空间 `OurTaiko`）所接受的 TJA 格式的完整规范，**以代码为准**整理自 `Runtime/` 下的实现（main `8a2c002`，2026-11-04；时刻一节按 2026-11-06 的哈希算法 `v2`）。游戏 OurTaikoPlay、OurTaikoView_Web 和两站后端（Fanmade、OurTaikoDatabase）用的都是这一份解析器，所以这里写的就是四处共同的行为。

规范与代码不一致时以代码为准，并应修正本文。改变解析结果的改动须同时更新本文和 `README.md`，并按第 12 节的版本规则处理哈希。

---

## 0. 约定

### 0.1 三级结果

| 级别 | 含义 | 出现方式 |
|---|---|---|
| **文件拒绝** | 整个文件不可用，游戏列不出任何块，服务器拒收 | `SongInfo.Read` 抛 `FormatException`（CLI `hash`、`inspect` 退出码 4） |
| **块解析失败** | 只有这一块不可玩；其他块照常 | `TjaParser.Parse` 抛异常（`FormatException`、`NotSupportedException` 或其他）；CLI 中该块带 `error`、没有 `hash` |
| **警告** | 照 OpenTaiko 的容错继续解析，结果可玩 | 文字记入 `TaikoChart.Warnings`（同一文字只记一次，带首次出现的行号：以 `(measure n)` 结尾的写成 `(measure n, line l)`，其余在末尾加 ` (line l)`）；CLI 中为 `warnings` |

本文中「丢弃并警告」指该指令不产生任何效果，原文记入警告。下文引用的警告文字都省略行号。

### 0.2 术语

- **块**：一个 `#START … #END` 区间。
- **侧别（side）**：块的 `#START` 后写的玩家：空 → `single`，`P1` → `p1`，`P2` → `p2`。
- **路线（route）**：分歧中的普通 `#N`、玄人 `#E`、达人 `#M`（`BranchRoute.Normal/Expert/Master`）。
- **共通段**：不在任何路线里的部分（`BranchId = -1`）。
- **槽（slot）**：一个小节里的一个音符字符位置（含 `0`）。
- **指令**：主体中以 `#` 开头的一整行。
- 「OpenTaiko 相同」指与 OpenTaiko `CTja` 的行为一致；「本库扩展」指 OpenTaiko 没有、本库新增的语法。

### 0.3 入口

| API | 作用 |
|---|---|
| `SongInfo.Read(text, language)` | 读头部与块列表，做整文件校验（第 3 节） |
| `TjaParser.Parse(text, course, side)` | 解析一块为可游玩的 `TaikoChart`；`course` 传 `TjaBlock.Course`（COURSE 原文），`side` 为 `single`/`p1`/`p2` |
| `ChartHash.OfFile(text)` / `ChartHash.Of(chart)` | 内容哈希（第 12 节） |
| `LaneAlpha.At(...)`、`JudgeShift.At(...)` | 由解析结果求某时刻的轨道透明度、判定框位移 |

---

## 1. 文件与行

### 1.1 编码

输入必须是 UTF-8，可带 BOM（开头的 U+FEFF 被去掉）。服务器先做自己的 UTF-8 规范化再调用 CLI；CLI 读不到或不是 UTF-8 时退出码 3。

### 1.2 换行

CRLF、单独的 CR、LF 都算一个换行（与 OpenTaiko 相同）。行号从 1 起，按这样切分后的行计数。U+0085、U+2028、U+2029、FF 不算换行。

### 1.3 注释

逐行从左向右扫描，先出现的开头符生效：

- `//`：到行尾都是注释。
- `/* … */`（本库扩展）：可以跨行。
  - 同一行内的块注释替换为**一个空格**，所以 `#BPMCHANGE/**/180` 等于 `#BPMCHANGE 180`。
  - 跨行时，`/*` 之前的内容留在原行、`*/` 之后的内容留在它所在的行；行号不变。
  - 块注释内部的 `//` 不算。
  - 未闭合的 `/*` 一直延续到文件末尾。
  - 多余的 `*/` 原样保留。
- 两者都不能转义：`TITLE:http://example` 读作 `http:`（与 OpenTaiko 相同）。

### 1.4 行的分类

去掉注释后对每行 `Trim()`，空行跳过。其余行按下表分类（`TjaLineKind`）：

| 条件 | 类别 |
|---|---|
| 以 `#START` 开头（不分大小写），且紧跟的不是字母 | `Start`；`#START` 后的部分 trim 并转大写，作为玩家。紧跟字母（如 `#STARTP1`）时是另一个指令名，与 OpenTaiko 按 `#` 加大写字母切出指令名相同 |
| 整行等于 `#END`（不分大小写） | `End` |
| 在块内的其他行 | `Body`（音符行或指令） |
| 块外、含 `:` | `Header`：第一个 `:` 前 trim 并转大写为键，后面 trim 为值 |
| 块外、不含 `:` | `Other`（例如块外的 `#BMSCROLL`） |

### 1.5 块边界与隐式 `#END`

- 块从 `#START` 开始，到 `#END` 结束。
- 块没写 `#END` 时（与 OpenTaiko 相同），它在下一个 `COURSE:` 行、下一个 `#START` 或文件末尾结束；`TjaParser.Parse` 为此记警告 `#END missing`。
- 块内的 `KEY:VALUE` 行不是头部，而是主体（会被当作音符行，通常因字母报错）；`COURSE:` 例外，它先结束当前块。

---

## 2. 头部字段

头部是块外的 `KEY:VALUE` 行。键不分大小写；值去掉首尾空白。读取头部的有两处：`SongInfo.Read`（元数据与块列表）和 `TjaParser.Parse`（解析某一块）。

### 2.1 作用范围

- `TjaParser.Parse` 从文件开头读到**所选块的 `#END` 为止**，此后的头部不起作用；所以 `BPM`、`OFFSET` 取的是该块结束前最后写的值，其他难度区域里写的也算。
- `COURSE:` 开始一个新难度：把 `LEVEL` 归零，`STYLE` 清空，气球列表恢复为全局默认（2.3），起始滚动方式恢复为普通（6.13）。
- 写在第一个 `COURSE:` 之前的 `BALLOON*` 是每个难度的默认值。

### 2.2 字段表

| 键 | 读取者 | 规则 |
|---|---|---|
| `TITLE` | 两者 | `SongInfo.Titles["origin"]`；`TaikoChart.Title`。不进哈希 |
| `TITLEEN` `TITLEJA` `TITLEJP` `TITLEKO` `TITLEZH` `TITLECN` `TITLEZH_CN` `TITLEZH-CN` `TITLETW` `TITLEZH_TW` `TITLEZH-TW` | SongInfo | 译名，键分别归一为 `en` `ja` `ko` `zh` `zh_tw`；其他后缀忽略 |
| `SUBTITLE`、`SUBTITLE` + 同上后缀 | 两者 | 同上，存入 `Subtitles`。显示时去掉开头的 `-` / `+`（`--` 隐藏、`++` 显示是选曲界面的约定）；`TaikoChart.Subtitle` 已去掉 |
| `BPM` | 两者 | 实数（见 2.3）。`Parse` 只读对所选块生效的那个值（块结束前最后写的），它读不出会使块解析失败，别处读不出的不影响；最终值必须为正且有限，否则块解析失败 `BPM must be positive and finite.`。SongInfo 读不出时保留上一个值（默认 120） |
| `OFFSET` | 两者 | 秒。`Parse` 把它放到 0.001 毫秒的格上（`TjaTime.OnGrid`，见 5.2；OpenTaiko 向零取整为整毫秒）；与 BPM 一样，只有生效的值读不出才使块解析失败。正值使谱面提前（见 5.2） |
| `COURSE` | 两者 | 见第 3 节 |
| `LEVEL` | 两者 | 星数，`(int)` 截断。不进哈希。所选块生效的 LEVEL 读不出时 **`Parse` 使块解析失败**，SongInfo 则当 0（见 14 节）；别的难度里的不影响 |
| `BALLOON` `BALLOONNOR` `BALLOONEXP` `BALLOONMAS` | Parse | 气球打数列表，见 7.1 |
| `STYLE` | SongInfo | 原文存入 `TjaBlock.Style`；解析器不按它选块 |
| `GENRE` `WAVE` `BGMOVIE` | SongInfo | 原文 |
| `MAKER` | SongInfo | trim 后存入；块的 `Maker` 在没有对应 `NOTESDESIGNERn` 时用它 |
| `NOTESDESIGNER0`–`NOTESDESIGNER4` | SongInfo | 分别是 Easy、Normal、Hard、Oni、Edit 的谱面作者 |
| `DEMOSTART` `MOVIEOFFSET` | SongInfo | 秒；读不出当 0 |
| 其他 | — | 忽略，不警告 |

### 2.3 数字

头部与指令里的数字（`TjaNumber`）：

- 先 trim，去掉末尾的逗号与空格（`#BPMCHANGE 120,` 读作 120）；
- 中间的逗号是小数点（`BPM:120,5` 是 120.5，与 OpenTaiko 相同），从不当千位分隔符；
- 再按 InvariantCulture 解析浮点：允许正负号、小数、指数（`1e2`）、`Infinity`、`NaN`。

各指令对无穷与 NaN 的限制见各自条目。

---

## 3. 难度与块（`SongInfo.Read`）

### 3.1 COURSE 取值

值先按 `_` 切开、只看第一段，再：

| 写法 | 难度 |
|---|---|
| `0` `1` `2` `3` `4` | Easy Normal Hard Oni Edit |
| `Easy` `Normal` `Hard` `Oni` `Edit`（不分大小写） | 同名 |
| `Ura`（不分大小写） | Edit（裏） |
| 其他（如 `5`、`Tower`、`Dan`） | **文件拒绝** `Unsupported COURSE '…' (line n).` |

整文件先扫一遍 COURSE，任何一处不支持都拒绝整个文件。第一个 `COURSE:` 之前的块按 `Oni` 处理。

### 3.2 块列表

- `Blocks` 按文件顺序列出每个 `#START`，带 `Line`、`Course`（原文）、`Difficulty`、`Side`、`Player`、`Style`、`Level`、`IsBranching`、`HasNotes`、`Maker`。
- `#START` 后只能是空、`P1`、`P2`（不分大小写），否则**文件拒绝**。
- 同一难度同一侧别出现两次：**文件拒绝** `… is written twice`。
- `IsBranching`：块内有以 `#BRANCHSTART` 开头的行。`HasNotes`：块内有不以 `#` 开头、且含会产生音符的字符的行（1–7、9、`C`、`F`、`G`；不含 `0`、`8`、`E` 与 `[…]` 里的内联打数）。
- 星数不在 1–10、`HasNotes` 为假、`STYLE:Double` 却没有 P1/P2 都**不**拒绝，留给服务器判断。
- 哪个座位玩哪一块（例如 1P 先找 p1 再找 single）是游戏的规则（OurTaikoPlay `SeatBlocks`），不在本库。

### 3.3 `TjaParser.Parse` 的选块

取文件中**第一个** COURSE 与请求的难度相同（按 3.1 归一，所以 `Edit`、`Ura`、`4` 是同一难度；3.1 不认识的写法按原文不分大小写比较）且侧别等于请求侧别的块。找不到：块解析失败 `No … course (#START) on the … side.`。`TaikoChart.Course` 为 COURSE 去掉 `_` 后缀后的名字。

---

## 4. 主体：小节与音符

### 4.1 行

块内每一行要么是**指令**（整行以 `#` 开头），要么是**音符行**。指令必须独占一行：`10#GOGOSTART` 中的 `#` 被当作音符字符。

### 4.2 音符行的切分

音符行逐字符读，空白跳过：

- `,` 结束一个小节；
- `[…]`（本库扩展）紧跟在前一个音符字符后，作为它的内联气球数（7.2）；不能跨行，未闭合或前面不是音符字符都**块解析失败**；
- 其他每个字符是一个槽。

一个小节可以跨多行：从上一个 `,` 之后到这个 `,` 之间的所有槽（中间可以夹指令行）属于同一小节。

### 4.3 音符字符

音符字符按 36 进制取值（`0`–`9`、`A`–`Z`，**只认大写**），值即 `NoteKind`：

| 字符 | 值 | 含义 | 备注 |
|---|---|---|---|
| `0` | 0 | 空白 | |
| `1` | 1 | 咚 `Don` | |
| `2` | 2 | 咔 `Ka` | |
| `3` | 3 | 大咚 `BigDon` | |
| `4` | 4 | 大咔 `BigKa` | |
| `5` | 5 | 连打 `Roll` | 长音符，需 `8` 收尾 |
| `6` | 6 | 大连打 `BigRoll` | 长音符 |
| `7` | 7 | 气球 `Balloon` | 长音符，可写 `7[n]` |
| `8` | 8 | 长音符结束 | 不产生音符，见 7.3 |
| `9` | 9 | 薯条 / 彩球 `Kusudama` | 长音符，可写 `9[n]` |
| `C` | 12 | 地雷 `Mine` | OpenTaiko；画出、无音符文字，打中扣分 |
| `E` | 14 | 未使用 | OpenTaiko 标为未用：先结束未收尾的连打，再当空白并警告 `The unused note E is treated as a blank` |
| `F` | 15 | Ad-Lib `AdLib` | OpenTaiko；隐藏的加分音符，无音符文字 |
| `G` | 16 | Kadon `Kadon` | OpenTaiko；紫色音符，咚咔各打一下，音符文字 カドン |
| 其他（`A` `B` `D` `H`–`Z`、小写字母、符号） | — | — | **块解析失败** `NotSupportedException: Unsupported TJA note: x` |

`C`、`F`、`G` 都是普通（非长）音符：遇到未收尾的连打时，先在该处结束连打再放下自己（7.3）。

---

## 5. 时间模型

### 5.1 游标

解析器维护一个游标状态（OpenTaiko 的 dbNowTime 等）：

| 量 | 初值 | 含义 |
|---|---|---|
| `RawMs` | 0 | 从 `#START` 起的毫秒数，不取整，含 `#DELAY`，不含 OFFSET |
| `Bpm` | 头部 `BPM` | 当前 BPM |
| `Measure` | 1 | 当前拍号（4/4 = 1） |
| `ScrollX`, `ScrollY` | 1, 0 | 当前 SCROLL（横、纵） |
| `Beat` | 0 | 四分音符拍位置 |
| `Clock16` | 0 | 节拍时钟（十六分音符数，BM/HB 滚动用） |
| `Mode` | 起始滚动方式 | `Normal`/`BMScroll`/`HBScroll` |
| `SuddenShow`, `SuddenMove` | ∞ | 当前 SUDDEN |
| `Barline` | 开 | 小节线是否显示 |

### 5.2 时刻

一个物件的游玩时刻（`Time`，单位秒，相对音频开头）：

```
Time = round((RawMs − OFFSET × 1000) × 1000) / 1 000 000
```

即 RawMs 减去 OFFSET 后，四舍五入到最近的 0.001 毫秒（正好一半时远离零，`TjaTime.OnGrid`）；OFFSET 自己也先放到同一个格上。所以 `OFFSET:-1` 使 `#START` 处在 1 秒，170 BPM 的第二个十六分音符（88.2352941… 毫秒）在 0.088235 秒。`ChartNote.RawMs` 另存未取整值（供定数计算，不进哈希）。

与 OpenTaiko 不同（哈希算法 `v2` 起，2026-11-06）：OpenTaiko 把时刻向零截到整毫秒。那样每个音符平均早 0.5 毫秒；OpenTaiko 的 RawMs 又是逐格累加的，同一段节奏换一种格数写法会累加出 99.99999… 或 100.00000…，截断后差 1 毫秒（150 BPM 的十六分音符写成 16 格与 48 格即如此），小节线和哈希跟着不同。放到 0.001 毫秒的格上后，误差在 ±0.0005 毫秒以内；位置也不再逐格累加（5.3），所以同一张谱换格数写法得到相同的时刻。

游戏时钟仍按整毫秒读（`TjaTime.Ms`，向零截断，与 OpenTaiko 的 `(long)` 时钟相同）：`NoteScroll`、`ScrollClockTimeline.At16`、`JudgeShift.At` 都先把「现在」截到整毫秒；音符自己的时刻不截（`TjaTime.Millis`）。节拍时钟的 BPM 点与 `#JPOSSCROLL` 仍按各自时刻所在的整毫秒生效。

### 5.3 小节的推进

一个小节有 `n` 个槽（`n ≥ 1`）时，整个小节长

```
RawMs    240000 / Bpm × Measure   毫秒
Beat     4 × Measure              拍
Clock16  16 × Measure             十六分
```

每个槽占其中的 `1 / n`。槽之间的指令在它所处的位置立即生效，所以小节中间的 `#BPMCHANGE` 只改变其后槽的长度。

**怎么算**（`TjaParser` 的 `TimingState`，哈希算法 `v2` 起）：OpenTaiko 每读一个槽就加一次槽长，上万次加法的舍入误差越积越多（600 小节可到 7×10⁻⁶ 毫秒），同一段节奏换格数写法就落在不同的地方。本库把小节分成「段」：同一 BPM、同一小节长度下连续读的槽是一段，小节结束、`#BPMCHANGE`、`#MEASURE`、`#DELAY` 都结束当前的段。段内第 `k` 个槽之后的位置是

```
位置 = 段起点 + 整小节长度 × k / n        （一次乘、一次除；k = n 时直接加整小节长度）
```

段起点之间用补偿求和（每次加法把舍掉的误差记下，下一次补回）。结果：

- 一口气读完的小节正好推进一个小节长度，与格数无关，所以小节线不随格数变；
- `Beat` 与 `Clock16` 是小节长度（两个 float 的商）乘整数再除一次，乘积没有误差，所以同一位置在任何格数下逐位相同；
- `RawMs` 在不同格数下最多差最后一位，放到 0.001 毫秒的格上后相同。

与 OpenTaiko 的 `dbNowTime` 相比，`RawMs` 相差在 10⁻⁶ 毫秒以内（它的累加误差）。

**空小节**（只有 `,`，或只有指令再 `,`）：放一条小节线，并按 `Measure` 推进一整小节（相当于 16 个十六分）。

**最后一个小节没写逗号**：若有音符字符，当作一个小节读入并警告 `Last measure without a comma`；只有指令则直接执行。

### 5.4 小节线

每个小节（含空小节）在第一个槽之前放一条小节线（`TaikoChart.Bars`，`Kind = 0`）。它带当时的 `Barline` 状态（`Display`）、滚动信息，以及是否为某路线的第一个小节（`IsBranchStart`）；小节线不受 SUDDEN 影响。只含指令、紧接分歧边界的「小节」不产生小节线（见 8.6）。

### 5.5 时长

`TaikoChart.Duration` 是游标到过的最晚时刻（每个小节结束时与 `#END` 处取最大值）。

---

## 6. 指令

指令行的第一个空白（空格或 Tab）之前是名字（不分大小写），之后 trim 为参数。未知的指令丢弃并警告（警告文字是大写的指令名，例如 `#FOO`）。参数里的数字规则见 2.3。

下表由 `TjaSchema`（`Runtime/TjaSchema.cs`）生成，测试 `SchemaTests` 保证两者一致；解析器按同一张表分发指令。改表后用 `TJA_UPDATE_SPEC=1 dotnet test --filter SchemaTests` 重新生成。

<!-- schema:commands -->
| 指令 | 参数 | 写在 | 来源 | 进哈希 | 读不出时 | 见 |
|---|---|---|---|---|---|---|
| `#START` | `[P1｜P2]` | 块外 | TJA | 否 | — | 3.2 |
| `#END` | — | 块内 | TJA | 否 | — | 1.5 |
| `#BPMCHANGE` | `bpm（BPM）` | 块内 | TJA | 是 | 块解析失败 | 6.1 |
| `#MEASURE` | `a/b` | 块内 | TJA | 是 | 块解析失败 | 6.2 |
| `#SCROLL` | `speed` | 块内 | TJA | 是 | 警告并忽略 | 6.3 |
| `#DELAY` | `seconds（s）` | 块内 | TJA | 是 | 警告并忽略 | 6.4 |
| `#GOGOSTART` | — | 块内 | TJA | 是 | — | 6.5 |
| `#GOGOEND` | — | 块内 | TJA | 是 | — | 6.5 |
| `#BARLINEOFF` | — | 块内 | TJA | 是 | — | 6.6 |
| `#BARLINEON` | — | 块内 | TJA | 是 | — | 6.6 |
| `#SECTION` | — | 块内 | TJA | 是 | — | 6.7 |
| `#BRANCHSTART` | `p｜pp｜jp｜d｜jg｜jb｜r｜rb｜s` `expert` `master` `[l｜m]` | 块内 | TJA | 是 | 警告并忽略 | 8.2 |
| `#N` | — | 块内 | TJA | 是 | 块解析失败 | 8.1 |
| `#E` | — | 块内 | TJA | 是 | 块解析失败 | 8.1 |
| `#M` | — | 块内 | TJA | 是 | 块解析失败 | 8.1 |
| `#BRANCHEND` | — | 块内 | TJA | 是 | 块解析失败 | 8.1 |
| `#LEVELHOLD` | — | 块内 | TJA | 是 | — | 6.9 |
| `#NMSCROLL` | — | 块外或块内 | OpenTaiko | 是 | — | 6.10 |
| `#BMSCROLL` | — | 块外或块内 | OpenTaiko | 是 | — | 6.10 |
| `#HBSCROLL` | — | 块外或块内 | OpenTaiko | 是 | — | 6.10 |
| `#JPOSSCROLL` | `seconds（s）` `move（px）` `[direction]` | 块内 | OpenTaiko | 是 | 警告并忽略 | 6.11 |
| `#SUDDEN` | `show（s）` `move（s）` | 块内 | OpenTaiko | 是 | 警告并忽略 | 6.12 |
| `#LANE_ALPHA` | `target（%）` `[ms（ms）]` | 块内 | 本库扩展 | 否 | 警告并忽略 | 6.13 |
<!-- /schema:commands -->

### 6.1 `#BPMCHANGE <bpm>`

从此处起 BPM 改为 `bpm`。读不出数，或不是正的有限数：**块解析失败**。记录一个 `ChartTempo` 和一个节拍时钟点。

### 6.2 `#MEASURE <a>/<b>`

拍号改为 `a/b`（两个数各转为单精度浮点再相除，与 OpenTaiko 相同）。结果必须为正且有限；读不出数、缺 `/`：**块解析失败**（`FormatException`）。拍号从下一个槽起生效，通常写在小节开头。

### 6.3 `#SCROLL <speed>`

滚动速度。实数，或含 `i` 的复数（OpenTaiko ParseComplex）：虚部是纵向速度，正值从判定框下方来。可写 `1+2i`、`1 + 2i`、`1 2i`、`2i`、`i`、`-i`、指数、`inf`/`infinity`（不分大小写）。读不出：丢弃并警告，保持当前速度。

### 6.4 `#DELAY <seconds>`

游标前移 `seconds` 秒（可为负），节拍不动（`Beat` 不变），节拍时钟按当前 BPM 继续走。记录一个 `ChartTempo`（`Time` 到 `ResumeTime` 之间拍位置停住）。读不出或非有限：丢弃并警告。

### 6.5 `#GOGOSTART` / `#GOGOEND`

在此刻开始 / 结束 GOGO（`ChartGogo`，记所在路线）。

### 6.6 `#BARLINEOFF` / `#BARLINEON`

此后的小节线隐藏 / 显示。

### 6.7 `#SECTION`

在此刻重置分歧计数（`ChartSection`，记所在路线）。

### 6.8 `#BRANCHSTART`、`#N`、`#E`、`#M`、`#BRANCHEND`

见第 8 节。

### 6.9 `#LEVELHOLD`

写在路线里：一旦分歧选中该路线，之后的分歧都保持它（`ChartBranch.HoldRoutes`）。写在路线外：从此刻起保持当前在玩的路线（`TaikoChart.LevelHolds`）。与 OpenTaiko 相同。

### 6.10 `#NMSCROLL` / `#BMSCROLL` / `#HBSCROLL`

此后的音符的运动方式：

- `NMSCROLL`：普通，按各自的时刻、BPM 和 SCROLL 运动；
- `BMSCROLL`：按共享的节拍时钟运动，忽略 SCROLL；
- `HBSCROLL`：按节拍时钟运动，并乘 SCROLL。

写在某个 `COURSE:` 之后、其 `#START` 之前（块外独占一行）时，设定该难度块的起始方式；下一个 `COURSE:` 恢复为普通。

### 6.11 `#JPOSSCROLL <seconds> <move> [direction]`

判定框（以及音符的出发点）在 `seconds` 秒内线性移动 `move`。`move` 是实数（横向）或复数（横、纵），单位是 OpenTaiko 的 1280 宽像素，向下为正；`direction` 为整数，**0（默认）表示反向**（与音符运动方向一致），其他值不反向。`seconds` 可写 `inf`；时长取 `max(0, 1000 × seconds)` 毫秒。缺参数或读不出：丢弃并警告。

所有路线上的 `#JPOSSCROLL` 都生效（不按路线区分）。解析后按时刻排序并串联（`JudgeShift.Link`）：每次移动从上一次的终点出发；上一次还没走完时被截断在已走过的比例。`JudgeShift.At(moves, time)` 给出 1920 设计宽度下取整的位移。

### 6.12 `#SUDDEN <show> <move>`

此后的音符在判定前 `show` 秒出现，在判定前 `move` 秒开始移动（之前停在出发位置）。两个参数都必须有；读不出则丢弃并警告。绝对值不足 1 毫秒的 `show` 视为未设置（∞）；`move` 截断到整毫秒，不足 1 毫秒视为未设置。TJAP3 的怪癖：设了 `show` 而 `move` 未设置时，连打与气球隐藏（`ChartNote.SuddenHidesRoll`）。

### 6.13 `#LANE_ALPHA <target>[%] [ms]`（本库扩展）

调节**轨道背景**的不透明度。

| 参数 | 规则 |
|---|---|
| `target` | 必填。0–100 的百分比，可带小数，可在末尾写 `%`（`50` 与 `50%` 相同）。0 全透明，100 不透明 |
| `ms` | 选填，毫秒，向零截断为整数。省略或 0：在指令处瞬间切换；正数：从指令处起 `ms` 毫秒内过渡到目标；负数：从「指令处 + ms」（之前 `|ms|` 毫秒）起过渡，到指令处正好到达目标 |

- 参数之间用空格或 Tab 分隔。`50,500` 不是两个参数，按 2.3 读作 50.5。
- 丢弃并警告（警告为整行原文）：缺 `target`；`target` 读不出、是 NaN 或不在 [0, 100]；`ms` 读不出或非有限；参数多于两个。
- 指令位置决定 `Time`（与音符一样，可在小节中间）。负的 `ms` 可以让过渡从 `#START` 之前、前一个小节甚至谱面开头之前开始，解析器不截断。
- 写在路线里时属于该路线（`BranchId`、`Route`）。若它的开始时刻早于该分歧的判定时刻（只可能由负的 `ms` 造成），警告 `#LANE_ALPHA on a route starts before branch n is decided: …`（游戏在判定之前不知道会走哪条路线）。
- 结果：`TaikoChart.LaneAlphas`，每条为 `ChartLaneAlpha { Time, StartTime, EndTime, Alpha, BranchId, Route }`，`Alpha = target / 100`，`StartTime = min(Time, Time + ms)`，`EndTime = max(Time, Time + ms)`；列表按 `StartTime` 稳定排序（同一时刻保持原文顺序）。
- 它只影响外观，**不进规范文本，不改变哈希**。

游戏如何使用见第 13 节。

---

## 7. 连打与气球

### 7.1 气球打数列表

`BALLOON`（即普通路线）、`BALLOONNOR`（普通）、`BALLOONEXP`（玄人）、`BALLOONMAS`（达人）各是一条列表，每次出现替换该路线的列表（与 OpenTaiko 相同）：

- 值按逗号切开，读到第一个空项为止（`5,,6` 是 `[5]`）；
- 每项必须是整数，有一项读不出（例如 `5.10`）整行无效：丢弃并警告（警告为整行原文），保留原列表；
- 第一个 `COURSE:` 之前的是全局默认，每个 `COURSE:` 从默认重新开始。

### 7.2 每个气球的打数

- 路线里的气球取该路线列表的下一个值；共通段的气球取普通列表的下一个值，并让三条列表的序号一起前进。
- 列表用完或为空：5 打，并警告 `A balloon has no count in BALLOON|BALLOONEXP|BALLOONMAS; it takes 5 hits (measure n)`（写了内联打数的不警告）。
- 内联打数 `7[n]`、`9[n]`（本库扩展）：`n` 必须是大于 0 的整数（方括号内可以有空格），否则**块解析失败**；写在 7、9 以外的音符上也**块解析失败**。内联打数仍占列表的一个序号（只是不用那个值），所以给某个气球加上它不会让后面的气球错位。哈希只看最终打数。

### 7.3 长音符的收尾与容错（与 OpenTaiko 相同，都记警告）

`5`、`6`、`7`、`9` 打开一个长音符，`8` 在该处结束它（`EndTime`、尾部 BPM、尾部节拍时钟）。

- 没有打开的长音符时遇到 `8`：当空白（`A stray 8 …`）。
- 长音符打开时又来 `5`–`7`、`9`：当空白（TaikoJiro 的 `9…9…8` 写法，`A roll head inside a roll …`）。
- 长音符打开时来了其他音符（`1`–`4`、`C`、`F`、`G`）：长音符在该处结束，音符照常放下（`An unended roll is ended by a note …`）；`E` 也先这样结束长音符，再当空白。
- 到 `#END` 还开着：在 `#END` 处结束（`An unended roll is ended by #END`）。
- 打开状态按路线各存一份：共通段打开的长音符在三条路线里各自用自己的 `8` 收尾；收尾时刻不同则保留第一次的，并警告。路线里打开的长音符可以跨过 `#E`/`#M`/下一个分歧，直到该路线再出现 `8` 或 `#END`。

---

## 8. 分歧

### 8.1 结构

```
#BRANCHSTART <条件>
#N
…普通…
#E
…玄人…
#M
…达人…
#BRANCHEND
```

- 每个 `#N`/`#E`/`#M` 把游标恢复到 `#BRANCHSTART` 处的状态（时刻、BPM、拍号、SCROLL、滚动方式、SUDDEN、小节线开关等），所以三条路线从同一点重叠着写。
- 分歧结束于 `#BRANCHEND`、下一个 `#BRANCHSTART`、或 `#END`（`#BRANCHEND` 可省）。结束后游标从**最后写的那条路线**的终点继续。
- `#BRANCHSTART` 与第一个路线标记之间的音符属于共通段，各路线从分歧起点与它重叠；警告 `Notes between #BRANCHSTART and its first #N/#E/#M are common …`。
- 路线顺序任意。没写 `#N`：普通路线为空，警告。没写 `#E`：玄人显示普通路线；没写 `#M`：达人显示玄人路线（与 Fanmade 渲染器相同）。映射在 `ChartBranch.Routes`，用 `ResolveRoute` 取。

**块解析失败**：同一分歧里同一路线写两次；`#N`/`#E`/`#M` 不在分歧里；`#BRANCHEND` 不在分歧里。

### 8.2 条件

`#BRANCHSTART <类型>, <玄人阈值>, <达人阈值>[, l|m]`。类型后的逗号可省（`p50,80`）。

| 类型 | 条件 | 大写 |
|---|---|---|
| `p` | 精度 | 只算大音符 |
| `pp` | 良的百分比 | 只算大音符 |
| `jp` | 良的个数 | 只算大音符 |
| `d` | 同 `jp`，总是只算大音符 | — |
| `jg` | 可的个数 | 只算大音符 |
| `jb` | 不可的个数 | 只算大音符 |
| `r` | 连打打数 | 只算大连打 |
| `rb` | 气球打数 | 只算彩球 |
| `s` | 分数 | 不区分大小写 |

- 类型首字母大写（`P`、`R`、`JP`…）为「只算大音符」（`BigOnly`）；`s` 永远不是，`d` 永远是。
- 第四个参数 `l`：阈值为上界（数值 < 达人阈值时进达人，否则 < 玄人阈值时进玄人，`Less`）；`m` 接受，不改变行为。
- 阈值存成 fumen 的形式 `ChartBranch.Thresholds[判定时所在路线, 0=玄人/1=达人]`，每条路线可以不同（fumen 的 NormalToMaster、AdvancedKeepAdvanced、MasterKeepMaster 等六个值）。TJA 只有一对，写进三行（`SameFromEveryRoute` 为真）。`ChartBranch.Choose(数值, 当前路线)` 按当前路线那一行先判达人、再判玄人。规范文本只写普通行那一对；三行不同（只会来自 fumen 等其他格式）时在该行末尾追加 `from` 与玄人行、达人行的四个值，所以 TJA 谱面的哈希不变。
- 阈值只看判定时在哪条路线，不记得之前走过哪条；「经过某条路线就一直保持」靠 `#LEVELHOLD`（见 6.8 节），fumen 没有对应物。
- 参数为空：条件为 `None`（保持当前路线），不警告。参数不足三个、阈值读不出、类型未知、第四参数不是 `l`/`m`：条件为 `None` 并警告（OpenTaiko 会丢弃整条指令，这里保留分歧本身）。

### 8.3 判定时刻

`ChartBranch.DecisionTime`（与 OpenTaiko `GetBranchJudgeChipTime` 相同）：分歧前最后一条小节线的时刻（即 `#BRANCHSTART` 前一个小节的开头）；没有小节线时为谱面开头前四拍（按头部 BPM：`−240000 / BPM − OFFSET` 毫秒，同样放到 0.001 毫秒的格上）。条件是 `r`/`rb` 时，若最后那个小节里有长音符在判定时刻之后结束，则等到它结束（不晚于分歧起点）。

### 8.4 路线的物件

路线里的音符、小节线、GOGO、SECTION、BPM 变化、`#LANE_ALPHA` 都带 `BranchId` 和 `Route`；共通段的 `BranchId = -1`。`ChartBranch.FirstEntries` 是每条路线的第一个物件（第一条小节线）。

### 8.5 `#LEVELHOLD`

见 6.9。

### 8.6 边界上的逗号

`#BRANCHSTART`、`#BRANCHEND`、`#N`、`#E`、`#M` 必须在小节边界上：前面若有未以逗号结束的音符，**块解析失败** `A measure is missing its comma before a branch boundary.`。边界前只有指令的部分会先执行，不形成小节。

---

## 9. 解析结果（`TaikoChart`）

| 成员 | 内容 |
|---|---|
| `Title` `Subtitle` `Course` `Level` `Bpm` `Offset` | 头部（`Offset` 在 0.001 毫秒的格上） |
| `Duration` | 见 5.5 |
| `Notes` | 全部音符，按 `Time` 排序（`ChartNote`） |
| `Bars` | 全部小节线，按 `Time` 排序（`Kind = 0`） |
| `Branches` | `ChartBranch`：`Id`、`Time`、`EndTime`、`DecisionTime`、`Condition`、`Thresholds`（按判定时路线的 3×2 阈值）、`BigOnly`、`Less`、`HoldRoutes`、`FirstEntries`、`Routes` |
| `Sections` | `#SECTION` |
| `ScrollClocks` | 节拍时钟的 BPM 点（开头、每次 BPMCHANGE、每个分歧的起止） |
| `JudgeMoves` | `#JPOSSCROLL`，已串联 |
| `LevelHolds` | 路线外的 `#LEVELHOLD` 时刻 |
| `Gogos` `Tempos` | 按时刻稳定排序 |
| `LaneAlphas` | `#LANE_ALPHA`，按开始时刻稳定排序 |
| `NoteLists` | 实际遍历的列表：先是共通段，然后每个分歧的三条路线；每条按原文顺序含音符、长音符尾（`IsTail`）、小节线 |
| `Warnings` | 警告，去重 |

`ChartNote`：`Kind`、`Time`、`EndTime`（长音符尾）、`RawMs`、`Beat`、`Bpm`、`TailBpm`、`ScrollX`、`ScrollY`、`ScrollMode`、`ScrollClock`、`EndScrollClock`、`SuddenShow`、`SuddenMove`、`BalloonHits`、`Moji`、`BranchId`、`Route`、`Display`、`IsBranchStart`，以及 `IsLong`、`IsBalloon`、`IsKa`、`IsDrumroll`、`IsBigNote`、`IsKadon`、`IsAdLib`、`IsMine`。

---

## 10. 音符文字（`NoteMoji`）

解析结束时为每个音符算出下方文字的帧（`ChartNote.Moji`），每条 `NoteList` 单独算（共通段与路线的连段不相连）：

- 帧：0 ドン、1 ド、2 コ、3 カッ、4 カ、5 ドン(大)、6 カッ(大)、7 連打ー、8 連打(大)ー、9 ふうせん、10 ーっ!!、11 くすだま、12 カドン；-1 无文字。
- Ad-Lib 无文字，且在连段计算中跳过（不打断也不连接）。地雷无文字，但占连段位置、不算咚。
- **连段**：相邻间隔（按拍位置 `Beat`）小于八分的音符组成密连段；不在密连段里、恰好隔八分的相邻音符组成八分连段（八分连段在密连段的第一个音符前停止）。长音符可以结束连段，不能延续连段。
- 连段最后一个音符是 ドン/カッ，除非它与前一个音符相距不超过二十四分；其余咚、咔为 ド/カ。
- **コ**：只在全是小咚的连段里交替出现（ドコドコ…ドン）：八分连段恰好 3 个且不与两侧都隔八分；密连段恰好 3 个或 ≥11 个奇数个；以长音符结尾的密连段 ≥10 个偶数个。

文字不进哈希。

---

## 11. 与 OpenTaiko 的差异汇总

| 项目 | 本库 |
|---|---|
| `/* */` 块注释 | 扩展 |
| 内联气球数 `7[n]` `9[n]` | 扩展 |
| `#LANE_ALPHA` | 扩展 |
| 时刻与 OFFSET | 四舍五入到 0.001 毫秒（OpenTaiko 向零截到整毫秒），见 5.2 |
| G 的音符文字 | カドン（OpenTaiko 为 ドン(大)） |
| Ad-Lib 在音符文字连段中 | 跳过（OpenTaiko 计入） |
| 读不出的 `#BRANCHSTART` 条件 | 保留分歧、条件 None（OpenTaiko 丢弃整条） |
| 读不出的 `#DELAY` | 丢弃并警告 |
| 缺 `#E`/`#M` 的显示 | 用 `#N`/`#E`（与 Fanmade 渲染器相同） |
| 其余字母音符（A、B、D、H–Z） | 不支持，块解析失败 |
| 未实现的 OpenTaiko 指令 | 丢弃并警告（见 `OurTaikoLogs/OurTaikoPlay/Documentation/2026-10-14-tja-opentaiko-gap-todo.md`） |

---

## 12. 规范文本与内容哈希

`ChartCanonical.Write(chart)` 把 `TaikoChart` 写成固定顺序、固定写法的文本；`ChartHash.Of` 对其 UTF-8 字节求 SHA-256，前缀 `v2:`（`v1` 的时刻截到整毫秒，见 5.2）。原则是**只写玩到的**：BPM、MEASURE、SCROLL 怎么写都不影响，只要每个物件的真实时刻和流动速度相同，哈希就相同。BM/HB 物件的节拍时钟写到小数 12 位（以小节计）；它们按 5.3 的算法在任何格数写法下逐位相同，取整只为抹掉别的求和路径（一个小节写成两个、`#DELAY`）留下的末位差别。测试 `GridRewriteTests` 把随机谱面的每个小节换成 2～8 倍的格数，三种滚动方式下规范文本都逐行相同。

**写入**：时长；每个音符与小节线的种类、时刻、结束时刻、运动方式（普通滚动写 BPM×SCROLL 的横纵两个积，四舍五入到小数 9 位；BM/HB 写头尾节拍时钟与有效 SCROLL）、SUDDEN 两个时刻、气球数、分歧与路线、是否显示、是否路线首小节；分歧的条件、阈值、大音符限定、`l`、LEVELHOLD、路线映射、各路线首个物件；SECTION、JPOSSCROLL、路线外 LEVELHOLD、GOGO；仅当有 BM/HB 物件时写 BPM 时间线；NoteLists。

**不写**：TITLE、SUBTITLE 及译名、MAKER、GENRE、WAVE、DEMOSTART、COURSE、LEVEL、OFFSET（时刻已含）、写下的 BPM、`Beat`、`Tempos`、`RawMs`、连打尾 BPM、警告、音符文字、**`LaneAlphas`**。

数字用最短往返十进制（InvariantCulture），负零写 `0`，无穷写 `inf`。

**版本规则**：`Runtime/` 中任何改变规范文本的改动（包括改变 `TaikoChart` 中被写入的内容），都要把 `ChartCanonical.Version` 加一，重新钉住 `KnownVectorTests`，并让两站对全部谱面重算。只新增不写入规范文本的字段（如 `RawMs`、`LaneAlphas`）、改警告文字或性能，不升版本。

---

## 13. `#LANE_ALPHA` 的游戏接入规范

解析器已提供数据；以下是游戏（OurTaikoPlay，以及需要时 View_Web）接入时须满足的行为。

### 13.1 作用对象

- 只改变**轨道背景**（音符流动的那条带子的底图）的不透明度：最终 alpha = 皮肤原有 alpha × `LaneAlpha.At(...)`。画在轨道背景这一层上的变化（分歧路线的底色、GOGO 时的底色）同样乘这个系数。
- **不**改变：音符、小节线、长音符身体、判定框、音符文字、判定特效，以及轨道以外的任何 HUD。
- 每个座位按自己所玩的块的 `LaneAlphas` 独立计算（双人时 1P、2P 互不影响）。

### 13.2 求值

- 每帧用当前的谱面时刻 `t`（与判定音符用的同一时钟，含 OFFSET、不含输入延迟补偿）调用：

  ```csharp
  double alpha = LaneAlpha.At(chart.LaneAlphas, t,
      a => a.BranchId < 0 || DrawnRoute(a.BranchId) == a.Route);
  ```

  `DrawnRoute(id)` 是该分歧**已判定**时实际画出的路线：`chart.Branches[id].ResolveRoute(玩家所在路线)`，与选择音符的规则相同（缺 `#E` 时玄人玩家用 `#N` 的指令，缺 `#M` 时用 `#E` 的）；尚未判定的分歧返回「无」，其路线指令都不计入。
- `LaneAlpha.At` 的定义（参考实现即规范）：
  1. 初值 `LaneAlpha.Initial = 1`（不透明）。
  2. 按 `StartTime` 顺序（同时刻按原文顺序）找出 `StartTime ≤ t` 且被选中的指令，**最后开始的那条**负责当前值。
  3. 它从「之前的指令在它的 `StartTime` 时给出的值」出发，在 `[StartTime, EndTime]` 内线性插值到 `Alpha`，此后保持 `Alpha`；`StartTime == EndTime` 时在该时刻直接跳到 `Alpha`。
  4. 因而一个还没走完的过渡被后开始的指令打断时，停在当时的值，由新指令从那里接着过渡。
- 它是时间的纯函数：练习模式跳转、回放、倒带都直接按新时刻求值，不需要累计状态。
- 插值是线性的（不加缓动）。若以后要缓动，需要在语法里加参数，并更新本节与 `LaneAlpha.At`。

### 13.3 边界情况

- `#START` 之前（谱面开头前的准备时间）同样求值；负 `ms` 使过渡从开头之前开始时，开头那一刻已是过渡中的值。
- 路线里的指令在分歧判定之前开始（解析器已警告）：判定前不计入，判定后计入，透明度可能在判定时刻跳变。这是谱面的问题，游戏按上述规则执行即可。
- 歌曲结束、结算画面：保持最后的值直到离开游玩画面；下一首重新从 1 开始。
- 透明度不影响判定、分数、游玩记录与上传。

### 13.4 待接入时确认

- 「轨道背景」在各皮肤 / 预制体（`NoteLane`、`LaneHud` 等）中具体对应哪些渲染器。
- 是否提供关闭此效果的设置（例如无障碍选项）。

---

## 14. 已知的不一致与问题

以下是整理本文时发现、尚未修改的行为，记录于此以免被误当作规范意图：

1. **生效的 `LEVEL`、`BPM`、`OFFSET` 读不出**：`SongInfo` 用默认值（0、上一个值、0），而 `TjaParser.Parse` 使该块解析失败。别的难度里读不出的值已不影响所选块。
