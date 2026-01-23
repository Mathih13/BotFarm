import type { EntityLookupResponse } from './types';

/**
 * Entity reference extracted from a task name string
 */
export interface EntityReference {
  type: 'creature' | 'quest' | 'object';
  id: number;
}

/**
 * Parsed task with entity references extracted
 */
export interface ParsedTask {
  original: string;
  entityRefs: EntityReference[];
}

// In-memory cache for resolved entity names (shared across components)
const entityCache: EntityLookupResponse = {
  npcs: {},
  quests: {},
  items: {},
  objects: {},
};

/**
 * Merge new entity lookup response into the cache
 */
export function mergeIntoCache(response: EntityLookupResponse): void {
  Object.assign(entityCache.npcs, response.npcs);
  Object.assign(entityCache.quests, response.quests);
  Object.assign(entityCache.items, response.items);
  Object.assign(entityCache.objects, response.objects);
}

/**
 * Get a creature/NPC name from the cache
 */
export function getCachedCreatureName(id: number): string | undefined {
  return entityCache.npcs[id];
}

/**
 * Get a quest name from the cache
 */
export function getCachedQuestName(id: number): string | undefined {
  return entityCache.quests[id];
}

/**
 * Get an object name from the cache
 */
export function getCachedObjectName(id: number): string | undefined {
  return entityCache.objects[id];
}

/**
 * Get an item name from the cache
 */
export function getCachedItemName(id: number): string | undefined {
  return entityCache.items[id];
}

/**
 * Get a cached entity name by type
 */
export function getCachedEntityName(
  type: 'npc' | 'quest' | 'item' | 'object',
  id: number
): string | undefined {
  switch (type) {
    case 'npc':
      return entityCache.npcs[id];
    case 'quest':
      return entityCache.quests[id];
    case 'item':
      return entityCache.items[id];
    case 'object':
      return entityCache.objects[id];
  }
}

/**
 * Set a cached entity name by type
 */
export function setCachedEntityName(
  type: 'npc' | 'quest' | 'item' | 'object',
  id: number,
  name: string
): void {
  switch (type) {
    case 'npc':
      entityCache.npcs[id] = name;
      break;
    case 'quest':
      entityCache.quests[id] = name;
      break;
    case 'item':
      entityCache.items[id] = name;
      break;
    case 'object':
      entityCache.objects[id] = name;
      break;
  }
}

/**
 * Extract entity IDs from a task name string
 *
 * Patterns recognized:
 *   kill[{count}/{total}x{creatureId},...]  → extract creatureIds
 *   obj[{count}/{total}x{objectId},...]     → extract objectIds
 *   AcceptQuest({questId})                  → extract questId
 *   TurnInQuest({questId})                  → extract questId
 *   AcceptClassQuest({questId})             → extract questId
 *   TurnInClassQuest({questId})             → extract questId
 *   AcceptQuestFromNPC({npcEntry})          → extract npcEntry
 *   TurnInQuestAtNPC({npcEntry})            → extract npcEntry
 *   MoveToNPC({npcEntry})                   → extract npcEntry
 */
export function parseTaskEntities(taskName: string): ParsedTask {
  const entityRefs: EntityReference[] = [];

  // Pattern: kill[count/totalxcreatureId, ...] - e.g., kill[0/8x6,0/5x38]
  const killPattern = /kill\[([^\]]+)\]/g;
  let match = killPattern.exec(taskName);
  while (match) {
    const killEntries = match[1].split(',');
    for (const entry of killEntries) {
      // Each entry is like "0/8x6" where 6 is the creature entry
      const entryMatch = /(\d+)\/(\d+)x(\d+)/.exec(entry.trim());
      if (entryMatch) {
        entityRefs.push({ type: 'creature', id: parseInt(entryMatch[3], 10) });
      }
    }
    match = killPattern.exec(taskName);
  }

  // Pattern: obj[count/totalxobjectId, ...] - e.g., obj[1/3x123]
  const objPattern = /obj\[([^\]]+)\]/g;
  match = objPattern.exec(taskName);
  while (match) {
    const objEntries = match[1].split(',');
    for (const entry of objEntries) {
      const entryMatch = /(\d+)\/(\d+)x(\d+)/.exec(entry.trim());
      if (entryMatch) {
        entityRefs.push({ type: 'object', id: parseInt(entryMatch[3], 10) });
      }
    }
    match = objPattern.exec(taskName);
  }

  // Pattern: AcceptQuest(questId) or TurnInQuest(questId)
  const questPattern = /(?:AcceptQuest|TurnInQuest|AcceptClassQuest|TurnInClassQuest)\((\d+)\)/g;
  match = questPattern.exec(taskName);
  while (match) {
    entityRefs.push({ type: 'quest', id: parseInt(match[1], 10) });
    match = questPattern.exec(taskName);
  }

  // Pattern: AcceptQuestFromNPC(npcEntry), TurnInQuestAtNPC(npcEntry), MoveToNPC(npcEntry)
  const npcPattern = /(?:AcceptQuestFromNPC|TurnInQuestAtNPC|MoveToNPC)\((\d+)\)/g;
  match = npcPattern.exec(taskName);
  while (match) {
    entityRefs.push({ type: 'creature', id: parseInt(match[1], 10) });
    match = npcPattern.exec(taskName);
  }

  return { original: taskName, entityRefs };
}

/**
 * Collect all entity IDs from multiple task names
 */
export function collectEntityIds(taskNames: string[]): {
  creatureIds: number[];
  questIds: number[];
  objectIds: number[];
} {
  const creatureIds = new Set<number>();
  const questIds = new Set<number>();
  const objectIds = new Set<number>();

  for (const taskName of taskNames) {
    const parsed = parseTaskEntities(taskName);
    for (const ref of parsed.entityRefs) {
      switch (ref.type) {
        case 'creature':
          creatureIds.add(ref.id);
          break;
        case 'quest':
          questIds.add(ref.id);
          break;
        case 'object':
          objectIds.add(ref.id);
          break;
      }
    }
  }

  return {
    creatureIds: Array.from(creatureIds),
    questIds: Array.from(questIds),
    objectIds: Array.from(objectIds),
  };
}

/**
 * Filter out IDs that are already in the cache
 */
export function filterUncachedIds(ids: {
  creatureIds: number[];
  questIds: number[];
  objectIds: number[];
}): {
  creatureIds: number[];
  questIds: number[];
  objectIds: number[];
} {
  return {
    creatureIds: ids.creatureIds.filter((id) => entityCache.npcs[id] === undefined),
    questIds: ids.questIds.filter((id) => entityCache.quests[id] === undefined),
    objectIds: ids.objectIds.filter((id) => entityCache.objects[id] === undefined),
  };
}

/**
 * Check if there are any uncached IDs
 */
export function hasUncachedIds(uncached: {
  creatureIds: number[];
  questIds: number[];
  objectIds: number[];
}): boolean {
  return (
    uncached.creatureIds.length > 0 ||
    uncached.questIds.length > 0 ||
    uncached.objectIds.length > 0
  );
}

/**
 * Replace entity IDs with their names in a task name string
 */
export function resolveTaskName(taskName: string): string {
  // Return as-is if empty or no recognizable patterns
  if (!taskName) return taskName;

  let resolved = taskName;

  // Replace kill[count/totalxcreatureId] with kill[count/total creatureName]
  resolved = resolved.replace(/kill\[([^\]]+)\]/g, (match, content) => {
    // Safety: if content is empty, return original match
    if (!content || content.trim() === '') return match;

    const parts = content.split(',').map((entry: string) => {
      const entryMatch = /(\d+)\/(\d+)x(\d+)/.exec(entry.trim());
      if (entryMatch) {
        const count = entryMatch[1];
        const total = entryMatch[2];
        const creatureId = parseInt(entryMatch[3], 10);
        const name = getCachedCreatureName(creatureId);
        if (name) {
          return `${count}/${total} ${name}`;
        }
      }
      return entry.trim();
    });
    return `kill[${parts.join(', ')}]`;
  });

  // Replace obj[count/totalxobjectId] with obj[count/total objectName]
  resolved = resolved.replace(/obj\[([^\]]+)\]/g, (match, content) => {
    // Safety: if content is empty, return original match
    if (!content || content.trim() === '') return match;

    const parts = content.split(',').map((entry: string) => {
      const entryMatch = /(\d+)\/(\d+)x(\d+)/.exec(entry.trim());
      if (entryMatch) {
        const count = entryMatch[1];
        const total = entryMatch[2];
        const objectId = parseInt(entryMatch[3], 10);
        const name = getCachedObjectName(objectId);
        if (name) {
          return `${count}/${total} ${name}`;
        }
      }
      return entry.trim();
    });
    return `obj[${parts.join(', ')}]`;
  });

  // Replace AcceptQuest(questId) with AcceptQuest(questName)
  resolved = resolved.replace(
    /(AcceptQuest|TurnInQuest|AcceptClassQuest|TurnInClassQuest)\((\d+)\)/g,
    (_, prefix, questId) => {
      const name = getCachedQuestName(parseInt(questId, 10));
      return name ? `${prefix}(${name})` : `${prefix}(${questId})`;
    }
  );

  // Replace NPC-related tasks with NPC names
  resolved = resolved.replace(
    /(AcceptQuestFromNPC|TurnInQuestAtNPC|MoveToNPC)\((\d+)\)/g,
    (_, prefix, npcId) => {
      const name = getCachedCreatureName(parseInt(npcId, 10));
      return name ? `${prefix}(${name})` : `${prefix}(${npcId})`;
    }
  );

  return resolved;
}
