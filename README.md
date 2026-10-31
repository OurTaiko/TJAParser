# TJAParser

OurTaikoPlay 实际游玩所用的 TJA 解析器与谱面模型，独立成库，供三处共用：

- 游戏 OurTaikoPlay 与 OurTaikoView_Web 以 UPM 包引用（源码编译，不发 DLL）；
- 两站后端（Fanmade、OurTaikoDatabase）通过命令行工具 `tjaparser` 计算每个谱面块的**内容哈希**。

内容哈希回答「这还是不是同一份谱面」：对解析后的可游玩对象求 SHA-256，所以改标题、译名、MAKER、注释、星数都不会改变它，而任何影响游玩的改动都会。它和文件 SHA 并存：文件 SHA 继续用于对象存储寻址与下载校验。背景与决策见 `OurTaikoLogs/TJAParser/AGENTS.md`。

## 目录

```
package.json                 UPM 包清单（org.ourtaiko.tjaparser）
Runtime/                     纯 C# 源码，Unity 与 dotnet 共用；asmdef 禁止引用 UnityEngine
  TjaParser.cs TjaLines.cs TjaNumber.cs TjaTime.cs     解析
  TaikoChart.cs ChartSide.cs JudgeShift.cs NoteMoji.cs  谱面模型及解析时填充的派生数据
  SongInfo.cs                头部与块列表：Blocks 是文件里每个 #START，游戏与服务器用同一份；哪个座位玩哪块是游戏的事
  ChartCanonical.cs          谱面对象的规范文本
  ChartHash.cs               块哈希：ChartHash.Of(chart)、ChartHash.OfFile(text)
dotnet~/                     Unity 忽略的目录：dotnet 工程
  OurTaiko.TjaParser/        netstandard2.1 类库，编译 ../../Runtime/**/*.cs
  tjaparser/                 net10 命令行工具
  OurTaiko.TjaParser.Tests/  xunit 测试与 fixture
```

`Runtime/` 下的文件就是游戏仓库 `Assets/OurTaiko/Runtime/Core` 里对应文件的唯一版本；游戏改为引用本包后不再各自保留副本。

## Unity 引用

`Packages/manifest.json`：

```json
"org.ourtaiko.tjaparser": "https://github.com/OurTaiko/TJAParser.git#v0.3.0"
```

程序集 `OurTaiko.TjaParser` 自动引用（`autoReferenced`），命名空间仍是 `OurTaiko`，游戏代码不需要改 `using`。游戏锁定 tag；服务器端 CLI 用同一个 tag 构建，哈希口径才一致。

## 命令行

```bash
cd dotnet~ && dotnet build TJAParser.slnx && dotnet test TJAParser.slnx
```

```
tjaparser hash <file|->                       游戏能列出的块：course、side、level、branching 与哈希（JSON）
tjaparser inspect <file|->                    头部元数据 + 文件里每个 #START 块 + 已列出块的哈希与各路线音符（JSON），服务器登记谱面用
tjaparser canonical <file|-> [course] [side]  某一块被哈希的规范文本（排查两份谱面为何不同）
tjaparser version                             算法版本，如 v1
```

`inspect` 让两站不再自己解析 TJA。头部：`titles`／`subtitles` 两个字典，键 `origin` 是 `TITLE`／`SUBTITLE` 原样（副标题带 `--`），其余键是语言码 en/ja/ko/zh/zh_tw，值原样——服务器整份存、整份返回，客户端自己按语言挑；另有 `maker`、`genre`、`notesDesigners`、`bpm`、`offset`、`demoStart`、`wave`、`movie`、`movieOffset`。`blocks`：文件顺序，每块 `line`、`course` 原文、`name` 归一后的难度名、`side`、`player`、`style`、`level`、`branching`、`hasNotes`、`maker`，以及 `hash` 或 `error`、`warnings`。能解析的块另有 `notes`：玩家在每条路线上要打的咚／咔（1–4，连打与气球不算）按时间排列，`{"<路线>":{"ms":[…],"kinds":[…]}}`，无分歧的块只有 `unbranched`，分歧块有 `normal`／`expert`／`master`（共通段加上每个分歧在该路线实际演奏的那条，缺 `#E` 用 `#N`、缺 `#M` 用 `#E`，与游戏 `ResolveRoute` 相同）。`ms` 是 `ChartNote.RawMs`：从 `#START` 起的毫秒数，含 `#DELAY`、不含 OFFSET、不取整（`Time` 是取整到毫秒的游玩时刻）。两站用它计算难度定数的原始值（`OurTaikoLogs/Fanmade/docs/2026-10-25-difficulty-constants.md`）；它不进规范文本，哈希不变。

读文件时直接报错（`FormatException`，退出码 4）的情况，游戏与服务器一致：COURSE 不是 Easy/Normal/Hard/Oni/Edit（含 0–4、Ura）——先扫整个文件再解析；同一难度同一侧别写了两次；`#START` 后不是空、P1、P2。留给服务器按返回结果判断的：`level` 不在 1–10、`hasNotes` 为假、STYLE:Double 却无 player。`maker` 按 Database 规则：`NOTESDESIGNERn` 记该难度，否则 `MAKER`。

注释与换行：`//` 到行尾；`/* … */` 可跨行（OpenTaiko 不支持，本库扩展），行内的块注释留一个空格，未闭合则到文件末尾；两者都不能转义，所以 `TITLE:http://…` 和 OpenTaiko 一样截成 `http:`。CRLF、单独的 CR 与 LF 都算换行。

气球打数可以直接写在音符上：`7[10]008,`、`9[10]008,`（OpenTaiko 不支持，本库扩展）。方括号紧跟在 7 或 9 后面，里面只能是大于 0 的整数，可以有空格，不能跨行；写在别的音符上、写错数值或方括号没闭合都会报 `FormatException`。内联打数仍占 BALLOON／BALLOONNOR／BALLOONEXP／BALLOONMAS 列表的一个序号，只是不用那个值，所以给某个气球加上它不会让后面的气球错位。写在 `#N`／`#E`／`#M` 里的只属于该路线，写在共通段的三条路线都用它。哈希只看最终打数，`7[10]` 和 `BALLOON:10` 加 `7` 是同一份谱面。

音符字符：`0`–`9`，以及 OpenTaiko 的 `G`（Kadon，紫色音符，咚和咔各打一下）。`G` 只认大写，与 OpenTaiko 相同；它是普通音符，会结束未闭合的连打，音符文字是新加的第 12 帧 カドン（`NoteMoji.Kadon`，OpenTaiko 用的是 ドン(大)），`NoteKind.Kadon` 的值为 16。其余字母音符仍报 `NotSupportedException`。`inspect` 的 `notes` 仍只含 1–4，不含 `G`。

输入必须是 UTF-8（可带 BOM）；后端先做自己的 UTF-8 规范化再调用。退出码：0 完成（解析失败的块在 JSON 里带 `error`、无 `hash`），2 用法错误，3 读不到输入或不是 UTF-8，4 `canonical` 指定的块不存在或解析失败。

```json
{"version":1,"prefix":"v1:","blocks":[
  {"course":"Oni","side":"single","level":10,"branching":false,"hash":"v1:848c…9a47"},
  {"course":"Edit","side":"p1","level":10,"branching":true,"error":"Long note is missing its 8 tail …"}]}
```

块的列表与顺序和 `SongInfo.Blocks` 一致：每个难度每个侧别只取文件里第一块，按难度、再按 single/p1/p2 排序；`COURSE:3`、`COURSE:Ura` 等写法归一为 `Oni`、`Edit`；未知 COURSE 的块游戏列不出，也不在列表里。

发布到服务器：

```bash
cd dotnet~ && dotnet publish tjaparser -c Release -r linux-musl-x64 -o out   # 自包含单文件、裁剪，选项在 tjaparser.csproj 里
```

## SongInfo 的结构

| 成员 | 内容 |
|---|---|
| `Titles`、`Subtitles` | `origin` + 语言码 → 原样文本 |
| `Language`、`Title`、`Subtitle` | `Read` 被要求的语言，以及据此挑出的显示文本（副标题去掉开头 `--`／`++`）；`DisplayTitle(lang)`／`DisplaySubtitle(lang)` 可按别的语言再挑 |
| `Genre`、`Maker`、`NotesDesigners`、`Bpm`、`Offset`、`DemoStart`、`Wave`、`Movie`、`MovieOffset` | 其余头部 |
| `Blocks` | 文件里每个 `#START`（`TjaBlock`：`Line`、`Course` 原文、`Difficulty`、`Side`、`Player`、`Style`、`Level`、`IsBranching`、`HasNotes`、`Maker`），文件顺序 |
| `Block(d, side)` | 某难度某侧别的那一块，没有则 null |

库里没有"座位"的概念。1P 读 p1 再 single、2P 读 p2、single、p1 这套选块规则在游戏仓库的 `SeatBlocks`（`PlayedBlock`、`Courses(seat)` 等扩展方法）里，服务器不需要它。

## 容错（照 OpenTaiko，均记入 `TaikoChart.Warnings`）

- 没有开着的连打时遇到 `8`：当空白。
- 连打开着时又来连打头 5／6／7／9：当空白（TaikoJiro 的 `9…9…8` 写法）。
- 连打开着时来了 1–4 普通音符：连打在该处结束，音符照常放入。
- 到 `#END` 连打还开着：在 `#END` 处结束。
- 连打状态按路线各存一份：共通段开的连打三条路线各自用自己的 `8` 收尾（收尾时刻不同则保留第一条，并警告）；路线里开的连打可以跨过 `#E`／`#M`／下一个分歧，直到该路线再出现 `8` 或 `#END`。
- 分歧没写 `#N`：普通路线为空。`#BRANCHSTART` 与第一个路线标记之间的音符算共通段，各路线从分歧起点重叠着写。
- 仍然报错（`FormatException`）：同一路线写两次、`#N`／`#E`／`#M` 或 `#BRANCHEND` 不在分歧里、小节在分歧边界前缺逗号、BPM／MEASURE 非法。

## 规范文本与哈希

`ChartCanonical.Write(chart)` 把 `TjaParser.Parse` 产出的 `TaikoChart` 写成固定顺序、固定写法的文本，`ChartHash.Of` 对其 UTF-8 字节求 SHA-256，前缀 `v1:`。原则是**只写玩到的**：BPM、MEASURE、SCROLL 怎么写都不影响，只要每个物件的真实时间和在屏幕上的流动速度相同，哈希就相同。

写进去的：时长；每个音符与小节线的种类、时刻、结束时刻、运动方式（普通滚动写速度 = BPM × SCROLL 的横纵两个积；BM/HB 滚动写头尾的节拍时钟与有效 SCROLL）、SUDDEN 两个时刻、气球数、所属分歧与路线、是否显示、是否分歧首小节；分歧的条件、阈值、大音符限定、l 范围、LEVELHOLD、路线映射、各路线首个物件；SECTION、JPOSSCROLL、GOGO、LEVELHOLD；BPM 时间线只在谱面含 BM/HB 物件时写（同一时刻只留最后一点、BPM 不变的点去掉）；以及游戏实际遍历的 NoteLists（公共段与每条路线）。

不写的：TITLE、SUBTITLE 及译名、MAKER、GENRE、WAVE、DEMOSTART、COURSE 与 LEVEL（块由 course/side 作键，星数是元数据）；OFFSET（时刻已是相对音频的真实时间，OFFSET 不同则时刻不同）；写下的 BPM、拍位置 `Beat`、`Tempos`（它们驱动连击表情与节拍动画，不决定音符）；连打尾的 BPM（渲染不用）；解析警告；音符下的文字帧。

数字用最短往返十进制（`R`，InvariantCulture），负零写 `0`，无穷写 `inf`；速度乘积四舍五入到小数 9 位，所以 150 × 0.8 与 120 × 1 相同。枚举写整数值。

等价示例（测试 `HashTests`）：`BPM:120` ≡ `BPM:150` + 块首 `#BPMCHANGE 120` ≡ `BPM:240` + `#MEASURE 8/4` + `#SCROLL 0.5` ≡ `BPM:60` + `#MEASURE 2/4` + `#SCROLL 2`；小节中间无效的 `#BPMCHANGE` 也不影响。不等价：OFFSET 变、速度变（只改 SCROLL 或只改 BPM）、BM 滚动下改 MEASURE（节拍时钟间距变）。

**版本规则**：`Runtime/` 里任何会改变 `TaikoChart` 内容或规范文本的改动，都要把 `ChartCanonical.Version` 加一，重新钉住 `KnownVectorTests` 的向量，并让两站对全部谱面重算；只改标题处理、警告文字或性能的改动不升版本。`package.json` 的版本与 git tag 随每次发布更新。
