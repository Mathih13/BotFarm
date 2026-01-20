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

// ============ Task Progress Event Types ============

export interface ApiTaskStartedEvent {
  runId: string;
  botName: string;
  taskName: string;
  taskIndex: number;
  totalTasks: number;
  timestamp: string;
}

export interface ApiTaskCompletedEvent {
  runId: string;
  botName: string;
  taskName: string;
  taskIndex: number;
  totalTasks: number;
  result: 'Success' | 'Failed' | 'Skipped';
  durationSeconds: number;
  errorMessage: string | null;
  timestamp: string;
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

// ============ Route Detail Types ============

export interface ApiRouteDetail {
  path: string;
  name: string;
  description: string | null;
  loop: boolean;
  harness: ApiHarnessSettings | null;
  tasks: ApiTaskInfo[];
  rawJson: string;
}

export interface ApiHarnessSettings {
  botCount: number;
  accountPrefix: string;
  classes: string[];
  race: string;
  level: number;
  setupTimeoutSeconds: number;
  testTimeoutSeconds: number;
  startPosition: ApiStartPosition | null;
}

export interface ApiStartPosition {
  mapId: number;
  x: number;
  y: number;
  z: number;
}

export interface ApiTaskInfo {
  type: string;
  parameters: Record<string, unknown>;
}

// ============ Route Editor Request Types ============

export interface CreateRouteRequest {
  path: string;
  content: string;
}

export interface UpdateRouteRequest {
  content: string;
}

// ============ Route Editor Form Types ============

export interface RouteFormData {
  name: string;
  description: string;
  loop: boolean;
  harness: HarnessFormData | null;
  tasks: TaskFormData[];
}

export interface HarnessFormData {
  botCount: number;
  accountPrefix: string;
  classes: string[];
  race: string;
  level: number;
  items: ItemRequirement[];
  completedQuests: number[];
  startPosition: PositionData | null;
  setupTimeoutSeconds: number;
  testTimeoutSeconds: number;
}

export interface PositionData {
  mapId: number;
  x: number;
  y: number;
  z: number;
}

export interface ItemRequirement {
  entry: number;
  count: number;
}

export interface TaskFormData {
  id: string;
  type: TaskType;
  parameters: Record<string, unknown>;
}

export type TaskType =
  | 'Wait'
  | 'LogMessage'
  | 'MoveToLocation'
  | 'MoveToNPC'
  | 'TalkToNPC'
  | 'AcceptQuest'
  | 'TurnInQuest'
  | 'KillMobs'
  | 'UseObject'
  | 'Adventure'
  | 'LearnSpells'
  | 'AssertQuestInLog'
  | 'AssertQuestNotInLog'
  | 'AssertHasItem'
  | 'AssertLevel';

// Kill requirements and collect items
export interface KillRequirement {
  entry: number;
  count: number;
}

export interface CollectItem {
  itemEntry: number;
  count: number;
  droppedBy?: number | number[];
}

export interface ObjectRequirement {
  entry: number;
  count: number;
}

// Class maps for class-specific overrides
export type ClassMap<T> = Partial<Record<PlayerClass, T>>;

export type PlayerClass =
  | 'Warrior'
  | 'Paladin'
  | 'Hunter'
  | 'Rogue'
  | 'Priest'
  | 'DeathKnight'
  | 'Shaman'
  | 'Mage'
  | 'Warlock'
  | 'Druid';

export const PLAYER_CLASSES: PlayerClass[] = [
  'Warrior',
  'Paladin',
  'Hunter',
  'Rogue',
  'Priest',
  'DeathKnight',
  'Shaman',
  'Mage',
  'Warlock',
  'Druid',
];

export const PLAYER_RACES = [
  'Human',
  'Dwarf',
  'NightElf',
  'Gnome',
  'Orc',
  'Undead',
  'Tauren',
  'Troll',
  'BloodElf',
  'Draenei',
] as const;

export type PlayerRace = (typeof PLAYER_RACES)[number];

// ============ Entity Lookup Types ============

export interface EntityLookupRequest {
  npcEntries?: number[];
  questIds?: number[];
  itemEntries?: number[];
  objectEntries?: number[];
}

export interface EntityLookupResponse {
  npcs: Record<number, string>;
  quests: Record<number, string>;
  items: Record<number, string>;
  objects: Record<number, string>;
}

export type EntityType = 'npc' | 'quest' | 'item' | 'object';

// ============ Entity Search Types ============

export interface EntitySearchResult {
  entry: number;
  name: string;
}

export interface EntitySearchResponse {
  results: EntitySearchResult[];
}

// ============ Configuration Types ============

export interface ConfigResponse {
  // Server Connection
  hostname: string;
  port: number;
  username: string;
  password: string;
  raPort: number;
  realmID: number;

  // Bot Settings
  minBotsCount: number;
  maxBotsCount: number;
  randomBots: boolean;
  createAccountOnly: boolean;

  // Data Paths
  mmapsFolderPath: string;
  vmapsFolderPath: string;
  mapsFolderPath: string;
  dbcsFolderPath: string;

  // MySQL Settings
  mySQLHost: string;
  mySQLPort: number;
  mySQLUser: string;
  mySQLPassword: string;
  mySQLCharactersDB: string;
  mySQLWorldDB: string;

  // Web UI Settings
  enableWebUI: boolean;
  webUIPort: number;
}

export interface ConfigUpdateRequest {
  // Server Connection
  hostname: string;
  port: number;
  username: string;
  password: string;
  raPort: number;
  realmID: number;

  // Bot Settings
  minBotsCount: number;
  maxBotsCount: number;
  randomBots: boolean;
  createAccountOnly: boolean;

  // Data Paths
  mmapsFolderPath: string;
  vmapsFolderPath: string;
  mapsFolderPath: string;
  dbcsFolderPath: string;

  // MySQL Settings
  mySQLHost: string;
  mySQLPort: number;
  mySQLUser: string;
  mySQLPassword: string;
  mySQLCharactersDB: string;
  mySQLWorldDB: string;

  // Web UI Settings
  enableWebUI: boolean;
  webUIPort: number;
}

export interface ConfigUpdateResponse {
  success: boolean;
  restartRequired: boolean;
  message: string;
  errors: string[];
}

export interface ConfigStatusResponse {
  isFirstRun: boolean;
  setupModeRequired: boolean;
  missingPaths: string[];
  invalidPaths: string[];
}

export interface PathValidationRequest {
  path: string;
  pathType: string;
}

export interface PathValidationResponse {
  valid: boolean;
  exists: boolean;
  hasExpectedFiles: boolean;
  message: string;
  foundFiles: string[];
}
