import { useMemo, useRef } from "react";
import { BarChart3, Database, LoaderCircle, RefreshCw, ShieldCheck, X } from "lucide-react";
import type { Skill } from "./skill-utils";
import { titleFor } from "./skill-utils";
import type { UsageScanProgress, UsageSummary } from "./usage";

type Props = {
  skills: Skill[];
  summary: UsageSummary;
  refreshing: boolean;
  progress: UsageScanProgress | null;
  onRefresh: () => Promise<void>;
  onClose: () => void;
};

export function UsagePanel({ skills, summary, refreshing, progress, onRefresh, onClose }: Props) {
  const dialogRef = useRef<HTMLElement>(null);
  const usedSkills = skills.filter((skill) => skill.usageCount > 0);
  const categoryStats = useMemo(() => {
    const values = new Map<string, number>();
    for (const skill of skills) {
      if (!skill.usageCount) continue;
      const category = skill.category || "未分类";
      values.set(category, (values.get(category) ?? 0) + skill.usageCount);
    }
    return [...values.entries()].sort((a, b) => b[1] - a[1]);
  }, [skills]);
  const maxCategory = Math.max(1, ...categoryStats.map(([, count]) => count));
  const topSkills = [...usedSkills].sort((a, b) => b.usageCount - a.usageCount).slice(0, 50);

  return (
    <div className="modal-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget && !refreshing) onClose();
    }}>
      <section
        ref={dialogRef}
        className="usage-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="usage-title"
        onKeyDown={(event) => {
          if (event.key === "Escape" && !refreshing) {
            event.preventDefault();
            onClose();
          }
          if (event.key !== "Tab") return;
          const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>("button:not([disabled])") ?? []);
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
      >
        <div className="dialog-heading usage-heading">
          <div>
            <span className="eyebrow">本地使用概览</span>
            <h2 id="usage-title">分类与调用统计</h2>
            <span className="dialog-subtitle">同一轮中同一 Skill 只统计一次。</span>
          </div>
          <button className="icon-button" onClick={onClose} disabled={refreshing} aria-label="关闭统计面板"><X size={18} /></button>
        </div>

        <div className="usage-summary-cards">
          <div><span>总调用</span><strong>{summary.total}</strong></div>
          <div><span>已使用 Skill</span><strong>{usedSkills.length}</strong></div>
          <div><span>已识别来源</span><strong>{summary.sources.filter((source) => source.detected).length}</strong></div>
        </div>

        <div className="usage-scroll">
          <section className="usage-section">
            <div className="section-title"><Database size={15} /><strong>统计来源</strong></div>
            <div className="source-grid">
              {summary.sources.map((source) => (
                <div className={`source-item${source.detected ? " detected" : ""}`} key={source.name}>
                  <span>{source.name}</span>
                  <strong>{source.count} 次</strong>
                  <small>{source.detected ? (source.files ? `${source.files} 个历史文件` : "当前程序") : "未检测到"}</small>
                </div>
              ))}
            </div>
          </section>

          <section className="usage-section">
            <div className="section-title"><BarChart3 size={15} /><strong>分类分布</strong></div>
            {categoryStats.length ? (
              <div className="category-bars">
                {categoryStats.map(([category, count]) => (
                  <div className="category-bar" key={category}>
                    <span>{category}</span>
                    <div><i style={{ width: `${Math.max(4, count / maxCategory * 100)}%` }} /></div>
                    <strong>{count}</strong>
                  </div>
                ))}
              </div>
            ) : <div className="usage-empty">完成分类并刷新统计后，这里会显示分布。</div>}
          </section>

          <section className="usage-section">
            <div className="section-title"><strong>常用 Skill</strong><span>{topSkills.length} 项</span></div>
            <div className="usage-ranking">
              {topSkills.map((skill, index) => (
                <div key={skill.invocation}>
                  <b>{index + 1}</b>
                  <span><strong>{titleFor(skill)}</strong><small>${skill.invocation} · {skill.category || "未分类"}</small></span>
                  <em>{skill.usageCount} 次</em>
                </div>
              ))}
              {!topSkills.length && <div className="usage-empty">还没有识别到调用记录。</div>}
            </div>
          </section>
        </div>

        <div className="usage-footer">
          <div className="privacy-note"><ShieldCheck size={14} /><span>仅在本机读取调用标识，不保存或上传对话内容。</span></div>
          {refreshing && progress && <span className="scan-progress">{progress.source} · {progress.processed}/{progress.total}</span>}
          <button className="primary-button compact-button" onClick={() => void onRefresh()} disabled={refreshing} autoFocus>
            {refreshing ? <LoaderCircle className="spin" size={15} /> : <RefreshCw size={15} />}
            {refreshing ? "正在读取历史…" : "刷新统计"}
          </button>
        </div>
      </section>
    </div>
  );
}
