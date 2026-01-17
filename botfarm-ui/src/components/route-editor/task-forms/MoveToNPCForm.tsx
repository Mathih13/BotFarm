import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { ClassMapEditor } from '../shared/ClassMapEditor'
import { EntityInput } from '../shared/EntityInput'

export function MoveToNPCForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="npcEntry">NPC Entry ID</Label>
          <EntityInput
            id="npcEntry"
            type="npc"
            value={parameters.npcEntry as number || 0}
            onChange={(value) => onChange({ npcEntry: value })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="threshold">Distance Threshold</Label>
          <Input
            id="threshold"
            type="number"
            step="0.1"
            value={parameters.threshold as number || 4.0}
            onChange={(e) => onChange({ threshold: parseFloat(e.target.value) || 4.0 })}
          />
        </div>
      </div>

      <ClassMapEditor
        label="Class-specific NPCs"
        description="Override NPC entry per class"
        value={parameters.classNPCs as Record<string, number> || {}}
        onChange={(classNPCs) => onChange({ classNPCs })}
        valueType="number"
      />
    </div>
  )
}
