import { Button } from '~/components/ui/button'
import { EntityInput } from './EntityInput'

interface QuestListEditorProps {
  quests: number[]
  onChange: (quests: number[]) => void
}

export function QuestListEditor({ quests, onChange }: QuestListEditorProps) {
  const addQuest = () => {
    onChange([...quests, 0])
  }

  const removeQuest = (index: number) => {
    onChange(quests.filter((_, i) => i !== index))
  }

  const updateQuest = (index: number, value: number) => {
    const updated = quests.map((q, i) => (i === index ? value : q))
    onChange(updated)
  }

  return (
    <div className="space-y-2">
      {quests.length > 0 ? (
        quests.map((quest, index) => (
          <div key={index} className="flex gap-2 items-start">
            <div className="flex-1">
              <EntityInput
                type="quest"
                value={quest}
                onChange={(value) => updateQuest(index, value)}
              />
            </div>
            <Button
              variant="ghost"
              size="sm"
              className="text-destructive hover:text-destructive mt-1"
              onClick={() => removeQuest(index)}
            >
              Remove
            </Button>
          </div>
        ))
      ) : (
        <p className="text-sm text-muted-foreground">No quests added</p>
      )}
      <Button variant="outline" size="sm" onClick={addQuest}>
        + Add Quest
      </Button>
    </div>
  )
}
