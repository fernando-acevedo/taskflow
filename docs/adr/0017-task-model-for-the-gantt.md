# 17. Tasks carry the fields the Gantt needs, and agents sync them by external key

- Status: Accepted
- Date: 2026-11-06

## Context

The Gantt panel plans work as tasks with start and end dates, dependencies, an agent in charge and a status note, and
it must be able to push its state into TaskFlow repeatedly without creating duplicates.

## Decision

- `ExternalKey`: an optional id chosen by the caller (`[A-Za-z0-9._:-]`, up to 100 characters), unique per
  project (filtered unique index). It is what makes the push idempotent.
- The agent in charge is a label (D8), for example `agent:backend-dev`; there is no service user.
- `StartDate` (optional, never after `DueDate`).
- `IsBlocked` is a flag on the task, not a fifth Kanban column (D9). Status stays To Do, In Progress, Review, Done.
- `StatusNote` is a text field of up to 500 characters (D10), not a comment, so the latest note is a plain read.
- `TaskDependency`: a task depends on other tasks of the same project; cycles are rejected in the domain, a task has
  at most 50 dependencies. Deleting a task deletes the dependencies on it.
- No Timeline view inside TaskFlow in this phase (D11); the fields are only exposed by the API.
- `PUT /api/projects/{id}/tasks/by-key/{externalKey}` creates or replaces the task with that key (200 when it
  existed, 201 when created). `POST /api/projects/{id}/tasks/sync` does the same for up to 200 items in one serializable
  transaction. Dependencies are given as external keys, resolved against the batch and the project. The batch is
  all-or-nothing: if any item fails, nothing is saved and the answer (422) lists the outcome of every item.
- Status changes go through the same code as the Kanban `MoveTask` (the task lands at the bottom of the target
  column, neighbors are renumbered, the activity entry is written). Upserts are last-write-wins: they carry no row
  version, unlike the editing endpoints.
- Sync does not apply the "due date not in the past" rule of task creation and does not send assignment notifications:
  it mirrors a plan, it is not a person adding work.

## Consequences

- Re-sending the same plan is safe.
- Three new columns and a new table on tasks; the migration is generated separately.
