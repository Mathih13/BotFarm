import type { EquipmentSetItemFormData } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Button } from '~/components/ui/button'
import { Label } from '~/components/ui/label'
import { EntityInput } from '~/components/route-editor/shared/EntityInput'
import { X } from 'lucide-react'

interface EquipmentSetItemEditorProps {
  item: EquipmentSetItemFormData
  index: number
  onUpdate: (updates: Partial<EquipmentSetItemFormData>) => void
  onRemove: () => void
}

export function EquipmentSetItemEditor({
  item,
  index,
  onUpdate,
  onRemove,
}: EquipmentSetItemEditorProps) {
  return (
    <div className="flex items-start gap-3 p-3 border rounded-lg bg-muted/30">
      <div className="flex-1 grid grid-cols-1 md:grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label htmlFor={`item-${item.id}-entry`} className="text-xs">
            Item #{index + 1}
          </Label>
          <EntityInput
            id={`item-${item.id}-entry`}
            type="item"
            value={item.entry}
            onChange={(value) => onUpdate({ entry: value })}
          />
        </div>

        <div className="space-y-1">
          <Label htmlFor={`item-${item.id}-count`} className="text-xs">
            Count
          </Label>
          <Input
            id={`item-${item.id}-count`}
            type="number"
            min={1}
            max={200}
            value={item.count}
            onChange={(e) => onUpdate({ count: parseInt(e.target.value) || 1 })}
            className="h-9"
          />
        </div>
      </div>

      <Button
        variant="ghost"
        size="icon"
        onClick={onRemove}
        className="shrink-0 h-9 w-9 text-muted-foreground hover:text-destructive mt-5"
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  )
}
