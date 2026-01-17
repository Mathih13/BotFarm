import { useState, useEffect, useRef } from 'react'
import { Input } from '~/components/ui/input'
import { Button } from '~/components/ui/button'
import { entitiesApi } from '~/lib/api'
import type { EntityType } from '~/lib/types'

interface EntityArrayEditorProps {
  values: number[]
  onChange: (values: number[]) => void
  entityType: EntityType
  placeholder?: string
}

export function EntityArrayEditor({
  values,
  onChange,
  entityType,
  placeholder = 'Entry'
}: EntityArrayEditorProps) {
  const [entityNames, setEntityNames] = useState<Record<number, string>>({})
  const [loading, setLoading] = useState(false)
  const debounceRef = useRef<NodeJS.Timeout | null>(null)
  const lastLookedUpValues = useRef<string>('')

  useEffect(() => {
    // Clear any pending debounce
    if (debounceRef.current) {
      clearTimeout(debounceRef.current)
    }

    // Filter out 0 values for lookup
    const validValues = values.filter(v => v && v !== 0)

    if (validValues.length === 0) {
      setEntityNames({})
      lastLookedUpValues.current = ''
      return
    }

    // Create a key to compare if values have changed
    const valuesKey = validValues.sort().join(',')
    if (valuesKey === lastLookedUpValues.current) {
      return
    }

    // Debounce the API call
    debounceRef.current = setTimeout(async () => {
      setLoading(true)

      try {
        const request = buildLookupRequest(entityType, validValues)
        const response = await entitiesApi.lookup(request)
        const names = getNamesFromResponse(entityType, response)
        setEntityNames(names)
        lastLookedUpValues.current = valuesKey
      } catch (err) {
        console.error('Entity lookup failed:', err)
      } finally {
        setLoading(false)
      }
    }, 300)

    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current)
      }
    }
  }, [values, entityType])

  const addValue = () => {
    onChange([...values, 0])
  }

  const removeValue = (index: number) => {
    onChange(values.filter((_, i) => i !== index))
  }

  const updateValue = (index: number, value: number) => {
    const updated = values.map((v, i) => (i === index ? value : v))
    onChange(updated)
  }

  return (
    <div className="space-y-2">
      {values.length > 0 ? (
        <div className="space-y-2">
          {values.map((value, index) => (
            <div key={index} className="flex items-start gap-2">
              <div className="flex-1 space-y-1">
                <div className="flex items-center gap-1">
                  <Input
                    type="number"
                    value={value || ''}
                    onChange={(e) => updateValue(index, parseInt(e.target.value) || 0)}
                    placeholder={placeholder}
                    className="w-32"
                  />
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:text-destructive h-8 px-2"
                    onClick={() => removeValue(index)}
                  >
                    x
                  </Button>
                </div>
                {value && value !== 0 && (
                  <div className="text-xs text-muted-foreground truncate pl-1">
                    {loading ? (
                      'Loading...'
                    ) : entityNames[value] ? (
                      entityNames[value]
                    ) : (
                      <span className="text-destructive">Not found</span>
                    )}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No values added</p>
      )}
      <Button variant="outline" size="sm" onClick={addValue}>
        + Add {placeholder}
      </Button>
    </div>
  )
}

function buildLookupRequest(type: EntityType, ids: number[]) {
  switch (type) {
    case 'npc':
      return { npcEntries: ids }
    case 'quest':
      return { questIds: ids }
    case 'item':
      return { itemEntries: ids }
    case 'object':
      return { objectEntries: ids }
  }
}

function getNamesFromResponse(
  type: EntityType,
  response: { npcs: Record<number, string>; quests: Record<number, string>; items: Record<number, string>; objects: Record<number, string> }
): Record<number, string> {
  switch (type) {
    case 'npc':
      return response.npcs
    case 'quest':
      return response.quests
    case 'item':
      return response.items
    case 'object':
      return response.objects
  }
}
