import { Input } from '~/components/ui/input'
import { Button } from '~/components/ui/button'

interface ArrayEditorProps {
  values: number[]
  onChange: (values: number[]) => void
  placeholder?: string
}

export function ArrayEditor({ values, onChange, placeholder = 'Value' }: ArrayEditorProps) {
  const addValue = () => {
    onChange([...values, 0])
  }

  const removeValue = (index: number) => {
    onChange(values.filter((_, i) => i !== index))
  }

  const updateValue = (index: number, value: number) => {
    const updated = values.map((v, i) => (i === index ? value : v))
    onChange(updated)
  }

  return (
    <div className="space-y-2">
      {values.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {values.map((value, index) => (
            <div key={index} className="flex items-center gap-1">
              <Input
                type="number"
                value={value || ''}
                onChange={(e) => updateValue(index, parseInt(e.target.value) || 0)}
                placeholder={placeholder}
                className="w-24"
              />
              <Button
                variant="ghost"
                size="sm"
                className="text-destructive hover:text-destructive h-8 px-2"
                onClick={() => removeValue(index)}
              >
                x
              </Button>
            </div>
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No values added</p>
      )}
      <Button variant="outline" size="sm" onClick={addValue}>
        + Add {placeholder}
      </Button>
    </div>
  )
}
