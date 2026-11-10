import { formatMinutes } from '@shared/ui/duration';
import { ActivityEntry } from '../data-access/activity-api.service';

export type Translate = (key: string, params?: Record<string, unknown>) => string;

export interface ActivityText {
  /** The sentence, with the task's real title. */
  readonly text: string;
  /** The same sentence cut around the task title, so the title can be a link. Null when it has no task title. */
  readonly aroundTask: readonly [before: string, after: string] | null;
  /** One line per change, for an update. */
  readonly details: readonly string[];
}

/** Stands in for the task title while the sentence is built; never shown. */
const TASK_MARK = '\u0001';

const KNOWN_TYPES = new Set([
  'TaskCreated',
  'TaskUpdated',
  'TaskAssigned',
  'TaskMoved',
  'TaskDeleted',
  'CommentAdded',
  'CommentEdited',
  'CommentDeleted',
  'MemberAdded',
  'MemberRemoved',
  'MemberRoleChanged',
  'LabelCreated',
  'LabelRenamed',
  'LabelDeleted',
]);

const str = (value: unknown): string => (typeof value === 'string' ? value : '');
const names = (value: unknown): string =>
  Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string').join(', ') : '';
const pair = (value: unknown): { from: unknown; to: unknown } | null =>
  typeof value === 'object' && value !== null
    ? { from: (value as { from?: unknown }).from ?? null, to: (value as { to?: unknown }).to ?? null }
    : null;

/** Builds the sentence of an entry from its type's translation. An unknown type gets a generic sentence. */
export function describeActivity(entry: ActivityEntry, t: Translate): ActivityText {
  const c = entry.changes;
  const actor = entry.actorName;
  const status = (value: unknown) => translateEnum(t, 'tasks.statuses', value);
  const role = (value: unknown) => translateEnum(t, 'members.roles', value);
  const priority = (value: unknown) => translateEnum(t, 'tasks.priorities', value);
  const taskTitle = str(c['taskTitle']);

  const defaultKey = KNOWN_TYPES.has(entry.type) ? `activity.types.${entry.type}` : 'activity.generic';
  const sentence = (params: Record<string, unknown>, key = defaultKey): ActivityText => {
    const text = t(key, { actor, ...params, task: taskTitle });
    const parts = taskTitle === '' ? undefined : t(key, { actor, ...params, task: TASK_MARK }).split(TASK_MARK);
    return { text, aroundTask: parts?.length === 2 ? [parts[0], parts[1]] : null, details: [] };
  };

  switch (entry.type) {
    case 'TaskCreated':
      return sentence({});
    case 'TaskUpdated': {
      const details: string[] = [];
      const title = pair(c['title']);
      if (title) {
        details.push(t('activity.changes.title', { from: str(title.from), to: str(title.to) }));
      }
      const prio = pair(c['priority']);
      if (prio) {
        details.push(t('activity.changes.priority', { from: priority(prio.from), to: priority(prio.to) }));
      }
      const due = pair(c['dueDate']);
      if (due) {
        const from = str(due.from);
        const to = str(due.to);
        if (to === '') {
          details.push(t('activity.changes.dueDateCleared'));
        } else if (from === '') {
          details.push(t('activity.changes.dueDateSet', { to }));
        } else {
          details.push(t('activity.changes.dueDate', { from, to }));
        }
      }
      const estimate = pair(c['estimatedMinutes']);
      if (estimate) {
        const from = typeof estimate.from === 'number' ? formatMinutes(estimate.from) : null;
        const to = typeof estimate.to === 'number' ? formatMinutes(estimate.to) : null;
        if (to === null) {
          details.push(t('activity.changes.estimateCleared'));
        } else if (from === null) {
          details.push(t('activity.changes.estimateSet', { to }));
        } else {
          details.push(t('activity.changes.estimate', { from, to }));
        }
      }
      if (c['descriptionChanged'] === true) {
        details.push(t('activity.changes.description'));
      }
      if (names(c['labelsAdded']) !== '') {
        details.push(t('activity.changes.labelsAdded', { labels: names(c['labelsAdded']) }));
      }
      if (names(c['labelsRemoved']) !== '') {
        details.push(t('activity.changes.labelsRemoved', { labels: names(c['labelsRemoved']) }));
      }
      return { ...sentence({}), details };
    }
    case 'TaskAssigned': {
      const to = str(c['assigneeName']);
      const from = str(c['previousAssigneeName']);
      const key = to === '' ? 'activity.types.TaskUnassigned' : from === '' ? 'activity.types.TaskAssigned' : 'activity.types.TaskReassigned';
      return sentence({ to, from }, key);
    }
    case 'TaskMoved':
      return sentence({ from: status(c['from']), to: status(c['to']) });
    case 'TaskDeleted':
    case 'CommentAdded':
    case 'CommentEdited':
    case 'CommentDeleted':
      return sentence({});
    case 'MemberAdded':
    case 'MemberRemoved':
      return sentence({ member: str(c['memberName']), role: role(c['role']) });
    case 'MemberRoleChanged':
      return sentence({ member: str(c['memberName']), from: role(c['from']), to: role(c['to']) });
    case 'LabelCreated':
      return sentence({ label: str(c['labelName']) });
    case 'LabelRenamed':
      return sentence({ from: str(c['from']), to: str(c['to']) });
    case 'LabelDeleted':
      return sentence({ label: str(c['labelName']) });
    default:
      return sentence({ type: entry.type });
  }
}

function translateEnum(t: Translate, prefix: string, value: unknown): string {
  const raw = str(value);
  return raw === '' ? '' : t(`${prefix}.${raw}`);
}
