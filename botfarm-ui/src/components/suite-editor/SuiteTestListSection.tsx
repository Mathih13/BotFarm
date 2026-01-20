import { useState } from 'react'
import type { SuiteTestFormData } from '~/lib/types'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Button } from '~/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import { SuiteTestEditor } from './SuiteTestEditor'

interface SuiteTestListSectionProps {
  tests: SuiteTestFormData[]
  availableRoutes: { path: string; name: string }[]
  onChange: (tests: SuiteTestFormData[]) => void
}

function generateTestId(): string {
  return `test-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
}

export function SuiteTestListSection({
  tests,
  availableRoutes,
  onChange,
}: SuiteTestListSectionProps) {
  const [selectedRoute, setSelectedRoute] = useState<string>('')
  const [expandedTests, setExpandedTests] = useState<Set<string>>(new Set())

  const addTest = () => {
    if (!selectedRoute) return
    const newTest: SuiteTestFormData = {
      id: generateTestId(),
      route: selectedRoute,
      dependsOn: [],
    }
    onChange([...tests, newTest])
    setExpandedTests((prev) => new Set([...prev, newTest.id]))
    setSelectedRoute('')
  }

  const removeTest = (id: string) => {
    // Remove the test and clean up any dependencies referencing it
    const updatedTests = tests
      .filter((t) => t.id !== id)
      .map((t) => ({
        ...t,
        dependsOn: t.dependsOn.filter((depId) => depId !== id),
      }))
    onChange(updatedTests)
    setExpandedTests((prev) => {
      const next = new Set(prev)
      next.delete(id)
      return next
    })
  }

  const updateTest = (id: string, updates: Partial<SuiteTestFormData>) => {
    onChange(tests.map((t) => (t.id === id ? { ...t, ...updates } : t)))
  }

  const moveTest = (index: number, direction: 'up' | 'down') => {
    const newIndex = direction === 'up' ? index - 1 : index + 1
    if (newIndex < 0 || newIndex >= tests.length) return

    const newTests = [...tests]
    const [test] = newTests.splice(index, 1)
    newTests.splice(newIndex, 0, test)
    onChange(newTests)
  }

  const toggleExpanded = (id: string) => {
    setExpandedTests((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  // Filter out routes already added
  const availableRoutesNotAdded = availableRoutes.filter(
    (route) => !tests.some((test) => test.route === route.path)
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tests ({tests.length})</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Add Test */}
        <div className="flex gap-2">
          <Select value={selectedRoute} onValueChange={setSelectedRoute}>
            <SelectTrigger className="flex-1">
              <SelectValue placeholder="Select a route to add..." />
            </SelectTrigger>
            <SelectContent>
              {availableRoutesNotAdded.length === 0 ? (
                <div className="p-2 text-sm text-muted-foreground">
                  All available routes have been added
                </div>
              ) : (
                availableRoutesNotAdded.map((route) => (
                  <SelectItem key={route.path} value={route.path}>
                    <div className="flex flex-col">
                      <span>{route.name}</span>
                      <span className="text-xs text-muted-foreground">{route.path}</span>
                    </div>
                  </SelectItem>
                ))
              )}
            </SelectContent>
          </Select>
          <Button onClick={addTest} disabled={!selectedRoute}>
            Add Test
          </Button>
        </div>

        {/* Test List */}
        {tests.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-8">
            No tests yet. Add a route to get started.
          </p>
        ) : (
          <div className="space-y-2">
            {tests.map((test, index) => (
              <SuiteTestEditor
                key={test.id}
                test={test}
                index={index}
                totalTests={tests.length}
                availableRoutes={availableRoutes}
                otherTests={tests.filter((t) => t.id !== test.id)}
                isExpanded={expandedTests.has(test.id)}
                onToggleExpanded={() => toggleExpanded(test.id)}
                onUpdate={(updates) => updateTest(test.id, updates)}
                onRemove={() => removeTest(test.id)}
                onMoveUp={() => moveTest(index, 'up')}
                onMoveDown={() => moveTest(index, 'down')}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
