# Skill Float

Skill Float 是一个面向 Windows 的轻量级 Skill 悬浮选择器。它会读取本机 Codex Skill 与已安装插件中的 `SKILL.md`，通过全局快捷键快速唤出，并把选中的真实调用名插入到唤出前的输入框。

## 主要功能

- 快速选择：搜索真实名称、中文别名、原始说明和中文用途。
- 键盘操作：使用方向键选择，按 `Enter` 插入，按 `Esc` 隐藏。
- 中文别名：可为每个 Skill 设置独立的中文名称和用途说明。
- 自动汉化：可通过 OpenAI 兼容接口生成中文用途、推荐中文简称、分类与标签，支持单项推荐与批量补全。
- 草稿恢复：每项推荐生成后立即保存，关闭或误点后重新打开仍可恢复，不会重复生成。
- 预览应用：AI 结果不会直接覆盖现有内容，可分别勾选简称、用途、分类与标签后再应用。
- 本地回退：未配置接口或接口调用失败时，自动使用本地规则提供基础推荐。
- 安全调用：别名不会修改 Skill 文件夹、`SKILL.md` 或真实调用名。
- 收藏置顶：常用 Skill 可收藏并单独筛选。
- 悬浮与托盘：关闭窗口后保留在系统托盘，可随时再次唤出。
- 失焦最小化：点击悬浮窗之外的区域时自动最小化，快捷键可立即再次唤出。
- 调用统计：汇总 Skill Float、Codex、Claude Code 与 OpenClaw 中可识别的调用次数，并按分类展示常用 Skill。
- 快捷键回退：优先使用 `Alt+S`；被占用时依次尝试 `Alt+Shift+S` 和 `Ctrl+Alt+S`，并在界面显示实际组合。
- 剪贴板保护：成功插入后恢复原有文本剪贴板；无法恢复目标输入框时保留调用文本供手动粘贴。

## 数据与隐私

应用只在本地读取以下目录中的 `SKILL.md`：

- `%USERPROFILE%\.codex\skills`
- `%USERPROFILE%\.codex\plugins\cache`

中文别名、用途、分类、标签、推荐草稿、收藏状态和调用统计保存在应用自己的配置目录中，不会写回原始 Skill。调用统计会在本地读取 Codex、Claude Code 与 OpenClaw 可识别的 JSONL 历史文件，只保存 Skill 调用名、来源、文件增量位置与次数，不保存或上传对话正文。使用 AI 汉化时，只会向用户配置的接口发送 Skill 调用名、原始名称和原始说明；API 密钥保存在 Windows 凭据管理器中，不写入应用配置文件。未使用 AI 汉化时，应用不会上传数据。

## 开发

需要 Node.js、Rust、Tauri 2 所需的 Windows 构建工具与 WebView2。

```powershell
npm install
npm test
npm run tauri dev
```

生产构建：

```powershell
npm run tauri build
```

Windows 仅生成简体中文 NSIS `-setup.exe` 安装程序，默认按当前用户安装，无需管理员权限。

## 技术栈

- Tauri 2 / Rust
- React 19 / TypeScript
- Vite
