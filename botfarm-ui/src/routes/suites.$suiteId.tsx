import { createFileRoute, Link } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { suitesApi } from '~/lib/api'
import { useSuiteEvents, subscribeToSuite, unsubscribeFromSuite } from '~/lib/signalr'
import { formatDuration, formatDateTime, getStatusColor, getStatusIcon, isRunning } from '~/lib/utils'
import type { ApiTestSuiteRun, ApiTestRun } from '~/lib/types'
import { Button } from '~/components/ui/button'
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

export const Route = createFileRoute('/suites/$suiteId')({
  component: SuiteDetail,
})

function SuiteDetail() {
  const { suiteId } = Route.useParams()
  const [suite, setSuite] = useState<ApiTestSuiteRun | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Load suite data
  useEffect(() => {
    async function loadSuite() {
      try {
        const suiteData = await suitesApi.getById(suiteId)
        setSuite(suiteData)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load suite')
      } finally {
        setLoading(false)
      }
    }
    loadSuite()
  }, [suiteId])

  // Subscribe to real-time updates
  useEffect(() => {
    subscribeToSuite(suiteId).catch(console.error)
    return () => {
      unsubscribeFromSuite(suiteId).catch(console.error)
    }
  }, [suiteId])

  // Real-time updates
  useSuiteEvents({
    onSuiteStatus: (s) => {
      if (s.id === suiteId) {
        // Merge status update with existing state to preserve testRuns
        setSuite((prev) => prev ? { ...prev, ...s, testRuns: s.testRuns || prev.testRuns } : s)
      }
    },
    onSuiteCompleted: (s) => {
      if (s.id === suiteId) {
        // Merge completed update with existing state to preserve testRuns
        setSuite((prev) => prev ? { ...prev, ...s, testRuns: s.testRuns || prev.testRuns } : s)
      }
    },
    onSuiteTestCompleted: (sId, test) => {
      if (sId === suiteId) {
        setSuite((prev) => {
          if (!prev) return prev
          const existingTests = prev.testRuns || []
          const updated = existingTests.some((t) => t.id === test.id)
            ? existingTests.map((t) => (t.id === test.id ? test : t))
            : [...existingTests, test]
          return {
            ...prev,
            testRuns: updated,
            testsCompleted: updated.filter((t) => !isRunning(t.status)).length,
            testsPassed: updated.filter((t) => t.status === 'Completed').length,
            testsFailed: updated.filter((t) => t.status === 'Failed' || t.status === 'TimedOut').length,
          }
        })
      }
    },
  })

  const handleStop = async () => {
    try {
      await suitesApi.stop(suiteId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to stop suite')
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  if (error || !suite) {
    return (
      <div className="max-w-7xl mx-auto px-4 py-8">
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          {error || 'Suite not found'}
        </div>
        <Link to="/suites" className="mt-4 inline-block text-primary hover:underline">
          Back to Suites
        </Link>
      </div>
    )
  }

  const progress = suite.totalTests > 0 ? (suite.testsCompleted / suite.totalTests) * 100 : 0

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* Header */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <Link
            to="/suites"
            className="text-sm text-muted-foreground hover:text-foreground mb-2 inline-block"
          >
            &larr; Back to Suites
          </Link>
          <h1 className="text-2xl font-bold">{suite.suiteName}</h1>
        </div>
        <div className="flex items-center gap-4">
          <Badge
            variant="outline"
            className={`${getStatusColor(suite.status)} px-3 py-1.5 text-sm`}
          >
            {getStatusIcon(suite.status)} {suite.status}
          </Badge>
          {isRunning(suite.status) && (
            <Button variant="destructive" onClick={handleStop}>
              Stop Suite
            </Button>
          )}
        </div>
      </div>

      {/* Overview Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Duration</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">{formatDuration(suite.durationSeconds)}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Tests</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">
              <span className="text-green-600">{suite.testsPassed}</span>
              {' / '}
              {suite.totalTests}
              {suite.testsFailed > 0 && (
                <span className="text-destructive text-sm ml-1">({suite.testsFailed} failed)</span>
              )}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Execution Mode</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">
              {suite.parallelMode ? 'Parallel' : 'Sequential'}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Skipped</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">{suite.testsSkipped}</p>
          </CardContent>
        </Card>
      </div>

      {/* Progress Bar */}
      <Card className="mb-6">
        <CardContent className="pt-6">
          <div className="flex justify-between text-sm text-muted-foreground mb-2">
            <span>Progress</span>
            <span>{suite.testsCompleted} / {suite.totalTests} tests completed</span>
          </div>
          <div className="h-4 bg-muted rounded-full overflow-hidden">
            <div
              className={`h-full transition-all duration-300 ${
                suite.testsFailed > 0 ? 'bg-amber-500' : 'bg-green-500'
              }`}
              style={{ width: `${progress}%` }}
            />
          </div>
        </CardContent>
      </Card>

      {/* Error Message */}
      {suite.errorMessage && (
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 mb-6">
          <h3 className="font-medium text-destructive mb-1">Error</h3>
          <p className="text-destructive text-sm">{suite.errorMessage}</p>
        </div>
      )}

      {/* Test Runs */}
      <Card>
        <CardHeader>
          <CardTitle>Test Runs</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {(!suite.testRuns || suite.testRuns.length === 0) ? (
            <div className="text-muted-foreground text-center py-8">
              {isRunning(suite.status) ? 'Waiting for tests to start...' : 'No test runs'}
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-12">#</TableHead>
                  <TableHead>Test</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Bots</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {suite.testRuns.map((test, idx) => (
                  <TestRunRow key={test.id} test={test} index={idx + 1} />
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* Timestamps */}
      <div className="mt-6 text-sm text-muted-foreground">
        <p>Started: {formatDateTime(suite.startTime)}</p>
        {suite.endTime && <p>Ended: {formatDateTime(suite.endTime)}</p>}
      </div>
    </div>
  )
}

function TestRunRow({ test, index }: { test: ApiTestRun; index: number }) {
  return (
    <TableRow>
      <TableCell className="text-muted-foreground">{index}</TableCell>
      <TableCell>
        <div className="font-medium">{test.routeName}</div>
      </TableCell>
      <TableCell>
        <Badge
          variant="outline"
          className={getStatusColor(test.status)}
        >
          {getStatusIcon(test.status)} {test.status}
        </Badge>
        {test.errorMessage && (
          <div className="text-xs text-destructive mt-1 max-w-xs truncate" title={test.errorMessage}>
            {test.errorMessage}
          </div>
        )}
      </TableCell>
      <TableCell>
        <div className="flex items-center gap-2">
          <div className="w-16 h-2 bg-muted rounded-full overflow-hidden">
            <div
              className={`h-full ${test.botsFailed > 0 ? 'bg-destructive' : 'bg-green-500'}`}
              style={{
                width: `${test.botCount > 0 ? (test.botsCompleted / test.botCount) * 100 : 0}%`,
              }}
            />
          </div>
          <span className="text-xs text-muted-foreground">
            {test.botsPassed}/{test.botCount}
          </span>
        </div>
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {formatDuration(test.durationSeconds)}
      </TableCell>
      <TableCell>
        <Link
          to="/tests/$testId"
          params={{ testId: test.id }}
          className="text-primary hover:underline text-sm font-medium"
        >
          View Details
        </Link>
      </TableCell>
    </TableRow>
  )
}
