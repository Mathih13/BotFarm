import type { TaskFormProps } from './index'
import { Label } from '~/components/ui/label'
import { ClassMapEditor } from '../shared/ClassMapEditor'
import { EntityInput } from '../shared/EntityInput'

export function TalkToNPCForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="npcEntry">NPC Entry ID</Label>
        <EntityInput
          id="npcEntry"
          type="npc"
          value={parameters.npcEntry as number || 0}
          onChange={(value) => onChange({ npcEntry: value })}
        />
      </div>

      <ClassMapEditor
        label="Class-specific NPCs"
        description="Override NPC entry per class (e.g., class trainers)"
        value={parameters.classNPCs as Record<string, number> || {}}
        onChange={(classNPCs) => onChange({ classNPCs })}
        valueType="number"
      />
    </div>
  )
}
