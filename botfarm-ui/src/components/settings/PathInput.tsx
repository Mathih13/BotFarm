import { useState } from 'react'
import { configApi } from '~/lib/api'
import type { PathValidationResponse } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Button } from '~/components/ui/button'
import { CheckCircle, XCircle, AlertCircle, Loader2 } from 'lucide-react'

interface PathInputProps {
  label: string
  value: string
  onChange: (value: string) => void
  pathType: string
  placeholder?: string
}

export function PathInput({
  label,
  value,
  onChange,
  pathType,
  placeholder,
}: PathInputProps) {
  const [validating, setValidating] = useState(false)
  const [validation, setValidation] = useState<PathValidationResponse | null>(null)

  const handleValidate = async () => {
    if (!value?.trim()) return

    setValidating(true)
    setValidation(null)

    try {
      const result = await configApi.validatePath({
        path: value,
        pathType,
      })
      setValidation(result)
    } catch (err) {
      setValidation({
        valid: false,
        exists: false,
        hasExpectedFiles: false,
        message: err instanceof Error ? err.message : 'Validation failed',
        foundFiles: [],
      })
    } finally {
      setValidating(false)
    }
  }

  const handleBlur = () => {
    // Auto-validate on blur if value changed
    const trimmedValue = value?.trim() ?? ''
    if (trimmedValue && trimmedValue !== 'C:\\' && trimmedValue !== 'C:/') {
      handleValidate()
    }
  }

  const getStatusIcon = () => {
    if (validating) {
      return <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
    }
    if (!validation) {
      return null
    }
    if (validation.valid) {
      return <CheckCircle className="h-4 w-4 text-green-500" />
    }
    if (validation.exists && !validation.hasExpectedFiles) {
      return <AlertCircle className="h-4 w-4 text-amber-500" />
    }
    return <XCircle className="h-4 w-4 text-destructive" />
  }

  return (
    <div className="space-y-2">
      <Label>{label}</Label>
      <div className="flex gap-2">
        <div className="flex-1 relative">
          <Input
            value={value ?? ''}
            onChange={(e) => {
              onChange(e.target.value)
              setValidation(null)
            }}
            onBlur={handleBlur}
            placeholder={placeholder}
            className="pr-8"
          />
          <div className="absolute right-2 top-1/2 -translate-y-1/2">
            {getStatusIcon()}
          </div>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={handleValidate}
          disabled={validating || !value?.trim()}
        >
          Validate
        </Button>
      </div>
      {validation && (
        <div
          className={`text-sm ${
            validation.valid
              ? 'text-green-600'
              : validation.exists
                ? 'text-amber-600'
                : 'text-destructive'
          }`}
        >
          {validation.message}
          {validation.foundFiles.length > 0 && (
            <span className="text-muted-foreground ml-1">
              (found: {validation.foundFiles.slice(0, 3).join(', ')}
              {validation.foundFiles.length > 3 ? '...' : ''})
            </span>
          )}
        </div>
      )}
    </div>
  )
}
