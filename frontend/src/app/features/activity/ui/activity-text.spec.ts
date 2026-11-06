import { anEntry } from '../data-access/store/testing';
import { describeActivity } from './activity-text';

/** Echoes the key and its params, so a spec sees which template was picked and what went into it. */
const t = (key: string, params: Record<string, unknown> = {}): string => {
  const values = Object.entries(params)
    .map(([name, value]) => `${name}=${String(value)}`)
    .join(',');
  return values === '' ? key : `${key}[${values}]`;
};

const describeType = (type: string, changes: Record<string, unknown>) =>
  describeActivity(anEntry({ id: 'x', type, changes: { taskTitle: 'Docs', ...changes } }), t);

describe('describeActivity', () => {
  it('translates status values of a move', () => {
    expect(describeType('TaskMoved', { from: 'ToDo', to: 'Done' }).text).toBe(
      'activity.types.TaskMoved[actor=Ana,from=tasks.statuses.ToDo,to=tasks.statuses.Done,task=Docs]',
    );
  });

  it('cuts the sentence around the task title so it can be a link', () => {
    const view = describeType('TaskDeleted', {});

    expect(view.aroundTask).toEqual(['activity.types.TaskDeleted[actor=Ana,task=', ']']);
  });

  it('has no title to link when the entry has no task', () => {
    const view = describeActivity(
      anEntry({ id: 'x', type: 'LabelCreated', taskId: null, changes: { labelName: 'Bug', color: 'red' } }),
      t,
    );

    expect(view.text).toContain('label=Bug');
    expect(view.aroundTask).toBeNull();
  });

  it('picks assigned, reassigned or unassigned from the names present', () => {
    expect(describeType('TaskAssigned', { assigneeName: 'Bruno' }).text).toContain('TaskAssigned[');
    expect(describeType('TaskAssigned', { assigneeName: 'Bruno', previousAssigneeName: 'Carla' }).text).toContain('TaskReassigned[');
    expect(describeType('TaskAssigned', { previousAssigneeName: 'Carla' }).text).toContain('TaskUnassigned[');
  });

  it('translates roles of member entries', () => {
    expect(describeType('MemberRoleChanged', { memberName: 'Bruno', from: 'Member', to: 'Owner' }).text).toContain(
      'member=Bruno,from=members.roles.Member,to=members.roles.Owner',
    );
  });

  it('describes a due date that was set, changed or removed, also when a key is absent', () => {
    const due = (dueDate: unknown) => describeType('TaskUpdated', { dueDate }).details;

    expect(due({ to: '2026-11-20' })).toEqual(['activity.changes.dueDateSet[to=2026-11-20]']);
    expect(due({ from: '2026-11-20' })).toEqual(['activity.changes.dueDateCleared']);
    expect(due({ from: '2026-11-20', to: null })).toEqual(['activity.changes.dueDateCleared']);
    expect(due({ from: '2026-11-20', to: '2026-11-25' })).toEqual([
      'activity.changes.dueDate[from=2026-11-20,to=2026-11-25]',
    ]);
  });

  it('describes an estimate that was set, changed or removed, either side possibly null', () => {
    const estimate = (value: unknown) => describeType('TaskUpdated', { estimatedMinutes: value }).details;

    expect(estimate({ from: null, to: 150 })).toEqual(['activity.changes.estimateSet[to=2 h 30 min]']);
    expect(estimate({ to: 60 })).toEqual(['activity.changes.estimateSet[to=1 h]']);
    expect(estimate({ from: 60, to: 90 })).toEqual(['activity.changes.estimate[from=1 h,to=1 h 30 min]']);
    expect(estimate({ from: 60, to: null })).toEqual(['activity.changes.estimateCleared']);
  });

  it('lists only what an update changed', () => {
    const view = describeType('TaskUpdated', {
      title: { from: 'A', to: 'B' },
      priority: { from: 'Low', to: 'High' },
      dueDate: { from: null, to: '2026-12-01' },
      descriptionChanged: true,
      labelsAdded: ['Bug', 'UI'],
      labelsRemoved: ['Old'],
    });

    expect(view.details).toEqual([
      'activity.changes.title[from=A,to=B]',
      'activity.changes.priority[from=tasks.priorities.Low,to=tasks.priorities.High]',
      'activity.changes.dueDateSet[to=2026-12-01]',
      'activity.changes.description',
      'activity.changes.labelsAdded[labels=Bug, UI]',
      'activity.changes.labelsRemoved[labels=Old]',
    ]);
    expect(describeType('TaskUpdated', { priority: { from: 'Low', to: 'High' } }).details).toHaveLength(1);
  });

  it('falls back to a generic sentence for an unknown type', () => {
    const view = describeType('SomethingNew', { foo: 1 });

    expect(view.text).toBe('activity.generic[actor=Ana,type=SomethingNew,task=Docs]');
    expect(view.details).toEqual([]);
  });
});
