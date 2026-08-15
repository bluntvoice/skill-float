import { describe, expect, it } from "vitest";
import { filterSkills, initialsFor, sortSkills, type Skill } from "./skill-utils";

const skill = (partial: Partial<Skill>): Skill => ({
  invocation: "base-skill",
  name: "base-skill",
  description: "English description",
  displayName: "",
  localizedDescription: "",
  source: "本地 Skill",
  sourcePath: "C:\\Skill.md",
  favorite: false,
  category: "",
  tags: [],
  usageCount: 0,
  usageSources: {},
  ...partial,
});

describe("skill utilities", () => {
  it("searches invocation, aliases and localized descriptions with multiple terms", () => {
    const skills = [
      skill({ invocation: "github:gh-fix-ci", displayName: "修复 CI", localizedDescription: "检查工作流失败" }),
      skill({ invocation: "legal-research", displayName: "法律研究" }),
    ];
    expect(filterSkills(skills, "修复 工作流")).toHaveLength(1);
    expect(filterSkills(skills, "github ci")[0].invocation).toBe("github:gh-fix-ci");
  });

  it("sorts favorites first and then uses the visible title", () => {
    const skills = [
      skill({ invocation: "z", displayName: "资料", favorite: false }),
      skill({ invocation: "a", displayName: "案件", favorite: true }),
    ];
    expect(sortSkills(skills).map((item) => item.invocation)).toEqual(["a", "z"]);
  });

  it("creates stable two-character monograms", () => {
    expect(initialsFor(skill({ displayName: "法律 研究" }))).toBe("法研");
    expect(initialsFor(skill({ name: "frontend-design" }))).toBe("FD");
  });
});
