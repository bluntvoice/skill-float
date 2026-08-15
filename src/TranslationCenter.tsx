import { useEffect, useMemo, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import {
  Check,
  KeyRound,
  Languages,
  LoaderCircle,
  Settings2,
  ShieldCheck,
  Square,
  X,
} from "lucide-react";
import type { Skill } from "./skill-utils";
import {
  isTauriRuntime,
  mockSuggestion,
  type AliasUpdate,
  type TranslationSettings,
  type TranslationSuggestion,
  type TranslationDraft,
} from "./translation";

type CenterTab = "batch" | "settings";

type ReviewItem = {
  skill: Skill;
  suggestion: TranslationSuggestion;
  applyName: boolean;
  applyDescription: boolean;
  applyClassification: boolean;
};

type TranslationCenterProps = {
  skills: Skill[];
  onClose: () => void;
  onApply: (updates: AliasUpdate[]) => Promise<void>;
};

const DEFAULT_SETTINGS: TranslationSettings = {
  endpoint: "https://api.openai.com/v1",
  model: "",
  hasApiKey: false,
};

export function TranslationCenter({ skills, onClose, onApply }: TranslationCenterProps) {
  const [tab, setTab] = useState<CenterTab>(() =>
    import.meta.env.DEV && new URLSearchParams(window.location.search).get("tab") === "settings"
      ? "settings"
      : "batch",
  );
  const [includeExisting, setIncludeExisting] = useState(false);
  const [reviews, setReviews] = useState<ReviewItem[]>([]);
  const [running, setRunning] = useState(false);
  const [applying, setApplying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [settings, setSettings] = useState(DEFAULT_SETTINGS);
  const [endpoint, setEndpoint] = useState(DEFAULT_SETTINGS.endpoint);
  const [model, setModel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [clearApiKey, setClearApiKey] = useState(false);
  const [savingSettings, setSavingSettings] = useState(false);
  const stopRequested = useRef(false);
  const dialogRef = useRef<HTMLElement>(null);

  const candidates = useMemo(
    () => skills.filter((skill) => includeExisting || !skill.displayName || !skill.localizedDescription || !skill.category || skill.tags.length === 0),
    [includeExisting, skills],
  );
  const pendingCandidates = useMemo(() => {
    const drafted = new Set(reviews.map((item) => item.skill.invocation));
    return candidates.filter((skill) => !drafted.has(skill.invocation));
  }, [candidates, reviews]);

  useEffect(() => {
    const load = async () => {
      try {
        const [loaded, drafts] = isTauriRuntime()
          ? await Promise.all([
              invoke<TranslationSettings>("get_translation_settings"),
              invoke<TranslationDraft[]>("list_translation_drafts"),
            ])
          : [DEFAULT_SETTINGS, [] as TranslationDraft[]];
        setSettings(loaded);
        setEndpoint(loaded.endpoint);
        setModel(loaded.model);
        if (drafts.length) {
          const byInvocation = new Map(skills.map((skill) => [skill.invocation, skill]));
          const restored = drafts.flatMap((draft) => {
            const skill = byInvocation.get(draft.invocation);
            return skill ? [{
              skill,
              suggestion: draft.suggestion,
              applyName: !skill.displayName,
              applyDescription: !skill.localizedDescription,
              applyClassification: !skill.category || skill.tags.length === 0,
            }] : [];
          });
          setReviews(restored);
          setProgress(0);
          setNotice(`已自动恢复 ${restored.length} 项推荐草稿。`);
        }
      } catch (loadError) {
        setError(`读取接口设置失败：${String(loadError)}`);
      }
    };
    void load();
  }, []);

  const requestSuggestion = async (skill: Skill) => {
    if (!isTauriRuntime()) return mockSuggestion(skill);
    return invoke<TranslationSuggestion>("recommend_translation", {
      invocation: skill.invocation,
      name: skill.name,
      description: skill.description,
    });
  };

  const generateBatch = async () => {
    if (pendingCandidates.length === 0 || running) return;
    setRunning(true);
    setProgress(0);
    setError("");
    setNotice("");
    stopRequested.current = false;
    const targets = [...pendingCandidates];
    const next: ReviewItem[] = [...reviews];
    for (let index = 0; index < targets.length; index += 1) {
      if (stopRequested.current) break;
      const skill = targets[index];
      try {
        const suggestion = await requestSuggestion(skill);
        next.push({
          skill,
          suggestion,
          applyName: !skill.displayName,
          applyDescription: !skill.localizedDescription,
          applyClassification: !skill.category || skill.tags.length === 0,
        });
        setReviews([...next]);
        setProgress(index + 1);
      } catch (suggestionError) {
        setError(`生成 ${skill.name} 的推荐失败：${String(suggestionError)}`);
        break;
      }
    }
    if (stopRequested.current) setNotice(`已停止，当前共保留 ${next.length} 项预览。`);
    setRunning(false);
  };

  const toggleReview = (index: number, field: "applyName" | "applyDescription" | "applyClassification") => {
    setReviews((current) =>
      current.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [field]: !item[field] } : item,
      ),
    );
  };

  const selectedCount = reviews.filter((item) => item.applyName || item.applyDescription || item.applyClassification).length;

  const applyReviews = async () => {
    const selected = reviews.filter((item) => item.applyName || item.applyDescription || item.applyClassification);
    if (selected.length === 0) return;
    const updates: AliasUpdate[] = selected.map(({ skill, suggestion, applyName, applyDescription, applyClassification }) => ({
      invocation: skill.invocation,
      displayName: applyName ? suggestion.shortName : skill.displayName,
      localizedDescription: applyDescription ? suggestion.descriptionZh : skill.localizedDescription,
      favorite: skill.favorite,
      category: applyClassification ? suggestion.category : skill.category,
      tags: applyClassification ? suggestion.tags : skill.tags,
    }));
    setApplying(true);
    setError("");
    try {
      await onApply(updates);
      if (isTauriRuntime()) {
        await invoke("delete_translation_drafts", { invocations: updates.map((update) => update.invocation) }).catch(() => undefined);
      }
      setNotice(`已应用 ${updates.length} 项中文推荐。`);
      setReviews([]);
    } catch (applyError) {
      setError(`批量保存失败：${String(applyError)}`);
    } finally {
      setApplying(false);
    }
  };

  const saveSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setSavingSettings(true);
    setError("");
    setNotice("");
    try {
      const saved = isTauriRuntime()
        ? await invoke<TranslationSettings>("save_translation_settings", {
            endpoint,
            model,
            apiKey: apiKey.trim() || null,
            clearApiKey,
          })
        : { endpoint, model, hasApiKey: Boolean(apiKey) && !clearApiKey };
      setSettings(saved);
      setApiKey("");
      setClearApiKey(false);
      setNotice("AI 接口设置已保存。密钥已单独存入 Windows 凭据管理器。");
    } catch (saveError) {
      setError(`保存接口设置失败：${String(saveError)}`);
    } finally {
      setSavingSettings(false);
    }
  };

  const closeIfIdle = () => {
    if (!running && !applying && !savingSettings) onClose();
  };

  return (
    <div className="modal-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) closeIfIdle();
    }}>
      <section
        ref={dialogRef}
        className="translation-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="translation-title"
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.preventDefault();
            closeIfIdle();
            return;
          }
          if (event.key === "Tab") {
            const focusable = Array.from(
              dialogRef.current?.querySelectorAll<HTMLElement>(
                'button:not([disabled]), input:not([disabled])',
              ) ?? [],
            );
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
              event.preventDefault();
              last?.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
              event.preventDefault();
              first?.focus();
            }
          }
        }}
      >
        <div className="dialog-heading translation-heading">
          <div>
            <span className="eyebrow">中文推荐中心</span>
            <h2 id="translation-title">自动汉化 Skill</h2>
            <span className="dialog-subtitle">逐项预览后再应用，不修改真实调用名。</span>
          </div>
          <button type="button" className="icon-button" onClick={closeIfIdle} aria-label="关闭中文推荐中心" disabled={running || applying || savingSettings}>
            <X size={18} />
          </button>
        </div>

        <div className="center-tabs" role="tablist" aria-label="中文推荐中心功能">
          <button type="button" role="tab" aria-selected={tab === "batch"} className={tab === "batch" ? "active" : ""} onClick={() => setTab("batch")} disabled={running || applying || savingSettings}>
            <Languages size={16} /> 批量推荐
          </button>
          <button type="button" role="tab" aria-selected={tab === "settings"} className={tab === "settings" ? "active" : ""} onClick={() => setTab("settings")} disabled={running || applying || savingSettings}>
            <Settings2 size={16} /> 接口设置
          </button>
        </div>

        {(error || notice) && (
          <div className={error ? "dialog-message error" : "dialog-message"} role={error ? "alert" : "status"}>
            {error || notice}
          </div>
        )}

        {tab === "batch" ? (
          <div className="batch-panel" role="tabpanel">
            <div className="batch-controls">
              <label className="check-control">
                <input type="checkbox" checked={includeExisting} onChange={(event) => {
                  setIncludeExisting(event.target.checked);
                  setProgress(0);
                }} disabled={running} />
                <span>包括已有汉化</span>
              </label>
              <span className="candidate-count">待生成 {pendingCandidates.length} 项</span>
              {running ? (
                <button type="button" className="secondary-button compact-button" onClick={() => { stopRequested.current = true; }}>
                  <Square size={14} fill="currentColor" /> 停止
                </button>
              ) : (
                <button type="button" className="primary-button compact-button" onClick={() => void generateBatch()} disabled={pendingCandidates.length === 0} autoFocus>
                  <Languages size={15} /> {reviews.length ? "继续生成" : "生成预览"}
                </button>
              )}
            </div>

            {(running || progress > 0) && (
              <div className="batch-progress" aria-live="polite">
                <div><span>本次生成进度</span><strong>{progress} / {Math.max(progress + pendingCandidates.length, progress)}</strong></div>
                <progress value={progress} max={Math.max(progress + pendingCandidates.length, 1)} />
              </div>
            )}

            <div className="review-list" aria-busy={running}>
              {reviews.length === 0 ? (
                <div className="review-empty">
                  {running ? <LoaderCircle className="spin" size={24} /> : <Languages size={24} />}
                  <strong>{running ? "正在生成第一项推荐…" : "还没有推荐预览"}</strong>
                  <span>每项生成后会自动保存，关闭窗口后仍可恢复。</span>
                </div>
              ) : reviews.map((item, index) => (
                <article className="review-card" key={item.skill.invocation}>
                  <div className="review-title">
                    <div><strong>{item.skill.name}</strong><code>${item.skill.invocation}</code></div>
                    <div className="review-badges"><span className="draft-badge">已保存</span><span className={`engine-badge ${item.suggestion.engine}`}>{item.suggestion.engine === "ai" ? "AI" : "本地"}</span></div>
                  </div>
                  <label className="review-option">
                    <input type="checkbox" checked={item.applyName} onChange={() => toggleReview(index, "applyName")} />
                    <span><small>推荐简称</small><strong>{item.suggestion.shortName}</strong></span>
                    {item.skill.displayName && <em>当前：{item.skill.displayName}</em>}
                  </label>
                  <label className="review-option description-option">
                    <input type="checkbox" checked={item.applyDescription} onChange={() => toggleReview(index, "applyDescription")} />
                    <span><small>推荐用途</small><p>{item.suggestion.descriptionZh}</p></span>
                  </label>
                  <label className="review-option classification-option">
                    <input type="checkbox" checked={item.applyClassification} onChange={() => toggleReview(index, "applyClassification")} />
                    <span><small>推荐分类与标签</small><strong>{item.suggestion.category}</strong><p>{item.suggestion.tags.join(" · ")}</p></span>
                    {item.skill.category && <em>当前：{item.skill.category}</em>}
                  </label>
                  {item.suggestion.notice && <div className="fallback-note">{item.suggestion.notice}</div>}
                </article>
              ))}
            </div>

            <div className="dialog-actions sticky-actions">
              <span>已选择 {selectedCount} 项</span>
              <button type="button" className="secondary-button" onClick={closeIfIdle} disabled={running || applying}>取消</button>
              <button type="button" className="primary-button" onClick={() => void applyReviews()} disabled={running || applying || selectedCount === 0}>
                <Check size={16} /> {applying ? "应用中…" : "应用所选推荐"}
              </button>
            </div>
          </div>
        ) : (
          <form className="settings-panel" role="tabpanel" onSubmit={saveSettings}>
            <div className="security-note">
              <ShieldCheck size={18} />
              <div><strong>仅发送 Skill 元数据</strong><span>请求只包含调用名、原始名称和原始说明；API 密钥不写入配置文件。</span></div>
            </div>
            <label htmlFor="api-endpoint">OpenAI 兼容接口地址</label>
            <input id="api-endpoint" type="url" value={endpoint} onChange={(event) => setEndpoint(event.target.value)} placeholder="https://api.openai.com/v1" required />
            <span className="field-help">支持完整的 /chat/completions 地址；localhost 可使用 HTTP。</span>
            <label htmlFor="api-model">模型名称</label>
            <input id="api-model" value={model} onChange={(event) => setModel(event.target.value)} placeholder="例如：gpt-4.1-mini" maxLength={120} required />
            <label htmlFor="api-key">API 密钥</label>
            <div className="key-input">
              <KeyRound size={17} />
              <input id="api-key" type="password" value={apiKey} onChange={(event) => setApiKey(event.target.value)} placeholder={settings.hasApiKey ? "已保存；留空则保持不变" : "输入 API 密钥"} autoComplete="new-password" disabled={clearApiKey} />
            </div>
            <label className="check-control clear-key-control">
              <input type="checkbox" checked={clearApiKey} onChange={(event) => setClearApiKey(event.target.checked)} />
              <span>清除已保存的 API 密钥</span>
            </label>
            <div className="dialog-actions settings-actions">
              <button type="button" className="secondary-button" onClick={closeIfIdle}>取消</button>
              <button type="submit" className="primary-button" disabled={savingSettings}>
                <Check size={16} /> {savingSettings ? "保存中…" : "保存接口设置"}
              </button>
            </div>
          </form>
        )}
      </section>
    </div>
  );
}
