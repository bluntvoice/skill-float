export type SkillUsageSummary = {
  invocation: string;
  count: number;
  sourceCounts: Record<string, number>;
};

export type UsageSourceSummary = {
  name: string;
  detected: boolean;
  files: number;
  count: number;
};

export type UsageSummary = {
  total: number;
  skills: SkillUsageSummary[];
  sources: UsageSourceSummary[];
  lastRefreshedAt: number;
};

export type UsageScanProgress = {
  processed: number;
  total: number;
  source: string;
};

export const EMPTY_USAGE: UsageSummary = {
  total: 0,
  skills: [],
  sources: ["Skill Float", "Codex", "Claude Code", "OpenClaw"].map((name) => ({
    name,
    detected: name === "Skill Float",
    files: 0,
    count: 0,
  })),
  lastRefreshedAt: 0,
};
