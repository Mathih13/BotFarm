import { useState, useEffect, useRef, useCallback } from 'react'
import { entitiesApi } from '~/lib/api'
import type { EntityType, EntityLookupResponse } from '~/lib/types'

// Global cache shared across all hook instances
const globalCache: {
  npcs: Record<number, string>
  quests: Record<number, string>
  items: Record<number, string>
  objects: Record<number, string>
} = {
  npcs: {},
  quests: {},
  items: {},
  objects: {},
}

// Pending lookups to batch requests
let pendingLookups: {
  npcEntries: Set<number>
  questIds: Set<number>
  itemEntries: Set<number>
  objectEntries: Set<number>
} = {
  npcEntries: new Set(),
  questIds: new Set(),
  itemEntries: new Set(),
  objectEntries: new Set(),
}

let batchTimeout: NodeJS.Timeout | null = null
let listeners: Set<() => void> = new Set()

function notifyListeners() {
  listeners.forEach(fn => fn())
}

async function executeBatchLookup() {
  const request: {
    npcEntries?: number[]
    questIds?: number[]
    itemEntries?: number[]
    objectEntries?: number[]
  } = {}

  if (pendingLookups.npcEntries.size > 0) {
    request.npcEntries = Array.from(pendingLookups.npcEntries)
  }
  if (pendingLookups.questIds.size > 0) {
    request.questIds = Array.from(pendingLookups.questIds)
  }
  if (pendingLookups.itemEntries.size > 0) {
    request.itemEntries = Array.from(pendingLookups.itemEntries)
  }
  if (pendingLookups.objectEntries.size > 0) {
    request.objectEntries = Array.from(pendingLookups.objectEntries)
  }

  // Clear pending
  pendingLookups = {
    npcEntries: new Set(),
    questIds: new Set(),
    itemEntries: new Set(),
    objectEntries: new Set(),
  }

  if (Object.keys(request).length === 0) return

  try {
    const response = await entitiesApi.lookup(request)

    // Merge into global cache
    Object.assign(globalCache.npcs, response.npcs)
    Object.assign(globalCache.quests, response.quests)
    Object.assign(globalCache.items, response.items)
    Object.assign(globalCache.objects, response.objects)

    notifyListeners()
  } catch (err) {
    console.error('Entity batch lookup failed:', err)
  }
}

function scheduleLookup(type: EntityType, id: number) {
  if (!id || id === 0) return

  // Check if already cached
  const cacheKey = type === 'npc' ? 'npcs' : type === 'quest' ? 'quests' : type === 'item' ? 'items' : 'objects'
  if (globalCache[cacheKey][id]) return

  // Add to pending
  switch (type) {
    case 'npc':
      pendingLookups.npcEntries.add(id)
      break
    case 'quest':
      pendingLookups.questIds.add(id)
      break
    case 'item':
      pendingLookups.itemEntries.add(id)
      break
    case 'object':
      pendingLookups.objectEntries.add(id)
      break
  }

  // Debounce batch execution
  if (batchTimeout) {
    clearTimeout(batchTimeout)
  }
  batchTimeout = setTimeout(executeBatchLookup, 100)
}

export function useEntityNames() {
  const [, forceUpdate] = useState({})

  useEffect(() => {
    const listener = () => forceUpdate({})
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  }, [])

  const getName = useCallback((type: EntityType, id: number): string | null => {
    if (!id || id === 0) return null

    const cacheKey = type === 'npc' ? 'npcs' : type === 'quest' ? 'quests' : type === 'item' ? 'items' : 'objects'
    const cached = globalCache[cacheKey][id]

    if (cached !== undefined) {
      return cached || null
    }

    // Schedule lookup
    scheduleLookup(type, id)
    return null
  }, [])

  const prefetch = useCallback((entities: { type: EntityType; id: number }[]) => {
    entities.forEach(({ type, id }) => {
      if (id && id !== 0) {
        scheduleLookup(type, id)
      }
    })
  }, [])

  return { getName, prefetch, cache: globalCache }
}

// Helper to format entity display
export function formatEntity(type: EntityType, id: number, name: string | null): string {
  const typeLabel = type === 'npc' ? 'NPC' : type === 'quest' ? 'Quest' : type === 'item' ? 'Item' : 'Object'
  if (name) {
    return `${name}`
  }
  return `${typeLabel} #${id}`
}
