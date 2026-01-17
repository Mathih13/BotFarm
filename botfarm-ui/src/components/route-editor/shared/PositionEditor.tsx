import type { PositionData } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'

interface PositionEditorProps {
  value: PositionData
  onChange: (value: PositionData) => void
}

export function PositionEditor({ value, onChange }: PositionEditorProps) {
  const handleChange = (field: keyof PositionData, newValue: string) => {
    const num = parseFloat(newValue) || 0
    onChange({ ...value, [field]: num })
  }

  return (
    <div className="grid grid-cols-4 gap-2">
      <div className="space-y-1">
        <Label className="text-xs">Map ID</Label>
        <Input
          type="number"
          value={value.mapId}
          onChange={(e) => handleChange('mapId', e.target.value)}
          placeholder="0"
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs">X</Label>
        <Input
          type="number"
          step="0.1"
          value={value.x}
          onChange={(e) => handleChange('x', e.target.value)}
          placeholder="0"
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs">Y</Label>
        <Input
          type="number"
          step="0.1"
          value={value.y}
          onChange={(e) => handleChange('y', e.target.value)}
          placeholder="0"
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs">Z</Label>
        <Input
          type="number"
          step="0.1"
          value={value.z}
          onChange={(e) => handleChange('z', e.target.value)}
          placeholder="0"
        />
      </div>
    </div>
  )
}
