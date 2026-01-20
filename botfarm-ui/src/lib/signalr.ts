import * as signalR from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';
import type { ApiTestRun, ApiTestSuiteRun, ApiBotResult, ApiTaskStartedEvent, ApiTaskCompletedEvent } from './types';

// Get the hub URL - use environment variable in dev, relative path in production
function getHubUrl(): string {
  // In browser, check if we have a custom API URL
  if (typeof window !== 'undefined') {
    // Use absolute URL to backend in development
    const isDev = import.meta.env.DEV;
    if (isDev) {
      return 'http://localhost:5000/hubs/test';
    }
  }
  return '/hubs/test';
}

// Lazy connection - only created on client side
let connection: signalR.HubConnection | null = null;
let connectionPromise: Promise<void> | null = null;

function getConnection(): signalR.HubConnection | null {
  // Only create connection in browser environment
  if (typeof window === 'undefined') {
    return null;
  }

  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(getHubUrl())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();
  }

  return connection;
}

async function ensureConnected(): Promise<void> {
  const conn = getConnection();
  if (!conn) return;

  if (conn.state === signalR.HubConnectionState.Connected) {
    return;
  }

  if (conn.state === signalR.HubConnectionState.Connecting) {
    return connectionPromise!;
  }

  connectionPromise = conn.start();
  return connectionPromise;
}

// Event types
export type TestRunStartedHandler = (run: ApiTestRun) => void;
export type TestRunCompletedHandler = (run: ApiTestRun) => void;
export type TestRunStatusHandler = (run: ApiTestRun) => void;
export type BotCompletedHandler = (runId: string, bot: ApiBotResult) => void;
export type TaskStartedHandler = (event: ApiTaskStartedEvent) => void;
export type TaskCompletedHandler = (event: ApiTaskCompletedEvent) => void;
export type SuiteStartedHandler = (suite: ApiTestSuiteRun) => void;
export type SuiteCompletedHandler = (suite: ApiTestSuiteRun) => void;
export type SuiteTestCompletedHandler = (suiteId: string, test: ApiTestRun) => void;
export type SuiteStatusHandler = (suite: ApiTestSuiteRun) => void;

// Hook for SignalR connection and events
export function useSignalR() {
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const conn = getConnection();
    if (!conn) return;

    const connect = async () => {
      try {
        await ensureConnected();
        setIsConnected(true);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Connection failed');
        setIsConnected(false);
      }
    };

    connect();

    conn.onreconnecting(() => {
      setIsConnected(false);
    });

    conn.onreconnected(() => {
      setIsConnected(true);
      setError(null);
    });

    conn.onclose((err) => {
      setIsConnected(false);
      if (err) {
        setError(err.message);
      }
    });

    return () => {
      // Don't disconnect - other components may be using the connection
    };
  }, []);

  return { isConnected, error };
}

// Hook for test run events
export function useTestRunEvents(handlers: {
  onTestRunStarted?: TestRunStartedHandler;
  onTestRunCompleted?: TestRunCompletedHandler;
  onTestRunStatus?: TestRunStatusHandler;
  onBotCompleted?: BotCompletedHandler;
  onTaskStarted?: TaskStartedHandler;
  onTaskCompleted?: TaskCompletedHandler;
}) {
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    const conn = getConnection();
    if (!conn) return;

    const onTestRunStarted = (run: ApiTestRun) => {
      handlersRef.current.onTestRunStarted?.(run);
    };

    const onTestRunCompleted = (run: ApiTestRun) => {
      handlersRef.current.onTestRunCompleted?.(run);
    };

    const onTestRunStatus = (run: ApiTestRun) => {
      handlersRef.current.onTestRunStatus?.(run);
    };

    const onBotCompleted = (runId: string, bot: ApiBotResult) => {
      handlersRef.current.onBotCompleted?.(runId, bot);
    };

    const onTaskStarted = (event: ApiTaskStartedEvent) => {
      handlersRef.current.onTaskStarted?.(event);
    };

    const onTaskCompleted = (event: ApiTaskCompletedEvent) => {
      handlersRef.current.onTaskCompleted?.(event);
    };

    // Register handlers immediately (works even before connection is established)
    conn.on('testRunStarted', onTestRunStarted);
    conn.on('testRunCompleted', onTestRunCompleted);
    conn.on('testRunStatus', onTestRunStatus);
    conn.on('botCompleted', onBotCompleted);
    conn.on('taskStarted', onTaskStarted);
    conn.on('taskCompleted', onTaskCompleted);

    // Ensure connection is established
    ensureConnected().catch(console.error);

    return () => {
      conn.off('testRunStarted', onTestRunStarted);
      conn.off('testRunCompleted', onTestRunCompleted);
      conn.off('testRunStatus', onTestRunStatus);
      conn.off('botCompleted', onBotCompleted);
      conn.off('taskStarted', onTaskStarted);
      conn.off('taskCompleted', onTaskCompleted);
    };
  }, []);
}

// Hook for suite events
export function useSuiteEvents(handlers: {
  onSuiteStarted?: SuiteStartedHandler;
  onSuiteCompleted?: SuiteCompletedHandler;
  onSuiteTestCompleted?: SuiteTestCompletedHandler;
  onSuiteStatus?: SuiteStatusHandler;
}) {
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    const conn = getConnection();
    if (!conn) return;

    const onSuiteStarted = (suite: ApiTestSuiteRun) => {
      handlersRef.current.onSuiteStarted?.(suite);
    };

    const onSuiteCompleted = (suite: ApiTestSuiteRun) => {
      handlersRef.current.onSuiteCompleted?.(suite);
    };

    const onSuiteTestCompleted = (suiteId: string, test: ApiTestRun) => {
      handlersRef.current.onSuiteTestCompleted?.(suiteId, test);
    };

    const onSuiteStatus = (suite: ApiTestSuiteRun) => {
      handlersRef.current.onSuiteStatus?.(suite);
    };

    // Register handlers immediately (works even before connection is established)
    conn.on('suiteStarted', onSuiteStarted);
    conn.on('suiteCompleted', onSuiteCompleted);
    conn.on('suiteTestCompleted', onSuiteTestCompleted);
    conn.on('suiteStatus', onSuiteStatus);

    // Ensure connection is established
    ensureConnected().catch(console.error);

    return () => {
      conn.off('suiteStarted', onSuiteStarted);
      conn.off('suiteCompleted', onSuiteCompleted);
      conn.off('suiteTestCompleted', onSuiteTestCompleted);
      conn.off('suiteStatus', onSuiteStatus);
    };
  }, []);
}

// Subscribe to specific test run updates
export async function subscribeToRun(runId: string): Promise<void> {
  const conn = getConnection();
  if (!conn) return;
  await ensureConnected();
  await conn.invoke('SubscribeToRun', runId);
}

export async function unsubscribeFromRun(runId: string): Promise<void> {
  const conn = getConnection();
  if (!conn) return;
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('UnsubscribeFromRun', runId);
  }
}

// Subscribe to specific suite updates
export async function subscribeToSuite(suiteId: string): Promise<void> {
  const conn = getConnection();
  if (!conn) return;
  await ensureConnected();
  await conn.invoke('SubscribeToSuite', suiteId);
}

export async function unsubscribeFromSuite(suiteId: string): Promise<void> {
  const conn = getConnection();
  if (!conn) return;
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('UnsubscribeFromSuite', suiteId);
  }
}
