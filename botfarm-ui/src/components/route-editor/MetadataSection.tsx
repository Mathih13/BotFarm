import type { RouteFormData } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Textarea } from '~/components/ui/textarea'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Label } from '~/components/ui/label'
import { Checkbox } from '~/components/ui/checkbox'

interface MetadataSectionProps {
  name: string
  description: string
  loop: boolean
  onChange: (updates: Partial<RouteFormData>) => void
}

export function MetadataSection({ name, description, loop, onChange }: MetadataSectionProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Route Information</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="name">Name *</Label>
          <Input
            id="name"
            value={name}
            onChange={(e) => onChange({ name: e.target.value })}
            placeholder="Enter route name"
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="description">Description</Label>
          <Textarea
            id="description"
            value={description}
            onChange={(e) => onChange({ description: e.target.value })}
            placeholder="Describe what this route does"
            rows={3}
          />
        </div>

        <div className="flex items-center gap-2">
          <Checkbox
            id="loop"
            checked={loop}
            onCheckedChange={(checked) => onChange({ loop: checked === true })}
          />
          <Label htmlFor="loop" className="cursor-pointer">
            Loop (repeat tasks continuously)
          </Label>
        </div>
      </CardContent>
    </Card>
  )
}
