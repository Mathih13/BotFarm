// API Types - matches C# ApiModels.cs

export interface ApiStatusResponse {
  online: boolean;
  activeBots: number;
  activeTestRuns: number;
  activeSuiteRuns: number;
  completedTestRuns: number;
  completedSuiteRuns: number;
  serverTime: string;
}

export interface ApiTestRun {
  id: string;
  routePath: string;
  routeName: string;
  status: TestRunStatus;
  startTime: string;
  endTime: string | null;
  durationSeconds: number;
  errorMessage: string | null;
  botCount: number;
  level: number;
  classes: string[];
  botsCompleted: number;
  botsPassed: number;
  botsFailed: number;
  botResults?: ApiBotResult[];
}

export type TestRunStatus =
  | 'Pending'
  | 'SettingUp'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'TimedOut'
  | 'Cancelled';

export interface ApiBotResult {
  botName: string;
  characterName: string;
  characterClass: string;
  success: boolean;
  isComplete: boolean;
  startTime: string;
  durationSeconds: number;
  errorMessage: string | null;
  tasksCompleted: number;
  tasksFailed: number;
  tasksSkipped: number;
  totalTasks: number;
  taskResults?: ApiTaskResult[];
  logs?: string[];
}

export interface ApiTaskResult {
  taskName: string;
  result: 'Success' | 'Failed' | 'Skipped';
  durationSeconds: number;
  errorMessage: string | null;
}

export interface ApiTestSuiteRun {
  id: string;
  suiteName: string;
  suitePath: string;
  parallelMode: boolean;
  status: SuiteRunStatus;
  startTime: string;
  endTime: string | null;
  durationSeconds: number;
  errorMessage: string | null;
  totalTests: number;
  testsCompleted: number;
  testsPassed: number;
  testsFailed: number;
  testsSkipped: number;
  testRuns?: ApiTestRun[];
}

export type SuiteRunStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

export interface ApiRouteInfo {
  path: string;
  name: string;
  directory: string;
  hasHarness: boolean;
  botCount: number | null;
  level: number | null;
  timeoutSeconds: number | null;
}

export interface ApiSuiteInfo {
  path: string;
  name: string;
  testCount: number;
}

// Request types
export interface StartTestRequest {
  routePath: string;
}

export interface StartSuiteRequest {
  suitePath: string;
  parallel: boolean;
}

// Log API Types
export interface ApiLogEntry {
  timestamp: string;
  message: string;
  level: string;
}

export interface ApiLogsResponse {
  logs: ApiLogEntry[];
  totalCount: number;
  filteredCount: number;
}
