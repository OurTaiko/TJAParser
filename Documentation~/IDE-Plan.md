# TJA 谱面 IDE 计划书

一个跨平台（Windows / macOS / Linux）的 TJA 谱面编辑器：以文本编辑为主，配实时谱面预览与音频同步，之后逐步加入可视化编辑。解析、诊断、补全等语言能力都由本仓库的解析库提供（见 [`Editor-API.md`](Editor-API.md)），IDE 只负责界面、预览与播放。

## 1. 技术选型

| 部分 | 选择 | 理由 |
|---|---|---|
| 界面框架 | **Avalonia 11**（C#，.NET 8 或更新） | 三大桌面平台一套代码；与解析库同为 C#，直接调用、无协议层 |
| 文本编辑控件 | **AvaloniaEdit**（AvalonEdit 的 Avalonia 移植） | 自带高亮、折叠、补全弹窗、行内标记；偏移是 UTF-16、行号从 1 起，与本库 `TextSpan`／`TjaSourceText` 一致 |
| 谱面预览 | Avalonia 自绘（`Control.Render` / Skia） | 库已给出每个音符的时刻、速度、路线，以及源码 ↔ 音符的双向定位 |
| 音频 | 待技术验证：ManagedBass（BASS，**商用需授权**）或 OpenAL Soft（Silk.NET / OpenTK 绑定）+ 解码库 | 需要跨平台、能精确取播放位置；NAudio 只支持 Windows，不选 |
| 解析库 | 本仓库，作为 git submodule，`ProjectReference` 到 `dotnet~/OurTaiko.TjaParser/OurTaiko.TjaParser.csproj` | 与游戏、服务器同一份解析器，哈希口径一致 |

备选：若更看重 VS Code 级别的文本编辑或想复用网页渲染，可改为 Tauri + Monaco + `tja-language-server`（本仓库已有，经 LSP 连接，见 README「语言服务」）。下文按 Avalonia 写。

## 2. 架构

```
┌──────────── IDE（新仓库）────────────┐
│ 界面：主窗口、AvaloniaEdit、预览面板、  │
│       问题列表、块/难度切换、播放条      │
│            ▲ 绑定 / 事件               │
│ 编辑服务 EditorSession：               │
│   持有 TjaDocument 版本链；文本改动 →   │
│   WithChange；防抖后在后台线程分析、     │
│   新改动取消旧的分析；结果回到 UI 线程   │
│            ▲ 直接调用                  │
└────────────┼──────────────────────────┘
             │
   TJAParser 库（本仓库 Runtime/，netstandard2.1）
```

- **一切以 `TjaDocument` 版本为准**：每次编辑得到新快照，旧快照不变，可在任意线程读取（I1-4）。
- **后台分析**：`Analyze`、`Diagnostics`、`StateAt` 都接受 `CancellationToken`；UI 线程只读结果。
- **位置换算**：AvaloniaEdit 的 offset 与 `TextSpan.Start` 相同，行号同为从 1 起；不需要换算。

AvaloniaEdit 功能与库接口的对应：

| IDE 功能 | 库接口 |
|---|---|
| 语法高亮 | `TjaDocument.Syntax`（`TjaSyntaxToken`，按 `Kind`／`Note` 着色） |
| 波浪线、问题列表 | `TjaDocument.Diagnostics()`（中文用 `MessageIn("zh")`） |
| 折叠 | `TjaFolding.Ranges` |
| 补全 | `TjaCompletion.At` |
| 悬停提示 | `TjaHover.At`（Markdown） |
| 参数提示 | `TjaSignatureHelp.At` |
| 小节号、时刻提示 | `TjaSummary.Hints` |
| 每块摘要（音符数、连击、时长、哈希） | `TjaSummary.Blocks` |
| 快速修复、格式化、拆分/合并小节 | `TjaQuickFix`、`TjaFormat`、`TjaRefactor`（都返回 `TjaTextEdit`） |
| 光标 → 预览时刻 | `TjaAnalysis.StateAt(offset).Time` |
| 点预览音符 → 跳到源码 | `TjaAnalysis.NoteNear(time, route)` + `SourceMap.TryGetSpan` |

## 3. 里程碑

| 阶段 | 内容 | 完成标准 |
|---|---|---|
| **M0 骨架与技术验证** | 新仓库、Avalonia 应用、AvaloniaEdit 打开/保存 `.tja`（UTF-8，保留 BOM 与换行风格）、引用解析库；音频原型：播放 ogg 并取得毫秒级播放位置 | 三个平台都能启动并打开谱面；音频方案定下来 |
| **M1 文本编辑核心** | 编辑服务（版本链、防抖、后台分析、取消）；高亮、诊断与问题列表、折叠、补全、悬停、参数提示、快速修复、格式化；界面语言中/日/英 | 大型谱面每次按键流畅（库预算：重读一块约 4 ms，见 Parser-TODO「编辑器性能预算」） |
| **M2 谱面预览** | 选难度与侧别（`TjaDocument.Blocks`）、选分歧路线（`Played(route)`）；按时间滚动画音符、小节线、GOGO、分歧；光标与预览双向联动 | 预览与游戏判定时刻一致（用 `ChartNote.Time`） |
| **M3 播放与同步** | 读 `WAVE`、`OFFSET`；播放/暂停/跳转、播放头跟随、可选节拍器；从光标处播放 | 播放头与音频偏差在一帧内 |
| **M4 可视化编辑** | 在预览上点选放置/删除/改音符、拖动、改 BPM、拆分合并小节 | 需库新增「按时刻编辑」接口（Parser-TODO E1）；所有编辑都落成文本修改，可撤销 |
| **M5 打包发布** | `dotnet publish -r win-x64 / osx-arm64 / osx-x64 / linux-x64` 自包含；文件关联、设置、最近文件、自动保存 | 三平台安装包 |

## 4. 风险与对策

- **音频同步**：各平台音频延迟不同，需提供手动延迟校准；BASS 商用授权要提前确认。
- **预览性能**：长谱面只画可见时间窗内的音符（`Notes` 已按时间排序，可二分）。
- **与游戏显示不一致**：预览先求时刻正确；外观对齐游戏（OurTaikoPlay / OurTaikoView_Web）放到后期。
- **解析库变更**：IDE 固定 submodule 的提交；库的行为变更以 golden 测试与 `diff-corpus.sh` 把关（见 README）。

## 5. 新会话起步清单

1. 新建 IDE 仓库（如 `OurTaiko/TJAEditor`），`git submodule add https://github.com/OurTaiko/TJAParser.git external/TJAParser`。
2. `dotnet new` 安装 Avalonia 模板并创建 MVVM 应用；加入 NuGet 包 `Avalonia.AvaloniaEdit`。
3. 应用工程 `ProjectReference` 到 `external/TJAParser/dotnet~/OurTaiko.TjaParser/OurTaiko.TjaParser.csproj`，命名空间 `OurTaiko`。
4. 先读 [`Editor-API.md`](Editor-API.md) 的「最小示例」，把打开文件 → `TjaDocument` → 诊断显示跑通，再按 M1 逐项接入。
5. 测试谱面可用 `dotnet~/OurTaiko.TjaParser.Tests/Fixtures/*.tja` 与 `dotnet~/Corpus/ChartCorpus.cs` 生成的谱。
