import type {
  ApiStatusResponse,
  ApiTestRun,
  ApiTestSuiteRun,
  ApiRouteInfo,
  ApiSuiteInfo,
  StartTestRequest,
  StartSuiteRequest,
  ApiLogsResponse,
} from './types';

// Use absolute URL in development, relative path in production
const API_BASE = typeof window !== 'undefined' && import.meta.env.DEV
  ? 'http://localhost:5000/api'
  : '/api';

async function fetchApi<T>(
  endpoint: string,
  options?: RequestInit
): Promise<T> {
  const response = await fetch(`${API_BASE}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: response.statusText }));
    throw new Error(error.error || `HTTP ${response.status}`);
  }

  return response.json();
}

// Status API
export const statusApi = {
  getStatus: () => fetchApi<ApiStatusResponse>('/status'),
};

// Tests API
export const testsApi = {
  getAll: () => fetchApi<ApiTestRun[]>('/tests'),
  getActive: () => fetchApi<ApiTestRun[]>('/tests/active'),
  getCompleted: () => fetchApi<ApiTestRun[]>('/tests/completed'),
  getById: (runId: string) => fetchApi<ApiTestRun>(`/tests/${runId}`),
  start: (request: StartTestRequest) =>
    fetchApi<ApiTestRun>('/tests', {
      method: 'POST',
      body: JSON.stringify(request),
    }),
  stop: (runId: string) =>
    fetchApi<{ message: string }>(`/tests/${runId}`, { method: 'DELETE' }),
  getReport: (runId: string) => fetchApi<ApiTestRun>(`/tests/${runId}/report`),
};

// Suites API
export const suitesApi = {
  getAll: () => fetchApi<ApiTestSuiteRun[]>('/suites'),
  getActive: () => fetchApi<ApiTestSuiteRun[]>('/suites/active'),
  getCompleted: () => fetchApi<ApiTestSuiteRun[]>('/suites/completed'),
  getById: (suiteId: string) => fetchApi<ApiTestSuiteRun>(`/suites/${suiteId}`),
  start: (request: StartSuiteRequest) =>
    fetchApi<ApiTestSuiteRun>('/suites', {
      method: 'POST',
      body: JSON.stringify(request),
    }),
  stop: (suiteId: string) =>
    fetchApi<{ message: string }>(`/suites/${suiteId}`, { method: 'DELETE' }),
};

// Routes API
export const routesApi = {
  getAll: () => fetchApi<ApiRouteInfo[]>('/routes'),
  getSuites: () => fetchApi<ApiSuiteInfo[]>('/routes/suites'),
  getByPath: (path: string) => fetchApi<unknown>(`/routes/${path}`),
};

// Logs API
export const logsApi = {
  getLogs: (params: { count?: number; filter?: string; since?: string; minLevel?: string } = {}) => {
    const searchParams = new URLSearchParams();
    if (params.count) searchParams.set('count', params.count.toString());
    if (params.filter) searchParams.set('filter', params.filter);
    if (params.since) searchParams.set('since', params.since);
    if (params.minLevel) searchParams.set('minLevel', params.minLevel);
    const query = searchParams.toString();
    return fetchApi<ApiLogsResponse>(`/logs${query ? `?${query}` : ''}`);
  },
};
