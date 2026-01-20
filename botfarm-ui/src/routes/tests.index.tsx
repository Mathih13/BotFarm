import { createFileRoute, Link } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { testsApi, routesApi } from '~/lib/api'
import { useTestRunEvents } from '~/lib/signalr'
import { formatDuration, formatDateTime, getStatusColor, getStatusIcon, isRunning } from '~/lib/utils'
import type { ApiTestRun, ApiRouteInfo } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Badge } from '~/components/ui/badge'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '~/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '~/components/ui/table'
import { Progress } from '~/components/ui/progress'

export const Route = createFileRoute('/tests/')({
  component: TestsIndex,
})

function TestsIndex() {
  const [tests, setTests] = useState<ApiTestRun[]>([])
  const [routes, setRoutes] = useState<ApiRouteInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | 'active' | 'completed'>('all')
  const [showNewTest, setShowNewTest] = useState(false)
  const [selectedRoute, setSelectedRoute] = useState<string>('')

  // Load initial data
  useEffect(() => {
    async function loadData() {
      try {
        const [testsRes, routesRes] = await Promise.all([
          testsApi.getAll(),
          routesApi.getAll(),
        ])
        setTests(testsRes)
        setRoutes(routesRes)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load data')
      } finally {
        setLoading(false)
      }
    }
    loadData()
  }, [])

  // Real-time updates
  useTestRunEvents({
    onTestRunStarted: (run) => {
      setTests((prev) => [run, ...prev.filter((r) => r.id !== run.id)])
    },
    onTestRunCompleted: (run) => {
      setTests((prev) => prev.map((r) => (r.id === run.id ? run : r)))
    },
    onTestRunStatus: (run) => {
      setTests((prev) => prev.map((r) => (r.id === run.id ? run : r)))
    },
  })

  const filteredTests = tests.filter((test) => {
    if (filter === 'active') return isRunning(test.status)
    if (filter === 'completed') return !isRunning(test.status)
    return true
  })

  const handleStartTest = async () => {
    if (!selectedRoute) return
    // Close dialog immediately
    setShowNewTest(false)
    const routePath = selectedRoute
    setSelectedRoute('')

    try {
      // Start the test
      const newRun = await testsApi.start({ routePath })
      // Immediately add to list as fallback in case SignalR event is missed
      setTests((prev) => {
        // Check if already added by SignalR
        if (prev.some(r => r.id === newRun.id)) {
          return prev
        }
        return [newRun, ...prev]
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start test')
    }
  }

  const handleStopTest = async (testId: string, e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    try {
      await testsApi.stop(testId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to stop test')
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold">Test Runs</h1>
        <Button
          onClick={() => {
            setError(null)
            setShowNewTest(true)
          }}
        >
          New Test
        </Button>
      </div>

      {error && (
        <div className="mb-4 bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          {error}
          <Button variant="ghost" size="sm" onClick={() => setError(null)} className="ml-2">
            Dismiss
          </Button>
        </div>
      )}

      {/* New Test Dialog */}
      <Dialog open={showNewTest} onOpenChange={setShowNewTest}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Start New Test</DialogTitle>
          </DialogHeader>
          {error && (
            <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-3 text-destructive text-sm">
              {error}
            </div>
          )}
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2">
                Select Route
              </label>
              <Select value={selectedRoute} onValueChange={setSelectedRoute}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Select a route..." />
                </SelectTrigger>
                <SelectContent>
                  {routes.map((route) => (
                    <SelectItem key={route.path} value={route.path}>
                      {route.name} ({route.path})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            {selectedRoute && (
              <div className="p-3 bg-muted rounded-lg">
                {(() => {
                  const route = routes.find((r) => r.path === selectedRoute)
                  if (!route) return null
                  return (
                    <div className="text-sm text-muted-foreground">
                      <div><strong>Path:</strong> {route.path}</div>
                      {route.botCount && <div><strong>Bot Count:</strong> {route.botCount}</div>}
                      {route.level && <div><strong>Level:</strong> {route.level}</div>}
                      {route.timeoutSeconds && <div><strong>Timeout:</strong> {route.timeoutSeconds}s</div>}
                      <div><strong>Has Harness:</strong> {route.hasHarness ? 'Yes' : 'No'}</div>
                    </div>
                  )
                })()}
              </div>
            )}
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => {
                setShowNewTest(false)
                setSelectedRoute('')
                setError(null)
              }}
            >
              Cancel
            </Button>
            <Button
              onClick={handleStartTest}
              disabled={!selectedRoute}
            >
              Start Test
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Filter Tabs */}
      <div className="flex gap-2 mb-6">
        {(['all', 'active', 'completed'] as const).map((f) => (
          <Button
            key={f}
            variant={filter === f ? 'default' : 'secondary'}
            onClick={() => setFilter(f)}
            className="capitalize"
          >
            {f}
          </Button>
        ))}
      </div>

      {/* Tests Table */}
      <div className="bg-card rounded-lg shadow-sm border overflow-hidden">
        {filteredTests.length === 0 ? (
          <div className="text-muted-foreground text-center py-8">No tests found</div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Test</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Progress</TableHead>
                <TableHead>Duration</TableHead>
                <TableHead>Started</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredTests.map((test) => (
                <TableRow key={test.id}>
                  <TableCell>
                    <Link
                      to="/tests/$testId"
                      params={{ testId: test.id }}
                      className="text-primary hover:underline font-medium"
                    >
                      {test.routeName}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant="outline"
                      className={getStatusColor(test.status)}
                    >
                      {getStatusIcon(test.status)} {test.status}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <div className="w-24 h-2 bg-muted rounded-full overflow-hidden">
                        <div
                          className={`h-full ${
                            test.botsFailed > 0 ? 'bg-destructive' : 'bg-green-500'
                          } transition-all duration-300`}
                          style={{
                            width: `${test.botCount > 0 ? (test.botsCompleted / test.botCount) * 100 : 0}%`,
                          }}
                        />
                      </div>
                      <span className="text-xs text-muted-foreground">
                        {test.botsPassed}/{test.botCount}
                        {test.botsFailed > 0 && (
                          <span className="text-destructive"> ({test.botsFailed} failed)</span>
                        )}
                      </span>
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDuration(test.durationSeconds)}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDateTime(test.startTime)}
                  </TableCell>
                  <TableCell>
                    {isRunning(test.status) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={(e) => handleStopTest(test.id, e)}
                        className="text-destructive hover:text-destructive"
                      >
                        Stop
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
    </div>
  )
}
