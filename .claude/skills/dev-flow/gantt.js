#!/usr/bin/env node
// Gantt helper for /dev-flow. Reads and writes the SEED of docs/panel-gantt.html, which is the source of truth
// for the published panel (its viewers keep their own copy in localStorage, so a change needs a KEY bump + republish).
//
//   node gantt.js phases                 phases with pending work, what blocks them and which can run in parallel
//   node gantt.js tasks F7 [F8 ...]      pending tasks of those phases, in dependency order ("Fase5" for a roadmap phase)
//   node gantt.js next-id                next free T-xx (ids are never reused)
//   node gantt.js set '<json>'           merge by id: {"id":"T-59","status":"hecho","notes":"..."} or an array of them;
//                                        a new id needs title, agent, start, end (and deps)
//   node gantt.js bump                   bump the panel KEY so viewers reload the seed (do it once before republishing)
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "../../..");
const htmlPath = path.join(root, "docs/panel-gantt.html");
const STATUSES = ["pendiente", "en_progreso", "bloqueado", "hecho"];
const ALIAS = { done: "hecho", in_progress: "en_progreso", blocked: "bloqueado", pending: "pendiente" };

function load() {
  const html = fs.readFileSync(htmlPath, "utf8");
  const start = html.indexOf("var SEED = ") + "var SEED = ".length;
  const end = html.indexOf("] }", start) + 3;
  if (start < "var SEED = ".length || end < 3) throw new Error("SEED not found in docs/panel-gantt.html");
  const seed = Function("return " + html.slice(start, end))();
  return { html, start, end, seed };
}

function save({ html, start, end, seed }) {
  const tasks = [...seed.tasks].sort((a, b) => num(a.id) - num(b.id));
  const text = "{ version: " + seed.version + ", project: " + JSON.stringify(seed.project) + ", example: false, tasks: [\n" +
    tasks.map((t) => "    " + JSON.stringify(t)).join(",\n") + "\n  ] }";
  fs.writeFileSync(htmlPath, html.slice(0, start) + text + html.slice(end));
}

const num = (id) => +String(id).replace(/^T-/, "");

// Features are "F7 Multitenant: ..." -> F7; roadmap phases are "Fase 5 Primer deploy ..." -> "Fase 5" (F5 is a feature).
function phaseOf(t) {
  const f = /^F(\d+)\b/.exec(t.title);
  if (f) return "F" + f[1];
  const p = /^Fase\s+(\d+)\b/i.exec(t.title);
  return p ? "Fase " + p[1] : "?";
}
const normPhase = (p) => (/^fase/i.test(p) ? "Fase " + String(p).replace(/^fase\s*/i, "") : "F" + String(p).replace(/^f/i, ""));
const manual = (t) => t.agent === "yo";

function phases(seed) {
  const byId = new Map(seed.tasks.map((t) => [t.id, t]));
  const groups = new Map();
  for (const t of seed.tasks) {
    const p = phaseOf(t);
    if (!groups.has(p)) groups.set(p, []);
    groups.get(p).push(t);
  }
  const out = [];
  for (const [p, tasks] of groups) {
    const pending = tasks.filter((t) => t.status !== "hecho");
    if (pending.length === 0) continue;
    const external = new Set();
    for (const t of pending) for (const d of t.deps || []) if (byId.get(d) && phaseOf(byId.get(d)) !== p) external.add(d);
    const waitingOn = [...external].filter((d) => byId.get(d).status !== "hecho");
    const name = (/^(?:F\d+|Fase\s+\d+)\s+([^:]+)/i.exec(pending[0].title) || [, ""])[1].trim();
    out.push({
      phase: p,
      name,
      pending: pending.map((t) => t.id),
      manual: pending.filter(manual).map((t) => t.id),
      waitingOn: waitingOn.map((d) => d + " (" + phaseOf(byId.get(d)) + ", " + byId.get(d).status + ")"),
      dependsOnPhases: [...new Set(waitingOn.map((d) => phaseOf(byId.get(d))))],
      migration: pending.some((t) => t.agent === "db-migrator"),
    });
  }
  return out.sort((a, b) => a.phase.localeCompare(b.phase, undefined, { numeric: true }));
}

function printPhases(seed) {
  const list = phases(seed);
  for (const p of list) {
    const state = p.manual.length === p.pending.length ? "manual (tuya)" : p.waitingOn.length ? "espera a " + p.dependsOnPhases.join(", ") : "lista";
    console.log(`${p.phase} ${p.name} | ${state} | pendientes ${p.pending.length}: ${p.pending.join(" ")}` +
      (p.manual.length ? ` | manuales: ${p.manual.join(" ")}` : "") +
      (p.waitingOn.length ? ` | bloquean: ${p.waitingOn.join(", ")}` : "") +
      (p.migration ? " | tiene migración" : ""));
  }
  // Two phases can run in parallel when neither waits on the other (directly or through a chain).
  const deps = new Map(list.map((p) => [p.phase, new Set(p.dependsOnPhases)]));
  const reaches = (a, b, seen = new Set()) => {
    if (seen.has(a)) return false;
    seen.add(a);
    for (const d of deps.get(a) || []) if (d === b || reaches(d, b, seen)) return true;
    return false;
  };
  const auto = list.filter((p) => p.manual.length < p.pending.length).map((p) => p.phase);
  const pairs = [];
  for (let i = 0; i < auto.length; i++)
    for (let j = i + 1; j < auto.length; j++)
      if (!reaches(auto[i], auto[j]) && !reaches(auto[j], auto[i])) pairs.push(auto[i] + " ∥ " + auto[j]);
  console.log("paralelo posible: " + (pairs.length ? pairs.join(", ") : "ninguno"));
}

function printTasks(seed, wanted) {
  const set = new Set(wanted.map(normPhase));
  const tasks = seed.tasks.filter((t) => set.has(phaseOf(t)) && t.status !== "hecho");
  const done = new Set(seed.tasks.filter((t) => t.status === "hecho").map((t) => t.id));
  const ordered = [];
  const left = new Set(tasks.map((t) => t.id));
  while (left.size) {
    const ready = tasks.filter((t) => left.has(t.id) && (t.deps || []).every((d) => done.has(d) || !left.has(d) && ordered.some((o) => o.id === d) || !seed.tasks.some((x) => x.id === d)));
    const batch = ready.length ? ready : tasks.filter((t) => left.has(t.id)); // external deps not done: list them anyway
    for (const t of batch) { left.delete(t.id); ordered.push(t); done.add(t.id); }
  }
  for (const t of ordered)
    console.log(`${t.id} [${phaseOf(t)}] ${t.agent} | ${t.status} | deps ${(t.deps || []).join(",") || "-"} | ${t.title}${t.notes ? " | " + t.notes : ""}`);
}

function set(seed, arg) {
  const items = [].concat(JSON.parse(arg));
  const byId = new Map(seed.tasks.map((t) => [t.id, t]));
  for (const u of items) {
    if (!/^T-\d+$/.test(u.id || "")) throw new Error("bad id: " + u.id);
    let t = byId.get(u.id);
    if (!t) {
      for (const f of ["title", "agent", "start", "end"]) if (!u[f]) throw new Error(u.id + " is new: it needs " + f);
      t = { id: u.id, title: "", agent: "", status: "pendiente", start: "", end: "", deps: [], notes: "" };
      seed.tasks.push(t);
      byId.set(u.id, t);
    }
    for (const f of ["title", "agent", "start", "end", "deps", "notes"]) if (u[f] !== undefined) t[f] = u[f];
    if (u.note !== undefined && u.notes === undefined) t.notes = u.note;
    if (u.status) {
      const s = ALIAS[u.status] || u.status;
      if (!STATUSES.includes(s)) throw new Error("bad status: " + u.status);
      t.status = s;
    }
  }
  return items.length;
}

const [cmd, ...args] = process.argv.slice(2);
const state = load();
switch (cmd) {
  case "phases": printPhases(state.seed); break;
  case "tasks": printTasks(state.seed, args); break;
  case "next-id": console.log("T-" + (Math.max(...state.seed.tasks.map((t) => num(t.id))) + 1)); break;
  case "set": console.log("updated " + set(state.seed, args.join(" "))); save(state); break;
  case "bump": {
    const html = fs.readFileSync(htmlPath, "utf8");
    const m = /var KEY = "panel-gantt\.taskflow\.v(\d+)";/.exec(html);
    if (!m) throw new Error("KEY not found");
    fs.writeFileSync(htmlPath, html.replace(m[0], `var KEY = "panel-gantt.taskflow.v${+m[1] + 1}";`));
    console.log("KEY v" + (+m[1] + 1));
    break;
  }
  default: console.log("usage: node gantt.js phases | tasks F7 [F8] | next-id | set '<json>' | bump"); process.exitCode = 1;
}
