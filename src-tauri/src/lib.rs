mod translation;

use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashMap};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::thread;
use std::time::Duration;
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{Emitter, Manager};
use tauri_plugin_clipboard_manager::ClipboardExt;
use tauri_plugin_global_shortcut::{GlobalShortcutExt, ShortcutState};
use walkdir::WalkDir;

use translation::{get_translation_settings, recommend_translation, save_translation_settings};

const SHORTCUT_CANDIDATES: [&str; 3] = ["Alt+S", "Alt+Shift+S", "Ctrl+Alt+S"];

struct FocusTarget(Mutex<isize>);

struct RuntimeState {
    shortcut: String,
    fallback_used: bool,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct AliasEntry {
    display_name: String,
    localized_description: String,
    favorite: bool,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
struct AliasStore {
    skills: BTreeMap<String, AliasEntry>,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AliasUpdate {
    invocation: String,
    display_name: String,
    localized_description: String,
    favorite: bool,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SkillView {
    invocation: String,
    name: String,
    description: String,
    display_name: String,
    localized_description: String,
    source: String,
    source_path: String,
    favorite: bool,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct PasteOutcome {
    inserted: bool,
    copied: bool,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeInfo {
    shortcut: String,
    fallback_used: bool,
}

fn trim_yaml_value(value: &str) -> String {
    let trimmed = value.trim();
    if trimmed.len() >= 2 {
        let first = trimmed.as_bytes()[0];
        let last = trimmed.as_bytes()[trimmed.len() - 1];
        if (first == b'"' && last == b'"') || (first == b'\'' && last == b'\'') {
            return trimmed[1..trimmed.len() - 1].trim().to_string();
        }
    }
    trimmed.to_string()
}

fn parse_frontmatter(content: &str) -> Option<(String, String)> {
    let mut lines = content.lines();
    if lines.next()?.trim() != "---" {
        return None;
    }

    let frontmatter: Vec<_> = lines.take_while(|line| line.trim() != "---").collect();
    let mut name = None;
    let mut description = String::new();
    let mut index = 0;
    while index < frontmatter.len() {
        let raw_line = frontmatter[index];
        let line = raw_line.trim();
        if let Some(value) = line.strip_prefix("name:") {
            name = Some(trim_yaml_value(value));
        } else if let Some(value) = line.strip_prefix("description:") {
            let value = value.trim();
            if matches!(value, ">" | ">-" | "|" | "|-") {
                let preserve_newlines = value.starts_with('|');
                let mut chunks = Vec::new();
                index += 1;
                while index < frontmatter.len() {
                    let continuation = frontmatter[index];
                    if !continuation.trim().is_empty()
                        && !continuation.starts_with(' ')
                        && !continuation.starts_with('\t')
                    {
                        index -= 1;
                        break;
                    }
                    let value = continuation.trim();
                    if !value.is_empty() {
                        chunks.push(value);
                    }
                    index += 1;
                }
                description = if preserve_newlines {
                    chunks.join("\n")
                } else {
                    chunks.join(" ")
                };
            } else {
                description = trim_yaml_value(value);
            }
        }
        index += 1;
    }

    let name = name?.trim().to_string();
    if name.is_empty() {
        None
    } else {
        Some((name, description))
    }
}

fn app_home() -> Result<PathBuf, String> {
    std::env::var_os("USERPROFILE")
        .or_else(|| std::env::var_os("HOME"))
        .map(PathBuf::from)
        .ok_or_else(|| "无法确定当前用户目录".to_string())
}

fn plugin_name(cache_root: &Path, skill_file: &Path) -> Option<String> {
    let components: Vec<_> = skill_file
        .strip_prefix(cache_root)
        .ok()?
        .components()
        .map(|part| part.as_os_str().to_string_lossy().into_owned())
        .collect();
    if components.len() < 5 || components.get(3).map(String::as_str) != Some("skills") {
        return None;
    }
    components.get(1).cloned().filter(|value| !value.is_empty())
}

fn collect_skill_files(root: &Path) -> Vec<PathBuf> {
    if !root.exists() {
        return Vec::new();
    }
    WalkDir::new(root)
        .follow_links(false)
        .into_iter()
        .filter_map(Result::ok)
        .filter(|entry| entry.file_type().is_file() && entry.file_name() == "SKILL.md")
        .map(|entry| entry.into_path())
        .collect()
}

fn discover_skills() -> Result<Vec<SkillView>, String> {
    let home = app_home()?;
    let local_root = home.join(".codex").join("skills");
    let plugin_root = home.join(".codex").join("plugins").join("cache");
    let mut discovered: HashMap<String, (usize, SkillView)> = HashMap::new();

    for path in collect_skill_files(&local_root) {
        let Ok(content) = fs::read_to_string(&path) else {
            continue;
        };
        let Some((name, description)) = parse_frontmatter(&content) else {
            continue;
        };
        let depth = path
            .strip_prefix(&local_root)
            .map(|p| p.components().count())
            .unwrap_or(99);
        let source = if path.starts_with(local_root.join(".system")) {
            "系统 Skill".to_string()
        } else {
            "本地 Skill".to_string()
        };
        let view = SkillView {
            invocation: name.clone(),
            name,
            description,
            display_name: String::new(),
            localized_description: String::new(),
            source,
            source_path: path.to_string_lossy().into_owned(),
            favorite: false,
        };
        match discovered.get(&view.invocation) {
            Some((existing_depth, _)) if *existing_depth <= depth => {}
            _ => {
                discovered.insert(view.invocation.clone(), (depth, view));
            }
        }
    }

    for path in collect_skill_files(&plugin_root) {
        let Some(plugin) = plugin_name(&plugin_root, &path) else {
            continue;
        };
        let Ok(content) = fs::read_to_string(&path) else {
            continue;
        };
        let Some((name, description)) = parse_frontmatter(&content) else {
            continue;
        };
        let invocation = if name.contains(':') {
            name.clone()
        } else {
            format!("{plugin}:{name}")
        };
        let view = SkillView {
            invocation: invocation.clone(),
            name,
            description,
            display_name: String::new(),
            localized_description: String::new(),
            source: format!("插件 · {plugin}"),
            source_path: path.to_string_lossy().into_owned(),
            favorite: false,
        };
        discovered.entry(invocation).or_insert((99, view));
    }

    let mut skills: Vec<_> = discovered.into_values().map(|(_, view)| view).collect();
    skills.sort_by(|a, b| {
        a.invocation
            .to_lowercase()
            .cmp(&b.invocation.to_lowercase())
    });
    Ok(skills)
}

fn aliases_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|dir| dir.join("aliases.json"))
        .map_err(|error| format!("无法确定应用配置目录：{error}"))
}

fn read_aliases(path: &Path) -> Result<AliasStore, String> {
    if !path.exists() {
        return Ok(AliasStore::default());
    }
    let content = fs::read_to_string(path).map_err(|error| format!("读取别名配置失败：{error}"))?;
    serde_json::from_str(&content).map_err(|error| format!("别名配置格式无效：{error}"))
}

fn write_aliases(path: &Path, store: &AliasStore) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| format!("创建配置目录失败：{error}"))?;
    }
    let content = serde_json::to_string_pretty(store)
        .map_err(|error| format!("生成别名配置失败：{error}"))?;
    fs::write(path, format!("{content}\n")).map_err(|error| format!("保存别名配置失败：{error}"))
}

fn validate_alias_update(update: &AliasUpdate) -> Result<(), String> {
    let invocation = update.invocation.trim();
    if invocation.is_empty() || invocation.len() > 160 {
        return Err("Skill 调用名无效".to_string());
    }
    if update.display_name.trim().chars().count() > 80 {
        return Err(format!("{} 的中文名称不能超过 80 个字符", invocation));
    }
    if update.localized_description.trim().chars().count() > 500 {
        return Err(format!("{} 的中文用途不能超过 500 个字符", invocation));
    }
    Ok(())
}

fn apply_alias_update(store: &mut AliasStore, update: &AliasUpdate) {
    let invocation = update.invocation.trim();
    let display_name = update.display_name.trim();
    let localized_description = update.localized_description.trim();
    if display_name.is_empty() && localized_description.is_empty() && !update.favorite {
        store.skills.remove(invocation);
    } else {
        store.skills.insert(
            invocation.to_string(),
            AliasEntry {
                display_name: display_name.to_string(),
                localized_description: localized_description.to_string(),
                favorite: update.favorite,
            },
        );
    }
}

#[tauri::command]
fn list_skills(app: tauri::AppHandle) -> Result<Vec<SkillView>, String> {
    let aliases = read_aliases(&aliases_path(&app)?)?;
    let mut skills = discover_skills()?;
    for skill in &mut skills {
        if let Some(alias) = aliases.skills.get(&skill.invocation) {
            skill.display_name = alias.display_name.clone();
            skill.localized_description = alias.localized_description.clone();
            skill.favorite = alias.favorite;
        }
    }
    skills.sort_by(|a, b| {
        b.favorite.cmp(&a.favorite).then_with(|| {
            let a_name = if a.display_name.is_empty() {
                &a.name
            } else {
                &a.display_name
            };
            let b_name = if b.display_name.is_empty() {
                &b.name
            } else {
                &b.display_name
            };
            a_name.to_lowercase().cmp(&b_name.to_lowercase())
        })
    });
    Ok(skills)
}

#[tauri::command]
fn save_skill_alias(
    app: tauri::AppHandle,
    invocation: String,
    display_name: String,
    localized_description: String,
    favorite: bool,
) -> Result<(), String> {
    let update = AliasUpdate {
        invocation,
        display_name,
        localized_description,
        favorite,
    };
    validate_alias_update(&update)?;
    let path = aliases_path(&app)?;
    let mut store = read_aliases(&path)?;
    apply_alias_update(&mut store, &update);
    write_aliases(&path, &store)
}

#[tauri::command]
fn save_skill_aliases(app: tauri::AppHandle, updates: Vec<AliasUpdate>) -> Result<(), String> {
    if updates.len() > 1000 {
        return Err("一次最多保存 1000 项 Skill".to_string());
    }
    for update in &updates {
        validate_alias_update(update)?;
    }
    let path = aliases_path(&app)?;
    let mut store = read_aliases(&path)?;
    for update in &updates {
        apply_alias_update(&mut store, update);
    }
    write_aliases(&path, &store)
}

fn valid_invocation(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 160
        && value.chars().all(|character| {
            character.is_alphanumeric() || matches!(character, '-' | '_' | ':' | '.')
        })
}

#[cfg(windows)]
fn foreground_window() -> isize {
    use windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow;
    unsafe { GetForegroundWindow() as isize }
}

#[cfg(not(windows))]
fn foreground_window() -> isize {
    0
}

#[cfg(windows)]
fn focus_window(target: isize) -> bool {
    use windows_sys::Win32::UI::WindowsAndMessaging::{IsWindow, SetForegroundWindow};
    if target == 0 {
        return false;
    }
    unsafe {
        let window = target as _;
        IsWindow(window) != 0 && SetForegroundWindow(window) != 0
    }
}

#[cfg(not(windows))]
fn focus_window(_target: isize) -> bool {
    false
}

#[cfg(windows)]
fn send_paste_shortcut() {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
        keybd_event, KEYEVENTF_KEYUP, VK_CONTROL,
    };
    unsafe {
        keybd_event(VK_CONTROL as u8, 0, 0, 0);
        keybd_event(b'V', 0, 0, 0);
        keybd_event(b'V', 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL as u8, 0, KEYEVENTF_KEYUP, 0);
    }
}

#[cfg(not(windows))]
fn send_paste_shortcut() {}

fn show_picker(app: &tauri::AppHandle, capture_focus: bool) {
    if capture_focus {
        if let Some(state) = app.try_state::<FocusTarget>() {
            if let Ok(mut target) = state.0.lock() {
                *target = foreground_window();
            }
        }
    }
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
        let _ = app.emit("picker-shown", ());
    }
}

#[tauri::command]
fn hide_picker(app: tauri::AppHandle) -> Result<(), String> {
    let window = app
        .get_webview_window("main")
        .ok_or_else(|| "找不到悬浮窗".to_string())?;
    window.hide().map_err(|error| error.to_string())
}

#[tauri::command]
fn runtime_info(state: tauri::State<RuntimeState>) -> RuntimeInfo {
    RuntimeInfo {
        shortcut: state.shortcut.clone(),
        fallback_used: state.fallback_used,
    }
}

#[tauri::command]
fn paste_skill(
    app: tauri::AppHandle,
    target: tauri::State<FocusTarget>,
    invocation: String,
) -> Result<PasteOutcome, String> {
    if !valid_invocation(&invocation) {
        return Err("Skill 调用名包含不支持的字符".to_string());
    }
    let text = format!("${invocation} ");
    let previous_clipboard = app.clipboard().read_text().ok();
    app.clipboard()
        .write_text(text)
        .map_err(|error| format!("写入剪贴板失败：{error}"))?;

    if let Some(window) = app.get_webview_window("main") {
        let _ = window.hide();
    }
    let target = target.0.lock().map(|value| *value).unwrap_or_default();
    thread::sleep(Duration::from_millis(90));
    let inserted = focus_window(target);
    if inserted {
        thread::sleep(Duration::from_millis(80));
        send_paste_shortcut();
        thread::sleep(Duration::from_millis(350));
        if let Some(previous) = previous_clipboard {
            let _ = app.clipboard().write_text(previous);
        }
    } else {
        show_picker(&app, false);
    }

    Ok(PasteOutcome {
        inserted,
        copied: true,
    })
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(FocusTarget(Mutex::new(0)))
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            show_picker(app, false);
        }))
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _, event| {
                    if event.state == ShortcutState::Pressed {
                        show_picker(app, true);
                    }
                })
                .build(),
        )
        .setup(|app| {
            let mut registered_shortcut = None;
            let mut registration_errors = Vec::new();
            for candidate in SHORTCUT_CANDIDATES {
                match app.global_shortcut().register(candidate) {
                    Ok(()) => {
                        registered_shortcut = Some(candidate.to_string());
                        break;
                    }
                    Err(error) => registration_errors.push(format!("{candidate}：{error}")),
                }
            }
            let shortcut = registered_shortcut
                .ok_or_else(|| format!("无法注册全局快捷键：{}", registration_errors.join("；")))?;
            app.manage(RuntimeState {
                fallback_used: shortcut != SHORTCUT_CANDIDATES[0],
                shortcut,
            });

            let open = MenuItem::with_id(app, "open", "打开 Skill Float", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "退出", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&open, &quit])?;
            let mut tray = TrayIconBuilder::new()
                .tooltip("Skill Float · 快速调用 Skill")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "open" => show_picker(app, false),
                    "quit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        show_picker(tray.app_handle(), false);
                    }
                });
            if let Some(icon) = app.default_window_icon().cloned() {
                tray = tray.icon(icon);
            }
            tray.build(app)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .invoke_handler(tauri::generate_handler![
            list_skills,
            save_skill_alias,
            save_skill_aliases,
            get_translation_settings,
            save_translation_settings,
            recommend_translation,
            paste_skill,
            hide_picker,
            runtime_info
        ])
        .run(tauri::generate_context!())
        .expect("Skill Float 启动失败");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_quoted_frontmatter() {
        let parsed = parse_frontmatter(
            "---\nname: \"skill-name\"\ndescription: 'A useful skill'\n---\n# Body",
        );
        assert_eq!(parsed, Some(("skill-name".into(), "A useful skill".into())));
    }

    #[test]
    fn parses_folded_multiline_descriptions() {
        let parsed = parse_frontmatter(
            "---\nname: agency-opinion\ndescription: >-\n  民商事诉讼代理意见统一起草器，\n  覆盖多个审级。\nlicense: MIT\n---",
        );
        assert_eq!(
            parsed,
            Some((
                "agency-opinion".into(),
                "民商事诉讼代理意见统一起草器， 覆盖多个审级。".into()
            ))
        );
    }

    #[test]
    fn rejects_missing_or_empty_names() {
        assert!(parse_frontmatter("# no frontmatter").is_none());
        assert!(parse_frontmatter("---\nname: \"\"\n---").is_none());
    }

    #[test]
    fn validates_safe_invocation_names() {
        assert!(valid_invocation("github:gh-fix-ci"));
        assert!(valid_invocation("photo-abstract-editorial"));
        assert!(!valid_invocation("skill name"));
        assert!(!valid_invocation("name;$env:PATH"));
    }

    #[test]
    fn extracts_plugin_name_from_cache_layout() {
        let root = Path::new(r"C:\Users\demo\.codex\plugins\cache");
        let file = root.join(r"openai-curated-remote\github\1.0.0\skills\github\SKILL.md");
        assert_eq!(plugin_name(root, &file).as_deref(), Some("github"));
    }
}
