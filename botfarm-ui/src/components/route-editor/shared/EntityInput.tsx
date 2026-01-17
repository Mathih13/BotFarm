import { useState, useEffect, useRef, useCallback } from 'react'
import { Input } from '~/components/ui/input'
import { Popover, PopoverContent, PopoverAnchor } from '~/components/ui/popover'
import { entitiesApi } from '~/lib/api'
import type { EntityType, EntitySearchResult } from '~/lib/types'
import { Search, Loader2 } from 'lucide-react'
import { cn } from '~/lib/utils'

interface EntityInputProps {
  type: EntityType
  value: number
  onChange: (value: number) => void
  className?: string
  id?: string
}

// Cache for search results (key: `${type}:${query}`)
const searchCache = new Map<string, EntitySearchResult[]>()

export function EntityInput({ type, value, onChange, className, id }: EntityInputProps) {
  const [inputValue, setInputValue] = useState(value ? String(value) : '')
  const [entityName, setEntityName] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [searchResults, setSearchResults] = useState<EntitySearchResult[]>([])
  const [isOpen, setIsOpen] = useState(false)
  const [selectedIndex, setSelectedIndex] = useState(0)

  const debounceRef = useRef<NodeJS.Timeout | null>(null)
  const lastLookedUpValue = useRef<number | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  // Sync input value when prop changes (e.g., from external source)
  useEffect(() => {
    if (value && value !== parseInt(inputValue)) {
      setInputValue(String(value))
    } else if (!value && inputValue !== '') {
      // Only clear if value is truly 0/undefined
      if (value === 0 || value === undefined) {
        setInputValue('')
      }
    }
  }, [value])

  // Lookup entity name by ID
  const lookupById = useCallback(async (entryId: number) => {
    if (!entryId || entryId === 0) {
      setEntityName(null)
      setError(null)
      lastLookedUpValue.current = null
      return
    }

    if (entryId === lastLookedUpValue.current) {
      return
    }

    setLoading(true)
    setError(null)

    try {
      const request = buildLookupRequest(type, [entryId])
      const response = await entitiesApi.lookup(request)
      const name = getNameFromResponse(type, entryId, response)

      if (name) {
        setEntityName(name)
        setError(null)
      } else {
        setEntityName(null)
        setError('Not found')
      }
      lastLookedUpValue.current = entryId
    } catch (err) {
      setEntityName(null)
      setError('Lookup failed')
      console.error('Entity lookup failed:', err)
    } finally {
      setLoading(false)
    }
  }, [type])

  // Lookup current value on mount and when value changes
  useEffect(() => {
    if (value && value > 0) {
      lookupById(value)
    }
  }, [value, lookupById])

  // Search by name
  const searchByName = useCallback(async (query: string) => {
    const cacheKey = `${type}:${query.toLowerCase()}`

    // Check cache first
    if (searchCache.has(cacheKey)) {
      const cached = searchCache.get(cacheKey)!
      setSearchResults(cached)
      setIsOpen(cached.length > 0)
      setSelectedIndex(0)
      return
    }

    setSearching(true)

    try {
      const response = await entitiesApi.search(type, query, 20)
      searchCache.set(cacheKey, response.results)
      setSearchResults(response.results)
      setIsOpen(response.results.length > 0)
      setSelectedIndex(0)
    } catch (err) {
      console.error('Entity search failed:', err)
      setSearchResults([])
      setIsOpen(false)
    } finally {
      setSearching(false)
    }
  }, [type])

  // Handle input change
  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = e.target.value
    setInputValue(newValue)

    // Clear any pending debounce
    if (debounceRef.current) {
      clearTimeout(debounceRef.current)
    }

    // If empty, clear value
    if (!newValue.trim()) {
      onChange(0)
      setEntityName(null)
      setError(null)
      setSearchResults([])
      setIsOpen(false)
      return
    }

    // Check if it's a number
    const numValue = parseInt(newValue)
    if (!isNaN(numValue) && numValue > 0 && String(numValue) === newValue.trim()) {
      // Direct ID entry - update value and lookup name
      onChange(numValue)
      setSearchResults([])
      setIsOpen(false)

      debounceRef.current = setTimeout(() => {
        lookupById(numValue)
      }, 300)
    } else if (newValue.length >= 2) {
      // Text search - debounce the search
      debounceRef.current = setTimeout(() => {
        searchByName(newValue)
      }, 300)
    } else {
      setSearchResults([])
      setIsOpen(false)
    }
  }

  // Handle selecting a search result
  const handleSelectResult = (result: EntitySearchResult) => {
    setInputValue(String(result.entry))
    onChange(result.entry)
    setEntityName(result.name)
    setError(null)
    lastLookedUpValue.current = result.entry
    setIsOpen(false)
    setSearchResults([])
    inputRef.current?.focus()
  }

  // Handle keyboard navigation
  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen || searchResults.length === 0) return

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        setSelectedIndex(i => Math.min(i + 1, searchResults.length - 1))
        break
      case 'ArrowUp':
        e.preventDefault()
        setSelectedIndex(i => Math.max(i - 1, 0))
        break
      case 'Enter':
        e.preventDefault()
        if (searchResults[selectedIndex]) {
          handleSelectResult(searchResults[selectedIndex])
        }
        break
      case 'Escape':
        e.preventDefault()
        setIsOpen(false)
        break
    }
  }

  // Handle blur - close dropdown after a small delay (to allow click on results)
  const handleBlur = () => {
    setTimeout(() => {
      setIsOpen(false)
    }, 150)
  }

  // Get label for entity type
  const getTypeLabel = () => {
    switch (type) {
      case 'npc': return 'NPC'
      case 'quest': return 'Quest'
      case 'item': return 'Item'
      case 'object': return 'Object'
    }
  }

  return (
    <div className="space-y-1">
      <Popover open={isOpen} onOpenChange={setIsOpen}>
        <PopoverAnchor asChild>
          <div className="relative">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
            {searching && (
              <Loader2 className="absolute right-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground animate-spin" />
            )}
            <Input
              ref={inputRef}
              id={id}
              type="text"
              value={inputValue}
              onChange={handleInputChange}
              onKeyDown={handleKeyDown}
              onBlur={handleBlur}
              onFocus={() => {
                // Re-open if we have results
                if (searchResults.length > 0) {
                  setIsOpen(true)
                }
              }}
              className={cn("pl-8", searching && "pr-8", className)}
              placeholder={`Search ${getTypeLabel()} by name or ID...`}
            />
          </div>
        </PopoverAnchor>
        <PopoverContent
          className="p-0 w-[var(--radix-popover-trigger-width)]"
          align="start"
          onOpenAutoFocus={(e) => e.preventDefault()}
        >
          <div className="max-h-60 overflow-y-auto">
            {searchResults.map((result, index) => (
              <button
                key={result.entry}
                type="button"
                className={cn(
                  "w-full px-3 py-2 text-left text-sm hover:bg-accent cursor-pointer flex justify-between items-center",
                  index === selectedIndex && "bg-accent"
                )}
                onMouseDown={(e) => {
                  e.preventDefault()
                  handleSelectResult(result)
                }}
                onMouseEnter={() => setSelectedIndex(index)}
              >
                <span className="truncate">{result.name}</span>
                <span className="text-muted-foreground ml-2 shrink-0">#{result.entry}</span>
              </button>
            ))}
          </div>
        </PopoverContent>
      </Popover>
      {loading && (
        <p className="text-xs text-muted-foreground">Loading...</p>
      )}
      {!loading && entityName && (
        <p className="text-xs text-muted-foreground truncate" title={entityName}>
          {entityName}
        </p>
      )}
      {!loading && error && (
        <p className="text-xs text-destructive">{error}</p>
      )}
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

function getNameFromResponse(
  type: EntityType,
  id: number,
  response: { npcs: Record<number, string>; quests: Record<number, string>; items: Record<number, string>; objects: Record<number, string> }
): string | null {
  switch (type) {
    case 'npc':
      return response.npcs[id] || null
    case 'quest':
      return response.quests[id] || null
    case 'item':
      return response.items[id] || null
    case 'object':
      return response.objects[id] || null
  }
}
