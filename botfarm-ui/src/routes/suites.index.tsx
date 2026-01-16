import { createFileRoute, Link } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { suitesApi, routesApi } from '~/lib/api'
import { useSuiteEvents } from '~/lib/signalr'
import { formatDuration, formatDateTime, getStatusColor, getStatusIcon, isRunning } from '~/lib/utils'
import type { ApiTestSuiteRun, ApiSuiteInfo } from '~/lib/types'
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
import { Checkbox } from '~/components/ui/checkbox'
import { Label } from '~/components/ui/label'

export const Route = createFileRoute('/suites/')({
  component: SuitesIndex,
})

function SuitesIndex() {
  const [suites, setSuites] = useState<ApiTestSuiteRun[]>([])
  const [availableSuites, setAvailableSuites] = useState<ApiSuiteInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | 'active' | 'completed'>('all')
  const [showNewSuite, setShowNewSuite] = useState(false)
  const [selectedSuite, setSelectedSuite] = useState<string>('')
  const [parallel, setParallel] = useState(false)

  // Load initial data
  useEffect(() => {
    async function loadData() {
      try {
        const [suitesRes, availableRes] = await Promise.all([
          suitesApi.getAll(),
          routesApi.getSuites(),
        ])
        setSuites(suitesRes)
        setAvailableSuites(availableRes)
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
  useSuiteEvents({
    onSuiteStarted: (suite) => {
      setSuites((prev) => [suite, ...prev.filter((s) => s.id !== suite.id)])
    },
    onSuiteCompleted: (suite) => {
      setSuites((prev) => prev.map((s) => (s.id === suite.id ? suite : s)))
    },
    onSuiteStatus: (suite) => {
      setSuites((prev) => prev.map((s) => (s.id === suite.id ? suite : s)))
    },
  })

  const filteredSuites = suites.filter((suite) => {
    if (filter === 'active') return isRunning(suite.status)
    if (filter === 'completed') return !isRunning(suite.status)
    return true
  })

  const handleStartSuite = () => {
    if (!selectedSuite) return
    // Close dialog immediately - SignalR will show the suite when it starts
    setShowNewSuite(false)
    const suitePath = selectedSuite
    const parallelMode = parallel
    setSelectedSuite('')
    setParallel(false)
    // Fire and forget - errors will show in the main error banner
    suitesApi.start({ suitePath, parallel: parallelMode }).catch((err) => {
      setError(err instanceof Error ? err.message : 'Failed to start suite')
    })
  }

  const handleStopSuite = async (suiteId: string, e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
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

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold">Test Suites</h1>
        <Button onClick={() => setShowNewSuite(true)}>
          New Suite
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

      {/* New Suite Dialog */}
      <Dialog open={showNewSuite} onOpenChange={setShowNewSuite}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Start New Suite</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <Label className="block mb-2">Select Suite</Label>
              <Select value={selectedSuite} onValueChange={setSelectedSuite}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Select a suite..." />
                </SelectTrigger>
                <SelectContent>
                  {availableSuites.map((suite) => (
                    <SelectItem key={suite.path} value={suite.path}>
                      {suite.name} ({suite.testCount} tests)
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            {selectedSuite && (
              <div className="p-3 bg-muted rounded-lg">
                {(() => {
                  const suite = availableSuites.find((s) => s.path === selectedSuite)
                  if (!suite) return null
                  return (
                    <div className="text-sm text-muted-foreground">
                      <div><strong>Path:</strong> {suite.path}</div>
                      <div><strong>Tests:</strong> {suite.testCount}</div>
                    </div>
                  )
                })()}
              </div>
            )}
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <Checkbox
                  id="parallel"
                  checked={parallel}
                  onCheckedChange={(checked) => setParallel(checked === true)}
                />
                <Label htmlFor="parallel" className="cursor-pointer">
                  Run tests in parallel
                </Label>
              </div>
              <p className="text-xs text-muted-foreground">
                Parallel execution ignores test dependencies and runs all tests simultaneously
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => {
                setShowNewSuite(false)
                setSelectedSuite('')
                setParallel(false)
              }}
            >
              Cancel
            </Button>
            <Button
              onClick={handleStartSuite}
              disabled={!selectedSuite}
            >
              Start Suite
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

      {/* Suites Table */}
      <div className="bg-card rounded-lg shadow-sm border overflow-hidden">
        {filteredSuites.length === 0 ? (
          <div className="text-muted-foreground text-center py-8">No suites found</div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Suite</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Progress</TableHead>
                <TableHead>Mode</TableHead>
                <TableHead>Duration</TableHead>
                <TableHead>Started</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredSuites.map((suite) => (
                <TableRow key={suite.id}>
                  <TableCell>
                    <Link
                      to="/suites/$suiteId"
                      params={{ suiteId: suite.id }}
                      className="text-primary hover:underline font-medium"
                    >
                      {suite.suiteName}
                    </Link>
                    <div className="text-xs text-muted-foreground">{suite.suitePath}</div>
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant="outline"
                      className={getStatusColor(suite.status)}
                    >
                      {getStatusIcon(suite.status)} {suite.status}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <div className="w-24 h-2 bg-muted rounded-full overflow-hidden">
                        <div
                          className={`h-full ${
                            suite.testsFailed > 0 ? 'bg-destructive' : 'bg-green-500'
                          } transition-all duration-300`}
                          style={{
                            width: `${
                              suite.totalTests > 0 ? (suite.testsCompleted / suite.totalTests) * 100 : 0
                            }%`,
                          }}
                        />
                      </div>
                      <span className="text-xs text-muted-foreground">
                        {suite.testsPassed}/{suite.totalTests}
                        {suite.testsFailed > 0 && (
                          <span className="text-destructive"> ({suite.testsFailed} failed)</span>
                        )}
                      </span>
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {suite.parallelMode ? 'Parallel' : 'Sequential'}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDuration(suite.durationSeconds)}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDateTime(suite.startTime)}
                  </TableCell>
                  <TableCell>
                    {isRunning(suite.status) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={(e) => handleStopSuite(suite.id, e)}
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
