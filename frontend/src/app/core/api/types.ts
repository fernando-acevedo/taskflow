import type { components } from './schema';

/**
 * Readable aliases over the types generated from the backend's OpenAPI contract (`schema.d.ts`, see
 * `npm run api:types`). API services use these instead of hand-written DTO interfaces; a front-end type that is
 * narrower on purpose derives from them with `Pick`/`Omit`.
 */
export type Schemas = components['schemas'];

export type ClientDto = Schemas['ClientDto'];
export type ClientOptionDto = Schemas['ClientOptionDto'];
export type LabelDto = Schemas['LabelDto'];
export type ProjectDto = Schemas['ProjectDto'];
export type ProjectMemberDto = Schemas['ProjectMemberDto'];
export type ProjectRole = Schemas['ProjectRole'];
export type TaskDto = Schemas['TaskDto'];
export type TaskPositionDto = Schemas['TaskPositionDto'];
export type MoveTaskResultDto = Schemas['MoveTaskResultDto'];
export type TaskItemStatus = Schemas['TaskItemStatus'];
export type TaskPriority = Schemas['TaskPriority'];
export type TaskCommentDto = Schemas['TaskCommentDto'];
