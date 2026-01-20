import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useEffect, useState, useCallback } from 'react'
import { suiteDefinitionsApi, routesApi } from '~/lib/api'
import type { SuiteFormData, SuiteTestFormData, ApiRouteInfo } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { SuiteEditorForm } from '~/components/suite-editor/SuiteEditorForm'

interface EditorSearch {
  path?: string
  new?: string
}

export const Route = createFileRoute('/suites/editor')({
  component: SuiteEditor,
  validateSearch: (search: Record<string, unknown>): EditorSearch => ({
    path: typeof search.path === 'string' ? search.path : undefined,
    new: typeof search.new === 'string' ? search.new : undefined,
  }),
})

function generateTestId(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
}

function getDefaultFormData(): SuiteFormData {
  return {
    name: 'New Suite',
    tests: [],
  }
}

function parseRawJsonToFormData(rawJson: string): SuiteFormData {
  try {
    const data = JSON.parse(rawJson)
    const formData: SuiteFormData = {
      name: data.name || 'Untitled',
      tests: [],
    }

    if (Array.isArray(data.tests)) {
      formData.tests = data.tests.map((test: { route: string; dependsOn?: string[] }) => ({
        id: generateTestId(),
        route: test.route,
        dependsOn: test.dependsOn || [],
      }))
    }

    return formData
  } catch (err) {
    console.error('Failed to parse suite JSON:', err)
    return getDefaultFormData()
  }
}

function formDataToJson(formData: SuiteFormData, allTests: SuiteTestFormData[]): string {
  // Build a map of test IDs to their indices for dependency resolution
  const idToIndex = new Map<string, number>()
  allTests.forEach((test, index) => {
    idToIndex.set(test.id, index)
  })

  const output: Record<string, unknown> = {
    name: formData.name,
    tests: formData.tests.map((test) => {
      const entry: Record<string, unknown> = {
        route: test.route,
      }
      // Convert dependency IDs to route paths
      if (test.dependsOn && test.dependsOn.length > 0) {
        const dependsOnRoutes = test.dependsOn
          .map((depId) => {
            const depTest = allTests.find((t) => t.id === depId)
            return depTest?.route
          })
          .filter(Boolean)
        if (dependsOnRoutes.length > 0) {
          entry.dependsOn = dependsOnRoutes
        }
      }
      return entry
    }),
  }

  return JSON.stringify(output, null, 2)
}

function SuiteEditor() {
  const { path, new: newPath } = Route.useSearch()
  const navigate = useNavigate()
  const isNew = !!newPath

  const [formData, setFormData] = useState<SuiteFormData>(getDefaultFormData())
  const [availableRoutes, setAvailableRoutes] = useState<ApiRouteInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [isDirty, setIsDirty] = useState(false)

  useEffect(() => {
    loadData()
  }, [path, isNew])

  async function loadData() {
    try {
      setLoading(true)
      // Always load available routes
      const routes = await routesApi.getAll()
      setAvailableRoutes(routes)

      // Load suite if editing
      if (path && !isNew) {
        const detail = await suiteDefinitionsApi.getByPath(path)
        setFormData(parseRawJsonToFormData(detail.rawJson))
      }
      setError(null)
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data')
    } finally {
      setLoading(false)
    }
  }

  const handleSave = async () => {
    const suitePath = isNew ? newPath : path
    if (!suitePath) return

    if (!formData.name.trim()) {
      setError('Suite name is required')
      return
    }

    if (formData.tests.length === 0) {
      setError('Suite must have at least one test')
      return
    }

    try {
      setSaving(true)
      setError(null)
      const json = formDataToJson(formData, formData.tests)

      if (isNew) {
        await suiteDefinitionsApi.create({ path: suitePath, content: json })
        setSuccess('Suite created successfully!')
        // Navigate to edit mode
        setTimeout(() => {
          navigate({
            to: '/suites/editor',
            search: { path: suitePath.endsWith('.json') ? suitePath : `${suitePath}.json` },
          })
        }, 1000)
      } else {
        await suiteDefinitionsApi.update(suitePath, json)
        setSuccess('Suite saved successfully!')
      }
      setIsDirty(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save suite')
    } finally {
      setSaving(false)
    }
  }

  const handleExport = () => {
    const json = formDataToJson(formData, formData.tests)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${formData.name.replace(/[^a-z0-9]/gi, '-').toLowerCase()}.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  const updateFormData = useCallback((updates: Partial<SuiteFormData>) => {
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

  const routeOptions = availableRoutes.map((r) => ({ path: r.path, name: r.name }))

  return (
    <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* Header */}
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-bold">{isNew ? 'Create New Suite' : 'Edit Suite'}</h1>
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
          <Button variant="outline" onClick={() => navigate({ to: '/suites/definitions' })}>
            Cancel
          </Button>
          <Button variant="outline" onClick={handleExport}>
            Export JSON
          </Button>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? 'Saving...' : isNew ? 'Create Suite' : 'Save Changes'}
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
      <SuiteEditorForm formData={formData} availableRoutes={routeOptions} onChange={updateFormData} />
    </div>
  )
}
