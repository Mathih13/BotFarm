import { useState } from 'react'
import type { RouteFormData, HarnessFormData, PositionData, ItemRequirement } from '~/lib/types'
import { PLAYER_CLASSES, PLAYER_RACES } from '~/lib/types'
import { getClassColor } from '~/lib/utils'
import { Input } from '~/components/ui/input'
import { Card, CardContent, CardHeader, CardTitle } from '~/components/ui/card'
import { Label } from '~/components/ui/label'
import { Checkbox } from '~/components/ui/checkbox'
import { Badge } from '~/components/ui/badge'
import { Button } from '~/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '~/components/ui/select'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '~/components/ui/collapsible'
import { PositionEditor } from './shared/PositionEditor'
import { ItemListEditor } from './shared/ItemListEditor'
import { QuestListEditor } from './shared/QuestListEditor'
import { EquipmentSetSelector } from './shared/EquipmentSetSelector'

interface HarnessSectionProps {
  harness: HarnessFormData | null
  onChange: (updates: Partial<RouteFormData>) => void
}

function getDefaultHarness(): HarnessFormData {
  return {
    botCount: 1,
    accountPrefix: 'testbot_',
    classes: [],
    race: 'Human',
    level: 1,
    items: [],
    completedQuests: [],
    startPosition: null,
    setupTimeoutSeconds: 120,
    testTimeoutSeconds: 600,
    equipmentSets: [],
    classEquipmentSets: {},
  }
}

export function HarnessSection({ harness, onChange }: HarnessSectionProps) {
  const [isOpen, setIsOpen] = useState(!!harness)
  const enabled = !!harness

  const toggleHarness = (checked: boolean) => {
    if (checked) {
      onChange({ harness: getDefaultHarness() })
    } else {
      onChange({ harness: null })
    }
  }

  const updateHarness = (updates: Partial<HarnessFormData>) => {
    if (!harness) return
    onChange({ harness: { ...harness, ...updates } })
  }

  const toggleClass = (className: string) => {
    if (!harness) return
    const classes = harness.classes.includes(className)
      ? harness.classes.filter((c) => c !== className)
      : [...harness.classes, className]
    updateHarness({ classes })
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle>Test Harness</CardTitle>
        <div className="flex items-center gap-2">
          <Checkbox
            id="enable-harness"
            checked={enabled}
            onCheckedChange={toggleHarness}
          />
          <Label htmlFor="enable-harness" className="cursor-pointer text-sm">
            Enable
          </Label>
        </div>
      </CardHeader>

      {enabled && harness && (
        <CardContent className="space-y-6 pt-4">
          {/* Basic Settings */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="space-y-2">
              <Label htmlFor="botCount">Bot Count</Label>
              <Input
                id="botCount"
                type="number"
                min={1}
                max={100}
                value={harness.botCount}
                onChange={(e) => updateHarness({ botCount: parseInt(e.target.value) || 1 })}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="accountPrefix">Account Prefix</Label>
              <Input
                id="accountPrefix"
                value={harness.accountPrefix}
                onChange={(e) => updateHarness({ accountPrefix: e.target.value })}
                placeholder="testbot_"
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="level">Level</Label>
              <Input
                id="level"
                type="number"
                min={1}
                max={80}
                value={harness.level}
                onChange={(e) => updateHarness({ level: parseInt(e.target.value) || 1 })}
              />
            </div>
          </div>

          {/* Race Selection */}
          <div className="space-y-2">
            <Label>Race</Label>
            <Select value={harness.race} onValueChange={(value) => updateHarness({ race: value })}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PLAYER_RACES.map((race) => (
                  <SelectItem key={race} value={race}>
                    {race}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* Class Selection */}
          <div className="space-y-2">
            <Label>Classes (select one or more)</Label>
            <div className="flex flex-wrap gap-2">
              {PLAYER_CLASSES.map((cls) => {
                const isSelected = harness.classes.includes(cls)
                const classColor = getClassColor(cls)
                return (
                  <Badge
                    key={cls}
                    variant="outline"
                    className="cursor-pointer"
                    style={{
                      backgroundColor: isSelected ? classColor : 'transparent',
                      borderColor: classColor,
                      color: isSelected ? '#fff' : classColor,
                    }}
                    onClick={() => toggleClass(cls)}
                  >
                    {cls}
                  </Badge>
                )
              })}
            </div>
            {harness.classes.length === 0 && (
              <p className="text-xs text-muted-foreground">
                No classes selected - will use random classes
              </p>
            )}
          </div>

          {/* Timeout Settings */}
          <Collapsible open={isOpen} onOpenChange={setIsOpen}>
            <CollapsibleTrigger asChild>
              <Button variant="ghost" className="w-full justify-start p-0 h-auto">
                <span className="text-sm font-medium">
                  {isOpen ? '- Hide' : '+ Show'} Advanced Settings
                </span>
              </Button>
            </CollapsibleTrigger>
            <CollapsibleContent className="space-y-4 pt-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="setupTimeout">Setup Timeout (seconds)</Label>
                  <Input
                    id="setupTimeout"
                    type="number"
                    min={10}
                    value={harness.setupTimeoutSeconds}
                    onChange={(e) =>
                      updateHarness({ setupTimeoutSeconds: parseInt(e.target.value) || 120 })
                    }
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="testTimeout">Test Timeout (seconds)</Label>
                  <Input
                    id="testTimeout"
                    type="number"
                    min={10}
                    value={harness.testTimeoutSeconds}
                    onChange={(e) =>
                      updateHarness({ testTimeoutSeconds: parseInt(e.target.value) || 600 })
                    }
                  />
                </div>
              </div>

              {/* Start Position */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <Label>Start Position</Label>
                  {harness.startPosition ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => updateHarness({ startPosition: null })}
                    >
                      Clear
                    </Button>
                  ) : (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() =>
                        updateHarness({
                          startPosition: { mapId: 0, x: 0, y: 0, z: 0 },
                        })
                      }
                    >
                      Set Position
                    </Button>
                  )}
                </div>
                {harness.startPosition && (
                  <PositionEditor
                    value={harness.startPosition}
                    onChange={(pos: PositionData) => updateHarness({ startPosition: pos })}
                  />
                )}
              </div>

              {/* Items */}
              <div className="space-y-2">
                <Label>Starting Items</Label>
                <ItemListEditor
                  items={harness.items}
                  onChange={(items: ItemRequirement[]) => updateHarness({ items })}
                />
              </div>

              {/* Equipment Sets */}
              <div className="space-y-2">
                <Label>Equipment Sets</Label>
                <EquipmentSetSelector
                  equipmentSets={harness.equipmentSets || []}
                  classEquipmentSets={harness.classEquipmentSets || {}}
                  selectedClasses={harness.classes}
                  onChange={(equipmentSets, classEquipmentSets) =>
                    updateHarness({ equipmentSets, classEquipmentSets })
                  }
                />
              </div>

              {/* Completed Quests */}
              <div className="space-y-2">
                <Label>Pre-completed Quests</Label>
                <QuestListEditor
                  quests={harness.completedQuests}
                  onChange={(completedQuests: number[]) => updateHarness({ completedQuests })}
                />
              </div>
            </CollapsibleContent>
          </Collapsible>
        </CardContent>
      )}
    </Card>
  )
}
