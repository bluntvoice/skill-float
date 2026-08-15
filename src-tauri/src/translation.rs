use keyring::{Entry, Error as KeyringError};
use reqwest::Url;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::{BTreeMap, BTreeSet};
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::Manager;

const DEFAULT_ENDPOINT: &str = "https://api.openai.com/v1";
const CREDENTIAL_SERVICE: &str = "com.bluntvoice.skill-float";
const CREDENTIAL_USER: &str = "translation-api-key";

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
struct StoredTranslationSettings {
    endpoint: String,
    model: String,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TranslationSettingsView {
    endpoint: String,
    model: String,
    has_api_key: bool,
}

const CATEGORIES: [&str; 7] = [
    "开发与代码",
    "文档与内容",
    "设计与多媒体",
    "数据与自动化",
    "法律与专业",
    "沟通与协作",
    "其他",
];

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TranslationSuggestion {
    short_name: String,
    description_zh: String,
    category: String,
    tags: Vec<String>,
    engine: String,
    notice: Option<String>,
}

#[derive(Debug, Deserialize)]
struct AiSuggestion {
    short_name: String,
    description_zh: String,
    category: String,
    tags: Vec<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TranslationDraft {
    invocation: String,
    suggestion: TranslationSuggestion,
    generated_at: u64,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default)]
struct TranslationDraftStore {
    drafts: BTreeMap<String, TranslationDraft>,
}

fn settings_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|dir| dir.join("translation-settings.json"))
        .map_err(|error| format!("无法确定应用配置目录：{error}"))
}

fn drafts_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|dir| dir.join("translation-drafts.json"))
        .map_err(|error| format!("无法确定应用配置目录：{error}"))
}

fn read_drafts(path: &Path) -> Result<TranslationDraftStore, String> {
    if !path.exists() {
        return Ok(TranslationDraftStore::default());
    }
    let content = fs::read_to_string(path).map_err(|error| format!("读取推荐草稿失败：{error}"))?;
    serde_json::from_str(&content).map_err(|error| format!("推荐草稿格式无效：{error}"))
}

fn write_drafts(path: &Path, store: &TranslationDraftStore) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| format!("创建配置目录失败：{error}"))?;
    }
    let content = serde_json::to_string_pretty(store)
        .map_err(|error| format!("生成推荐草稿失败：{error}"))?;
    fs::write(path, format!("{content}\n")).map_err(|error| format!("保存推荐草稿失败：{error}"))
}

fn save_draft(
    app: &tauri::AppHandle,
    invocation: &str,
    suggestion: &TranslationSuggestion,
) -> Result<(), String> {
    let path = drafts_path(app)?;
    let mut store = read_drafts(&path)?;
    store.drafts.insert(
        invocation.to_string(),
        TranslationDraft {
            invocation: invocation.to_string(),
            suggestion: suggestion.clone(),
            generated_at: SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap_or_default()
                .as_millis() as u64,
        },
    );
    write_drafts(&path, &store)
}

fn read_settings(path: &Path) -> Result<StoredTranslationSettings, String> {
    if !path.exists() {
        return Ok(StoredTranslationSettings {
            endpoint: DEFAULT_ENDPOINT.to_string(),
            model: String::new(),
        });
    }
    let content = fs::read_to_string(path).map_err(|error| format!("读取翻译设置失败：{error}"))?;
    let mut settings: StoredTranslationSettings =
        serde_json::from_str(&content).map_err(|error| format!("翻译设置格式无效：{error}"))?;
    if settings.endpoint.trim().is_empty() {
        settings.endpoint = DEFAULT_ENDPOINT.to_string();
    }
    Ok(settings)
}

fn write_settings(path: &Path, settings: &StoredTranslationSettings) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| format!("创建配置目录失败：{error}"))?;
    }
    let content = serde_json::to_string_pretty(settings)
        .map_err(|error| format!("生成翻译设置失败：{error}"))?;
    fs::write(path, format!("{content}\n")).map_err(|error| format!("保存翻译设置失败：{error}"))
}

fn credential_entry() -> Result<Entry, String> {
    Entry::new(CREDENTIAL_SERVICE, CREDENTIAL_USER)
        .map_err(|error| format!("无法访问 Windows 凭据管理器：{error}"))
}

fn read_api_key() -> Result<Option<String>, String> {
    match credential_entry()?.get_password() {
        Ok(value) if !value.trim().is_empty() => Ok(Some(value)),
        Ok(_) | Err(KeyringError::NoEntry) => Ok(None),
        Err(error) => Err(format!("读取 API 密钥失败：{error}")),
    }
}

fn store_api_key(api_key: Option<String>, clear: bool) -> Result<(), String> {
    let entry = credential_entry()?;
    if clear {
        return match entry.delete_credential() {
            Ok(()) | Err(KeyringError::NoEntry) => Ok(()),
            Err(error) => Err(format!("清除 API 密钥失败：{error}")),
        };
    }
    if let Some(value) = api_key
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
    {
        entry
            .set_password(&value)
            .map_err(|error| format!("保存 API 密钥失败：{error}"))?;
    }
    Ok(())
}

fn validate_endpoint(value: &str) -> Result<Url, String> {
    let url = Url::parse(value.trim()).map_err(|_| "接口地址格式无效".to_string())?;
    let host = url.host_str().unwrap_or_default();
    let is_local = matches!(host, "localhost" | "127.0.0.1" | "::1");
    if url.scheme() != "https" && !(url.scheme() == "http" && is_local) {
        return Err("接口地址必须使用 HTTPS；本机 localhost 可使用 HTTP".to_string());
    }
    if host.is_empty() {
        return Err("接口地址缺少主机名".to_string());
    }
    Ok(url)
}

fn chat_completions_url(endpoint: &str) -> Result<Url, String> {
    let mut url = validate_endpoint(endpoint)?;
    let current = url.path().trim_end_matches('/');
    if !current.ends_with("/chat/completions") {
        let next = if current.is_empty() {
            "/chat/completions".to_string()
        } else {
            format!("{current}/chat/completions")
        };
        url.set_path(&next);
    }
    Ok(url)
}

fn normalize(value: &str, max_chars: usize) -> String {
    value
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .chars()
        .take(max_chars)
        .collect::<String>()
        .trim_matches(|character: char| {
            character == '"' || character == '\'' || character.is_whitespace()
        })
        .to_string()
}

fn contains_chinese(value: &str) -> bool {
    value
        .chars()
        .any(|character| matches!(character as u32, 0x3400..=0x9fff))
}

fn normalize_category(value: &str) -> String {
    let normalized = normalize(value, 40);
    CATEGORIES
        .iter()
        .find(|category| **category == normalized)
        .copied()
        .unwrap_or("其他")
        .to_string()
}

fn normalize_tags(values: Vec<String>) -> Vec<String> {
    let mut tags = BTreeSet::new();
    for value in values {
        let tag = normalize(&value, 12);
        if !tag.is_empty() {
            tags.insert(tag);
        }
        if tags.len() == 4 {
            break;
        }
    }
    tags.into_iter().collect()
}

fn offline_classification(
    invocation: &str,
    name: &str,
    description: &str,
) -> (String, Vec<String>) {
    let source = format!("{invocation} {name} {description}").to_lowercase();
    let category = if [
        "legal",
        "law",
        "court",
        "contract",
        "litigation",
        "patent",
        "trademark",
    ]
    .iter()
    .any(|token| source.contains(token))
    {
        "法律与专业"
    } else if [
        "image",
        "photo",
        "video",
        "audio",
        "design",
        "svg",
        "ppt",
        "presentation",
    ]
    .iter()
    .any(|token| source.contains(token))
    {
        "设计与多媒体"
    } else if [
        "document", "article", "book", "pdf", "word", "write", "content",
    ]
    .iter()
    .any(|token| source.contains(token))
    {
        "文档与内容"
    } else if ["calendar", "email", "gmail", "slack", "teams", "meeting"]
        .iter()
        .any(|token| source.contains(token))
    {
        "沟通与协作"
    } else if [
        "data",
        "spreadsheet",
        "excel",
        "automation",
        "workflow",
        "batch",
    ]
    .iter()
    .any(|token| source.contains(token))
    {
        "数据与自动化"
    } else if [
        "code", "git", "github", "frontend", "react", "plugin", "release", "ci",
    ]
    .iter()
    .any(|token| source.contains(token))
    {
        "开发与代码"
    } else {
        "其他"
    };
    let tag_rules = [
        ("github", "GitHub"),
        ("git", "Git"),
        ("legal", "法律"),
        ("contract", "合同"),
        ("document", "文档"),
        ("pdf", "PDF"),
        ("image", "图像"),
        ("video", "视频"),
        ("audio", "音频"),
        ("ocr", "OCR"),
        ("translation", "翻译"),
        ("design", "设计"),
        ("research", "研究"),
        ("review", "审查"),
        ("automation", "自动化"),
        ("workflow", "流程"),
        ("email", "邮件"),
        ("calendar", "日历"),
    ];
    let mut tags = Vec::new();
    for (token, tag) in tag_rules {
        if source.contains(token)
            && !(token == "git" && source.contains("github"))
            && !tags.contains(&tag.to_string())
        {
            tags.push(tag.to_string());
        }
        if tags.len() == 4 {
            break;
        }
    }
    if tags.is_empty() {
        tags.push("通用".to_string());
    }
    (category.to_string(), tags)
}

fn offline_short_name(invocation: &str, name: &str) -> String {
    let source = format!("{invocation} {name}").to_lowercase();
    let rules = [
        ("github", "GitHub"),
        ("git", "Git"),
        ("legal", "法律"),
        ("contract", "合同"),
        ("research", "研究"),
        ("document", "文档"),
        ("pdf", "PDF"),
        ("image", "图像"),
        ("photo", "图片"),
        ("video", "视频"),
        ("audio", "音频"),
        ("transcri", "转录"),
        ("ocr", "OCR"),
        ("translation", "翻译"),
        ("translate", "翻译"),
        ("frontend", "前端"),
        ("design", "设计"),
        ("review", "审查"),
        ("analysis", "分析"),
        ("manager", "管理"),
        ("organizer", "整理"),
        ("workflow", "流程"),
        ("generator", "生成"),
        ("create", "创建"),
        ("fix", "修复"),
        ("ci", "CI"),
        ("release", "发布"),
        ("email", "邮件"),
        ("calendar", "日历"),
        ("presentation", "演示文稿"),
        ("spreadsheet", "表格"),
        ("skill", "技能"),
    ];
    let mut parts = Vec::new();
    for (token, label) in rules {
        if token == "git" && source.contains("github") {
            continue;
        }
        if source.contains(token) && !parts.contains(&label) {
            parts.push(label);
        }
        if parts.len() == 3 {
            break;
        }
    }
    if parts.is_empty() {
        "技能助手".to_string()
    } else {
        parts.join("")
    }
}

fn local_suggestion(
    invocation: &str,
    name: &str,
    description: &str,
    notice: Option<String>,
) -> TranslationSuggestion {
    let short_name = offline_short_name(invocation, name);
    let description_zh = if contains_chinese(description) {
        normalize(description, 240)
    } else if description.trim().is_empty() {
        format!("用于{short_name}，帮助处理与 {name} 相关的任务。")
    } else {
        format!("用于{short_name}，根据 Skill 原始说明完成对应任务。")
    };
    let (category, tags) = offline_classification(invocation, name, description);
    TranslationSuggestion {
        short_name,
        description_zh,
        category,
        tags,
        engine: "local".to_string(),
        notice,
    }
}

fn extract_suggestion(content: &str) -> Result<AiSuggestion, String> {
    let start = content
        .find('{')
        .ok_or_else(|| "AI 返回内容不含 JSON".to_string())?;
    let end = content
        .rfind('}')
        .ok_or_else(|| "AI 返回内容不完整".to_string())?;
    if end < start {
        return Err("AI 返回内容不完整".to_string());
    }
    let raw: AiSuggestion = serde_json::from_str(&content[start..=end])
        .map_err(|_| "AI 返回内容格式无效".to_string())?;
    let short_name = normalize(&raw.short_name, 24);
    let description_zh = normalize(&raw.description_zh, 300);
    if short_name.is_empty() || description_zh.is_empty() || !contains_chinese(&description_zh) {
        return Err("AI 未返回有效的中文推荐".to_string());
    }
    Ok(AiSuggestion {
        short_name,
        description_zh,
        category: normalize_category(&raw.category),
        tags: normalize_tags(raw.tags),
    })
}

async fn ai_suggestion(
    endpoint: &str,
    model: &str,
    api_key: &str,
    invocation: &str,
    name: &str,
    description: &str,
) -> Result<TranslationSuggestion, String> {
    let url = chat_completions_url(endpoint)?;
    let payload = json!({
        "model": model,
        "temperature": 0.2,
        "messages": [
            {
                "role": "system",
                "content": "你是软件能力名称本地化与分类助手。返回严格 JSON：{\"short_name\":\"推荐中文简称\",\"description_zh\":\"简洁中文用途\",\"category\":\"分类\",\"tags\":[\"标签1\",\"标签2\"]}。分类只能从：开发与代码、文档与内容、设计与多媒体、数据与自动化、法律与专业、沟通与协作、其他 中选择。标签给 2-4 个简短中文词。简称优先 2-8 个汉字，可保留 AI、API、GitHub、PDF 等必要缩写；用途使用一到两句自然中文，不夸大原始能力，不加入原文没有的功能。"
            },
            {
                "role": "user",
                "content": format!("调用名：{}\n原始名称：{}\n原始说明：{}", normalize(invocation, 160), normalize(name, 160), normalize(description, 1600))
            }
        ]
    });
    let response = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(40))
        .build()
        .map_err(|error| format!("创建网络请求失败：{error}"))?
        .post(url)
        .bearer_auth(api_key)
        .json(&payload)
        .send()
        .await
        .map_err(|error| format!("AI 接口请求失败：{error}"))?;
    let status = response.status();
    let body = response
        .text()
        .await
        .map_err(|error| format!("读取 AI 响应失败：{error}"))?;
    if !status.is_success() {
        let detail = normalize(&body, 180);
        return Err(if detail.is_empty() {
            format!("AI 接口返回 {status}")
        } else {
            format!("AI 接口返回 {status}：{detail}")
        });
    }
    let value: Value =
        serde_json::from_str(&body).map_err(|_| "AI 接口返回的响应不是 JSON".to_string())?;
    let content = value
        .pointer("/choices/0/message/content")
        .and_then(Value::as_str)
        .ok_or_else(|| "AI 接口响应缺少推荐内容".to_string())?;
    let result = extract_suggestion(content)?;
    Ok(TranslationSuggestion {
        short_name: result.short_name,
        description_zh: result.description_zh,
        category: result.category,
        tags: result.tags,
        engine: "ai".to_string(),
        notice: None,
    })
}

#[tauri::command]
pub fn get_translation_settings(app: tauri::AppHandle) -> Result<TranslationSettingsView, String> {
    let settings = read_settings(&settings_path(&app)?)?;
    Ok(TranslationSettingsView {
        endpoint: settings.endpoint,
        model: settings.model,
        has_api_key: read_api_key()?.is_some(),
    })
}

#[tauri::command]
pub fn save_translation_settings(
    app: tauri::AppHandle,
    endpoint: String,
    model: String,
    api_key: Option<String>,
    clear_api_key: bool,
) -> Result<TranslationSettingsView, String> {
    let endpoint = endpoint.trim().trim_end_matches('/').to_string();
    validate_endpoint(&endpoint)?;
    let model = normalize(&model, 120);
    if model.is_empty() {
        return Err("请填写模型名称".to_string());
    }
    write_settings(
        &settings_path(&app)?,
        &StoredTranslationSettings {
            endpoint: endpoint.clone(),
            model: model.clone(),
        },
    )?;
    store_api_key(api_key, clear_api_key)?;
    Ok(TranslationSettingsView {
        endpoint,
        model,
        has_api_key: read_api_key()?.is_some(),
    })
}

#[tauri::command]
pub async fn recommend_translation(
    app: tauri::AppHandle,
    invocation: String,
    name: String,
    description: String,
) -> Result<TranslationSuggestion, String> {
    let settings = read_settings(&settings_path(&app)?)?;
    let suggestion = match read_api_key() {
        Err(error) => local_suggestion(
            &invocation,
            &name,
            &description,
            Some(format!("{error}，已使用本地推荐。")),
        ),
        Ok(None) => local_suggestion(
            &invocation,
            &name,
            &description,
            Some("尚未配置 API 密钥，已使用本地推荐。".to_string()),
        ),
        Ok(Some(_)) if settings.model.trim().is_empty() => local_suggestion(
            &invocation,
            &name,
            &description,
            Some("尚未配置模型名称，已使用本地推荐。".to_string()),
        ),
        Ok(Some(api_key)) => match ai_suggestion(
            &settings.endpoint,
            &settings.model,
            &api_key,
            &invocation,
            &name,
            &description,
        )
        .await
        {
            Ok(result) => result,
            Err(error) => local_suggestion(
                &invocation,
                &name,
                &description,
                Some(format!("{error}，已使用本地推荐。")),
            ),
        },
    };
    save_draft(&app, &invocation, &suggestion)?;
    Ok(suggestion)
}

#[tauri::command]
pub fn list_translation_drafts(app: tauri::AppHandle) -> Result<Vec<TranslationDraft>, String> {
    let mut drafts: Vec<_> = read_drafts(&drafts_path(&app)?)?
        .drafts
        .into_values()
        .collect();
    drafts.sort_by(|a, b| b.generated_at.cmp(&a.generated_at));
    Ok(drafts)
}

#[tauri::command]
pub fn delete_translation_drafts(
    app: tauri::AppHandle,
    invocations: Vec<String>,
) -> Result<(), String> {
    let path = drafts_path(&app)?;
    let mut store = read_drafts(&path)?;
    for invocation in invocations {
        store.drafts.remove(invocation.trim());
    }
    write_drafts(&path, &store)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_https_and_local_http_endpoints() {
        assert!(validate_endpoint("https://api.openai.com/v1").is_ok());
        assert!(validate_endpoint("http://localhost:11434/v1").is_ok());
        assert!(validate_endpoint("http://example.com/v1").is_err());
    }

    #[test]
    fn appends_chat_completions_path_once() {
        assert_eq!(
            chat_completions_url("https://example.com/v1")
                .unwrap()
                .as_str(),
            "https://example.com/v1/chat/completions"
        );
        assert_eq!(
            chat_completions_url("https://example.com/v1/chat/completions")
                .unwrap()
                .as_str(),
            "https://example.com/v1/chat/completions"
        );
    }

    #[test]
    fn parses_json_wrapped_in_markdown() {
        let parsed = extract_suggestion("```json\n{\"short_name\":\"合同审查\",\"description_zh\":\"审阅合同并提示风险。\",\"category\":\"法律与专业\",\"tags\":[\"合同\",\"审查\"]}\n```").unwrap();
        assert_eq!(parsed.short_name, "合同审查");
        assert_eq!(parsed.description_zh, "审阅合同并提示风险。");
        assert_eq!(parsed.category, "法律与专业");
    }

    #[test]
    fn local_name_uses_known_tokens() {
        assert_eq!(
            offline_short_name("github:gh-fix-ci", "gh-fix-ci"),
            "GitHub修复CI"
        );
        assert_eq!(
            offline_short_name("legal-contract-review", "legal-contract-review"),
            "法律合同审查"
        );
    }
}
