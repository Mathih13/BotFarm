import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { EntityInput } from '../shared/EntityInput'

export function AssertHasItemForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="itemEntry">Item Entry ID</Label>
          <EntityInput
            id="itemEntry"
            type="item"
            value={parameters.itemEntry as number || 0}
            onChange={(value) => onChange({ itemEntry: value })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="minCount">Minimum Count</Label>
          <Input
            id="minCount"
            type="number"
            min={1}
            value={parameters.minCount as number || 1}
            onChange={(e) => onChange({ minCount: parseInt(e.target.value) || 1 })}
          />
        </div>
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
