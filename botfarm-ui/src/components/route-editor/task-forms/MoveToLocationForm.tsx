import type { TaskFormProps } from './index'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'

export function MoveToLocationForm({ parameters, onChange }: TaskFormProps) {
  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="description">Description (optional)</Label>
        <Input
          id="description"
          value={(parameters.description as string) || ''}
          onChange={(e) => onChange({ description: e.target.value || undefined })}
          placeholder="e.g. Northshire Abbey, Quest Giver Area"
        />
        <p className="text-xs text-muted-foreground">
          Human-readable name shown instead of coordinates
        </p>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <div className="space-y-2">
          <Label htmlFor="mapId">Map ID</Label>
          <Input
            id="mapId"
            type="number"
            value={parameters.mapId as number || 0}
            onChange={(e) => onChange({ mapId: parseInt(e.target.value) || 0 })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="x">X</Label>
          <Input
            id="x"
            type="number"
            step="0.1"
            value={parameters.x as number || 0}
            onChange={(e) => onChange({ x: parseFloat(e.target.value) || 0 })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="y">Y</Label>
          <Input
            id="y"
            type="number"
            step="0.1"
            value={parameters.y as number || 0}
            onChange={(e) => onChange({ y: parseFloat(e.target.value) || 0 })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="z">Z</Label>
          <Input
            id="z"
            type="number"
            step="0.1"
            value={parameters.z as number || 0}
            onChange={(e) => onChange({ z: parseFloat(e.target.value) || 0 })}
          />
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="threshold">Distance Threshold</Label>
        <Input
          id="threshold"
          type="number"
          step="0.1"
          value={parameters.threshold as number || 3.0}
          onChange={(e) => onChange({ threshold: parseFloat(e.target.value) || 3.0 })}
        />
        <p className="text-xs text-muted-foreground">
          How close the bot needs to get to the target position
        </p>
      </div>
    </div>
  )
}
