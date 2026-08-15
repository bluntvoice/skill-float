import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import {
  Check,
  Edit3,
  Languages,
  LoaderCircle,
  Search,
  Sparkles,
  Star,
  WandSparkles,
  X,
} from "lucide-react";
import "./App.css";
import { TranslationCenter } from "./TranslationCenter";
import {
  isTauriRuntime,
  mockSuggestion,
  type AliasUpdate,
  type TranslationSuggestion,
} from "./translation";
import {
  MOCK_SKILLS,
  filterSkills,
  initialsFor,
  sortSkills,
  titleFor,
  type Skill,
} from "./skill-utils";

type FilterMode = "all" | "favorites";

type PasteOutcome = {
  inserted: boolean;
  copied: boolean;
};

type RuntimeInfo = {
  shortcut: string;
  fallbackUsed: boolean;
};

function App() {
  const [skills, setSkills] = useState<Skill[]>([]);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<FilterMode>("all");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [editing, setEditing] = useState<Skill | null>(null);
  const [translationOpen, setTranslationOpen] = useState(
    () => import.meta.env.DEV && !isTauriRuntime() && new URLSearchParams(window.location.search).has("translation"),
  );
  const [draftName, setDraftName] = useState("");
  const [draftDescription, setDraftDescription] = useState("");
  const [suggestion, setSuggestion] = useState<TranslationSuggestion | null>(null);
  const [suggesting, setSuggesting] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [shortcut, setShortcut] = useState("Alt+S");
  const searchRef = useRef<HTMLInputElement>(null);

  const loadSkills = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [result, runtime] = isTauriRuntime()
        ? await Promise.all([
            invoke<Skill[]>("list_skills"),
            invoke<RuntimeInfo>("runtime_info"),
          ])
        : [MOCK_SKILLS, { shortcut: "Alt+S", fallbackUsed: false }];
      setSkills(sortSkills(result));
      setShortcut(runtime.shortcut);
      if (runtime.fallbackUsed) {
        setNotice(`Alt+S 已被其他程序占用，当前唤出快捷键为 ${runtime.shortcut}`);
      }
    } catch (loadError) {
      setError(`读取 Skill 失败：${String(loadError)}`);
    } finally {
      setLoading(false);
      requestAnimationFrame(() => searchRef.current?.focus());
    }
  }, []);

  useEffect(() => {
    void loadSkills();
    if (!isTauriRuntime()) return;
    const unlisten = listen("picker-shown", () => {
      setQuery("");
      setSelectedIndex(0);
      requestAnimationFrame(() => searchRef.current?.focus());
    });
    return () => {
      void unlisten.then((dispose) => dispose());
    };
  }, [loadSkills]);

  const visibleSkills = useMemo(
    () =>
      filterSkills(
        skills.filter((skill) => filter === "all" || skill.favorite),
        query,
      ),
    [filter, query, skills],
  );

  useEffect(() => {
    setSelectedIndex((current) =>
      Math.min(current, Math.max(visibleSkills.length - 1, 0)),
    );
  }, [visibleSkills.length]);

  const hide = useCallback(async () => {
    if (isTauriRuntime()) {
      await invoke("hide_picker");
    }
  }, []);

  const runSkill = useCallback(async (skill: Skill) => {
    if (busy) return;
    setBusy(true);
    setNotice("");
    try {
      if (!isTauriRuntime()) {
        setNotice(`预览模式：将插入 $${skill.invocation}`);
        return;
      }
      const outcome = await invoke<PasteOutcome>("paste_skill", {
        invocation: skill.invocation,
      });
      if (!outcome.inserted && outcome.copied) {
        setNotice("未找到唤出前的输入框，调用文本已复制到剪贴板");
      }
    } catch (invokeError) {
      setError(`调用失败：${String(invokeError)}`);
    } finally {
      setBusy(false);
    }
  }, [busy]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (editing || translationOpen) return;
      if (event.key === "Escape") {
        event.preventDefault();
        void hide();
        return;
      }
      if (event.key === "ArrowDown") {
        event.preventDefault();
        setSelectedIndex((current) =>
          Math.min(current + 1, visibleSkills.length - 1),
        );
      } else if (event.key === "ArrowUp") {
        event.preventDefault();
        setSelectedIndex((current) => Math.max(current - 1, 0));
      } else if (event.key === "Enter" && visibleSkills[selectedIndex]) {
        event.preventDefault();
        void runSkill(visibleSkills[selectedIndex]);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [editing, hide, runSkill, selectedIndex, translationOpen, visibleSkills]);

  useEffect(() => {
    document
      .querySelector<HTMLElement>(`[data-skill-index="${selectedIndex}"]`)
      ?.scrollIntoView({ block: "nearest" });
  }, [selectedIndex]);

  const openEditor = (skill: Skill) => {
    setEditing(skill);
    setDraftName(skill.displayName);
    setDraftDescription(skill.localizedDescription);
    setSuggestion(null);
    setSuggesting(false);
    setError("");
  };

  const persistAlias = async (
    skill: Skill,
    displayName: string,
    localizedDescription: string,
    favorite: boolean,
  ) => {
    if (isTauriRuntime()) {
      await invoke("save_skill_alias", {
        invocation: skill.invocation,
        displayName,
        localizedDescription,
        favorite,
      });
    }
    setSkills((current) =>
      sortSkills(
        current.map((item) =>
          item.invocation === skill.invocation
            ? {
                ...item,
                displayName,
                localizedDescription,
                favorite,
              }
            : item,
        ),
      ),
    );
  };

  const applyBatchAliases = async (updates: AliasUpdate[]) => {
    if (isTauriRuntime()) {
      await invoke("save_skill_aliases", { updates });
    }
    const byInvocation = new Map(updates.map((update) => [update.invocation, update]));
    setSkills((current) =>
      sortSkills(
        current.map((skill) => {
          const update = byInvocation.get(skill.invocation);
          return update ? { ...skill, ...update } : skill;
        }),
      ),
    );
  };

  const recommendForEditor = async () => {
    if (!editing || suggesting) return;
    setSuggesting(true);
    setSuggestion(null);
    setError("");
    try {
      const result = isTauriRuntime()
        ? await invoke<TranslationSuggestion>("recommend_translation", {
            invocation: editing.invocation,
            name: editing.name,
            description: editing.description,
          })
        : mockSuggestion(editing);
      setSuggestion(result);
    } catch (suggestionError) {
      setError(`生成中文推荐失败：${String(suggestionError)}`);
    } finally {
      setSuggesting(false);
    }
  };

  const toggleFavorite = async (skill: Skill) => {
    try {
      await persistAlias(
        skill,
        skill.displayName,
        skill.localizedDescription,
        !skill.favorite,
      );
    } catch (favoriteError) {
      setError(`保存收藏失败：${String(favoriteError)}`);
    }
  };

  const saveEditor = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!editing) return;
    setBusy(true);
    try {
      await persistAlias(
        editing,
        draftName.trim(),
        draftDescription.trim(),
        editing.favorite,
      );
      setEditing(null);
      setNotice("中文名称与用途已保存");
      requestAnimationFrame(() => searchRef.current?.focus());
    } catch (saveError) {
      setError(`保存别名失败：${String(saveError)}`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="app-shell">
      <header className="titlebar" data-tauri-drag-region>
        <div className="brand" data-tauri-drag-region>
          <span className="brand-mark" aria-hidden="true">S</span>
          <div data-tauri-drag-region>
            <strong>Skill Float</strong>
            <span>轻按一下，调用所需能力</span>
          </div>
        </div>
        <button className="icon-button close-button" onClick={() => void hide()} aria-label="隐藏悬浮窗" title="隐藏">
          <X size={18} strokeWidth={1.8} />
        </button>
      </header>

      <section className="toolbar" aria-label="Skill 筛选">
        <label className="search-box" htmlFor="skill-search">
          <Search size={18} strokeWidth={1.8} aria-hidden="true" />
          <input
            ref={searchRef}
            id="skill-search"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setSelectedIndex(0);
            }}
            placeholder="搜索名称、中文别名或用途…"
            autoComplete="off"
            spellCheck={false}
          />
          {query && (
            <button className="clear-search" onClick={() => setQuery("")} aria-label="清空搜索">
              <X size={15} strokeWidth={2} />
            </button>
          )}
          <kbd>{shortcut.replace(/\+/g, " ")}</kbd>
        </label>
        <div className="toolbar-lower">
          <div className="filter-tabs" role="group" aria-label="显示范围">
            <button className={filter === "all" ? "active" : ""} onClick={() => setFilter("all")}>
              全部 <span>{skills.length}</span>
            </button>
            <button className={filter === "favorites" ? "active" : ""} onClick={() => setFilter("favorites")}>
              收藏 <span>{skills.filter((skill) => skill.favorite).length}</span>
            </button>
          </div>
          <button className="translation-center-button" onClick={() => { setTranslationOpen(true); setError(""); }}>
            <Languages size={15} /> AI 汉化
          </button>
        </div>
      </section>

      {(error || notice) && (
        <div className={error ? "status-banner error" : "status-banner"} role={error ? "alert" : "status"}>
          {error || notice}
          <button onClick={() => { setError(""); setNotice(""); }} aria-label="关闭提示">
            <X size={15} />
          </button>
        </div>
      )}

      <section className="skill-list" aria-label="可用 Skill" aria-busy={loading}>
        {loading ? (
          <div className="loading-list" aria-label="正在读取 Skill">
            {[0, 1, 2, 3, 4].map((item) => <span key={item} />)}
          </div>
        ) : visibleSkills.length === 0 ? (
          <div className="empty-state">
            <Sparkles size={26} strokeWidth={1.5} aria-hidden="true" />
            <strong>{filter === "favorites" ? "还没有收藏 Skill" : "没有匹配的 Skill"}</strong>
            <span>{filter === "favorites" ? "点击列表右侧的星标即可收藏" : "试试缩短关键词，或搜索真实调用名"}</span>
          </div>
        ) : (
          visibleSkills.map((skill, index) => {
            const selected = index === selectedIndex;
            const description = skill.localizedDescription || skill.description || "尚未添加用途说明";
            return (
              <article
                key={skill.invocation}
                className={`skill-row${selected ? " selected" : ""}`}
                data-skill-index={index}
                aria-current={selected ? "true" : undefined}
                onMouseEnter={() => setSelectedIndex(index)}
                onDoubleClick={() => void runSkill(skill)}
              >
                <button className="skill-main" onClick={() => void runSkill(skill)} disabled={busy}>
                  <span className="skill-monogram" aria-hidden="true">{initialsFor(skill)}</span>
                  <span className="skill-copy">
                    <span className="skill-title-line">
                      <strong>{titleFor(skill)}</strong>
                      {skill.displayName && <span className="real-name">{skill.name}</span>}
                    </span>
                    <span className="skill-description">{description}</span>
                    <span className="skill-meta"><code>${skill.invocation}</code><span>{skill.source}</span></span>
                  </span>
                </button>
                <div className="row-actions">
                  <button
                    className={`icon-button favorite-button${skill.favorite ? " active" : ""}`}
                    onClick={() => void toggleFavorite(skill)}
                    aria-label={skill.favorite ? `取消收藏 ${titleFor(skill)}` : `收藏 ${titleFor(skill)}`}
                    title={skill.favorite ? "取消收藏" : "收藏"}
                  >
                    <Star size={17} fill={skill.favorite ? "currentColor" : "none"} strokeWidth={1.8} />
                  </button>
                  <button className="icon-button" onClick={() => openEditor(skill)} aria-label={`编辑 ${titleFor(skill)} 的中文信息`} title="编辑中文名称与用途">
                    <Edit3 size={17} strokeWidth={1.8} />
                  </button>
                </div>
              </article>
            );
          })
        )}
      </section>

      <footer className="shortcut-hints">
        <span><kbd>↑</kbd><kbd>↓</kbd> 选择</span>
        <span><kbd>Enter</kbd> 插入</span>
        <span><kbd>Esc</kbd> 隐藏</span>
        <span className="result-count">显示 {visibleSkills.length} 项</span>
      </footer>

      {editing && (
        <div className="modal-backdrop" onMouseDown={(event) => {
          if (event.target === event.currentTarget) setEditing(null);
        }}>
          <form
            className="alias-dialog"
            onSubmit={saveEditor}
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                event.preventDefault();
                setEditing(null);
                return;
              }
              if (event.key !== "Tab") return;
              const focusable = Array.from(
                event.currentTarget.querySelectorAll<HTMLElement>(
                  'button:not([disabled]), input:not([disabled]), textarea:not([disabled])',
                ),
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
            }}
            role="dialog"
            aria-modal="true"
            aria-labelledby="alias-title"
          >
            <div className="dialog-heading">
              <div>
                <span className="eyebrow">显示信息</span>
                <h2 id="alias-title">汉化与重命名</h2>
                <code>${editing.invocation}</code>
              </div>
              <button type="button" className="icon-button" onClick={() => setEditing(null)} aria-label="关闭编辑窗口">
                <X size={18} />
              </button>
            </div>
            <button type="button" className="recommend-button" onClick={() => void recommendForEditor()} disabled={suggesting}>
              {suggesting ? <LoaderCircle className="spin" size={16} /> : <WandSparkles size={16} />}
              {suggesting ? "正在生成推荐…" : "生成中文简称与用途推荐"}
            </button>
            {suggestion && (
              <div className="single-suggestion">
                <div className="suggestion-heading">
                  <span>推荐结果</span>
                  <span className={`engine-badge ${suggestion.engine}`}>{suggestion.engine === "ai" ? "AI" : "本地"}</span>
                </div>
                <strong>{suggestion.shortName}</strong>
                <p>{suggestion.descriptionZh}</p>
                {suggestion.notice && <div className="fallback-note">{suggestion.notice}</div>}
                <div className="suggestion-actions">
                  <button type="button" onClick={() => setDraftName(suggestion.shortName)}>采用简称</button>
                  <button type="button" onClick={() => setDraftDescription(suggestion.descriptionZh)}>采用用途</button>
                  <button type="button" onClick={() => { setDraftName(suggestion.shortName); setDraftDescription(suggestion.descriptionZh); }}>全部采用</button>
                </div>
              </div>
            )}
            <label htmlFor="alias-name">中文名称</label>
            <input
              id="alias-name"
              value={draftName}
              onChange={(event) => setDraftName(event.target.value)}
              placeholder={editing.name}
              maxLength={80}
              autoFocus
            />
            <span className="field-help">只改变悬浮窗显示，真实调用名保持不变。</span>
            <label htmlFor="alias-description">中文用途</label>
            <textarea
              id="alias-description"
              value={draftDescription}
              onChange={(event) => setDraftDescription(event.target.value)}
              placeholder="用一句中文说明这个 Skill 主要能做什么"
              maxLength={500}
              rows={4}
            />
            <div className="original-description">
              <span>原始说明</span>
              <p>{editing.description || "此 Skill 没有提供原始说明。"}</p>
            </div>
            <div className="dialog-actions">
              <button type="button" className="secondary-button" onClick={() => setEditing(null)}>取消</button>
              <button type="submit" className="primary-button" disabled={busy}>
                <Check size={17} /> {busy ? "保存中…" : "保存显示信息"}
              </button>
            </div>
          </form>
        </div>
      )}

      {translationOpen && (
        <TranslationCenter
          skills={skills}
          onClose={() => {
            setTranslationOpen(false);
            requestAnimationFrame(() => searchRef.current?.focus());
          }}
          onApply={async (updates) => {
            await applyBatchAliases(updates);
            setNotice(`已应用 ${updates.length} 项中文推荐`);
          }}
        />
      )}
    </main>
  );
}

export default App;
