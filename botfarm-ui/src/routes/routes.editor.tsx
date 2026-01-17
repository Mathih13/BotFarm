import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useEffect, useState, useCallback } from 'react'
import { routesApi } from '~/lib/api'
import type { RouteFormData, TaskFormData } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { RouteEditorForm } from '~/components/route-editor/RouteEditorForm'

interface EditorSearch {
  path?: string
  new?: string
}

export const Route = createFileRoute('/routes/editor')({
  component: RouteEditor,
  validateSearch: (search: Record<string, unknown>): EditorSearch => ({
    path: typeof search.path === 'string' ? search.path : undefined,
    new: typeof search.new === 'string' ? search.new : undefined,
  }),
})

function generateTaskId(): string {
  return `task-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
}

function getDefaultFormData(): RouteFormData {
  return {
    name: 'New Route',
    description: '',
    loop: false,
    harness: null,
    tasks: [],
  }
}

function parseRawJsonToFormData(rawJson: string): RouteFormData {
  try {
    const data = JSON.parse(rawJson)
    const formData: RouteFormData = {
      name: data.name || 'Untitled',
      description: data.description || '',
      loop: data.loop || false,
      harness: null,
      tasks: [],
    }

    if (data.harness) {
      formData.harness = {
        botCount: data.harness.botCount || 1,
        accountPrefix: data.harness.accountPrefix || 'testbot_',
        classes: data.harness.classes || [],
        race: data.harness.race || 'Human',
        level: data.harness.level || 1,
        items: data.harness.items || [],
        completedQuests: data.harness.completedQuests || [],
        startPosition: data.harness.startPosition || null,
        setupTimeoutSeconds: data.harness.setupTimeoutSeconds || 120,
        testTimeoutSeconds: data.harness.testTimeoutSeconds || 600,
      }
    }

    if (Array.isArray(data.tasks)) {
      formData.tasks = data.tasks.map((task: { type: string; parameters: Record<string, unknown> }) => ({
        id: generateTaskId(),
        type: task.type,
        parameters: task.parameters || {},
      }))
    }

    return formData
  } catch (err) {
    console.error('Failed to parse route JSON:', err)
    return getDefaultFormData()
  }
}

function formDataToJson(formData: RouteFormData): string {
  const output: Record<string, unknown> = {
    name: formData.name,
    description: formData.description || undefined,
    loop: formData.loop,
  }

  if (formData.harness) {
    const harness: Record<string, unknown> = {
      botCount: formData.harness.botCount,
      accountPrefix: formData.harness.accountPrefix,
      classes: formData.harness.classes,
      race: formData.harness.race,
      level: formData.harness.level,
      setupTimeoutSeconds: formData.harness.setupTimeoutSeconds,
      testTimeoutSeconds: formData.harness.testTimeoutSeconds,
    }
    if (formData.harness.items.length > 0) {
      harness.items = formData.harness.items
    }
    if (formData.harness.completedQuests.length > 0) {
      harness.completedQuests = formData.harness.completedQuests
    }
    if (formData.harness.startPosition) {
      harness.startPosition = formData.harness.startPosition
    }
    output.harness = harness
  }

  output.tasks = formData.tasks.map((task) => ({
    type: task.type,
    parameters: task.parameters,
  }))

  return JSON.stringify(output, null, 2)
}

function RouteEditor() {
  const { path, new: newPath } = Route.useSearch()
  const navigate = useNavigate()
  const isNew = !!newPath

  const [formData, setFormData] = useState<RouteFormData>(getDefaultFormData())
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [isDirty, setIsDirty] = useState(false)

  useEffect(() => {
    if (path && !isNew) {
      loadRoute(path)
    }
  }, [path, isNew])

  async function loadRoute(routePath: string) {
    try {
      setLoading(true)
      const detail = await routesApi.getByPath(routePath)
      setFormData(parseRawJsonToFormData(detail.rawJson))
      setError(null)
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load route')
    } finally {
      setLoading(false)
    }
  }

  const handleSave = async () => {
    const routePath = isNew ? newPath : path
    if (!routePath) return

    try {
      setSaving(true)
      setError(null)
      const json = formDataToJson(formData)

      if (isNew) {
        await routesApi.create({ path: routePath, content: json })
        setSuccess('Route created successfully!')
        // Navigate to edit mode
        setTimeout(() => {
          navigate({ to: '/routes/editor', search: { path: routePath.endsWith('.json') ? routePath : `${routePath}.json` } })
        }, 1000)
      } else {
        await routesApi.update(routePath, json)
        setSuccess('Route saved successfully!')
      }
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save route')
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

  const updateFormData = useCallback((updates: Partial<RouteFormData>) => {
    setFormData((prev) => ({ ...prev, ...updates }))
    setIsDirty(true)
    setSuccess(null)
  }, [])

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
          <h1 className="text-2xl font-bold">{isNew ? 'Create New Route' : 'Edit Route'}</h1>
          {!isNew && path && (
            <p className="text-sm text-muted-foreground mt-1">
              <code className="bg-muted px-1 py-0.5 rounded">{path}</code>
            </p>
          )}
          {isNew && newPath && (
            <p className="text-sm text-muted-foreground mt-1">
              Creating: <code className="bg-muted px-1 py-0.5 rounded">{newPath}</code>
            </p>
          )}
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => navigate({ to: '/routes' })}>
            Cancel
          </Button>
          <Button variant="outline" onClick={handleExport}>
            Export JSON
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? 'Saving...' : isNew ? 'Create Route' : 'Save Changes'}
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
      <RouteEditorForm formData={formData} onChange={updateFormData} />
    </div>
  )
}
