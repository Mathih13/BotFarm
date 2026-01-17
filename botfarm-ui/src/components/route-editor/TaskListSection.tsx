import { useState } from 'react'
import type { TaskFormData, TaskType } from '~/lib/types'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Button } from '~/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import { TaskEditor } from './TaskEditor'

interface TaskListSectionProps {
  tasks: TaskFormData[]
  onChange: (tasks: TaskFormData[]) => void
}

const TASK_TYPES: { type: TaskType; label: string; category: string }[] = [
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

function getDefaultParameters(type: TaskType): Record<string, unknown> {
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

export function TaskListSection({ tasks, onChange }: TaskListSectionProps) {
  const [newTaskType, setNewTaskType] = useState<TaskType>('Wait')
  const [expandedTasks, setExpandedTasks] = useState<Set<string>>(new Set())

  const addTask = () => {
    const newTask: TaskFormData = {
      id: generateTaskId(),
      type: newTaskType,
      parameters: getDefaultParameters(newTaskType),
    }
    onChange([...tasks, newTask])
    setExpandedTasks((prev) => new Set([...prev, newTask.id]))
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

  const moveTask = (index: number, direction: 'up' | 'down') => {
    const newIndex = direction === 'up' ? index - 1 : index + 1
    if (newIndex < 0 || newIndex >= tasks.length) return

    const newTasks = [...tasks]
    const [task] = newTasks.splice(index, 1)
    newTasks.splice(newIndex, 0, task)
    onChange(newTasks)
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

  return (
    <Card>
      <CardHeader>
        <CardTitle>Tasks ({tasks.length})</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Add Task */}
        <div className="flex gap-2">
          <Select
            value={newTaskType}
            onValueChange={(value) => setNewTaskType(value as TaskType)}
          >
            <SelectTrigger className="flex-1">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TASK_TYPES.map((task) => (
                <SelectItem key={task.type} value={task.type}>
                  <span className="text-muted-foreground text-xs mr-2">[{task.category}]</span>
                  {task.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button onClick={addTask}>Add Task</Button>
        </div>

        {/* Task List */}
        {tasks.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-8">
            No tasks yet. Add a task to get started.
          </p>
        ) : (
          <div className="space-y-2">
            {tasks.map((task, index) => (
              <TaskEditor
                key={task.id}
                task={task}
                index={index}
                totalTasks={tasks.length}
                isExpanded={expandedTasks.has(task.id)}
                onToggleExpanded={() => toggleExpanded(task.id)}
                onUpdate={(updates) => updateTask(task.id, updates)}
                onRemove={() => removeTask(task.id)}
                onMoveUp={() => moveTask(index, 'up')}
                onMoveDown={() => moveTask(index, 'down')}
                onDuplicate={() => duplicateTask(task)}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
