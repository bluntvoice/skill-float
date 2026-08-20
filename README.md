# Skill Float

> 按下 `Alt+S`，快速搜索并调用你的 Codex Skills。

Skill Float 是一个面向 Windows 的轻量原生工具。它从本机读取 Codex Skill 和已安装插件，在一个简洁的浅色面板中完成搜索、分类、收藏与调用；中文显示信息单独保存在用户数据目录，不会改写原始 `SKILL.md`。

![Skill Float 图标](assets/icon.png)

## 主要特点

- 快速调用：全局快捷键唤出，方向键选择，`Enter` 插入，`Esc` 隐藏。
- 智能搜索：文本相关性优先，精确调用名不会被历史使用次数挤到后面。
- 中文管理：支持中文简称、用途、分类、标签以及批量 AI 汉化草稿。
- 安全管理：本地 Skill 经路径校验后移入回收站；系统 Skill 禁止删除；插件 Skill 只能隐藏。
- 本地统计：可选择读取 Skill Float、Codex、Claude Code 和 OpenClaw 的本地调用痕迹。
- 原生轻量：C# WinForms 与 .NET Framework 4.8，不依赖 WebView2 或 Node.js 运行时。
- 升级保留：卸载和覆盖安装默认保留汉化、分类、收藏、API 配置和调用统计。

## 下载

当前仓库尚未提供 GitHub Release 安装包。可以按照下方“开发与构建”在本机生成简体中文 NSIS 安装程序；正式发布后，本节再补充经过校验的 Release 下载链接。

## 系统要求

- Windows 10 或 Windows 11，x64
- .NET Framework 4.8
- 使用 AI 汉化时，需要用户自行配置 OpenAI 兼容接口

## 快速开始

1. 启动 Skill Float。
2. 使用实际注册成功的全局快捷键唤出面板。默认优先尝试 `Alt+S`；如被占用，会自动尝试备用组合。
3. 输入调用名、中文名、标签、分类或用途关键词。
4. 选择 Skill 后按 `Enter`，或点击“插入”。

快捷键的实际注册结果会显示在状态栏和托盘菜单中。所有组合均注册失败时，程序会明确提示，托盘入口仍可正常使用。自定义快捷键、开机启动、AI 和统计来源统一在托盘的“设置”中管理，也可以在主窗口按 `Ctrl+,` 打开。

## Skill 来源与管理

应用只在本地扫描：

- `%USERPROFILE%\.codex\skills`
- `%USERPROFILE%\.codex\plugins\cache`

不规范或损坏的 `SKILL.md` 会被跳过；重名项采用稳定的路径优先规则。中文名、用途、分类、标签、收藏和隐藏状态不会写回 Skill 文件。

删除规则：

- 用户本地 Skill：二次确认后，将所在目录移入 Windows 回收站；
- `.system` Skill：禁止删除；
- 插件 Skill：禁止物理删除，只允许从列表隐藏，之后可以恢复显示；
- 包含符号链接或目录联接的目标：拒绝删除，避免越过允许目录。

## AI 汉化

AI 汉化支持 OpenAI 兼容的 `/chat/completions` 接口，可生成中文简称、用途、分类和标签。每项预览生成后会立即保存在本地，重新打开不会重复请求；应用前仍可逐项修改或取消。

“自动为新 Skill 分类”默认关闭。仅当用户主动开启、已配置 AI，并且发现缺少分类或标签的 Skill 时，程序才会在启动后请求接口。接口未配置或请求失败时会明确报错，不会伪装成本地 AI 推荐。

## 数据与隐私

用户数据保存在：

```text
%LOCALAPPDATA%\SkillFloat\UserData
```

其中包括中文显示信息、分类、标签、收藏、隐藏列表、AI 预览草稿、应用设置和调用统计。API Key 使用 Windows DPAPI 按当前用户范围加密保存。

调用统计在本机增量读取可识别的 JSONL 文件，只保存 Skill 调用名、来源、文件路径、增量位置、修改时间和次数；不保存聊天正文副本，也不会上传正文。用户可以分别关闭 Codex、Claude Code 和 OpenClaw 扫描。

AI 请求只发送当前 Skill 的调用名、原始名称和受长度限制的原始说明，不发送对话历史或完整 `SKILL.md`。非本机接口必须使用 HTTPS。

卸载程序只移除程序文件、快捷方式、注册信息和开机启动项，不会默认删除用户数据。重新安装后会继续读取原数据。

## 开发与构建

生产代码位于 `native/SkillFloat`，技术路线为 C#、WinForms、.NET Framework 4.8 和 NSIS。旧 Tauri/React 迁移代码已从主分支移除，仍可通过 Git 历史追溯。

构建要求：

- PowerShell 7.0 或更新稳定版；
- Visual Studio 2022 Build Tools，包含 .NET 桌面生成工具和 .NET Framework 4.8 Developer Pack；
- 生成安装器时另需系统安装的 NSIS。

```powershell
pwsh -File .\scripts\build-native.ps1
```

脚本按以下顺序定位 MSBuild：显式 `-MSBuildPath`、`vswhere.exe`、PATH、常见安装路径。NSIS 按显式 `-MakensisPath`、PATH、常见系统安装路径查找；脚本不会自动下载依赖。

只编译并运行隔离自检：

```powershell
pwsh -File .\scripts\build-native.ps1 -SkipInstaller
```

`VERSION` 是唯一产品版本来源。构建过程会把它同步到程序集版本、NSIS 产品版本及安装包文件名。CI 只执行 Windows 编译与隔离自检，不上传安装包或创建 Release。

## License

本项目采用 [MIT License](LICENSE)。
