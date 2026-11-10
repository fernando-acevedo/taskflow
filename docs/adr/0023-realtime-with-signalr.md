# 23. Real-time updates with a SignalR hub

- Status: Accepted
- Date: 2026-11-10

## Context

Notifications are polled (ADR 0010), but a board that several people use at once goes stale until someone reloads.
Tasks, their order and their comments need to reach the other open tabs quickly.

## Decision

- **Hub.** SignalR at `/hubs/projects`, authenticated with the same bearer token as the API. WebSockets cannot send
  headers, so the token travels in the `access_token` query parameter and the tenant (the `X-Tenant` header, ADR 0013)
  in the `tenant` query parameter.
- **Groups.** The client calls `JoinProject(projectId)` and `LeaveProject(projectId)`. On joining, the server checks
  `ProjectAccess` at level View (404/403 as usual) and adds the connection to the group `project:{tenantId}:{projectId}`.
- **Events, server to client** (camelCase):
  - `taskChanged { projectId, task: TaskDto, originClientId }`
  - `taskDeleted { projectId, taskId, originClientId }`
  - `tasksReordered { projectId, tasks: TaskPositionDto[], originClientId }`
  - `commentsChanged { projectId, taskId, originClientId }`
- **Echo suppression.** The frontend sends `X-Client-Id` (a random id per tab) on every request. The server copies it
  into `originClientId`, so each tab ignores the events its own requests caused.
- **After the commit.** Events are published from domain events (ADR 0024) once the transaction has committed, so a
  client never hears about a change that was rolled back. Publishing is best effort.
- **Scale-out.** Azure SignalR Service is optional (`Azure:SignalR:ConnectionString`). Without it the connections live
  in the memory of one instance, so the API must run as a single instance or behind sticky sessions, and an event
  raised on one instance does not reach the clients connected to another. With the service, any number of instances
  work.
- **Fallback.** If the connection is down, the client refreshes the project when the tab becomes visible again.

## Consequences

- The board and comments update without reloading; notifications keep polling.
- Scaling beyond one instance needs Azure SignalR Service (or sticky sessions, with the limitation above).
- `X-Client-Id` is a hint, not an identity: a tab that omits it just sees its own events too.

## Implementation notes

- **Authentication.** The bearer handler of Identity reads the token from `access_token` only on `/hubs/projects` (`BearerTokenEvents.OnMessageReceived`). The session check and the tenant middleware run as for any request; on the hub path the tenant comes from the `tenant` query parameter. The middleware keeps the resolved tenant in `HttpContext.Items`, the hub copies it to the connection and sets it again, with the connection's user, in the scope of each hub call. API keys cannot connect (negotiate answers 403, and the hub aborts a key's connection).
- **Rate limit.** The global limiter charges HTTP requests: negotiate, the WebSocket upgrade and each long-polling request. A message on an open WebSocket is not an HTTP request, so it never consumes the token bucket and cannot cut the connection.
- **CORS and CSP.** The CORS policy adds `AllowCredentials` (the browser client negotiates with credentials; the origins are an explicit list). `X-Client-Id` is covered by `AllowAnyHeader`. The API's CSP is a response header and does not affect WebSockets.
- **CORS at startup.** `Cors:AllowedOrigins` must list explicit origins: an empty list, a blank entry or a `*` makes the app fail to start with a clear message, because the policy allows credentials.
- **The token in the query string.** A WebSocket cannot send headers, so `access_token` travels in the URL. Nothing in the app records it: Serilog's request log writes the path only and `Microsoft.AspNetCore` logs are at Warning (so the "Request starting" line, which includes the query, is off); OpenTelemetry's `url.query` (and `http.target`/`url.full`) of `/hubs/` requests is replaced with `REDACTED` (`TelemetryRedaction`, tested). What the app cannot control: App Service HTTP logs, the front door and any proxy may log the full URL. The mitigation is that the token is short-lived (the Identity bearer token, one hour by default) and that it only opens this hub; keep those platform logs restricted and not exported.
- **Losing access.** The hub registers each connection (`HubConnections`: person, tenant, joined projects, per instance). `IRealtimeRevoker`, called by the use cases after their commit, takes a removed project member's connections out of the project group, and closes a person's connections in the tenant when their role changes or they are removed (they reconnect and join again, so access is checked anew; a deactivated user cannot, the session check rejects them) and all of them when the user is deactivated. The hub is mapped with `CloseOnAuthenticationExpiration`, so a connection closes when its token expires and the client reconnects with a renewed one. With Azure SignalR Service and several instances the registry only knows the connections of its own instance; for the others the bound is the token lifetime.
