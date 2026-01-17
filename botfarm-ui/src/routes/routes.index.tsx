import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { routesApi } from '~/lib/api'
import type { ApiRouteInfo } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Badge } from '~/components/ui/badge'
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

export const Route = createFileRoute('/routes/')({
  component: RoutesIndex,
})

function RoutesIndex() {
  const navigate = useNavigate()
  const [routes, setRoutes] = useState<ApiRouteInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showCreateDialog, setShowCreateDialog] = useState(false)
  const [newRoutePath, setNewRoutePath] = useState('')
  const [filter, setFilter] = useState('')
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null)

  useEffect(() => {
    loadRoutes()
  }, [])

  async function loadRoutes() {
    try {
      setLoading(true)
      const data = await routesApi.getAll()
      setRoutes(data)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load routes')
    } finally {
      setLoading(false)
    }
  }

  const filteredRoutes = routes.filter(
    (route) =>
      route.name.toLowerCase().includes(filter.toLowerCase()) ||
      route.path.toLowerCase().includes(filter.toLowerCase())
  )

  const handleCreateNew = () => {
    if (!newRoutePath.trim()) return
    setShowCreateDialog(false)
    navigate({ to: '/routes/editor', search: { new: newRoutePath } })
  }

  const handleDelete = async (path: string) => {
    try {
      await routesApi.delete(path)
      setRoutes((prev) => prev.filter((r) => r.path !== path))
      setDeleteConfirm(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete route')
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
        <h1 className="text-2xl font-bold">Route Editor</h1>
        <Button onClick={() => setShowCreateDialog(true)}>Create New Route</Button>
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
            <DialogTitle>Create New Route</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2">Route Path</label>
              <Input
                placeholder="e.g., my-routes/new-route.json"
                value={newRoutePath}
                onChange={(e) => setNewRoutePath(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleCreateNew()}
              />
              <p className="text-xs text-muted-foreground mt-1">
                Relative path within the routes directory
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreateDialog(false)}>
              Cancel
            </Button>
            <Button onClick={handleCreateNew} disabled={!newRoutePath.trim()}>
              Continue to Editor
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={!!deleteConfirm} onOpenChange={() => setDeleteConfirm(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete Route</DialogTitle>
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
          placeholder="Filter routes..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="max-w-sm"
        />
      </div>

      {/* Routes Table */}
      <div className="bg-card rounded-lg shadow-sm border overflow-hidden">
        {filteredRoutes.length === 0 ? (
          <div className="text-muted-foreground text-center py-8">
            {routes.length === 0 ? 'No routes found' : 'No routes match the filter'}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Path</TableHead>
                <TableHead>Harness</TableHead>
                <TableHead>Bots</TableHead>
                <TableHead>Level</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredRoutes.map((route) => (
                <TableRow key={route.path}>
                  <TableCell className="font-medium">{route.name}</TableCell>
                  <TableCell>
                    <code className="text-xs bg-muted px-1 py-0.5 rounded">{route.path}</code>
                  </TableCell>
                  <TableCell>
                    {route.hasHarness ? (
                      <Badge variant="outline" className="text-green-600 border-green-300">
                        Yes
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        No
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell>{route.botCount ?? '-'}</TableCell>
                  <TableCell>{route.level ?? '-'}</TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        asChild
                      >
                        <Link to="/routes/editor" search={{ path: route.path }}>
                          Edit
                        </Link>
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-destructive hover:text-destructive"
                        onClick={() => setDeleteConfirm(route.path)}
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
