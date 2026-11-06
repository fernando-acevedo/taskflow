# 10. Notifications reach the browser by polling, not SignalR

- Status: Accepted
- Date: 2026-11-05

## Context

Users get an in-app notification when a task is assigned to them, when they are mentioned in a comment, when someone
comments on a task they are assigned to, and when a task of theirs is due today or tomorrow. The bell in the
frontend has to show the unread count and the list soon after the event. Two ways to deliver it:

- **SignalR (WebSockets)**: the server pushes the event at once. It needs a hub, authentication of the socket with the
  bearer token (the browser cannot set headers on a WebSocket, so the token goes in the query string), and, once
  there is more than one API instance, a backplane (Redis or Azure SignalR Service) or sticky sessions.
  The API runs on Azure App Service (phase 5) with no hub, no Redis and no sticky sessions today.
- **Polling**: the frontend asks `GET /api/notifications/unread-count` every so often and loads the list when the
  user opens the bell. It works over the plain bearer-token API that already exists, behind any number of instances,
  and needs nothing new to deploy.

## Decision

Polling. The notification is stored in the same transaction as the change that causes it (like the activity log,
ADR 0009), so the database is the source of truth and any instance serves it.

- The frontend polls `GET /api/notifications/unread-count` every **30 seconds** while the tab is visible and the
  user is signed in, pauses while the tab is hidden, refreshes once when the tab becomes visible again, and
  reloads the list when the bell opens. Marking as read refreshes the count at once.
- Cost: one request of a few bytes per open tab every 30 s. The query is a count over the index
  `(UserId, ReadAt)`, so it stays cheap as the table grows. 200 open tabs are about 7 requests per second. The
  worst latency of a notification is 30 s, which is fine for "someone assigned you a task".
- Email is separate from delivery to the browser. A user who turns on the preference gets an email right after
  the change is saved (`EmailOnAssigned`, `EmailOnMentioned`, `EmailOnCommentOnMyTask`) or one daily digest
  (`EmailDailyDigest`). A failed send is logged and never fails the operation.
- The due-soon notices come from a `BackgroundService` (`DueSoonNotificationService`) that runs
  `NotifyDueSoonHandler` once a day at `Notifications:DueSoonHourUtc` (default 8) and can be turned off with
  `Notifications:DueSoonEnabled`. The handler does not create a second TaskDueSoon for the same task, user and due
  date, so a restart, a second instance or a repeated run creates nothing new. If the app starts after the hour it
  runs at once for that day. With several instances every one runs the job and the handler stays correct, but a
  race between two runs at the same moment could create a duplicate; if that ever matters, a unique index on a
  stored due date or a lock decides which instance runs it.

### The contract

- `Notification`: `id`, `type`, `projectId`, `taskId` (null when the task has none; may name a deleted task),
  `actorId` (null for the system), `data`, `createdAt`, `readAt` (null while unread).
- `type` is `TaskAssigned`, `Mentioned`, `CommentOnYourTask` or `TaskDueSoon`.
- `data` stores the values for the sentence as they were: `taskTitle` and `projectName` for every type, `actorName`
  for the first three, `commentId` for `Mentioned` and `CommentOnYourTask`, and `dueDate` (`yyyy-MM-dd`) for
  `TaskDueSoon`.
- Nobody is notified about their own action, and an assignee who is also mentioned in a comment gets only
  `Mentioned` for it. Editing a comment notifies only the people the edit adds.

### Details worth knowing

- **Links.** The task link in emails and in the bell is `/projects/{p}/board?task={t}`, a query parameter and not
  the fragment. The fragment rule in `LinkEmail.FrontendLink` is for tokens; this link carries only ids that are
  not secret, and the same URL works in the app (the bell) and in the email.
- **Frontend state.** The `notifications` state and effects are registered at the root (like auth) because the bell
  lives in the shell, which is always present, not in a lazy route.
- **Visibility.** The list and the unread count only include notifications of projects the user is still a member
  of, so the badge matches the list and nothing leaks after leaving a project.
- **Known, low-risk items left as they are.** Two instances running the due-soon job at the same moment could
  create a duplicate `TaskDueSoon`. Emails are sent inline after the commit, so a slow SMTP server delays the
  response (a failure is only logged).

## Consequences

- No new infrastructure and nothing sticky: it works on one App Service instance or ten. Notifications are not
  instant (up to 30 s) and an idle open tab keeps asking.
- **Moving to SignalR later** does not change the data. Add a `NotificationsHub` and have `Notifier` publish the
  new notification to `Clients.User(userId)` after `SaveChangesAsync` (through a port in Application, like
  `INotificationEmailSender`, so Application does not reference SignalR); authenticate the socket with the access
  token in the query string; add Azure SignalR Service or a Redis backplane before scaling out. The frontend
  keeps the REST endpoints for the list and for the first load, replaces the timer with the hub connection, and can
  keep a slow poll (for example every 5 minutes) as a fallback when the socket drops.
- The table only grows. Retention can delete read notifications older than some months.
