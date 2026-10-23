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
  SongInfo.cs                头部与块列表（Easy/Normal/Hard/Oni/Edit × single/p1/p2）
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
"org.ourtaiko.tjaparser": "https://github.com/OurTaiko/TJAParser.git#v0.1.0"
```

程序集 `OurTaiko.TjaParser` 自动引用（`autoReferenced`），命名空间仍是 `OurTaiko`，游戏代码不需要改 `using`。游戏锁定 tag；服务器端 CLI 用同一个 tag 构建，哈希口径才一致。

## 命令行

```bash
cd dotnet~ && dotnet build TJAParser.slnx && dotnet test TJAParser.slnx
```

```
tjaparser hash <file|->                       所有块的 course、side、level、branching 与哈希（JSON）
tjaparser canonical <file|-> [course] [side]  某一块被哈希的规范文本（排查两份谱面为何不同）
tjaparser version                             算法版本，如 v1
```

输入必须是 UTF-8（可带 BOM）；后端先做自己的 UTF-8 规范化再调用。退出码：0 完成（解析失败的块在 JSON 里带 `error`、无 `hash`），2 用法错误，3 读不到输入或不是 UTF-8，4 `canonical` 指定的块不存在或解析失败。

```json
{"version":1,"prefix":"v1:","blocks":[
  {"course":"Oni","side":"single","level":10,"branching":false,"hash":"v1:848c…9a47"},
  {"course":"Edit","side":"p1","level":10,"branching":true,"error":"Long note is missing its 8 tail …"}]}
```

块的列表与顺序和 `SongInfo.Blocks` 一致：每个难度每个侧别只取文件里第一块，按难度、再按 single/p1/p2 排序；`COURSE:3`、`COURSE:Ura` 等写法归一为 `Oni`、`Edit`；未知 COURSE 的块游戏列不出，也不在列表里。

发布到服务器：

```bash
cd dotnet~ && dotnet publish tjaparser -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o out
```

## 规范文本与哈希

`ChartCanonical.Write(chart)` 把 `TjaParser.Parse` 产出的 `TaikoChart` 写成固定顺序、固定写法的文本，`ChartHash.Of` 对其 UTF-8 字节求 SHA-256，前缀 `v1:`。

写进去的：BPM、OFFSET、时长；每个音符与小节线的种类、时刻、结束时刻、拍、BPM、SCROLL 实部虚部与模式、节拍时钟、SUDDEN、气球数、所属分歧与路线、是否显示；分歧的条件、阈值、大音符限定、l 范围、LEVELHOLD、路线映射、各路线首个物件；SECTION、BPM 时钟点、JPOSSCROLL、GOGO、节拍锚点；以及游戏实际遍历的 NoteLists（公共段与每条路线）。

不写的：TITLE、SUBTITLE 及译名、MAKER、GENRE、WAVE、DEMOSTART、COURSE 与 LEVEL（块由 course/side 作键，星数是元数据）、解析警告、音符下的文字帧（NoteMoji，由音符推得）。

数字用最短往返十进制（`R`，InvariantCulture），负零写 `0`，无穷写 `inf`。枚举写整数值。

**版本规则**：`Runtime/` 里任何会改变 `TaikoChart` 内容或规范文本的改动，都要把 `ChartCanonical.Version` 加一，重新钉住 `KnownVectorTests` 的向量，并让两站对全部谱面重算；只改标题处理、警告文字或性能的改动不升版本。`package.json` 的版本与 git tag 随每次发布更新。
