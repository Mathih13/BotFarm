import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { TaskFormData, TaskType } from '~/lib/types'
import { TaskEditor } from './TaskEditor'

interface SortableTaskEditorProps {
  task: TaskFormData
  index: number
  totalTasks: number
  isExpanded: boolean
  onToggleExpanded: () => void
  onUpdate: (updates: Partial<TaskFormData>) => void
  onRemove: () => void
  onDuplicate: () => void
  onTypeChange: (newType: TaskType) => void
}

export function SortableTaskEditor({
  task,
  ...props
}: SortableTaskEditorProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: task.id })

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
    zIndex: isDragging ? 1 : 0,
  }

  return (
    <div ref={setNodeRef} style={style}>
      <TaskEditor
        task={task}
        dragHandleProps={{ ...attributes, ...listeners }}
        {...props}
      />
    </div>
  )
}
