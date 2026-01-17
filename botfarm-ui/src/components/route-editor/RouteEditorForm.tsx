import type { RouteFormData, TaskFormData } from '~/lib/types'
import { MetadataSection } from './MetadataSection'
import { HarnessSection } from './HarnessSection'
import { TaskListSection } from './TaskListSection'

interface RouteEditorFormProps {
  formData: RouteFormData
  onChange: (updates: Partial<RouteFormData>) => void
}

export function RouteEditorForm({ formData, onChange }: RouteEditorFormProps) {
  return (
    <div className="space-y-6">
      <MetadataSection
        name={formData.name}
        description={formData.description}
        loop={formData.loop}
        onChange={onChange}
      />

      <HarnessSection harness={formData.harness} onChange={onChange} />

      <TaskListSection
        tasks={formData.tasks}
        onChange={(tasks: TaskFormData[]) => onChange({ tasks })}
      />
    </div>
  )
}
