import { useState } from 'react'
import { Plus } from 'lucide-react'
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import type { TaskFormData, TaskType } from '~/lib/types'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Button } from '~/components/ui/button'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '~/components/ui/popover'
import { SortableTaskEditor } from './SortableTaskEditor'

interface TaskListSectionProps {
  tasks: TaskFormData[]
  onChange: (tasks: TaskFormData[]) => void
}

export const TASK_TYPES: { type: TaskType; label: string; category: string }[] = [
  { type: 'Wait', label: 'Wait', category: 'Utility' },
  { type: 'LogMessage', label: 'Log Message', category: 'Utility' },
  { type: 'MoveToLocation', label: 'Move to Location', category: 'Movement' },
  { type: 'MoveToNPC', label: 'Move to NPC', category: 'Movement' },
  { type: 'TalkToNPC', label: 'Talk to NPC', category: 'Interaction' },
  { type: 'AcceptQuest', label: 'Accept Quest', category: 'Quest' },
  { type: 'TurnInQuest', label: 'Turn In Quest', category: 'Quest' },
  { type: 'KillMobs', label: 'Kill Mobs', category: 'Combat' },
  { type: 'UseObject', label: 'Use Object', category: 'Interaction' },
  { type: 'Adventure', label: 'Adventure', category: 'Combat' },
  { type: 'LearnSpells', label: 'Learn Spells', category: 'Interaction' },
  { type: 'AssertQuestInLog', label: 'Assert Quest In Log', category: 'Assert' },
  { type: 'AssertQuestNotInLog', label: 'Assert Quest Not In Log', category: 'Assert' },
  { type: 'AssertHasItem', label: 'Assert Has Item', category: 'Assert' },
  { type: 'AssertLevel', label: 'Assert Level', category: 'Assert' },
]

function generateTaskId(): string {
  return `task-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
}

export function getDefaultParameters(type: TaskType): Record<string, unknown> {
  switch (type) {
    case 'Wait':
      return { seconds: 1 }
    case 'LogMessage':
      return { message: '', level: 'Info' }
    case 'MoveToLocation':
      return { x: 0, y: 0, z: 0, mapId: 0, threshold: 3.0 }
    case 'MoveToNPC':
      return { npcEntry: 0, threshold: 4.0 }
    case 'TalkToNPC':
      return { npcEntry: 0 }
    case 'AcceptQuest':
      return { npcEntry: 0, questId: 0 }
    case 'TurnInQuest':
      return { npcEntry: 0, questId: 0, rewardChoice: 0 }
    case 'KillMobs':
      return { targetEntries: [], killCount: 1, searchRadius: 50 }
    case 'UseObject':
      return { objectEntry: 0, useCount: 1, searchRadius: 50 }
    case 'Adventure':
      return { targetEntries: [], objectEntries: [], searchRadius: 100, defendSelf: true }
    case 'LearnSpells':
      return { npcEntry: 0, spellIds: [] }
    case 'AssertQuestInLog':
    case 'AssertQuestNotInLog':
      return { questId: 0, message: '' }
    case 'AssertHasItem':
      return { itemEntry: 0, minCount: 1, message: '' }
    case 'AssertLevel':
      return { minLevel: 1, message: '' }
    default:
      return {}
  }
}

// Group task types by category for the popover
const groupedTaskTypes = TASK_TYPES.reduce(
  (acc, task) => {
    if (!acc[task.category]) {
      acc[task.category] = []
    }
    acc[task.category].push(task)
    return acc
  },
  {} as Record<string, typeof TASK_TYPES>
)

export function TaskListSection({ tasks, onChange }: TaskListSectionProps) {
  const [expandedTasks, setExpandedTasks] = useState<Set<string>>(new Set())
  const [isAddPopoverOpen, setIsAddPopoverOpen] = useState(false)

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  )

  const addTask = (type: TaskType) => {
    const newTask: TaskFormData = {
      id: generateTaskId(),
      type,
      parameters: getDefaultParameters(type),
    }
    onChange([...tasks, newTask])
    setExpandedTasks((prev) => new Set([...prev, newTask.id]))
    setIsAddPopoverOpen(false)
  }

  const removeTask = (id: string) => {
    onChange(tasks.filter((t) => t.id !== id))
    setExpandedTasks((prev) => {
      const next = new Set(prev)
      next.delete(id)
      return next
    })
  }

  const updateTask = (id: string, updates: Partial<TaskFormData>) => {
    onChange(tasks.map((t) => (t.id === id ? { ...t, ...updates } : t)))
  }

  const updateTaskType = (id: string, newType: TaskType) => {
    onChange(
      tasks.map((t) =>
        t.id === id
          ? { ...t, type: newType, parameters: getDefaultParameters(newType) }
          : t
      )
    )
  }

  const toggleExpanded = (id: string) => {
    setExpandedTasks((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const duplicateTask = (task: TaskFormData) => {
    const newTask: TaskFormData = {
      id: generateTaskId(),
      type: task.type,
      parameters: JSON.parse(JSON.stringify(task.parameters)),
    }
    const index = tasks.findIndex((t) => t.id === task.id)
    const newTasks = [...tasks]
    newTasks.splice(index + 1, 0, newTask)
    onChange(newTasks)
    setExpandedTasks((prev) => new Set([...prev, newTask.id]))
  }

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event

    if (over && active.id !== over.id) {
      const oldIndex = tasks.findIndex((t) => t.id === active.id)
      const newIndex = tasks.findIndex((t) => t.id === over.id)
      onChange(arrayMove(tasks, oldIndex, newIndex))
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tasks ({tasks.length})</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Task List */}
        {tasks.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-8">
            No tasks yet. Add a task to get started.
          </p>
        ) : (
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={handleDragEnd}
          >
            <SortableContext
              items={tasks.map((t) => t.id)}
              strategy={verticalListSortingStrategy}
            >
              <div className="space-y-2">
                {tasks.map((task, index) => (
                  <SortableTaskEditor
                    key={task.id}
                    task={task}
                    index={index}
                    totalTasks={tasks.length}
                    isExpanded={expandedTasks.has(task.id)}
                    onToggleExpanded={() => toggleExpanded(task.id)}
                    onUpdate={(updates) => updateTask(task.id, updates)}
                    onRemove={() => removeTask(task.id)}
                    onDuplicate={() => duplicateTask(task)}
                    onTypeChange={(newType) => updateTaskType(task.id, newType)}
                  />
                ))}
              </div>
            </SortableContext>
          </DndContext>
        )}

        {/* Add Task Button */}
        <div className="flex justify-center pt-2">
          <Popover open={isAddPopoverOpen} onOpenChange={setIsAddPopoverOpen}>
            <PopoverTrigger asChild>
              <Button variant="outline" size="icon" className="rounded-full">
                <Plus className="h-4 w-4" />
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-64 p-2" align="center">
              <div className="space-y-2">
                {Object.entries(groupedTaskTypes).map(([category, types]) => (
                  <div key={category}>
                    <div className="text-xs font-medium text-muted-foreground px-2 py-1">
                      {category}
                    </div>
                    <div className="space-y-0.5">
                      {types.map((t) => (
                        <button
                          key={t.type}
                          onClick={() => addTask(t.type)}
                          className="w-full text-left px-2 py-1.5 text-sm rounded hover:bg-muted transition-colors"
                        >
                          {t.label}
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </PopoverContent>
          </Popover>
        </div>
      </CardContent>
    </Card>
  )
}
