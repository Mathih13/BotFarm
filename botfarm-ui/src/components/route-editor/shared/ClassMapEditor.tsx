import { useState } from 'react'
import { PLAYER_CLASSES } from '~/lib/types'
import type { PlayerClass } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Button } from '~/components/ui/button'
import { Checkbox } from '~/components/ui/checkbox'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import { ArrayEditor } from './ArrayEditor'

interface ClassMapEditorProps {
  label: string
  description?: string
  value: Record<string, number | number[]>
  onChange: (value: Record<string, number | number[]>) => void
  valueType: 'number' | 'numberArray'
}

export function ClassMapEditor({
  label,
  description,
  value,
  onChange,
  valueType,
}: ClassMapEditorProps) {
  const [enabled, setEnabled] = useState(Object.keys(value).length > 0)
  const [selectedClass, setSelectedClass] = useState<PlayerClass>('Warrior')

  const availableClasses = PLAYER_CLASSES.filter((cls) => !(cls in value))

  const toggleEnabled = (checked: boolean) => {
    setEnabled(checked)
    if (!checked) {
      onChange({})
    }
  }

  const addClass = () => {
    if (!availableClasses.includes(selectedClass)) return
    const newValue = { ...value }
    newValue[selectedClass] = valueType === 'numberArray' ? [] : 0
    onChange(newValue)
    // Select next available class
    const nextClass = availableClasses.find((c) => c !== selectedClass)
    if (nextClass) setSelectedClass(nextClass)
  }

  const removeClass = (cls: string) => {
    const newValue = { ...value }
    delete newValue[cls]
    onChange(newValue)
  }

  const updateValue = (cls: string, newVal: number | number[]) => {
    onChange({ ...value, [cls]: newVal })
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <Checkbox id={`enable-${label}`} checked={enabled} onCheckedChange={toggleEnabled} />
        <Label htmlFor={`enable-${label}`} className="cursor-pointer">
          {label}
        </Label>
      </div>

      {description && !enabled && (
        <p className="text-xs text-muted-foreground">{description}</p>
      )}

      {enabled && (
        <div className="space-y-3 pl-6 border-l-2 border-muted">
          {Object.entries(value).map(([cls, val]) => (
            <div key={cls} className="flex items-start gap-2">
              <span className="text-sm font-medium w-24 pt-2">{cls}</span>
              <div className="flex-1">
                {valueType === 'numberArray' ? (
                  <ArrayEditor
                    values={Array.isArray(val) ? val : []}
                    onChange={(newVal) => updateValue(cls, newVal)}
                    placeholder="ID"
                  />
                ) : (
                  <Input
                    type="number"
                    value={typeof val === 'number' ? val : 0}
                    onChange={(e) => updateValue(cls, parseInt(e.target.value) || 0)}
                  />
                )}
              </div>
              <Button
                variant="ghost"
                size="sm"
                className="text-destructive hover:text-destructive"
                onClick={() => removeClass(cls)}
              >
                Remove
              </Button>
            </div>
          ))}

          {availableClasses.length > 0 && (
            <div className="flex gap-2 pt-2">
              <Select
                value={selectedClass}
                onValueChange={(v) => setSelectedClass(v as PlayerClass)}
              >
                <SelectTrigger className="w-40">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {availableClasses.map((cls) => (
                    <SelectItem key={cls} value={cls}>
                      {cls}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button variant="outline" size="sm" onClick={addClass}>
                Add Class
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
