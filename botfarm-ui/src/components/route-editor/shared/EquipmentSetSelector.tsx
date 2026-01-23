import { useState, useEffect } from 'react'
import { Link } from '@tanstack/react-router'
import { equipmentSetsApi } from '~/lib/api'
import type { ApiEquipmentSetInfo } from '~/lib/types'
import { Button } from '~/components/ui/button'
import { Label } from '~/components/ui/label'
import { Badge } from '~/components/ui/badge'
import { getClassColor } from '~/lib/utils'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import { X, Plus, ExternalLink } from 'lucide-react'

interface EquipmentSetSelectorProps {
  equipmentSets: string[]
  classEquipmentSets: Record<string, string>
  selectedClasses: string[]
  onChange: (equipmentSets: string[], classEquipmentSets: Record<string, string>) => void
}

export function EquipmentSetSelector({
  equipmentSets,
  classEquipmentSets,
  selectedClasses,
  onChange,
}: EquipmentSetSelectorProps) {
  const [availableSets, setAvailableSets] = useState<ApiEquipmentSetInfo[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    loadEquipmentSets()
  }, [])

  async function loadEquipmentSets() {
    try {
      const sets = await equipmentSetsApi.getAll()
      setAvailableSets(sets)
    } catch (err) {
      console.error('Failed to load equipment sets:', err)
    } finally {
      setLoading(false)
    }
  }

  // Filter sets that can be used by all selected classes (or have no restriction)
  const universalSets = availableSets.filter(
    (set) => !set.classRestriction
  )

  // Get sets available for a specific class
  const getSetsForClass = (className: string) =>
    availableSets.filter(
      (set) => !set.classRestriction || set.classRestriction === className
    )

  const addEquipmentSet = (setName: string) => {
    if (!equipmentSets.includes(setName)) {
      onChange([...equipmentSets, setName], classEquipmentSets)
    }
  }

  const removeEquipmentSet = (setName: string) => {
    onChange(
      equipmentSets.filter((s) => s !== setName),
      classEquipmentSets
    )
  }

  const setClassEquipmentSet = (className: string, setName: string | null) => {
    const newClassSets = { ...classEquipmentSets }
    if (setName) {
      newClassSets[className] = setName
    } else {
      delete newClassSets[className]
    }
    onChange(equipmentSets, newClassSets)
  }

  if (loading) {
    return (
      <div className="text-sm text-muted-foreground">Loading equipment sets...</div>
    )
  }

  if (availableSets.length === 0) {
    return (
      <div className="space-y-2">
        <p className="text-sm text-muted-foreground">
          No equipment sets available.{' '}
          <Link to="/equipment-sets" className="text-primary hover:underline">
            Create one
          </Link>
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Universal Equipment Sets (applied to all bots) */}
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label>Equipment Sets (all classes)</Label>
          <Button
            variant="ghost"
            size="sm"
            asChild
            className="h-7 text-xs"
          >
            <Link to="/equipment-sets">
              <ExternalLink className="h-3 w-3 mr-1" />
              Manage Sets
            </Link>
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          These sets will be applied to all bots (filtered by class restriction)
        </p>

        {/* Selected sets */}
        {equipmentSets.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {equipmentSets.map((setName) => {
              const setInfo = availableSets.find((s) => s.name === setName)
              return (
                <Badge
                  key={setName}
                  variant="secondary"
                  className="flex items-center gap-1"
                >
                  {setName}
                  {setInfo?.classRestriction && (
                    <span
                      className="text-xs"
                      style={{ color: getClassColor(setInfo.classRestriction) }}
                    >
                      ({setInfo.classRestriction})
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={() => removeEquipmentSet(setName)}
                    className="ml-1 hover:text-destructive"
                  >
                    <X className="h-3 w-3" />
                  </button>
                </Badge>
              )
            })}
          </div>
        )}

        {/* Add set dropdown */}
        <Select
          value=""
          onValueChange={(value) => {
            if (value) addEquipmentSet(value)
          }}
        >
          <SelectTrigger className="w-full">
            <span className="flex items-center gap-2 text-muted-foreground">
              <Plus className="h-4 w-4" />
              Add equipment set...
            </span>
          </SelectTrigger>
          <SelectContent>
            {availableSets
              .filter((set) => !equipmentSets.includes(set.name))
              .map((set) => (
                <SelectItem key={set.name} value={set.name}>
                  <div className="flex items-center gap-2">
                    <span>{set.name}</span>
                    {set.classRestriction ? (
                      <Badge
                        variant="outline"
                        className="text-xs"
                        style={{
                          borderColor: getClassColor(set.classRestriction),
                          color: getClassColor(set.classRestriction),
                        }}
                      >
                        {set.classRestriction}
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-xs">
                        All
                      </Badge>
                    )}
                    <span className="text-muted-foreground text-xs">
                      ({set.itemCount} items)
                    </span>
                  </div>
                </SelectItem>
              ))}
            {availableSets.filter((set) => !equipmentSets.includes(set.name)).length ===
              0 && (
              <div className="px-2 py-1 text-sm text-muted-foreground">
                All sets already added
              </div>
            )}
          </SelectContent>
        </Select>
      </div>

      {/* Class-Specific Overrides */}
      {selectedClasses.length > 0 && (
        <div className="space-y-2 pt-2 border-t">
          <Label>Class-Specific Overrides</Label>
          <p className="text-xs text-muted-foreground">
            Override the equipment set for specific classes (takes priority over universal sets)
          </p>

          <div className="space-y-2">
            {selectedClasses.map((className) => {
              const classColor = getClassColor(className)
              const classSets = getSetsForClass(className)
              const currentValue = classEquipmentSets[className] || ''

              return (
                <div key={className} className="flex items-center gap-2">
                  <Badge
                    variant="outline"
                    className="w-24 justify-center"
                    style={{
                      borderColor: classColor,
                      color: classColor,
                    }}
                  >
                    {className}
                  </Badge>
                  <Select
                    value={currentValue || '_none'}
                    onValueChange={(value) =>
                      setClassEquipmentSet(className, value === '_none' ? null : value)
                    }
                  >
                    <SelectTrigger className="flex-1">
                      <SelectValue placeholder="Use universal sets" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="_none">Use universal sets</SelectItem>
                      {classSets.map((set) => (
                        <SelectItem key={set.name} value={set.name}>
                          {set.name} ({set.itemCount} items)
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )
            })}
          </div>
        </div>
      )}
    </div>
  )
}
