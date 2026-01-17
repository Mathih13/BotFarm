import type { TaskFormProps } from './index'
import { Label } from '~/components/ui/label'
import { ClassMapEditor } from '../shared/ClassMapEditor'
import { EntityInput } from '../shared/EntityInput'

export function AcceptQuestForm({ parameters, onChange }: TaskFormProps) {
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
          <Label htmlFor="questId">Quest ID</Label>
          <EntityInput
            id="questId"
            type="quest"
            value={parameters.questId as number || 0}
            onChange={(value) => onChange({ questId: value })}
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

      <ClassMapEditor
        label="Class-specific Quests"
        description="Override quest ID per class"
        value={parameters.classQuests as Record<string, number> || {}}
        onChange={(classQuests) => onChange({ classQuests })}
        valueType="number"
      />
    </div>
  )
}
