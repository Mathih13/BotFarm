import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'

export function LogMessageForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="message">Message</Label>
        <Input
          id="message"
          value={parameters.message as string || ''}
          onChange={(e) => onChange({ message: e.target.value })}
          placeholder="Enter log message"
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="level">Log Level</Label>
        <Select
          value={parameters.level as string || 'Info'}
          onValueChange={(value) => onChange({ level: value })}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Info">Info</SelectItem>
            <SelectItem value="Warn">Warn</SelectItem>
            <SelectItem value="Error">Error</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  )
}
