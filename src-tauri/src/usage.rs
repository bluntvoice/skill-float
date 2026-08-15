use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{BTreeMap, BTreeSet, HashMap};
use std::fs::{self, File};
use std::io::{BufRead, BufReader, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::{Emitter, Manager};
use walkdir::WalkDir;

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default)]
struct FileCursor {
    source: String,
    path: String,
    offset: u64,
    modified_ms: u64,
    counts: BTreeMap<String, u64>,
    current_turn: String,
    seen_in_turn: BTreeSet<String>,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default)]
struct UsageStore {
    local_counts: BTreeMap<String, u64>,
    files: BTreeMap<String, FileCursor>,
    last_refreshed_at: u64,
}

pub struct UsageState(Mutex<UsageStore>);

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SkillUsageSummary {
    invocation: String,
    count: u64,
    source_counts: BTreeMap<String, u64>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UsageSourceSummary {
    name: String,
    detected: bool,
    files: usize,
    count: u64,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UsageSummary {
    total: u64,
    skills: Vec<SkillUsageSummary>,
    sources: Vec<UsageSourceSummary>,
    last_refreshed_at: u64,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct UsageScanProgress {
    processed: usize,
    total: usize,
    source: String,
}

#[derive(Clone, Debug)]
struct HistoryFile {
    key: String,
    source: String,
    path: PathBuf,
}

#[derive(Default)]
struct SkillCatalog {
    invocations: BTreeSet<String>,
    names: HashMap<String, String>,
    paths: Vec<(String, String)>,
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

fn modified_ms(metadata: &fs::Metadata) -> u64 {
    metadata
        .modified()
        .ok()
        .and_then(|value| value.duration_since(UNIX_EPOCH).ok())
        .map(|value| value.as_millis() as u64)
        .unwrap_or_default()
}

fn usage_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|dir| dir.join("usage-stats.json"))
        .map_err(|error| format!("无法确定应用配置目录：{error}"))
}

fn read_store(path: &Path) -> Result<UsageStore, String> {
    if !path.exists() {
        return Ok(UsageStore::default());
    }
    let content = fs::read_to_string(path).map_err(|error| format!("读取使用统计失败：{error}"))?;
    serde_json::from_str(&content).map_err(|error| format!("使用统计格式无效：{error}"))
}

fn write_store(path: &Path, store: &UsageStore) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| format!("创建配置目录失败：{error}"))?;
    }
    let content = serde_json::to_string_pretty(store)
        .map_err(|error| format!("生成使用统计失败：{error}"))?;
    fs::write(path, format!("{content}\n")).map_err(|error| format!("保存使用统计失败：{error}"))
}

pub fn initialize(app: &tauri::AppHandle) -> UsageState {
    let store = usage_path(app)
        .and_then(|path| read_store(&path))
        .unwrap_or_default();
    UsageState(Mutex::new(store))
}

fn normalize_path_text(value: &str) -> String {
    let mut normalized = value.replace('/', "\\").to_lowercase();
    while normalized.contains("\\\\") {
        normalized = normalized.replace("\\\\", "\\");
    }
    normalized
}

impl SkillCatalog {
    fn from_skills(skills: &[super::SkillView]) -> Self {
        let mut catalog = Self::default();
        for skill in skills {
            catalog.invocations.insert(skill.invocation.clone());
            let name_key = skill.name.to_lowercase();
            match catalog.names.get(&name_key) {
                Some(existing) if !existing.contains(':') => {}
                _ => {
                    catalog.names.insert(name_key, skill.invocation.clone());
                }
            }
            catalog.paths.push((
                normalize_path_text(&skill.source_path),
                skill.invocation.clone(),
            ));
        }
        catalog
    }

    fn match_name(&self, value: &str) -> Option<String> {
        let cleaned = value
            .trim()
            .trim_matches(|character: char| {
                character.is_whitespace()
                    || matches!(character, '$' | '"' | '\'' | '`' | ',' | ';' | '(' | ')')
            })
            .to_lowercase();
        if let Some(invocation) = self
            .invocations
            .iter()
            .find(|invocation| invocation.to_lowercase() == cleaned)
        {
            return Some(invocation.clone());
        }
        let suffix = cleaned.rsplit(':').next().unwrap_or(&cleaned);
        self.names.get(suffix).cloned()
    }

    fn match_paths(&self, value: &str) -> BTreeSet<String> {
        let normalized = normalize_path_text(value);
        let mut matches = BTreeSet::new();
        for (path, invocation) in &self.paths {
            if path.len() > 8 && normalized.contains(path) {
                matches.insert(invocation.clone());
            }
        }
        if normalized.contains("skill.md") {
            let parts: Vec<_> = normalized
                .split('\\')
                .filter(|part| !part.is_empty())
                .collect();
            for window in parts.windows(2) {
                if window[1] == "skill.md" {
                    if let Some(invocation) = self.match_name(window[0]) {
                        matches.insert(invocation);
                    }
                }
            }
        }
        matches
    }
}

fn dollar_invocations(text: &str, catalog: &SkillCatalog) -> BTreeSet<String> {
    let mut matches = BTreeSet::new();
    let characters: Vec<char> = text.chars().collect();
    let mut index = 0;
    while index < characters.len() {
        if characters[index] != '$' {
            index += 1;
            continue;
        }
        index += 1;
        let start = index;
        while index < characters.len()
            && (characters[index].is_alphanumeric()
                || matches!(characters[index], '-' | '_' | ':' | '.'))
        {
            index += 1;
        }
        if index > start {
            let token: String = characters[start..index].iter().collect();
            if let Some(invocation) = catalog.match_name(&token) {
                matches.insert(invocation);
            }
        }
    }
    matches
}

fn collect_strings(value: &Value, output: &mut Vec<String>) {
    match value {
        Value::String(text) => output.push(text.clone()),
        Value::Array(items) => {
            for item in items {
                collect_strings(item, output);
            }
        }
        Value::Object(object) => {
            for item in object.values() {
                collect_strings(item, output);
            }
        }
        _ => {}
    }
}

fn register_detected(cursor: &mut FileCursor, detected: BTreeSet<String>) {
    for invocation in detected {
        if cursor.seen_in_turn.insert(invocation.clone()) {
            *cursor.counts.entry(invocation).or_insert(0) += 1;
        }
    }
}

fn set_turn(cursor: &mut FileCursor, turn: String) {
    if !turn.is_empty() && turn != cursor.current_turn {
        cursor.current_turn = turn;
        cursor.seen_in_turn.clear();
    }
}

fn detect_codex_line(line: &str, cursor: &mut FileCursor, catalog: &SkillCatalog) {
    if !line.contains("turn_context")
        && !line.contains("user_message")
        && !line.contains("SKILL.md")
    {
        return;
    }
    let Ok(value) = serde_json::from_str::<Value>(line) else {
        return;
    };
    let record_type = value
        .get("type")
        .and_then(Value::as_str)
        .unwrap_or_default();
    let payload = value.get("payload").unwrap_or(&Value::Null);
    let payload_type = payload
        .get("type")
        .and_then(Value::as_str)
        .unwrap_or_default();
    if record_type == "turn_context" {
        set_turn(
            cursor,
            payload
                .get("turn_id")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string(),
        );
        return;
    }
    if record_type == "event_msg" && payload_type == "user_message" {
        let text = payload
            .get("message")
            .and_then(Value::as_str)
            .unwrap_or_default();
        register_detected(cursor, dollar_invocations(text, catalog));
        return;
    }
    if record_type != "response_item"
        || !matches!(payload_type, "custom_tool_call" | "function_call")
    {
        return;
    }
    let mut strings = Vec::new();
    if let Some(input) = payload.get("input") {
        collect_strings(input, &mut strings);
    }
    if let Some(arguments) = payload.get("arguments") {
        collect_strings(arguments, &mut strings);
    }
    let mut detected = BTreeSet::new();
    for text in strings {
        detected.extend(catalog.match_paths(&text));
    }
    register_detected(cursor, detected);
}

fn detect_claude_line(line: &str, cursor: &mut FileCursor, catalog: &SkillCatalog) {
    if !line.contains("\"type\":\"user\"")
        && !line.contains("\"name\":\"Skill\"")
        && !line.contains("SKILL.md")
    {
        return;
    }
    let Ok(value) = serde_json::from_str::<Value>(line) else {
        return;
    };
    let record_type = value
        .get("type")
        .and_then(Value::as_str)
        .unwrap_or_default();
    if record_type == "user" && value.get("toolUseResult").is_none() {
        let turn = value
            .get("promptId")
            .or_else(|| value.get("uuid"))
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string();
        set_turn(cursor, turn);
        let mut strings = Vec::new();
        if let Some(message) = value.get("message") {
            collect_strings(message.get("content").unwrap_or(message), &mut strings);
        }
        let mut detected = BTreeSet::new();
        for text in strings {
            detected.extend(dollar_invocations(&text, catalog));
        }
        register_detected(cursor, detected);
        return;
    }
    if record_type != "assistant" {
        return;
    }
    let Some(content) = value.pointer("/message/content").and_then(Value::as_array) else {
        return;
    };
    let mut detected = BTreeSet::new();
    for item in content {
        if item.get("type").and_then(Value::as_str) != Some("tool_use") {
            continue;
        }
        let name = item.get("name").and_then(Value::as_str).unwrap_or_default();
        if name == "Skill" {
            if let Some(skill) = item.pointer("/input/skill").and_then(Value::as_str) {
                if let Some(invocation) = catalog.match_name(skill) {
                    detected.insert(invocation);
                }
            }
        } else if name == "Read" {
            if let Some(path) = item.pointer("/input/file_path").and_then(Value::as_str) {
                detected.extend(catalog.match_paths(path));
            }
        }
    }
    register_detected(cursor, detected);
}

fn detect_openclaw_line(line: &str, cursor: &mut FileCursor, catalog: &SkillCatalog) {
    if !line.contains('$') && !line.to_lowercase().contains("skill") {
        return;
    }
    let Ok(value) = serde_json::from_str::<Value>(line) else {
        return;
    };
    let role = value
        .get("role")
        .or_else(|| value.pointer("/message/role"))
        .and_then(Value::as_str)
        .unwrap_or_default();
    if role == "user" {
        let turn = value
            .get("id")
            .or_else(|| value.get("uuid"))
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string();
        set_turn(cursor, turn);
    }
    let mut strings = Vec::new();
    collect_strings(&value, &mut strings);
    let mut detected = BTreeSet::new();
    for text in strings {
        detected.extend(dollar_invocations(&text, catalog));
        detected.extend(catalog.match_paths(&text));
    }
    register_detected(cursor, detected);
}

fn scan_file(
    file: &HistoryFile,
    cursor: &mut FileCursor,
    catalog: &SkillCatalog,
) -> Result<(), String> {
    let metadata =
        fs::metadata(&file.path).map_err(|error| format!("读取历史文件信息失败：{error}"))?;
    let length = metadata.len();
    let modified = modified_ms(&metadata);
    if length < cursor.offset || (length == cursor.offset && modified != cursor.modified_ms) {
        cursor.offset = 0;
        cursor.counts.clear();
        cursor.current_turn.clear();
        cursor.seen_in_turn.clear();
    }
    if length == cursor.offset && modified == cursor.modified_ms {
        cursor.path = file.path.to_string_lossy().into_owned();
        return Ok(());
    }
    let mut handle =
        File::open(&file.path).map_err(|error| format!("打开历史文件失败：{error}"))?;
    handle
        .seek(SeekFrom::Start(cursor.offset))
        .map_err(|error| format!("定位历史文件失败：{error}"))?;
    let mut reader = BufReader::new(handle);
    loop {
        let start = cursor.offset;
        let mut line = String::new();
        let bytes = reader
            .read_line(&mut line)
            .map_err(|error| format!("读取历史记录失败：{error}"))?;
        if bytes == 0 {
            break;
        }
        if !line.ends_with('\n') {
            cursor.offset = start;
            break;
        }
        cursor.offset += bytes as u64;
        match file.source.as_str() {
            "Codex" => detect_codex_line(&line, cursor, catalog),
            "Claude Code" => detect_claude_line(&line, cursor, catalog),
            "OpenClaw" => detect_openclaw_line(&line, cursor, catalog),
            _ => {}
        }
    }
    cursor.source = file.source.clone();
    cursor.path = file.path.to_string_lossy().into_owned();
    cursor.modified_ms = modified;
    Ok(())
}

fn add_history_files(
    files: &mut BTreeMap<String, HistoryFile>,
    source: &str,
    roots: &[PathBuf],
) -> (bool, usize) {
    let detected = roots.iter().any(|root| root.exists());
    let mut count = 0;
    for root in roots.iter().filter(|root| root.exists()) {
        for entry in WalkDir::new(root)
            .follow_links(false)
            .into_iter()
            .filter_map(Result::ok)
            .filter(|entry| entry.file_type().is_file())
            .filter(|entry| {
                entry.path().extension().and_then(|value| value.to_str()) == Some("jsonl")
            })
        {
            let path = entry.into_path();
            let file_name = path
                .file_name()
                .map(|value| value.to_string_lossy().into_owned())
                .unwrap_or_else(|| path.to_string_lossy().into_owned());
            let key = if source == "OpenClaw" {
                format!("{source}:{}", normalize_path_text(&path.to_string_lossy()))
            } else {
                format!("{source}:{file_name}")
            };
            files.insert(
                key.clone(),
                HistoryFile {
                    key,
                    source: source.to_string(),
                    path,
                },
            );
            count += 1;
        }
    }
    (detected, count)
}

fn summarize(
    store: &UsageStore,
    detected_sources: Option<&BTreeMap<String, (bool, usize)>>,
) -> UsageSummary {
    let mut per_skill: BTreeMap<String, BTreeMap<String, u64>> = BTreeMap::new();
    for (invocation, count) in &store.local_counts {
        per_skill
            .entry(invocation.clone())
            .or_default()
            .insert("Skill Float".to_string(), *count);
    }
    for cursor in store.files.values() {
        for (invocation, count) in &cursor.counts {
            *per_skill
                .entry(invocation.clone())
                .or_default()
                .entry(cursor.source.clone())
                .or_insert(0) += *count;
        }
    }
    let mut source_counts: BTreeMap<String, u64> = BTreeMap::new();
    let mut skills = Vec::new();
    for (invocation, counts) in per_skill {
        let count: u64 = counts.values().sum();
        for (source, value) in &counts {
            *source_counts.entry(source.clone()).or_insert(0) += value;
        }
        skills.push(SkillUsageSummary {
            invocation,
            count,
            source_counts: counts,
        });
    }
    skills.sort_by(|a, b| {
        b.count
            .cmp(&a.count)
            .then_with(|| a.invocation.cmp(&b.invocation))
    });
    let mut sources = Vec::new();
    for name in ["Skill Float", "Codex", "Claude Code", "OpenClaw"] {
        let (detected, files) = if name == "Skill Float" {
            (true, 0)
        } else {
            detected_sources
                .and_then(|values| values.get(name))
                .copied()
                .unwrap_or_else(|| {
                    let files = store
                        .files
                        .values()
                        .filter(|cursor| cursor.source == name)
                        .count();
                    (files > 0, files)
                })
        };
        sources.push(UsageSourceSummary {
            name: name.to_string(),
            detected,
            files,
            count: source_counts.get(name).copied().unwrap_or_default(),
        });
    }
    UsageSummary {
        total: skills.iter().map(|skill| skill.count).sum(),
        skills,
        sources,
        last_refreshed_at: store.last_refreshed_at,
    }
}

fn refresh_sync(app: &tauri::AppHandle, store: &mut UsageStore) -> Result<UsageSummary, String> {
    let home = super::app_home()?;
    let skills = super::discover_skills()?;
    let catalog = SkillCatalog::from_skills(&skills);
    let mut files = BTreeMap::new();
    let mut detected_sources = BTreeMap::new();
    let codex = add_history_files(
        &mut files,
        "Codex",
        &[
            home.join(".codex").join("sessions"),
            home.join(".codex").join("archived_sessions"),
        ],
    );
    detected_sources.insert("Codex".to_string(), codex);
    let claude = add_history_files(
        &mut files,
        "Claude Code",
        &[
            home.join(".claude").join("projects"),
            home.join(".claude").join("sessions"),
        ],
    );
    detected_sources.insert("Claude Code".to_string(), claude);
    let openclaw = add_history_files(
        &mut files,
        "OpenClaw",
        &[
            home.join(".openclaw"),
            home.join(".config").join("openclaw"),
        ],
    );
    detected_sources.insert("OpenClaw".to_string(), openclaw);

    let active_keys: BTreeSet<_> = files.keys().cloned().collect();
    store.files.retain(|key, _| active_keys.contains(key));
    let total = files.len();
    for (index, file) in files.into_values().enumerate() {
        let cursor = store
            .files
            .entry(file.key.clone())
            .or_insert_with(|| FileCursor {
                source: file.source.clone(),
                path: file.path.to_string_lossy().into_owned(),
                ..FileCursor::default()
            });
        scan_file(&file, cursor, &catalog)?;
        if index % 5 == 0 || index + 1 == total {
            let _ = app.emit(
                "usage-scan-progress",
                UsageScanProgress {
                    processed: index + 1,
                    total,
                    source: file.source,
                },
            );
        }
    }
    store.last_refreshed_at = now_ms();
    write_store(&usage_path(app)?, store)?;
    Ok(summarize(store, Some(&detected_sources)))
}

#[tauri::command]
pub fn get_usage_stats(state: tauri::State<UsageState>) -> Result<UsageSummary, String> {
    let store = state
        .0
        .lock()
        .map_err(|_| "使用统计状态不可用".to_string())?;
    Ok(summarize(&store, None))
}

#[tauri::command]
pub async fn refresh_usage_stats(app: tauri::AppHandle) -> Result<UsageSummary, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let state = app.state::<UsageState>();
        let mut snapshot = state
            .0
            .lock()
            .map_err(|_| "使用统计状态不可用".to_string())?
            .clone();
        let summary = refresh_sync(&app, &mut snapshot)?;
        let mut current = state
            .0
            .lock()
            .map_err(|_| "使用统计状态不可用".to_string())?;
        snapshot.local_counts = current.local_counts.clone();
        *current = snapshot;
        write_store(&usage_path(&app)?, &current)?;
        let mut final_summary = summarize(&current, None);
        final_summary.sources = summary.sources;
        let _ = app.emit("usage-stats-updated", final_summary.clone());
        Ok(final_summary)
    })
    .await
    .map_err(|error| format!("刷新使用统计失败：{error}"))?
}

pub fn record_skill_usage(app: &tauri::AppHandle, invocation: &str) -> Result<u64, String> {
    let state = app.state::<UsageState>();
    let mut store = state
        .0
        .lock()
        .map_err(|_| "使用统计状态不可用".to_string())?;
    *store
        .local_counts
        .entry(invocation.to_string())
        .or_insert(0) += 1;
    write_store(&usage_path(app)?, &store)?;
    let summary = summarize(&store, None);
    Ok(summary
        .skills
        .into_iter()
        .find(|skill| skill.invocation == invocation)
        .map(|skill| skill.count)
        .unwrap_or_default())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn catalog() -> SkillCatalog {
        SkillCatalog {
            invocations: BTreeSet::from([
                "frontend-design".to_string(),
                "github:gh-fix-ci".to_string(),
            ]),
            names: HashMap::from([
                ("frontend-design".to_string(), "frontend-design".to_string()),
                ("gh-fix-ci".to_string(), "github:gh-fix-ci".to_string()),
            ]),
            paths: vec![(
                normalize_path_text(r"C:\Users\demo\.codex\skills\frontend-design\SKILL.md"),
                "frontend-design".to_string(),
            )],
        }
    }

    #[test]
    fn extracts_only_known_dollar_invocations() {
        let found = dollar_invocations(
            "使用 $frontend-design 和 $unknown，再用 $github:gh-fix-ci。",
            &catalog(),
        );
        assert_eq!(
            found,
            BTreeSet::from([
                "frontend-design".to_string(),
                "github:gh-fix-ci".to_string()
            ])
        );
    }

    #[test]
    fn codex_deduplicates_explicit_and_auto_use_in_one_turn() {
        let mut cursor = FileCursor::default();
        let catalog = catalog();
        detect_codex_line(
            r#"{"type":"turn_context","payload":{"turn_id":"t1"}}"#,
            &mut cursor,
            &catalog,
        );
        detect_codex_line(
            r#"{"type":"event_msg","payload":{"type":"user_message","message":"用 $frontend-design"}}"#,
            &mut cursor,
            &catalog,
        );
        detect_codex_line(
            r#"{"type":"response_item","payload":{"type":"custom_tool_call","name":"exec","input":"Get-Content C:\\\\Users\\\\demo\\\\.codex\\\\skills\\\\frontend-design\\\\SKILL.md"}}"#,
            &mut cursor,
            &catalog,
        );
        assert_eq!(cursor.counts.get("frontend-design"), Some(&1));
    }

    #[test]
    fn claude_skill_tool_counts_auto_trigger() {
        let mut cursor = FileCursor::default();
        let line = r#"{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Skill","input":{"skill":"frontend-design"}}]}}"#;
        detect_claude_line(line, &mut cursor, &catalog());
        assert_eq!(cursor.counts.get("frontend-design"), Some(&1));
    }
}
