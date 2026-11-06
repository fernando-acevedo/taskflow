// Sends Gantt tasks to a TaskFlow project with POST /api/projects/{id}/tasks/sync (ADR 0016, 0017). Shared by
// .claude/hooks/gantt-sync.js (one task per agent update) and .claude/skills/dev-flow/gantt-import.js (the whole seed).
//
// Enabled only when TASKFLOW_URL, TASKFLOW_API_KEY (a Write key `tf_...`) and TASKFLOW_PROJECT (the project id) are all
// set. The key is read from the environment and never printed or put in an error message.
"use strict";
const fs = require("fs");
const path = require("path");

const MAX_BATCH = 200; // the API's limit per sync
const TITLE_MAX = 200;
const DESCRIPTION_MAX = 4000;
const NOTE_MAX = 500;
const ALIAS = { done: "hecho", in_progress: "en_progreso", blocked: "bloqueado", pending: "pendiente", todo: "pendiente" };
// Gantt state -> Kanban column. "bloqueado" is a flag (D9), not a column: it keeps the column the task is in.
const COLUMN = { pendiente: "ToDo", en_progreso: "InProgress", hecho: "Done" };

// The key would travel in clear over http:, so only localhost / 127.0.0.1 may use it.
function insecureUrl(env = process.env) {
  const url = (env.TASKFLOW_URL || "").trim();
  if (!/^http:/i.test(url)) return false;
  try {
    const host = new URL(url).hostname;
    return host !== "localhost" && host !== "127.0.0.1";
  } catch {
    return true;
  }
}

function config(env = process.env) {
  if (insecureUrl(env)) return null;
  const url = (env.TASKFLOW_URL || "").trim().replace(/\/+$/, "");
  const key = (env.TASKFLOW_API_KEY || "").trim();
  const project = (env.TASKFLOW_PROJECT || "").trim();
  return url && key && project ? { url, key, project } : null;
}

// Same parsing as .claude/skills/dev-flow/gantt.js: the SEED literal of docs/panel-gantt.html.
function loadSeed(root) {
  const html = fs.readFileSync(path.join(root, "docs", "panel-gantt.html"), "utf8");
  const start = html.indexOf("var SEED = ") + "var SEED = ".length;
  const end = html.indexOf("] }", start) + 3;
  if (start < "var SEED = ".length || end < 3) throw new Error("SEED not found in docs/panel-gantt.html");
  return Function("return " + html.slice(start, end))();
}

const clip = (text, max) => (text.length > max ? text.slice(0, max - 1) + "…" : text);

// A Gantt task as an item of the sync request. Every field is sent because the API replaces what is stored; the status
// is omitted for "bloqueado" so the task stays in its column. D8: the agent is the label "agent:<agent>"; the description
// only carries the full title when the Kanban title is cut at 200 characters.
function toItem(task) {
  const status = ALIAS[task.status] || task.status || "pendiente";
  const title = String(task.title || task.id);
  const description = title.length > TITLE_MAX ? title : "";
  const item = {
    externalKey: task.id,
    title: clip(title, TITLE_MAX),
    description: clip(description, DESCRIPTION_MAX),
    isBlocked: status === "bloqueado",
    statusNote: clip(String(task.notes ?? task.note ?? ""), NOTE_MAX),
    dependsOn: Array.isArray(task.deps) ? task.deps : [],
    labels: task.agent ? [`agent:${task.agent}`] : [],
  };
  if (!item.description) item.description = null;
  if (!item.statusNote) item.statusNote = null;
  if (task.start) item.startDate = task.start;
  if (task.end) item.dueDate = task.end;
  if (COLUMN[status]) item.status = COLUMN[status];
  return item;
}

// Groups of at most `size` items. Beyond one batch, dependencies could point at a task of a later batch, so the tasks are
// sent first without dependencies (omitted means "keep") and then again with them.
function plan(items, size = MAX_BATCH) {
  const chunk = (list) => {
    const out = [];
    for (let i = 0; i < list.length; i += size) out.push(list.slice(i, i + size));
    return out;
  };
  if (items.length <= size) return chunk(items);
  const bare = items.map(({ dependsOn, ...rest }) => rest);
  return [...chunk(bare), ...chunk(items)];
}

// Posts one batch. Returns { ok, status, detail } and never throws or includes the key.
async function post(cfg, items, timeoutMs = 8000) {
  const url = `${cfg.url}/api/projects/${encodeURIComponent(cfg.project)}/tasks/sync`;
  try {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${cfg.key}` },
      body: JSON.stringify({ items }),
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (response.ok) return { ok: true, status: response.status, detail: `${items.length} tasks` };
    let detail = "";
    try {
      const body = await response.json();
      const failed = (body.items || []).filter((i) => i.outcome === "Failed").map((i) => `${i.externalKey}: ${i.errorCode}`);
      detail = failed.length ? failed.join("; ") : body.extensions?.code || body.title || "";
    } catch { /* no JSON body */ }
    return { ok: false, status: response.status, detail: String(detail).slice(0, 300) };
  } catch (error) {
    return { ok: false, status: 0, detail: error && error.name === "TimeoutError" ? "timeout" : "network error" };
  }
}

module.exports = { MAX_BATCH, config, insecureUrl, loadSeed, toItem, plan, post, ALIAS };
