import { createFileRoute, Link } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { statusApi, testsApi, suitesApi, configApi } from '~/lib/api'
import { useTestRunEvents, useSuiteEvents } from '~/lib/signalr'
import { formatDuration, formatDateTime, getStatusColor, getStatusIcon, isRunning } from '~/lib/utils'
import type { ApiStatusResponse, ApiTestRun, ApiTestSuiteRun, ConfigStatusResponse } from '~/lib/types'
import { Badge } from '~/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '~/components/ui/table'
import { FirstRunBanner } from '~/components/settings/FirstRunBanner'

export const Route = createFileRoute('/')({
  component: Dashboard,
})

function Dashboard() {
  const [status, setStatus] = useState<ApiStatusResponse | null>(null)
  const [activeTests, setActiveTests] = useState<ApiTestRun[]>([])
  const [activeSuites, setActiveSuites] = useState<ApiTestSuiteRun[]>([])
  const [recentTests, setRecentTests] = useState<ApiTestRun[]>([])
  const [configStatus, setConfigStatus] = useState<ConfigStatusResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Load initial data
  useEffect(() => {
    async function loadData() {
      try {
        const [statusRes, testsRes, suitesRes, completedRes, configStatusRes] = await Promise.all([
          statusApi.getStatus(),
          testsApi.getActive(),
          suitesApi.getActive(),
          testsApi.getCompleted(),
          configApi.getStatus(),
        ])
        setStatus(statusRes)
        setActiveTests(testsRes)
        setActiveSuites(suitesRes)
        setRecentTests(completedRes.slice(0, 10))
        setConfigStatus(configStatusRes)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load data')
      } finally {
        setLoading(false)
      }
    }
    loadData()
  }, [])

  // Real-time test updates
  useTestRunEvents({
    onTestRunStarted: (run) => {
      setActiveTests((prev) => [run, ...prev.filter((r) => r.id !== run.id)])
      setStatus((prev) => prev ? { ...prev, activeTestRuns: prev.activeTestRuns + 1 } : prev)
    },
    onTestRunCompleted: (run) => {
      setActiveTests((prev) => prev.filter((r) => r.id !== run.id))
      setRecentTests((prev) => [run, ...prev.slice(0, 9)])
      setStatus((prev) =>
        prev
          ? {
              ...prev,
              activeTestRuns: Math.max(0, prev.activeTestRuns - 1),
              completedTestRuns: prev.completedTestRuns + 1,
            }
          : prev
      )
    },
    onTestRunStatus: (run) => {
      setActiveTests((prev) => prev.map((r) => (r.id === run.id ? run : r)))
    },
  })

  // Real-time suite updates
  useSuiteEvents({
    onSuiteStarted: (suite) => {
      setActiveSuites((prev) => [suite, ...prev.filter((s) => s.id !== suite.id)])
      setStatus((prev) => prev ? { ...prev, activeSuiteRuns: prev.activeSuiteRuns + 1 } : prev)
    },
    onSuiteCompleted: (suite) => {
      setActiveSuites((prev) => prev.filter((s) => s.id !== suite.id))
      setStatus((prev) =>
        prev
          ? {
              ...prev,
              activeSuiteRuns: Math.max(0, prev.activeSuiteRuns - 1),
              completedSuiteRuns: prev.completedSuiteRuns + 1,
            }
          : prev
      )
    },
    onSuiteStatus: (suite) => {
      setActiveSuites((prev) => prev.map((s) => (s.id === suite.id ? suite : s)))
    },
  })

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="max-w-7xl mx-auto px-4 py-8">
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          Error: {error}
        </div>
      </div>
    )
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <h1 className="text-2xl font-bold mb-8">Dashboard</h1>

      {/* First Run Banner */}
      {configStatus?.isFirstRun && (
        <FirstRunBanner
          missingPaths={configStatus.missingPaths}
          invalidPaths={configStatus.invalidPaths}
        />
      )}

      {/* Status Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <StatusCard
          title="Active Bots"
          value={status?.activeBots ?? 0}
        />
        <StatusCard
          title="Running Tests"
          value={status?.activeTestRuns ?? 0}
          highlight={status?.activeTestRuns ? status.activeTestRuns > 0 : false}
        />
        <StatusCard
          title="Running Suites"
          value={status?.activeSuiteRuns ?? 0}
          highlight={status?.activeSuiteRuns ? status.activeSuiteRuns > 0 : false}
        />
        <StatusCard
          title="Completed Tests"
          value={status?.completedTestRuns ?? 0}
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 mb-8">
        {/* Active Tests Panel */}
        <Card>
          <CardHeader className="pb-3">
            <div className="flex justify-between items-center">
              <CardTitle className="text-base">Active Tests</CardTitle>
              <Link
                to="/tests"
                className="text-sm text-primary hover:underline"
              >
                View All
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            {activeTests.length === 0 ? (
              <div className="text-muted-foreground text-sm text-center py-4">No active tests</div>
            ) : (
              <div className="space-y-3">
                {activeTests.map((test) => (
                  <ActiveTestCard key={test.id} test={test} />
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Active Suites Panel */}
        <Card>
          <CardHeader className="pb-3">
            <div className="flex justify-between items-center">
              <CardTitle className="text-base">Active Suites</CardTitle>
              <Link
                to="/suites"
                className="text-sm text-primary hover:underline"
              >
                View All
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            {activeSuites.length === 0 ? (
              <div className="text-muted-foreground text-sm text-center py-4">No active suites</div>
            ) : (
              <div className="space-y-3">
                {activeSuites.map((suite) => (
                  <ActiveSuiteCard key={suite.id} suite={suite} />
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Recent Results */}
      <Card>
        <CardHeader>
          <CardTitle>Recent Test Results</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {recentTests.length === 0 ? (
            <div className="text-muted-foreground text-sm text-center py-8">No completed tests yet</div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Test</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Bots</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Completed</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {recentTests.map((test) => (
                  <TableRow key={test.id}>
                    <TableCell>
                      <Link
                        to="/tests/$testId"
                        params={{ testId: test.id }}
                        className="text-primary hover:underline font-medium"
                      >
                        {test.routeName}
                      </Link>
                      <div className="text-xs text-muted-foreground">{test.routePath}</div>
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant="outline"
                        className={getStatusColor(test.status)}
                      >
                        {getStatusIcon(test.status)} {test.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm">
                      <span className="text-green-600">{test.botsPassed}</span>
                      {' / '}
                      <span>{test.botCount}</span>
                      {test.botsFailed > 0 && (
                        <span className="text-destructive ml-1">({test.botsFailed} failed)</span>
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {formatDuration(test.durationSeconds)}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {test.endTime ? formatDateTime(test.endTime) : '-'}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function StatusCard({
  title,
  value,
  highlight = false,
}: {
  title: string
  value: number
  highlight?: boolean
}) {
  return (
    <Card className={highlight ? 'border-primary ring-1 ring-primary/20' : ''}>
      <CardContent className="pt-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{title}</p>
            <p className="text-2xl font-bold">{value}</p>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function ActiveTestCard({ test }: { test: ApiTestRun }) {
  const progress = test.botCount > 0 ? (test.botsCompleted / test.botCount) * 100 : 0

  return (
    <Link
      to="/tests/$testId"
      params={{ testId: test.id }}
      className="block p-3 bg-muted/50 rounded-lg hover:bg-muted border border-border"
    >
      <div className="flex justify-between items-start mb-2">
        <div>
          <div className="font-medium">{test.routeName}</div>

        </div>
        <Badge variant="outline" className={getStatusColor(test.status)}>
          {getStatusIcon(test.status)} {test.status}
        </Badge>
      </div>
      <div className="flex items-center gap-2">
        <div className="flex-1 h-2 bg-muted rounded-full overflow-hidden">
          <div
            className="h-full bg-primary transition-all duration-300"
            style={{ width: `${progress}%` }}
          />
        </div>
        <span className="text-xs text-muted-foreground">
          {test.botsCompleted}/{test.botCount}
        </span>
      </div>
    </Link>
  )
}

function ActiveSuiteCard({ suite }: { suite: ApiTestSuiteRun }) {
  const progress = suite.totalTests > 0 ? (suite.testsCompleted / suite.totalTests) * 100 : 0

  return (
    <Link
      to="/suites/$suiteId"
      params={{ suiteId: suite.id }}
      className="block p-3 bg-muted/50 rounded-lg hover:bg-muted border border-border"
    >
      <div className="flex justify-between items-start mb-2">
        <div>
          <div className="font-medium">{suite.suiteName}</div>
          <div className="text-xs text-muted-foreground">{suite.suitePath}</div>
        </div>
        <Badge variant="outline" className={getStatusColor(suite.status)}>
          {getStatusIcon(suite.status)} {suite.status}
        </Badge>
      </div>
      <div className="flex items-center gap-2">
        <div className="flex-1 h-2 bg-muted rounded-full overflow-hidden">
          <div
            className="h-full bg-primary transition-all duration-300"
            style={{ width: `${progress}%` }}
          />
        </div>
        <span className="text-xs text-muted-foreground">
          {suite.testsCompleted}/{suite.totalTests}
        </span>
      </div>
      {suite.testsFailed > 0 && (
        <div className="mt-1 text-xs text-destructive">{suite.testsFailed} test(s) failed</div>
      )}
    </Link>
  )
}
