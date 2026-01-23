import { useQuery } from '@tanstack/react-query';
import { useMemo, useCallback } from 'react';
import { entitiesApi } from '~/lib/api';
import {
  collectEntityIds,
  filterUncachedIds,
  hasUncachedIds,
  mergeIntoCache,
  resolveTaskName,
  getCachedEntityName,
} from '~/lib/entityNames';
import { batchEntityNameLookup } from '~/lib/entityNameBatcher';
import type { EntityLookupRequest, EntityType } from '~/lib/types';

/**
 * Hook to resolve entity names for task displays
 *
 * Takes an array of task names, extracts entity IDs, fetches missing names
 * from the API, and returns a function to get display names for tasks.
 */
export function useEntityNames(taskNames: string[]) {
  // Memoize the ID collection to avoid recalculating on every render
  const { allIds, uncachedIds, shouldFetch } = useMemo(() => {
    const allIds = collectEntityIds(taskNames);
    const uncachedIds = filterUncachedIds(allIds);
    return {
      allIds,
      uncachedIds,
      shouldFetch: hasUncachedIds(uncachedIds),
    };
  }, [taskNames]);

  // Create a stable query key from the uncached IDs
  const queryKey = useMemo(() => [
    'entityNames',
    [...uncachedIds.creatureIds].sort().join(','),
    [...uncachedIds.questIds].sort().join(','),
    [...uncachedIds.objectIds].sort().join(','),
  ], [uncachedIds]);

  // Build the lookup request
  const lookupRequest = useMemo((): EntityLookupRequest => ({
    npcEntries: uncachedIds.creatureIds.length > 0 ? uncachedIds.creatureIds : undefined,
    questIds: uncachedIds.questIds.length > 0 ? uncachedIds.questIds : undefined,
    objectEntries: uncachedIds.objectIds.length > 0 ? uncachedIds.objectIds : undefined,
  }), [uncachedIds]);

  // Fetch uncached entity names
  const { isLoading, error } = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await entitiesApi.lookup(lookupRequest);
      mergeIntoCache(response);
      return response;
    },
    enabled: shouldFetch,
    staleTime: Infinity, // Entity names don't change
    gcTime: Infinity, // Keep in cache forever during session
  });

  /**
   * Resolve a task name by replacing IDs with cached names
   * Memoized to ensure stable reference
   */
  const getDisplayName = useCallback((taskName: string): string => {
    try {
      return resolveTaskName(taskName);
    } catch {
      // Fallback to original name if resolution fails
      return taskName;
    }
  }, []);

  return {
    getDisplayName,
    isLoading: isLoading && shouldFetch,
    error,
  };
}

/**
 * Hook to lookup a single entity name by type and entry ID
 *
 * Uses a batching mechanism to collect multiple lookups within a short time
 * window and send them as a single API request. This prevents MySQL concurrency
 * issues when many components mount simultaneously.
 */
export function useEntityName(type: EntityType, entry: number) {
  // Check cache first to avoid query if already cached
  const cachedName = entry > 0 ? getCachedEntityName(type, entry) : undefined;

  const { data, isLoading, isFetching, isError, error, fetchStatus } = useQuery({
    queryKey: ['entityName', type, entry],
    queryFn: () => batchEntityNameLookup(type, entry),
    staleTime: 5 * 60 * 1000, // 5 minutes - names don't change often
    gcTime: 30 * 60 * 1000, // Keep in cache for 30 minutes
    enabled: entry > 0 && cachedName === undefined, // Skip query if we have a cached value
  });

  // Return cached name if available, otherwise query result
  const entityName = cachedName ?? data;

  // Consider loading if:
  // - Cache miss and query is loading/fetching
  // - Cache miss and fetch hasn't completed yet (fetchStatus === 'fetching')
  // - Query is enabled but hasn't started yet (no cachedName, no data, not an error)
  const stillLoading = cachedName === undefined && (
    isLoading ||
    isFetching ||
    (entry > 0 && data === undefined && !isError)
  );

  return {
    entityName: entityName ?? null,
    isLoading: stillLoading,
    isError,
    error,
  };
}
