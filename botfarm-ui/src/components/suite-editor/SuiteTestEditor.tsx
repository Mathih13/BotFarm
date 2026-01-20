import type { SuiteTestFormData } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Badge } from '~/components/ui/badge'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '~/components/ui/collapsible'
import { Label } from '~/components/ui/label'

interface SuiteTestEditorProps {
  test: SuiteTestFormData
  index: number
  totalTests: number
  availableRoutes: { path: string; name: string }[]
  otherTests: SuiteTestFormData[]
  isExpanded: boolean
  onToggleExpanded: () => void
  onUpdate: (updates: Partial<SuiteTestFormData>) => void
  onRemove: () => void
  onMoveUp: () => void
  onMoveDown: () => void
}

export function SuiteTestEditor({
  test,
  index,
  totalTests,
  availableRoutes,
  otherTests,
  isExpanded,
  onToggleExpanded,
  onUpdate,
  onRemove,
  onMoveUp,
  onMoveDown,
}: SuiteTestEditorProps) {
  const selectedRoute = availableRoutes.find((r) => r.path === test.route)
  const routeName = selectedRoute?.name || test.route || 'No route selected'

  const handleDependencyToggle = (testId: string) => {
    const currentDeps = test.dependsOn || []
    if (currentDeps.includes(testId)) {
      onUpdate({ dependsOn: currentDeps.filter((d) => d !== testId) })
    } else {
      onUpdate({ dependsOn: [...currentDeps, testId] })
    }
  }

  return (
    <Collapsible open={isExpanded} onOpenChange={onToggleExpanded}>
      <div className="border rounded-lg bg-card">
        {/* Header */}
        <div className="flex items-center gap-2 p-3">
          <CollapsibleTrigger asChild>
            <Button variant="ghost" size="sm" className="h-6 w-6 p-0">
              {isExpanded ? (
                <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                </svg>
              ) : (
                <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                </svg>
              )}
            </Button>
          </CollapsibleTrigger>

          <span className="text-sm text-muted-foreground min-w-[2rem]">#{index + 1}</span>

          <span className="font-medium flex-1 truncate">{routeName}</span>

          {test.dependsOn && test.dependsOn.length > 0 && (
            <Badge variant="outline" className="text-xs">
              {test.dependsOn.length} {test.dependsOn.length === 1 ? 'dependency' : 'dependencies'}
            </Badge>
          )}

          <div className="flex gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={onMoveUp}
              disabled={index === 0}
              className="h-7 w-7 p-0"
              title="Move up"
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 15l7-7 7 7" />
              </svg>
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={onMoveDown}
              disabled={index === totalTests - 1}
              className="h-7 w-7 p-0"
              title="Move down"
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
              </svg>
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={onRemove}
              className="h-7 w-7 p-0 text-destructive hover:text-destructive"
              title="Remove"
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </Button>
          </div>
        </div>

        {/* Expanded Content */}
        <CollapsibleContent>
          <div className="border-t p-4 space-y-4">
            {/* Route Selection */}
            <div className="space-y-2">
              <Label>Route *</Label>
              <Select value={test.route} onValueChange={(value) => onUpdate({ route: value })}>
                <SelectTrigger>
                  <SelectValue placeholder="Select a route..." />
                </SelectTrigger>
                <SelectContent>
                  {availableRoutes.map((route) => (
                    <SelectItem key={route.path} value={route.path}>
                      <div className="flex flex-col">
                        <span>{route.name}</span>
                        <span className="text-xs text-muted-foreground">{route.path}</span>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {/* Dependencies */}
            {otherTests.length > 0 && (
              <div className="space-y-2">
                <Label>Dependencies (runs after these tests complete)</Label>
                <div className="space-y-1">
                  {otherTests.map((otherTest) => {
                    const otherRoute = availableRoutes.find((r) => r.path === otherTest.route)
                    const isDependent = (test.dependsOn || []).includes(otherTest.id)
                    return (
                      <div
                        key={otherTest.id}
                        className={`flex items-center gap-2 p-2 rounded border cursor-pointer transition-colors ${
                          isDependent ? 'bg-primary/10 border-primary' : 'hover:bg-muted'
                        }`}
                        onClick={() => handleDependencyToggle(otherTest.id)}
                      >
                        <input
                          type="checkbox"
                          checked={isDependent}
                          onChange={() => {}}
                          className="pointer-events-none"
                        />
                        <span className="text-sm">
                          {otherRoute?.name || otherTest.route || 'Unknown route'}
                        </span>
                      </div>
                    )
                  })}
                </div>
                <p className="text-xs text-muted-foreground">
                  Select tests that must complete before this test runs. Leave empty for no dependencies.
                </p>
              </div>
            )}
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  )
}
