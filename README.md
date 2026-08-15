# Skill Float

Skill Float 是一个面向 Windows 的轻量级 Skill 悬浮选择器。它会读取本机 Codex Skill 与已安装插件中的 `SKILL.md`，通过全局快捷键快速唤出，并把选中的真实调用名插入到唤出前的输入框。

## 主要功能

- 快速选择：搜索真实名称、中文别名、原始说明和中文用途。
- 键盘操作：使用方向键选择，按 `Enter` 插入，按 `Esc` 隐藏。
- 中文别名：可为每个 Skill 设置独立的中文名称和用途说明。
- 安全调用：别名不会修改 Skill 文件夹、`SKILL.md` 或真实调用名。
- 收藏置顶：常用 Skill 可收藏并单独筛选。
- 悬浮与托盘：关闭窗口后保留在系统托盘，可随时再次唤出。
- 快捷键回退：优先使用 `Alt+S`；被占用时依次尝试 `Alt+Shift+S` 和 `Ctrl+Alt+S`，并在界面显示实际组合。
- 剪贴板保护：成功插入后恢复原有文本剪贴板；无法恢复目标输入框时保留调用文本供手动粘贴。

## 数据与隐私

应用只在本地读取以下目录中的 `SKILL.md`：

- `%USERPROFILE%\.codex\skills`
- `%USERPROFILE%\.codex\plugins\cache`

中文别名、用途和收藏状态保存在应用自己的配置目录中，不会写回原始 Skill，也不会上传数据。

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

## 技术栈

- Tauri 2 / Rust
- React 19 / TypeScript
- Vite
