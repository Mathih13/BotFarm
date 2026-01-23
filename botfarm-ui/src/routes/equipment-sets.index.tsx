import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { equipmentSetsApi } from '~/lib/api'
import type { ApiEquipmentSetInfo } from '~/lib/types'
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
import { Badge } from '~/components/ui/badge'
import { getClassColor } from '~/lib/utils'

export const Route = createFileRoute('/equipment-sets/')({
  component: EquipmentSetsIndex,
})

function EquipmentSetsIndex() {
  const navigate = useNavigate()
  const [sets, setSets] = useState<ApiEquipmentSetInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showCreateDialog, setShowCreateDialog] = useState(false)
  const [newSetName, setNewSetName] = useState('')
  const [filter, setFilter] = useState('')
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null)

  useEffect(() => {
    loadSets()
  }, [])

  async function loadSets() {
    try {
      setLoading(true)
      const data = await equipmentSetsApi.getAll()
      setSets(data)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load equipment sets')
    } finally {
      setLoading(false)
    }
  }

  const filteredSets = sets.filter(
    (set) =>
      set.name.toLowerCase().includes(filter.toLowerCase()) ||
      (set.description?.toLowerCase().includes(filter.toLowerCase()) ?? false) ||
      (set.classRestriction?.toLowerCase().includes(filter.toLowerCase()) ?? false)
  )

  const handleCreateNew = () => {
    if (!newSetName.trim()) return
    setShowCreateDialog(false)
    navigate({ to: '/equipment-sets/editor', search: { new: newSetName } })
  }

  const handleDelete = async (name: string) => {
    try {
      await equipmentSetsApi.delete(name)
      setSets((prev) => prev.filter((s) => s.name !== name))
      setDeleteConfirm(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete equipment set')
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
          <h1 className="text-2xl font-bold">Equipment Sets</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Reusable item sets that can be attached to test harnesses
          </p>
        </div>
        <Button onClick={() => setShowCreateDialog(true)}>Create New Set</Button>
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
            <DialogTitle>Create New Equipment Set</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2">Set Name</label>
              <Input
                placeholder="e.g., warrior-starter-gear"
                value={newSetName}
                onChange={(e) => setNewSetName(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleCreateNew()}
              />
              <p className="text-xs text-muted-foreground mt-1">
                This will also be used as the file name
              </p>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreateDialog(false)}>
              Cancel
            </Button>
            <Button onClick={handleCreateNew} disabled={!newSetName.trim()}>
              Continue to Editor
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={!!deleteConfirm} onOpenChange={() => setDeleteConfirm(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete Equipment Set</DialogTitle>
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
          placeholder="Filter equipment sets..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="max-w-sm"
        />
      </div>

      {/* Equipment Sets Table */}
      <div className="bg-card rounded-lg shadow-sm border overflow-hidden">
        {filteredSets.length === 0 ? (
          <div className="text-muted-foreground text-center py-8">
            {sets.length === 0 ? 'No equipment sets found' : 'No sets match the filter'}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Description</TableHead>
                <TableHead>Class</TableHead>
                <TableHead>Items</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredSets.map((set) => (
                <TableRow key={set.name}>
                  <TableCell className="font-medium">{set.name}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {set.description || '-'}
                  </TableCell>
                  <TableCell>
                    {set.classRestriction ? (
                      <Badge
                        variant="outline"
                        style={{
                          borderColor: getClassColor(set.classRestriction),
                          color: getClassColor(set.classRestriction),
                        }}
                      >
                        {set.classRestriction}
                      </Badge>
                    ) : (
                      <span className="text-muted-foreground text-sm">All Classes</span>
                    )}
                  </TableCell>
                  <TableCell>{set.itemCount}</TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <Button variant="outline" size="sm" asChild>
                        <Link to="/equipment-sets/editor" search={{ name: set.name }}>
                          Edit
                        </Link>
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-destructive hover:text-destructive"
                        onClick={() => setDeleteConfirm(set.name)}
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
