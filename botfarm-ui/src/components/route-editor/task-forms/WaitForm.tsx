import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'

export function WaitForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="seconds">Wait Duration (seconds)</Label>
        <Input
          id="seconds"
          type="number"
          min={0}
          step={0.1}
          value={parameters.seconds as number || 1}
          onChange={(e) => onChange({ seconds: parseFloat(e.target.value) || 1 })}
        />
      </div>
    </div>
  )
}
