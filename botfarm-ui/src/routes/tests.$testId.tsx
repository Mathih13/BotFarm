import { createFileRoute, Link } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { testsApi } from '~/lib/api'
import { useTestRunEvents, subscribeToRun, unsubscribeFromRun } from '~/lib/signalr'
import { formatDuration, formatDateTime, getStatusColor, getStatusIcon, isRunning, getClassColor } from '~/lib/utils'
import type { ApiTestRun, ApiBotResult, ApiTaskResult, ApiTaskStartedEvent, ApiTaskCompletedEvent } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Badge } from '~/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Progress } from '~/components/ui/progress'
import { RawLogsDialog } from '~/components/RawLogsDialog'

export const Route = createFileRoute('/tests/$testId')({
  component: TestDetail,
})

// Type for tracking current task per bot
interface CurrentTaskInfo {
  taskName: string;
  taskIndex: number;
  totalTasks: number;
}

function TestDetail() {
  const { testId } = Route.useParams()
  const [test, setTest] = useState<ApiTestRun | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [expandedBots, setExpandedBots] = useState<Set<string>>(new Set())
  const [botCurrentTasks, setBotCurrentTasks] = useState<Map<string, CurrentTaskInfo>>(new Map())

  // Load test data
  useEffect(() => {
    async function loadTest() {
      try {
        const testData = await testsApi.getById(testId)
        setTest(testData)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load test')
      } finally {
        setLoading(false)
      }
    }
    loadTest()
  }, [testId])

  // Subscribe to real-time updates for this specific run
  useEffect(() => {
    subscribeToRun(testId).catch(console.error)
    return () => {
      unsubscribeFromRun(testId).catch(console.error)
    }
  }, [testId])

  // Real-time updates
  useTestRunEvents({
    onTestRunStatus: (run) => {
      if (run.id === testId) {
        // Merge status update with existing state to preserve botResults
        setTest((prev) => prev ? { ...prev, ...run, botResults: run.botResults || prev.botResults } : run)
      }
    },
    onTestRunCompleted: (run) => {
      if (run.id === testId) {
        // Merge completed update with existing state to preserve botResults
        setTest((prev) => prev ? { ...prev, ...run, botResults: run.botResults || prev.botResults } : run)
        // Clear current tasks when test completes
        setBotCurrentTasks(new Map())
      }
    },
    onBotCompleted: (runId, bot) => {
      if (runId === testId) {
        setTest((prev) => {
          if (!prev) return prev
          const existingBots = prev.botResults || []
          const updated = existingBots.some((b) => b.botName === bot.botName)
            ? existingBots.map((b) => (b.botName === bot.botName ? bot : b))
            : [...existingBots, bot]
          return {
            ...prev,
            botResults: updated,
            botsCompleted: updated.filter((b) => b.isComplete).length,
            botsPassed: updated.filter((b) => b.success).length,
            botsFailed: updated.filter((b) => b.isComplete && !b.success).length,
          }
        })
        // Clear current task for this bot when it completes
        setBotCurrentTasks((prev) => {
          const next = new Map(prev)
          next.delete(bot.botName)
          return next
        })
      }
    },
    onTaskStarted: (event) => {
      if (event.runId === testId) {
        setBotCurrentTasks((prev) => {
          const next = new Map(prev)
          next.set(event.botName, {
            taskName: event.taskName,
            taskIndex: event.taskIndex,
            totalTasks: event.totalTasks,
          })
          return next
        })
      }
    },
    onTaskCompleted: (event) => {
      if (event.runId === testId) {
        // Update bot's task results in real-time
        setTest((prev) => {
          if (!prev) return prev
          const existingBots = prev.botResults || []
          const botIndex = existingBots.findIndex((b) => b.botName === event.botName)

          if (botIndex === -1) return prev

          const bot = existingBots[botIndex]
          const newTaskResult: ApiTaskResult = {
            taskName: event.taskName,
            result: event.result,
            durationSeconds: event.durationSeconds,
            errorMessage: event.errorMessage,
          }

          // Add the task result if not already present
          const existingTaskResults = bot.taskResults || []
          const taskExists = existingTaskResults.some(
            (t) => t.taskName === event.taskName && Math.abs(t.durationSeconds - event.durationSeconds) < 0.01
          )

          if (taskExists) return prev

          const updatedBot: ApiBotResult = {
            ...bot,
            taskResults: [...existingTaskResults, newTaskResult],
            tasksCompleted: bot.tasksCompleted + (event.result === 'Success' ? 1 : 0),
            tasksFailed: bot.tasksFailed + (event.result === 'Failed' ? 1 : 0),
            tasksSkipped: bot.tasksSkipped + (event.result === 'Skipped' ? 1 : 0),
            totalTasks: event.totalTasks,
          }

          const updatedBots = [...existingBots]
          updatedBots[botIndex] = updatedBot

          return {
            ...prev,
            botResults: updatedBots,
          }
        })
      }
    },
  })

  const handleStop = async () => {
    try {
      await testsApi.stop(testId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to stop test')
    }
  }

  const toggleBot = (botName: string) => {
    setExpandedBots((prev) => {
      const next = new Set(prev)
      if (next.has(botName)) {
        next.delete(botName)
      } else {
        next.add(botName)
      }
      return next
    })
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  if (error || !test) {
    return (
      <div className="max-w-7xl mx-auto px-4 py-8">
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          {error || 'Test not found'}
        </div>
        <Link to="/tests" className="mt-4 inline-block text-primary hover:underline">
          Back to Tests
        </Link>
      </div>
    )
  }

  const progress = test.botCount > 0 ? (test.botsCompleted / test.botCount) * 100 : 0

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* Header */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <Link
            to="/tests"
            className="text-sm text-muted-foreground hover:text-foreground mb-2 inline-block"
          >
            &larr; Back to Tests
          </Link>
          <h1 className="text-2xl font-bold">{test.routeName}</h1>
        </div>
        <div className="flex items-center gap-4">
          <Badge
            variant="outline"
            className={`${getStatusColor(test.status)} px-3 py-1.5 text-sm`}
          >
            {getStatusIcon(test.status)} {test.status}
          </Badge>
          {isRunning(test.status) && (
            <Button variant="destructive" onClick={handleStop}>
              Stop Test
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
            <p className="text-xl font-bold">{formatDuration(test.durationSeconds)}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Bots</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">
              <span className="text-green-600">{test.botsPassed}</span>
              {' / '}
              {test.botCount}
              {test.botsFailed > 0 && (
                <span className="text-destructive text-sm ml-1">({test.botsFailed} failed)</span>
              )}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Level</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-bold">{test.level}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Classes</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm font-medium">
              {test.classes.length > 0 ? (
                test.classes.map((cls, i) => (
                  <span key={cls}>
                    <span style={{ color: getClassColor(cls) }}>{cls}</span>
                    {i < test.classes.length - 1 && ', '}
                  </span>
                ))
              ) : (
                <span>All</span>
              )}
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Progress Bar */}
      <Card className="mb-6">
        <CardContent className="pt-6">
          <div className="flex justify-between text-sm text-muted-foreground mb-2">
            <span>Progress</span>
            <span>{test.botsCompleted} / {test.botCount} bots completed</span>
          </div>
          <div className="h-4 bg-muted rounded-full overflow-hidden">
            <div
              className={`h-full transition-all duration-300 ${
                test.botsFailed > 0 ? 'bg-amber-500' : 'bg-green-500'
              }`}
              style={{ width: `${progress}%` }}
            />
          </div>
        </CardContent>
      </Card>

      {/* Error Message */}
      {test.errorMessage && (
        <div className="bg-destructive/10 border border-destructive/20 rounded-lg p-4 mb-6">
          <h3 className="font-medium text-destructive mb-1">Error</h3>
          <p className="text-destructive text-sm">{test.errorMessage}</p>
        </div>
      )}

      {/* Bot Results */}
      <Card>
        <CardHeader>
          <CardTitle>Bot Results</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <div className="divide-y">
            {(!test.botResults || test.botResults.length === 0) ? (
              <div className="text-muted-foreground text-center py-8">
                {isRunning(test.status) ? 'Waiting for bot results...' : 'No bot results'}
              </div>
            ) : (
              test.botResults.map((bot) => (
                <BotResultRow
                  key={bot.botName}
                  bot={bot}
                  expanded={expandedBots.has(bot.botName)}
                  onToggle={() => toggleBot(bot.botName)}
                  currentTask={botCurrentTasks.get(bot.botName)}
                />
              ))
            )}
          </div>
        </CardContent>
      </Card>

      {/* Timestamps */}
      <div className="mt-6 text-sm text-muted-foreground">
        <p>Started: {formatDateTime(test.startTime)}</p>
        {test.endTime && <p>Ended: {formatDateTime(test.endTime)}</p>}
      </div>
    </div>
  )
}

function BotResultRow({
  bot,
  expanded,
  onToggle,
  currentTask,
}: {
  bot: ApiBotResult
  expanded: boolean
  onToggle: () => void
  currentTask?: CurrentTaskInfo
}) {
  const [elapsedSeconds, setElapsedSeconds] = useState(bot.durationSeconds)
  const [rawLogsOpen, setRawLogsOpen] = useState(false)

  // Update elapsed time for running bots
  useEffect(() => {
    if (bot.isComplete) {
      setElapsedSeconds(bot.durationSeconds)
      return
    }

    // If no startTime available, just use durationSeconds
    if (!bot.startTime) {
      setElapsedSeconds(bot.durationSeconds)
      return
    }

    // Calculate initial elapsed time from start time
    const startTime = new Date(bot.startTime).getTime()
    if (isNaN(startTime)) {
      setElapsedSeconds(bot.durationSeconds)
      return
    }

    const updateElapsed = () => {
      const now = Date.now()
      setElapsedSeconds((now - startTime) / 1000)
    }
    updateElapsed()

    const interval = setInterval(updateElapsed, 1000)
    return () => clearInterval(interval)
  }, [bot.isComplete, bot.startTime, bot.durationSeconds])

  const taskProgress =
    bot.totalTasks > 0
      ? ((bot.tasksCompleted + bot.tasksFailed + bot.tasksSkipped) / bot.totalTasks) * 100
      : 0

  return (
    <div>
      <div
        className="px-4 py-3 hover:bg-muted/50 cursor-pointer"
        onClick={onToggle}
      >
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <span className="text-muted-foreground">{expanded ? '▼' : '▶'}</span>
            <div>
              <div className="font-medium">
                {bot.characterName}
                <span className="ml-2 text-sm" style={{ color: getClassColor(bot.characterClass) }}>
                  ({bot.characterClass})
                </span>
              </div>
              <div className="text-xs text-muted-foreground">{bot.botName}</div>
              {/* Show current task when bot is running */}
              {!bot.isComplete && currentTask && (
                <div className="text-xs text-blue-400 mt-0.5">
                  Current: {currentTask.taskName} ({currentTask.taskIndex + 1}/{currentTask.totalTasks})
                </div>
              )}
            </div>
          </div>
          <div className="flex items-center gap-4">
            <div className="text-sm text-muted-foreground">
              {formatDuration(elapsedSeconds)}
            </div>
            <div className="flex items-center gap-2">
              <div className="w-20 h-2 bg-muted rounded-full overflow-hidden">
                <div
                  className={`h-full ${bot.tasksFailed > 0 ? 'bg-destructive' : 'bg-green-500'}`}
                  style={{ width: `${taskProgress}%` }}
                />
              </div>
              <span className="text-xs text-muted-foreground">
                {bot.tasksCompleted}/{bot.totalTasks}
              </span>
            </div>
            <Badge
              variant={bot.success ? 'default' : bot.isComplete ? 'destructive' : 'secondary'}
              className={bot.success ? 'bg-green-500' : ''}
            >
              {bot.success ? '✓ Passed' : bot.isComplete ? '✗ Failed' : '⟳ Running'}
            </Badge>
          </div>
        </div>
        {bot.errorMessage && (
          <div className="mt-2 text-sm text-destructive">{bot.errorMessage}</div>
        )}
      </div>

      {/* Expanded Task Details */}
      {expanded && (
        <div className="px-4 pb-4 bg-muted/30">
          {bot.taskResults && bot.taskResults.length > 0 ? (
            <div className="ml-6 border-l-2 border-border pl-4">
              {bot.taskResults.map((task, idx) => (
                <TaskResultRow key={idx} task={task} />
              ))}
            </div>
          ) : (
            <div className="ml-6 text-sm text-muted-foreground">No task details available</div>
          )}

          {/* Logs */}
          {bot.logs && bot.logs.length > 0 && (
            <div className="ml-6 mt-4">
              <div className="flex items-center justify-between mb-2">
                <h4 className="text-sm font-medium">Logs</h4>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={(e) => {
                    e.stopPropagation()
                    setRawLogsOpen(true)
                  }}
                >
                  View Raw Logs
                </Button>
              </div>
              <div className="bg-zinc-900 rounded p-3 max-h-48 overflow-y-auto">
                {bot.logs.map((log, idx) => (
                  <div key={idx} className="text-xs text-zinc-300 font-mono">
                    {log}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Show View Raw Logs button even if no inline logs */}
          {(!bot.logs || bot.logs.length === 0) && (
            <div className="ml-6 mt-4">
              <Button
                variant="outline"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  setRawLogsOpen(true)
                }}
              >
                View Raw Logs
              </Button>
            </div>
          )}
        </div>
      )}

      {/* Raw Logs Dialog */}
      <RawLogsDialog
        open={rawLogsOpen}
        onOpenChange={setRawLogsOpen}
        botName={bot.botName}
        characterName={bot.characterName}
      />
    </div>
  )
}

function TaskResultRow({ task }: { task: ApiTaskResult }) {
  return (
    <div className="py-2 text-sm">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Badge
            variant={task.result === 'Success' ? 'default' : task.result === 'Failed' ? 'destructive' : 'secondary'}
            className={`px-1.5 py-0.5 text-xs ${task.result === 'Success' ? 'bg-green-500' : ''}`}
          >
            {task.result === 'Success' ? '✓' : task.result === 'Failed' ? '✗' : '○'}
          </Badge>
          <span>{task.taskName}</span>
        </div>
        <span className="text-muted-foreground">{formatDuration(task.durationSeconds)}</span>
      </div>
      {task.errorMessage && (
        <div className="mt-1 ml-6 text-xs text-destructive bg-destructive/10 px-2 py-1 rounded">
          {task.errorMessage}
        </div>
      )}
    </div>
  )
}
