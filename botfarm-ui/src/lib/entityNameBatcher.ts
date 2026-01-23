import { entitiesApi } from '~/lib/api';
import { mergeIntoCache, getCachedEntityName } from '~/lib/entityNames';
import type { EntityType, EntityLookupRequest, EntityLookupResponse } from '~/lib/types';

interface PendingRequest {
  type: EntityType;
  entry: number;
  resolve: (name: string | null) => void;
  reject: (error: Error) => void;
}

// Batch window in milliseconds - collect requests within this window
const BATCH_WINDOW_MS = 16; // ~1 frame at 60fps

// Pending requests waiting to be batched
let pendingRequests: PendingRequest[] = [];
let batchTimeout: ReturnType<typeof setTimeout> | null = null;
let batchPromise: Promise<void> | null = null;

/**
 * Process all pending requests in a single batch
 */
async function processBatch(): Promise<void> {
  const requests = pendingRequests;
  pendingRequests = [];
  batchTimeout = null;
  batchPromise = null;

  if (requests.length === 0) return;

  // Group requests by type
  const npcEntries: number[] = [];
  const questIds: number[] = [];
  const itemEntries: number[] = [];
  const objectEntries: number[] = [];

  for (const req of requests) {
    // Check cache first - might have been populated by another batch
    const cached = getCachedEntityName(req.type, req.entry);
    if (cached !== undefined) {
      req.resolve(cached);
      continue;
    }

    switch (req.type) {
      case 'npc':
        if (!npcEntries.includes(req.entry)) npcEntries.push(req.entry);
        break;
      case 'quest':
        if (!questIds.includes(req.entry)) questIds.push(req.entry);
        break;
      case 'item':
        if (!itemEntries.includes(req.entry)) itemEntries.push(req.entry);
        break;
      case 'object':
        if (!objectEntries.includes(req.entry)) objectEntries.push(req.entry);
        break;
    }
  }

  // Build the lookup request
  const lookupRequest: EntityLookupRequest = {};
  if (npcEntries.length > 0) lookupRequest.npcEntries = npcEntries;
  if (questIds.length > 0) lookupRequest.questIds = questIds;
  if (itemEntries.length > 0) lookupRequest.itemEntries = itemEntries;
  if (objectEntries.length > 0) lookupRequest.objectEntries = objectEntries;

  // Skip API call if all requests were cache hits
  if (Object.keys(lookupRequest).length === 0) {
    return;
  }

  try {
    const response = await entitiesApi.lookup(lookupRequest);

    // Merge into shared cache
    mergeIntoCache(response);

    // Resolve all pending requests
    for (const req of requests) {
      // Skip already resolved (cache hits)
      const cached = getCachedEntityName(req.type, req.entry);
      if (cached !== undefined) {
        req.resolve(cached);
      } else {
        // Not found in response
        req.resolve(null);
      }
    }
  } catch (error) {
    // Reject all pending requests
    for (const req of requests) {
      req.reject(error instanceof Error ? error : new Error(String(error)));
    }
  }
}

/**
 * Schedule a batch lookup for an entity name.
 * Requests are collected and sent in a single batch after a short delay.
 */
export function batchEntityNameLookup(type: EntityType, entry: number): Promise<string | null> {
  // Check cache synchronously first
  const cached = getCachedEntityName(type, entry);
  if (cached !== undefined) {
    return Promise.resolve(cached);
  }

  return new Promise((resolve, reject) => {
    pendingRequests.push({ type, entry, resolve, reject });

    // Schedule batch processing if not already scheduled
    if (batchTimeout === null) {
      batchTimeout = setTimeout(() => {
        batchPromise = processBatch();
      }, BATCH_WINDOW_MS);
    }
  });
}

/**
 * Get the current batch promise (for testing/debugging)
 */
export function getCurrentBatchPromise(): Promise<void> | null {
  return batchPromise;
}
