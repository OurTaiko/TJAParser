# TJA (OurTaiko) for VS Code

`tja-language-server`（`dotnet~/OurTaiko.TjaParser.LanguageServer`）的 VS Code 客户端：打开 `.tja` 即启动语言服务，显示诊断等。

```bash
cd dotnet~ && dotnet publish OurTaiko.TjaParser.LanguageServer -c Release -o ../Editors~/vscode/server
cd ../Editors~/vscode && npm install
```

设置 `tja.server.path` 为 `server/tja-language-server`（或 `server/tja-language-server.dll`，用 `dotnet` 运行）。开发时在 VS Code 中打开本目录按 F5；打包用 `npx @vscode/vsce package`。

其它支持 LSP 的编辑器（如 Monaco + monaco-languageclient）同样以标准输入输出连接这个服务。
