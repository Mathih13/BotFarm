import type { TaskFormProps } from './index'
import type { KillRequirement, CollectItem } from '~/lib/types'
import { Input } from '~/components/ui/input'
import { Label } from '~/components/ui/label'
import { Button } from '~/components/ui/button'
import { EntityArrayEditor } from '../shared/EntityArrayEditor'
import { EntityInput } from '../shared/EntityInput'

export function KillMobsForm({ parameters, onChange }: TaskFormProps) {
  const targetEntries = (parameters.targetEntries as number[]) || []
  const killRequirements = (parameters.killRequirements as KillRequirement[]) || []
  const collectItems = (parameters.collectItems as CollectItem[]) || []

  const updateKillRequirement = (index: number, field: keyof KillRequirement, value: number) => {
    const updated = killRequirements.map((req, i) =>
      i === index ? { ...req, [field]: value } : req
    )
    onChange({ killRequirements: updated })
  }

  const addKillRequirement = () => {
    onChange({ killRequirements: [...killRequirements, { entry: 0, count: 1 }] })
  }

  const removeKillRequirement = (index: number) => {
    onChange({ killRequirements: killRequirements.filter((_, i) => i !== index) })
  }

  const updateCollectItem = (index: number, field: string, value: number | number[]) => {
    const updated = collectItems.map((item, i) =>
      i === index ? { ...item, [field]: value } : item
    )
    onChange({ collectItems: updated })
  }

  const addCollectItem = () => {
    onChange({ collectItems: [...collectItems, { itemEntry: 0, count: 1, droppedBy: [] }] })
  }

  const removeCollectItem = (index: number) => {
    onChange({ collectItems: collectItems.filter((_, i) => i !== index) })
  }

  return (
    <div className="space-y-6">
      {/* Target Entries */}
      <div className="space-y-2">
        <Label>Target Mob Entries</Label>
        <EntityArrayEditor
          values={targetEntries}
          onChange={(values) => onChange({ targetEntries: values })}
          entityType="npc"
          placeholder="Mob Entry"
        />
      </div>

      {/* Basic Settings */}
      <div className="grid grid-cols-3 gap-4">
        <div className="space-y-2">
          <Label htmlFor="killCount">Kill Count (legacy)</Label>
          <Input
            id="killCount"
            type="number"
            min={0}
            value={parameters.killCount as number || 0}
            onChange={(e) => onChange({ killCount: parseInt(e.target.value) || 0 })}
          />
          <p className="text-xs text-muted-foreground">Use killRequirements instead for per-mob counts</p>
        </div>
        <div className="space-y-2">
          <Label htmlFor="searchRadius">Search Radius</Label>
          <Input
            id="searchRadius"
            type="number"
            min={1}
            value={parameters.searchRadius as number || 50}
            onChange={(e) => onChange({ searchRadius: parseInt(e.target.value) || 50 })}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="maxDuration">Max Duration (seconds)</Label>
          <Input
            id="maxDuration"
            type="number"
            min={0}
            value={parameters.maxDurationSeconds as number || 0}
            onChange={(e) => onChange({ maxDurationSeconds: parseInt(e.target.value) || 0 })}
          />
          <p className="text-xs text-muted-foreground">0 = no limit</p>
        </div>
      </div>

      {/* Kill Requirements */}
      <div className="space-y-2">
        <Label>Kill Requirements (per-mob counts)</Label>
        <div className="space-y-2">
          {killRequirements.map((req, index) => (
            <div key={index} className="flex gap-2 items-start">
              <div className="flex-1">
                <Label className="text-xs">Mob Entry</Label>
                <EntityInput
                  type="npc"
                  value={req.entry || 0}
                  onChange={(value) => updateKillRequirement(index, 'entry', value)}
                />
              </div>
              <div className="w-24 space-y-1">
                <Label className="text-xs">Count</Label>
                <Input
                  type="number"
                  min={1}
                  value={req.count || ''}
                  onChange={(e) => updateKillRequirement(index, 'count', parseInt(e.target.value) || 1)}
                />
              </div>
              <Button
                variant="ghost"
                size="sm"
                className="text-destructive mt-5"
                onClick={() => removeKillRequirement(index)}
              >
                Remove
              </Button>
            </div>
          ))}
          <Button variant="outline" size="sm" onClick={addKillRequirement}>
            + Add Kill Requirement
          </Button>
        </div>
      </div>

      {/* Collect Items */}
      <div className="space-y-2">
        <Label>Collect Items</Label>
        <div className="space-y-3">
          {collectItems.map((item, index) => (
            <div key={index} className="p-3 border rounded-lg space-y-2">
              <div className="flex gap-2 items-start">
                <div className="flex-1">
                  <Label className="text-xs">Item Entry</Label>
                  <EntityInput
                    type="item"
                    value={item.itemEntry || 0}
                    onChange={(value) => updateCollectItem(index, 'itemEntry', value)}
                  />
                </div>
                <div className="w-24 space-y-1">
                  <Label className="text-xs">Count</Label>
                  <Input
                    type="number"
                    min={1}
                    value={item.count || ''}
                    onChange={(e) => updateCollectItem(index, 'count', parseInt(e.target.value) || 1)}
                  />
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-destructive mt-5"
                  onClick={() => removeCollectItem(index)}
                >
                  Remove
                </Button>
              </div>
              <div className="space-y-1">
                <Label className="text-xs">Dropped By (mob entries)</Label>
                <EntityArrayEditor
                  values={Array.isArray(item.droppedBy) ? item.droppedBy : item.droppedBy ? [item.droppedBy] : []}
                  onChange={(values) => updateCollectItem(index, 'droppedBy', values)}
                  entityType="npc"
                  placeholder="Mob Entry"
                />
              </div>
            </div>
          ))}
          <Button variant="outline" size="sm" onClick={addCollectItem}>
            + Add Collect Item
          </Button>
        </div>
      </div>

      {/* Optional Center Position */}
      <div className="space-y-2">
        <Label>Center Position (optional)</Label>
        <div className="grid grid-cols-4 gap-2">
          <div className="space-y-1">
            <Label className="text-xs">Map ID</Label>
            <Input
              type="number"
              value={parameters.mapId as number || ''}
              onChange={(e) => onChange({ mapId: parseInt(e.target.value) || 0 })}
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Center X</Label>
            <Input
              type="number"
              step="0.1"
              value={parameters.centerX as number || ''}
              onChange={(e) => onChange({ centerX: parseFloat(e.target.value) || 0 })}
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Center Y</Label>
            <Input
              type="number"
              step="0.1"
              value={parameters.centerY as number || ''}
              onChange={(e) => onChange({ centerY: parseFloat(e.target.value) || 0 })}
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Center Z</Label>
            <Input
              type="number"
              step="0.1"
              value={parameters.centerZ as number || ''}
              onChange={(e) => onChange({ centerZ: parseFloat(e.target.value) || 0 })}
            />
          </div>
        </div>
        <p className="text-xs text-muted-foreground">Leave empty to use current position</p>
      </div>
    </div>
  )
}
