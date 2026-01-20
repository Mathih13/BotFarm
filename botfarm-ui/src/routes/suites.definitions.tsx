import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { suiteDefinitionsApi } from '~/lib/api'
import type { ApiSuiteDefinitionInfo } from '~/lib/types'
import { Button } from '~/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '~/components/ui/table'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '~/components/ui/dialog'
import { Input } from '~/components/ui/input'

export const Route = createFileRoute('/suites/definitions')({
  component: SuiteDefinitions,
})

function SuiteDefinitions() {
  const navigate = useNavigate()
  const [suites, setSuites] = useState<ApiSuiteDefinitionInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showCreateDialog, setShowCreateDialog] = useState(false)
  const [newSuitePath, setNewSuitePath] = useState('')
  const [filter, setFilter] = useState('')
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null)

  useEffect(() => {
    loadSuites()
  }, [])

  async function loadSuites() {
    try {
      setLoading(true)
      const data = await suiteDefinitionsApi.getAll()
      setSuites(data)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load suite definitions')
    } finally {
      setLoading(false)
    }
  }

  const filteredSuites = suites.filter(
    (suite) =>
      suite.name.toLowerCase().includes(filter.toLowerCase()) ||
      suite.path.toLowerCase().includes(filter.toLowerCase())
  )

  const handleCreateNew = () => {
    if (!newSuitePath.trim()) return
    setShowCreateDialog(false)
    navigate({ to: '/suites/editor', search: { new: newSuitePath } })
  }

  const handleDelete = async (path: string) => {
    try {
      await suiteDefinitionsApi.delete(path)
      setSuites((prev) => prev.filter((s) => s.path !== path))
      setDeleteConfirm(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete suite')
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
        <div>
          <h1 className="text-2xl font-bold">Suite Definitions</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage test suite definition files
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate({ to: '/suites' })}>
            Back to Suites
          </Button>
          <Button onClick={() => setShowCreateDialog(true)}>Create New Suite</Button>
        </div>
      </div>

      {error && (
        <div className="mb-4 bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          {error}
          <Button variant="ghost" size="sm" onClick={() => setError(null)} className="ml-2">
            Dismiss
          </Button>
        </div>
      )}

      {/* Create New Dialog */}
      <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Create New Suite</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2">Suite Path</label>
              <Input
                placeholder="e.g., my-suite.json"
                value={newSuitePath}
                onChange={(e) => setNewSuitePath(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleCreateNew()}
              />
              <p className="text-xs text-muted-foreground mt-1">
                File name for the suite (will be saved in routes/suites/)
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreateDialog(false)}>
              Cancel
            </Button>
            <Button onClick={handleCreateNew} disabled={!newSuitePath.trim()}>
              Continue to Editor
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={!!deleteConfirm} onOpenChange={() => setDeleteConfirm(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete Suite</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground">
            Are you sure you want to delete <strong>{deleteConfirm}</strong>? This action cannot be
            undone.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteConfirm(null)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={() => deleteConfirm && handleDelete(deleteConfirm)}>
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Filter */}
      <div className="mb-4">
        <Input
          placeholder="Filter suites..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="max-w-sm"
        />
      </div>

      {/* Suites Table */}
      <div className="bg-card rounded-lg shadow-sm border overflow-hidden">
        {filteredSuites.length === 0 ? (
          <div className="text-muted-foreground text-center py-8">
            {suites.length === 0 ? 'No suite definitions found' : 'No suites match the filter'}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Path</TableHead>
                <TableHead>Tests</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredSuites.map((suite) => (
                <TableRow key={suite.path}>
                  <TableCell className="font-medium">{suite.name}</TableCell>
                  <TableCell>
                    <code className="text-xs bg-muted px-1 py-0.5 rounded">{suite.path}</code>
                  </TableCell>
                  <TableCell>{suite.testCount}</TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <Button variant="outline" size="sm" asChild>
                        <Link to="/suites/editor" search={{ path: suite.path }}>
                          Edit
                        </Link>
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-destructive hover:text-destructive"
                        onClick={() => setDeleteConfirm(suite.path)}
                      >
                        Delete
                      </Button>
                    </div>
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
