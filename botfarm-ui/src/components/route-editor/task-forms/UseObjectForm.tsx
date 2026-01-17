import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Checkbox } from '~/components/ui/checkbox'
import { EntityInput } from '../shared/EntityInput'

export function UseObjectForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <div className="space-y-2">
          <Label htmlFor="objectEntry">Object Entry ID</Label>
          <EntityInput
            id="objectEntry"
            type="object"
            value={parameters.objectEntry as number || 0}
            onChange={(value) => onChange({ objectEntry: value })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="useCount">Use Count</Label>
          <Input
            id="useCount"
            type="number"
            min={1}
            value={parameters.useCount as number || 1}
            onChange={(e) => onChange({ useCount: parseInt(e.target.value) || 1 })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="searchRadius">Search Radius</Label>
          <Input
            id="searchRadius"
            type="number"
            min={1}
            value={parameters.searchRadius as number || 50}
            onChange={(e) => onChange({ searchRadius: parseInt(e.target.value) || 50 })}
          />
        </div>
      </div>

      <div className="flex items-center gap-2">
        <Checkbox
          id="waitForLoot"
          checked={parameters.waitForLoot as boolean || false}
          onCheckedChange={(checked) => onChange({ waitForLoot: checked === true })}
        />
        <Label htmlFor="waitForLoot" className="cursor-pointer">
          Wait for Loot (for chests and lootable objects)
        </Label>
      </div>

      <div className="space-y-2">
        <Label htmlFor="maxWaitSeconds">Max Wait Time (seconds)</Label>
        <Input
          id="maxWaitSeconds"
          type="number"
          min={1}
          value={parameters.maxWaitSeconds as number || 5}
          onChange={(e) => onChange({ maxWaitSeconds: parseInt(e.target.value) || 5 })}
        />
        <p className="text-xs text-muted-foreground">
          Timeout for object interaction completion detection
        </p>
      </div>
    </div>
  )
}
