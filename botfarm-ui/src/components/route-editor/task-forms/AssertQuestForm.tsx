import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { EntityInput } from '../shared/EntityInput'

export function AssertQuestForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="questId">Quest ID</Label>
        <EntityInput
          id="questId"
          type="quest"
          value={parameters.questId as number || 0}
          onChange={(value) => onChange({ questId: value })}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="message">Assertion Message</Label>
        <Input
          id="message"
          value={parameters.message as string || ''}
          onChange={(e) => onChange({ message: e.target.value })}
          placeholder="Message shown if assertion fails"
        />
      </div>
    </div>
  )
}
