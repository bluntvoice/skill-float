export type Skill = {
  invocation: string;
  name: string;
  description: string;
  displayName: string;
  localizedDescription: string;
  source: string;
  sourcePath: string;
  favorite: boolean;
  category: string;
  tags: string[];
  usageCount: number;
  usageSources: Record<string, number>;
};

export const titleFor = (skill: Skill) => skill.displayName || skill.name;

export const initialsFor = (skill: Skill) => {
  const words = titleFor(skill)
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .trim()
    .split(/\s+/)
    .filter(Boolean);
  if (words.length === 0) return "SK";
  if (words.length === 1) return Array.from(words[0]).slice(0, 2).join("").toUpperCase();
  return `${Array.from(words[0])[0]}${Array.from(words[1])[0]}`.toUpperCase();
};

export const filterSkills = (skills: Skill[], query: string) => {
  const normalized = query.trim().toLocaleLowerCase("zh-CN");
  if (!normalized) return skills;
  const terms = normalized.split(/\s+/).filter(Boolean);
  return skills.filter((skill) => {
    const haystack = [
      skill.invocation,
      skill.name,
      skill.displayName,
      skill.description,
      skill.localizedDescription,
      skill.source,
      skill.category,
      ...skill.tags,
    ]
      .join(" ")
      .toLocaleLowerCase("zh-CN");
    return terms.every((term) => haystack.includes(term));
  });
};

export const sortSkills = (skills: Skill[]) =>
  [...skills].sort(
    (a, b) =>
      Number(b.favorite) - Number(a.favorite) ||
      titleFor(a).localeCompare(titleFor(b), "zh-CN", { sensitivity: "base" }),
  );

export const MOCK_SKILLS: Skill[] = sortSkills([
  {
    invocation: "frontend-design",
    name: "frontend-design",
    description: "Guidance for distinctive, intentional visual design when building new UI.",
    displayName: "前端视觉设计",
    localizedDescription: "为新界面建立有辨识度且克制的视觉方向，并检查排版、颜色与细节。",
    source: "本地 Skill",
    sourcePath: "C:\\Users\\demo\\.codex\\skills\\frontend-design\\SKILL.md",
    favorite: true,
    category: "开发与代码",
    tags: ["前端", "设计"],
    usageCount: 18,
    usageSources: { Codex: 11, "Skill Float": 7 },
  },
  {
    invocation: "github:gh-fix-ci",
    name: "gh-fix-ci",
    description: "Debug and fix failing GitHub Actions checks.",
    displayName: "修复 GitHub CI",
    localizedDescription: "定位 GitHub Actions 失败原因，提出并验证修复。",
    source: "插件 · github",
    sourcePath: "C:\\Users\\demo\\.codex\\plugins\\github\\SKILL.md",
    favorite: true,
    category: "开发与代码",
    tags: ["GitHub", "CI"],
    usageCount: 12,
    usageSources: { Codex: 9, "Claude Code": 3 },
  },
  {
    invocation: "legal-research",
    name: "legal-research",
    description: "生成中国法律研究报告（Markdown 格式）。",
    displayName: "法律研究",
    localizedDescription: "围绕中国法律问题检索法规与案例，并形成结构化研究报告。",
    source: "本地 Skill",
    sourcePath: "C:\\Users\\demo\\.codex\\skills\\legal-research\\SKILL.md",
    favorite: false,
    category: "法律与专业",
    tags: ["法律", "研究"],
    usageCount: 9,
    usageSources: { Codex: 9 },
  },
  {
    invocation: "documents:documents",
    name: "documents",
    description: "Create, edit, redline, and comment on professional documents.",
    displayName: "文档处理",
    localizedDescription: "创建、编辑和审阅 Word 等专业文档。",
    source: "插件 · documents",
    sourcePath: "C:\\Users\\demo\\.codex\\plugins\\documents\\SKILL.md",
    favorite: false,
    category: "文档与内容",
    tags: ["文档", "Word"],
    usageCount: 6,
    usageSources: { "Skill Float": 2, Codex: 4 },
  },
]);
