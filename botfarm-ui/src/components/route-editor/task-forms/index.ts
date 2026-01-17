import type { TaskType } from '~/lib/types'
import { WaitForm } from './WaitForm'
import { LogMessageForm } from './LogMessageForm'
import { MoveToLocationForm } from './MoveToLocationForm'
import { MoveToNPCForm } from './MoveToNPCForm'
import { TalkToNPCForm } from './TalkToNPCForm'
import { AcceptQuestForm } from './AcceptQuestForm'
import { TurnInQuestForm } from './TurnInQuestForm'
import { KillMobsForm } from './KillMobsForm'
import { UseObjectForm } from './UseObjectForm'
import { AdventureForm } from './AdventureForm'
import { LearnSpellsForm } from './LearnSpellsForm'
import { AssertQuestForm } from './AssertQuestForm'
import { AssertHasItemForm } from './AssertHasItemForm'
import { AssertLevelForm } from './AssertLevelForm'

export interface TaskFormProps {
  parameters: Record<string, unknown>
  onChange: (updates: Record<string, unknown>) => void
}

type TaskFormComponent = React.FC<TaskFormProps>

const taskForms: Record<TaskType, TaskFormComponent> = {
  Wait: WaitForm,
  LogMessage: LogMessageForm,
  MoveToLocation: MoveToLocationForm,
  MoveToNPC: MoveToNPCForm,
  TalkToNPC: TalkToNPCForm,
  AcceptQuest: AcceptQuestForm,
  TurnInQuest: TurnInQuestForm,
  KillMobs: KillMobsForm,
  UseObject: UseObjectForm,
  Adventure: AdventureForm,
  LearnSpells: LearnSpellsForm,
  AssertQuestInLog: AssertQuestForm,
  AssertQuestNotInLog: AssertQuestForm,
  AssertHasItem: AssertHasItemForm,
  AssertLevel: AssertLevelForm,
}

export function getTaskForm(type: TaskType): TaskFormComponent {
  return taskForms[type] || WaitForm
}
