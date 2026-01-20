import type { SuiteFormData, SuiteTestFormData } from '~/lib/types'
import { SuiteMetadataSection } from './SuiteMetadataSection'
import { SuiteTestListSection } from './SuiteTestListSection'

interface SuiteEditorFormProps {
  formData: SuiteFormData
  availableRoutes: { path: string; name: string }[]
  onChange: (updates: Partial<SuiteFormData>) => void
}

export function SuiteEditorForm({ formData, availableRoutes, onChange }: SuiteEditorFormProps) {
  return (
    <div className="space-y-6">
      <SuiteMetadataSection name={formData.name} onChange={onChange} />

      <SuiteTestListSection
        tests={formData.tests}
        availableRoutes={availableRoutes}
        onChange={(tests: SuiteTestFormData[]) => onChange({ tests })}
      />
    </div>
  )
}
