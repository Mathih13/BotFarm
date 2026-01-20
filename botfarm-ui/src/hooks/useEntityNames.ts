import { useQuery } from '@tanstack/react-query';
import { useMemo, useCallback } from 'react';
import { entitiesApi } from '~/lib/api';
import {
  collectEntityIds,
  filterUncachedIds,
  hasUncachedIds,
  mergeIntoCache,
  resolveTaskName,
} from '~/lib/entityNames';
import type { EntityLookupRequest } from '~/lib/types';

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
