import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'

export function AssertLevelForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="minLevel">Minimum Level</Label>
        <Input
          id="minLevel"
          type="number"
          min={1}
          max={80}
          value={parameters.minLevel as number || 1}
          onChange={(e) => onChange({ minLevel: parseInt(e.target.value) || 1 })}
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
