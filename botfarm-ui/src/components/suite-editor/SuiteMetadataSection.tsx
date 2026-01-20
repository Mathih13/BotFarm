import type { SuiteFormData } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Label } from '~/components/ui/label'

interface SuiteMetadataSectionProps {
  name: string
  onChange: (updates: Partial<SuiteFormData>) => void
}

export function SuiteMetadataSection({ name, onChange }: SuiteMetadataSectionProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Suite Information</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="name">Name *</Label>
          <Input
            id="name"
            value={name}
            onChange={(e) => onChange({ name: e.target.value })}
            placeholder="Enter suite name"
          />
          <p className="text-xs text-muted-foreground">
            A descriptive name for this test suite
          </p>
        </div>
      </CardContent>
    </Card>
  )
}
