import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useEffect, useState, useCallback } from 'react'
import { equipmentSetsApi } from '~/lib/api'
import type { EquipmentSetFormData, EquipmentSetItemFormData } from '~/lib/types'
import { PLAYER_CLASSES } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Textarea } from '~/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import { EquipmentSetItemEditor } from '~/components/equipment-set-editor/EquipmentSetItemEditor'

interface EditorSearch {
  name?: string
  new?: string
}

export const Route = createFileRoute('/equipment-sets/editor')({
  component: EquipmentSetEditor,
  validateSearch: (search: Record<string, unknown>): EditorSearch => ({
    name: typeof search.name === 'string' ? search.name : undefined,
    new: typeof search.new === 'string' ? search.new : undefined,
  }),
})

function generateItemId(): string {
  return `item-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
}

function getDefaultFormData(name: string = 'New Equipment Set'): EquipmentSetFormData {
  return {
    name,
    description: '',
    classRestriction: null,
    items: [],
  }
}

function parseRawJsonToFormData(rawJson: string): EquipmentSetFormData {
  try {
    const data = JSON.parse(rawJson)
    const formData: EquipmentSetFormData = {
      name: data.name || 'Untitled',
      description: data.description || '',
      classRestriction: data.classRestriction || null,
      items: [],
    }

    if (Array.isArray(data.items)) {
      formData.items = data.items.map((item: { entry: number; count?: number }) => ({
        id: generateItemId(),
        entry: item.entry,
        count: item.count || 1,
      }))
    }

    return formData
  } catch (err) {
    console.error('Failed to parse equipment set JSON:', err)
    return getDefaultFormData()
  }
}

function formDataToJson(formData: EquipmentSetFormData): string {
  const output: Record<string, unknown> = {
    name: formData.name,
  }

  if (formData.description) {
    output.description = formData.description
  }

  if (formData.classRestriction) {
    output.classRestriction = formData.classRestriction
  }

  output.items = formData.items.map((item) => ({
    entry: item.entry,
    count: item.count,
  }))

  return JSON.stringify(output, null, 2)
}

function EquipmentSetEditor() {
  const { name, new: newName } = Route.useSearch()
  const navigate = useNavigate()
  const isNew = !!newName

  const [formData, setFormData] = useState<EquipmentSetFormData>(
    getDefaultFormData(newName || 'New Equipment Set')
  )
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [isDirty, setIsDirty] = useState(false)

  useEffect(() => {
    if (!isNew && name) {
      loadData()
    }
  }, [name, isNew])

  async function loadData() {
    try {
      setLoading(true)
      const detail = await equipmentSetsApi.getByName(name!)
      setFormData(parseRawJsonToFormData(detail.rawJson))
      setError(null)
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load equipment set')
    } finally {
      setLoading(false)
    }
  }

  const handleSave = async () => {
    const setName = isNew ? newName : name
    if (!setName) return

    if (!formData.name.trim()) {
      setError('Equipment set name is required')
      return
    }

    try {
      setSaving(true)
      setError(null)
      const json = formDataToJson(formData)

      if (isNew) {
        await equipmentSetsApi.create({ name: setName, content: json })
        setSuccess('Equipment set created successfully!')
        // Navigate to edit mode
        setTimeout(() => {
          navigate({
            to: '/equipment-sets/editor',
            search: { name: formData.name },
          })
        }, 1000)
      } else {
        await equipmentSetsApi.update(setName, json)
        setSuccess('Equipment set saved successfully!')
      }
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save equipment set')
    } finally {
      setSaving(false)
    }
  }

  const handleExport = () => {
    const json = formDataToJson(formData)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${formData.name.replace(/[^a-z0-9]/gi, '-').toLowerCase()}.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  const updateFormData = useCallback((updates: Partial<EquipmentSetFormData>) => {
    setFormData((prev) => ({ ...prev, ...updates }))
    setIsDirty(true)
    setSuccess(null)
  }, [])

  const addItem = () => {
    updateFormData({
      items: [...formData.items, { id: generateItemId(), entry: 0, count: 1 }],
    })
  }

  const updateItem = (id: string, updates: Partial<EquipmentSetItemFormData>) => {
    updateFormData({
      items: formData.items.map((item) =>
        item.id === id ? { ...item, ...updates } : item
      ),
    })
  }

  const removeItem = (id: string) => {
    updateFormData({
      items: formData.items.filter((item) => item.id !== id),
    })
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    )
  }

  return (
    <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* Header */}
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-bold">
            {isNew ? 'Create New Equipment Set' : 'Edit Equipment Set'}
          </h1>
          {!isNew && name && (
            <p className="text-sm text-muted-foreground mt-1">
              Editing: <code className="bg-muted px-1 py-0.5 rounded">{name}</code>
            </p>
          )}
          {isNew && newName && (
            <p className="text-sm text-muted-foreground mt-1">
              Creating: <code className="bg-muted px-1 py-0.5 rounded">{newName}</code>
            </p>
          )}
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate({ to: '/equipment-sets' })}>
            Cancel
          </Button>
          <Button variant="outline" onClick={handleExport}>
            Export JSON
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? 'Saving...' : isNew ? 'Create Set' : 'Save Changes'}
          </Button>
        </div>
      </div>

      {/* Dirty indicator */}
      {isDirty && (
        <div className="mb-4 bg-yellow-50 border border-yellow-200 rounded-lg p-3 text-yellow-800 text-sm">
          You have unsaved changes
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="mb-4 bg-destructive/10 border border-destructive/20 rounded-lg p-4 text-destructive">
          {error}
          <Button variant="ghost" size="sm" onClick={() => setError(null)} className="ml-2">
            Dismiss
          </Button>
        </div>
      )}

      {/* Success */}
      {success && (
        <div className="mb-4 bg-green-50 border border-green-200 rounded-lg p-4 text-green-700">
          {success}
        </div>
      )}

      {/* Form */}
      <div className="space-y-6">
        {/* Basic Info */}
        <Card>
          <CardHeader>
            <CardTitle>Basic Information</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                value={formData.name}
                onChange={(e) => updateFormData({ name: e.target.value })}
                placeholder="Equipment Set Name"
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="description">Description (optional)</Label>
              <Textarea
                id="description"
                value={formData.description}
                onChange={(e) => updateFormData({ description: e.target.value })}
                placeholder="Brief description of this equipment set..."
                rows={2}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="classRestriction">Class Restriction (optional)</Label>
              <Select
                value={formData.classRestriction || '_all'}
                onValueChange={(value) =>
                  updateFormData({ classRestriction: value === '_all' ? null : value })
                }
              >
                <SelectTrigger>
                  <SelectValue placeholder="All classes" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="_all">All Classes</SelectItem>
                  {PLAYER_CLASSES.map((cls) => (
                    <SelectItem key={cls} value={cls}>
                      {cls}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Leave as "All Classes" if this set can be used by any class
              </p>
            </div>
          </CardContent>
        </Card>

        {/* Items */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0">
            <CardTitle>Items</CardTitle>
            <Button onClick={addItem} size="sm">
              Add Item
            </Button>
          </CardHeader>
          <CardContent>
            {formData.items.length === 0 ? (
              <div className="text-center text-muted-foreground py-8">
                No items added yet. Click "Add Item" to add equipment.
              </div>
            ) : (
              <div className="space-y-3">
                {formData.items.map((item, index) => (
                  <EquipmentSetItemEditor
                    key={item.id}
                    item={item}
                    index={index}
                    onUpdate={(updates) => updateItem(item.id, updates)}
                    onRemove={() => removeItem(item.id)}
                  />
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
