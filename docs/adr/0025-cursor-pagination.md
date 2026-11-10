# 25. Cursor pagination for activity and notifications

- Status: Accepted
- Date: 2026-11-10

## Context

Activity and notifications are feeds read newest first, and new items arrive while someone scrolls. Offset pages
repeat or skip items when that happens, and every request pays for a `COUNT` that nobody shows.

## Decision

- Both listings take `?cursor=<opaque>&pageSize=` and answer `{ items, nextCursor }`; `nextCursor` is null on the last
  page. `pageSize` is 1 to 100 (defaults unchanged: 50 for activity, 20 for notifications).
- The cursor is the base64url of the last item's timestamp (`OccurredAt` / `CreatedAt`, as UTC ticks) and its
  tiebreaker (`Sequence` / `Id`). The query is a keyset condition on `(timestamp, tiebreaker)` descending, which uses
  the existing indexes, and reads `pageSize + 1` rows to know whether there is a next page. There is no `COUNT`.
- An invalid cursor is a 400 validation error.
- This is a breaking change for those two listings (`page`, `totalCount` are gone), accepted because the frontend ships
  with the API. The other listings keep offset pagination (`PagedResult`).
- The unread count of notifications keeps its own endpoint.

## Consequences

- No repeats or gaps when items are added during paging.
- A client cannot jump to page N or show a total; feeds do not need to.
