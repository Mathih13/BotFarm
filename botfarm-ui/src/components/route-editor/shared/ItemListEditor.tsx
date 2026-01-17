import type { ItemRequirement } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Button } from '~/components/ui/button'
import { Label } from '~/components/ui/label'

interface ItemListEditorProps {
  items: ItemRequirement[]
  onChange: (items: ItemRequirement[]) => void
}

export function ItemListEditor({ items, onChange }: ItemListEditorProps) {
  const addItem = () => {
    onChange([...items, { entry: 0, count: 1 }])
  }

  const removeItem = (index: number) => {
    onChange(items.filter((_, i) => i !== index))
  }

  const updateItem = (index: number, field: keyof ItemRequirement, value: number) => {
    const updated = items.map((item, i) =>
      i === index ? { ...item, [field]: value } : item
    )
    onChange(updated)
  }

  return (
    <div className="space-y-2">
      {items.map((item, index) => (
        <div key={index} className="flex gap-2 items-end">
          <div className="flex-1 space-y-1">
            <Label className="text-xs">Entry ID</Label>
            <Input
              type="number"
              value={item.entry || ''}
              onChange={(e) => updateItem(index, 'entry', parseInt(e.target.value) || 0)}
              placeholder="Item Entry"
            />
          </div>
          <div className="w-24 space-y-1">
            <Label className="text-xs">Count</Label>
            <Input
              type="number"
              min={1}
              value={item.count || ''}
              onChange={(e) => updateItem(index, 'count', parseInt(e.target.value) || 1)}
              placeholder="1"
            />
          </div>
          <Button
            variant="ghost"
            size="sm"
            className="text-destructive hover:text-destructive"
            onClick={() => removeItem(index)}
          >
            Remove
          </Button>
        </div>
      ))}
      <Button variant="outline" size="sm" onClick={addItem}>
        + Add Item
      </Button>
    </div>
  )
}
