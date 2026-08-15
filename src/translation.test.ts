import { describe, expect, it } from "vitest";
import { mockSuggestion } from "./translation";
import type { Skill } from "./skill-utils";

const skill = (values: Partial<Skill>): Skill => ({
  invocation: "unknown-skill",
  name: "unknown-skill",
  description: "A useful capability.",
  displayName: "",
  localizedDescription: "",
  source: "本地 Skill",
  sourcePath: "C:\\demo\\SKILL.md",
  favorite: false,
  category: "",
  tags: [],
  usageCount: 0,
  usageSources: {},
  ...values,
});

describe("mockSuggestion", () => {
  it("provides a recommended Chinese short name from known tokens", () => {
    const result = mockSuggestion(skill({ invocation: "github:gh-fix-ci", name: "gh-fix-ci" }));
    expect(result.shortName).toBe("GitHub修复CI");
    expect(result.engine).toBe("local");
    expect(result.category).toBe("开发与代码");
  });

  it("keeps an existing Chinese description", () => {
    const result = mockSuggestion(skill({ description: "生成中国法律研究报告。" }));
    expect(result.descriptionZh).toBe("生成中国法律研究报告。");
  });
});
