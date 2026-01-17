import type { TaskFormProps } from './index'
import { Label } from '~/components/ui/label'
import { ArrayEditor } from '../shared/ArrayEditor'
import { ClassMapEditor } from '../shared/ClassMapEditor'
import { EntityInput } from '../shared/EntityInput'

export function LearnSpellsForm({ parameters, onChange }: TaskFormProps) {
  const spellIds = (parameters.spellIds as number[]) || []

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="npcEntry">Trainer NPC Entry ID</Label>
        <EntityInput
          id="npcEntry"
          type="npc"
          value={parameters.npcEntry as number || 0}
          onChange={(value) => onChange({ npcEntry: value })}
        />
        <p className="text-xs text-muted-foreground">
          Bot must already be at the trainer (use MoveToNPC + TalkToNPC first)
        </p>
      </div>

      <div className="space-y-2">
        <Label>Spell IDs to Learn</Label>
        <ArrayEditor
          values={spellIds}
          onChange={(values) => onChange({ spellIds: values })}
          placeholder="Spell ID"
        />
      </div>

      <ClassMapEditor
        label="Class-specific Trainers"
        description="Override trainer NPC per class"
        value={parameters.classNPCs as Record<string, number> || {}}
        onChange={(classNPCs) => onChange({ classNPCs })}
        valueType="number"
      />

      <ClassMapEditor
        label="Class-specific Spells"
        description="Override spell list per class"
        value={parameters.classSpells as Record<string, number[]> || {}}
        onChange={(classSpells) => onChange({ classSpells })}
        valueType="numberArray"
      />
    </div>
  )
}
