import { useEffect, useState, useCallback } from 'react'
import { logsApi } from '~/lib/api'
import type { ApiLogEntry } from '~/lib/types'
import { Button } from '~/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '~/components/ui/dialog'

interface RawLogsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  botName: string
  characterName: string
}

function getLogLevelColor(level: string): string {
  switch (level.toLowerCase()) {
    case 'error':
    case 'fatal':
      return 'text-red-400'
    case 'warn':
    case 'warning':
      return 'text-yellow-400'
    case 'info':
      return 'text-blue-400'
    case 'debug':
      return 'text-zinc-500'
    default:
      return 'text-zinc-300'
  }
}

function formatTimestamp(timestamp: string): string {
  try {
    const date = new Date(timestamp)
    return date.toLocaleTimeString('en-US', {
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return timestamp
  }
}

export function RawLogsDialog({
  open,
  onOpenChange,
  botName,
  characterName,
}: RawLogsDialogProps) {
  const [logs, setLogs] = useState<ApiLogEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [totalCount, setTotalCount] = useState(0)
  const [filteredCount, setFilteredCount] = useState(0)

  const fetchLogs = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const response = await logsApi.getLogs({
        count: 500,
        filter: botName,
        minLevel: 'Warning',  // Filter out Debug and Detail logs (Warning < Info < Error in this codebase)
      })
      setLogs(response.logs)
      setTotalCount(response.totalCount)
      setFilteredCount(response.filteredCount)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch logs')
    } finally {
      setLoading(false)
    }
  }, [botName])

  useEffect(() => {
    if (open) {
      fetchLogs()
    }
  }, [open, fetchLogs])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="min-w-4xl max-h-[80vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>
            Raw Logs: {characterName}
            <span className="text-sm font-normal text-muted-foreground ml-2">
              ({botName})
            </span>
          </DialogTitle>
        </DialogHeader>

        <div className="flex items-center justify-between mb-2">
          <div className="text-sm text-muted-foreground">
            Showing {logs.length} of {filteredCount} matching logs ({totalCount} total)
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={fetchLogs}
            disabled={loading}
          >
            {loading ? 'Refreshing...' : 'Refresh'}
          </Button>
        </div>

        <div className="flex-1 min-h-0 bg-zinc-900 rounded p-3 overflow-y-auto font-mono text-xs">
          {loading && logs.length === 0 ? (
            <div className="text-zinc-500 text-center py-8">Loading logs...</div>
          ) : error ? (
            <div className="text-red-400 text-center py-8">{error}</div>
          ) : logs.length === 0 ? (
            <div className="text-zinc-500 text-center py-8">
              No logs found for this bot
            </div>
          ) : (
            logs.map((log, idx) => (
              <div key={idx} className="flex gap-2 py-0.5 hover:bg-zinc-800">
                <span className="text-zinc-500 flex-shrink-0">
                  {formatTimestamp(log.timestamp)}
                </span>
                <span className={`flex-shrink-0 w-12 ${getLogLevelColor(log.level)}`}>
                  [{log.level.toUpperCase().slice(0, 5).padEnd(5)}]
                </span>
                <span className="text-zinc-300 break-all">{log.message}</span>
              </div>
            ))
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
