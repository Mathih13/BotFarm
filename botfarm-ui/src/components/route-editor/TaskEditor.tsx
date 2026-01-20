import { useEffect } from 'react'
import { GripVertical } from 'lucide-react'
import type { TaskFormData, EntityType, TaskType } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Card, CardContent } from '~/components/ui/card'
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
import { getTaskForm } from './task-forms'
import { useEntityNames, formatEntity } from './shared/useEntityNames'
import { TASK_TYPES } from './TaskListSection'

interface TaskEditorProps {
  task: TaskFormData
  index: number
  totalTasks: number
  isExpanded: boolean
  onToggleExpanded: () => void
  onUpdate: (updates: Partial<TaskFormData>) => void
  onRemove: () => void
  onDuplicate: () => void
  onTypeChange: (newType: TaskType) => void
  dragHandleProps?: Record<string, unknown>
}

// Extract entity IDs from task for prefetching
function getTaskEntityIds(task: TaskFormData): { type: EntityType; id: number }[] {
  const params = task.parameters
  const entities: { type: EntityType; id: number }[] = []

  const npcEntry = params.npcEntry as number | undefined
  const questId = params.questId as number | undefined
  const objectEntry = params.objectEntry as number | undefined
  const itemEntry = params.itemEntry as number | undefined
  const targetEntries = params.targetEntries as number[] | undefined

  if (npcEntry) entities.push({ type: 'npc', id: npcEntry })
  if (questId) entities.push({ type: 'quest', id: questId })
  if (objectEntry) entities.push({ type: 'object', id: objectEntry })
  if (itemEntry) entities.push({ type: 'item', id: itemEntry })
  if (targetEntries) {
    targetEntries.forEach(id => entities.push({ type: 'npc', id }))
  }

  return entities
}

function getTaskSummaryWithNames(
  task: TaskFormData,
  getName: (type: EntityType, id: number) => string | null
): string {
  const params = task.parameters

  const npcName = (id: number) => formatEntity('npc', id, getName('npc', id))
  const questName = (id: number) => formatEntity('quest', id, getName('quest', id))
  const objectName = (id: number) => formatEntity('object', id, getName('object', id))
  const itemName = (id: number) => formatEntity('item', id, getName('item', id))

  switch (task.type) {
    case 'Wait':
      return `${params.seconds}s`
    case 'LogMessage':
      return params.message ? `"${String(params.message).slice(0, 30)}..."` : '(empty)'
    case 'MoveToLocation':
      const desc = params.description as string
      return desc || `(${params.x}, ${params.y}, ${params.z})`
    case 'MoveToNPC':
    case 'TalkToNPC':
      return params.npcEntry ? npcName(params.npcEntry as number) : '(no NPC)'
    case 'AcceptQuest':
      const aqid = params.questId as number
      const anid = params.npcEntry as number
      if (aqid && anid) {
        return `${questName(aqid)} from ${npcName(anid)}`
      } else if (aqid) {
        return questName(aqid)
      }
      return '(not configured)'
    case 'TurnInQuest':
      const tqid = params.questId as number
      const tnid = params.npcEntry as number
      if (tqid && tnid) {
        return `${questName(tqid)} to ${npcName(tnid)}`
      } else if (tqid) {
        return questName(tqid)
      }
      return '(not configured)'
    case 'KillMobs':
      const entries = params.targetEntries as number[] | undefined
      if (!entries?.length) return '(no targets)'
      if (entries.length === 1) {
        return npcName(entries[0])
      }
      return `${npcName(entries[0])} +${entries.length - 1} more`
    case 'UseObject':
      return params.objectEntry ? objectName(params.objectEntry as number) : '(no object)'
    case 'Adventure':
      return 'Combat + Objects'
    case 'LearnSpells':
      const spells = params.spellIds as number[] | undefined
      return spells?.length ? `${spells.length} spell(s)` : '(no spells)'
    case 'AssertQuestInLog':
    case 'AssertQuestNotInLog':
      return params.questId ? questName(params.questId as number) : '(no quest)'
    case 'AssertHasItem':
      const iid = params.itemEntry as number
      return iid ? `${itemName(iid)} x${params.minCount}` : '(no item)'
    case 'AssertLevel':
      return `Level >= ${params.minLevel}`
    default:
      return ''
  }
}

function getTaskColor(type: string): string {
  if (type.startsWith('Assert')) return 'bg-purple-100 text-purple-700 border-purple-200'
  if (type.includes('Move')) return 'bg-blue-100 text-blue-700 border-blue-200'
  if (type.includes('Quest')) return 'bg-yellow-100 text-yellow-700 border-yellow-200'
  if (type.includes('Kill') || type === 'Adventure') return 'bg-red-100 text-red-700 border-red-200'
  if (type.includes('NPC') || type.includes('Object') || type.includes('Spell'))
    return 'bg-green-100 text-green-700 border-green-200'
  return 'bg-gray-100 text-gray-700 border-gray-200'
}

export function TaskEditor({
  task,
  index,
  totalTasks,
  isExpanded,
  onToggleExpanded,
  onUpdate,
  onRemove,
  onDuplicate,
  onTypeChange,
  dragHandleProps,
}: TaskEditorProps) {
  const TaskForm = getTaskForm(task.type)
  const { getName, prefetch } = useEntityNames()

  // Prefetch entity names for this task
  useEffect(() => {
    const entities = getTaskEntityIds(task)
    if (entities.length > 0) {
      prefetch(entities)
    }
  }, [task, prefetch])

  const updateParameters = (updates: Record<string, unknown>) => {
    onUpdate({ parameters: { ...task.parameters, ...updates } })
  }

  const summary = getTaskSummaryWithNames(task, getName)

  return (
    <Collapsible open={isExpanded} onOpenChange={onToggleExpanded}>
      <Card className="border">
        <div className="flex items-center gap-2 p-3">
          {/* Drag Handle */}
          <div
            {...dragHandleProps}
            className="cursor-grab active:cursor-grabbing text-muted-foreground hover:text-foreground"
          >
            <GripVertical className="h-4 w-4" />
          </div>
          <span className="text-sm font-mono text-muted-foreground w-6">{index + 1}.</span>
          {/* Task Type Selector */}
          <div onClick={(e) => e.stopPropagation()}>
            <Select
              value={task.type}
              onValueChange={(value) => onTypeChange(value as TaskType)}
            >
              <SelectTrigger className={`h-7 text-xs px-2 ${getTaskColor(task.type)}`}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TASK_TYPES.map((t) => (
                  <SelectItem key={t.type} value={t.type}>
                    <span className="text-muted-foreground text-xs mr-2">[{t.category}]</span>
                    {t.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <CollapsibleTrigger asChild>
            <div className="flex items-center gap-2 flex-1 cursor-pointer hover:bg-muted/50 rounded px-2 py-1">
              <span className="text-sm text-muted-foreground flex-1 truncate">
                {summary}
              </span>
              <span className="text-xs text-muted-foreground">
                {isExpanded ? '[-]' : '[+]'}
              </span>
            </div>
          </CollapsibleTrigger>
        </div>

        <CollapsibleContent>
          <CardContent className="pt-0 pb-4 border-t">
            {/* Task Actions */}
            <div className="flex gap-1 mb-4 pt-3">
              <Button variant="outline" size="sm" onClick={onDuplicate}>
                Duplicate
              </Button>
              <div className="flex-1" />
              <Button
                variant="ghost"
                size="sm"
                className="text-destructive hover:text-destructive"
                onClick={onRemove}
              >
                Remove
              </Button>
            </div>

            {/* Task Form */}
            <TaskForm parameters={task.parameters} onChange={updateParameters} />
          </CardContent>
        </CollapsibleContent>
      </Card>
    </Collapsible>
  )
}
