export interface RuleSet {
  id: string;
  name: string;
  version: number;
  isActive: boolean;
  ignorePaths: string[];
  arraySortKeys: Record<string, string>;
  numericAbsTolerance: number;
  numericRelTolerance: number;
  dynamicPatterns: Record<string, string>;
  ignoreHeaders: string[];
  createdAt: string;
}

export interface Scenario {
  id: string;
  name: string;
  description?: string | null;
  method: string;
  pathTemplate: string;
  pathParameters: Record<string, string>;
  queryParameters: Record<string, string>;
  headers: Record<string, string>;
  jsonBody?: string | null;
  secretRefs: string[];
  ruleSetId: string;
  enabled: boolean;
  updatedAt: string;
}

export interface RunOptions {
  concurrency: number;
  timeoutMs: number;
  baselineRateLimit: number;
  candidateRateLimit: number;
}

export interface RunSummary {
  id: string;
  status: 'Pending' | 'Running' | 'Completed' | 'Cancelled' | 'Failed';
  ruleSetId: string;
  ruleSetVersion: number;
  ruleSetName: string;
  baselineBaseUrl: string;
  candidateBaseUrl: string;
  options: RunOptions;
  totalScenarios: number;
  completedScenarios: number;
  diffCount: number;
  networkFailureCount: number;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
}

export interface DiffEntry {
  area: 'Status' | 'Header' | 'Body' | 'Timing' | 'Reachability';
  path: string;
  expected?: string | null;
  actual?: string | null;
  kind?: string | null;
}

export interface TargetCall {
  reached: boolean;
  statusCode?: number | null;
  errorKind?: string | null;
  errorDetail?: string | null;
  headers: Record<string, string>;
  bodyText?: string | null;
  bodyIsJson: boolean;
  elapsedMs: number;
}

export interface RunResult {
  id: string;
  scenarioId: string;
  scenarioName: string;
  orderIndex: number;
  outcome: 'Match' | 'Diff' | 'NetworkFailure' | 'RequestError';
  summary?: string | null;
  baseline: TargetCall;
  candidate: TargetCall;
  diffs: DiffEntry[];
  completedAt: string;
}

export interface AppConfig {
  baselineBaseUrl: string;
  candidateBaseUrl: string;
  secretNames: string[];
}
