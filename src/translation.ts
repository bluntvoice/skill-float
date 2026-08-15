import type { Skill } from "./skill-utils";

export type TranslationSettings = {
  endpoint: string;
  model: string;
  hasApiKey: boolean;
};

export type TranslationSuggestion = {
  shortName: string;
  descriptionZh: string;
  engine: "ai" | "local";
  notice?: string | null;
};

export type AliasUpdate = {
  invocation: string;
  displayName: string;
  localizedDescription: string;
  favorite: boolean;
};

export const isTauriRuntime = () => "__TAURI_INTERNALS__" in window;

const localLabels: Array<[string, string]> = [
  ["github", "GitHub"], ["git", "Git"], ["legal", "法律"],
  ["contract", "合同"], ["research", "研究"], ["document", "文档"],
  ["pdf", "PDF"], ["image", "图像"], ["photo", "图片"],
  ["video", "视频"], ["audio", "音频"], ["ocr", "OCR"],
  ["translat", "翻译"], ["frontend", "前端"], ["design", "设计"],
  ["review", "审查"], ["analysis", "分析"], ["manager", "管理"],
  ["workflow", "流程"], ["generator", "生成"], ["fix", "修复"],
  ["ci", "CI"], ["release", "发布"], ["email", "邮件"], ["calendar", "日历"],
  ["skill", "技能"],
];

export const mockSuggestion = (skill: Skill): TranslationSuggestion => {
  const source = `${skill.invocation} ${skill.name}`.toLowerCase();
  const labels = localLabels
    .filter(([token]) => source.includes(token) && !(token === "git" && source.includes("github")))
    .map(([, label]) => label)
    .filter((label, index, values) => values.indexOf(label) === index)
    .slice(0, 3);
  const shortName = labels.join("") || "技能助手";
  const hasChinese = /[\u3400-\u9fff]/u.test(skill.description);
  return {
    shortName,
    descriptionZh: hasChinese
      ? skill.description
      : `用于${shortName}，根据 Skill 原始说明完成对应任务。`,
    engine: "local",
    notice: "浏览器预览模式使用本地推荐。",
  };
};
