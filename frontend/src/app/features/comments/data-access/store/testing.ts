import { TaskComment } from '../comments-api.service';

/** A comment with sensible defaults for specs. */
export function aComment(overrides: Partial<TaskComment> & Pick<TaskComment, 'id'>): TaskComment {
  return {
    taskId: 't1',
    authorId: 'u1',
    authorName: 'Ana',
    body: `Comment ${overrides.id}`,
    createdAt: '2026-11-01T10:00:00Z',
    editedAt: null,
    isEdited: false,
    mentionedUserIds: [],
    ...overrides,
  };
}
