#!/usr/bin/env node
// Sincroniza el Panel Gantt con los subagentes, sin usar el modelo (0 tokens). Escribe en docs/gantt-updates.js,
// que docs/panel-gantt.html (abierto desde el disco) relee cada 5 s y fusiona por id.
//
//   node gantt-sync.js start   → hook PreToolUse del tool Agent: la tarea T-xx del encargo pasa a "en_progreso".
//   node gantt-sync.js stop    → hook SubagentStop: aplica la última línea `Gantt: {...}` del agente; si no hay,
//                                marca la tarea "bloqueado" para que la revises.
//
// Si están TASKFLOW_URL, TASKFLOW_API_KEY y TASKFLOW_PROJECT, además manda cada actualización a TaskFlow
// (POST /api/projects/{id}/tasks/sync, ver taskflow-sync.js). Si faltan o la API falla, todo sigue como antes
// (docs/gantt-updates.js). Con --dry-run no escribe ni manda nada: imprime el payload.
// Nunca falla en voz alta: ante cualquier error sale con 0 y no bloquea nada.
"use strict";
const fs = require("fs");
const path = require("path");
const taskflow = require("./taskflow-sync");

const STATUSES = ["pendiente", "en_progreso", "bloqueado", "hecho", "todo", "pending", "in_progress", "blocked", "done"];
const GANTT_RE = /Gantt:\s*(\{[^\n]*?\})(?=\s*(?:$|\n|`|"|\\n))/g;
const ID_RE = /\bT-\d{2,}\b/;
const DRY_RUN = process.argv.includes("--dry-run");

function collectStrings(value, out) {
  if (typeof value === "string") out.push(value);
  else if (Array.isArray(value)) value.forEach((v) => collectStrings(v, out));
  else if (value && typeof value === "object") Object.values(value).forEach((v) => collectStrings(v, out));
}

function lastGantt(texts) {
  let found = null;
  for (const text of texts) {
    for (const m of text.matchAll(GANTT_RE)) {
      try {
        const obj = JSON.parse(m[1]);
        // Descarta la plantilla del encargo ("status":"...") y lo que no tenga id o estado conocido
        if (obj && obj.id && STATUSES.includes(String(obj.status || "").toLowerCase())) found = obj;
      } catch { /* no es JSON válido */ }
    }
  }
  return found;
}

function transcriptLines(input) {
  const file = input.agent_transcript_path || input.transcript_path;
  if (!file || !fs.existsSync(file)) return [];
  return fs.readFileSync(file, "utf8").split("\n").filter(Boolean);
}

const rootOf = (input) => process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();

// Respaldo local (lo de siempre) y, si hay configuración, la misma actualización a TaskFlow.
async function write(input, update) {
  const root = rootOf(input);
  update.at = new Date().toISOString();
  if (DRY_RUN) { dryRun(root, update); return; }
  writeLocal(root, update);
  const remote = await sendToTaskFlow(root, update);
  process.stdout.write(JSON.stringify({ systemMessage: "Panel Gantt: " + update.id + " → " + update.status + remote }));
}

function writeLocal(root, update) {
  const file = path.join(root, "docs", "gantt-updates.js");
  if (!fs.existsSync(file)) {
    fs.writeFileSync(file, "// Generado por .claude/hooks/gantt-sync.js. Lo lee docs/panel-gantt.html abierto desde el disco.\n");
  }
  fs.appendFileSync(file, "(window.GANTT_UPDATES = window.GANTT_UPDATES || []).push(" + JSON.stringify(update) + ");\n");
}

// La tarea completa = la del SEED fusionada con la actualización (la API reemplaza todos los campos, así que hay que
// mandar título, fechas y deps). Una tarea que no está en el SEED y no trae título no se manda.
function buildItem(root, update) {
  let seedTask = null;
  try { seedTask = taskflow.loadSeed(root).tasks.find((t) => t.id === update.id) || null; } catch { /* sin SEED */ }
  if (!seedTask && !update.title) return null;
  const { at, ...fields } = update;
  const merged = Object.assign({}, seedTask, fields);
  if (fields.notes === undefined && fields.note !== undefined) merged.notes = fields.note;
  return taskflow.toItem(merged);
}

async function sendToTaskFlow(root, update) {
  const cfg = taskflow.config();
  if (!cfg) return "";
  const item = buildItem(root, update);
  if (!item) return " (TaskFlow: tarea desconocida, no enviada)";
  const result = await taskflow.post(cfg, [item]);
  return result.ok ? " (TaskFlow ok)" : " (TaskFlow falló: HTTP " + result.status + " " + result.detail + "; queda el respaldo local)";
}

function dryRun(root, update) {
  const cfg = taskflow.config();
  const item = buildItem(root, update);
  process.stdout.write(JSON.stringify({
    dryRun: true,
    taskflowConfigured: Boolean(cfg),
    endpoint: cfg ? cfg.url + "/api/projects/" + cfg.project + "/tasks/sync" : "(TASKFLOW_URL / TASKFLOW_API_KEY / TASKFLOW_PROJECT sin definir)",
    localUpdate: update,
    payload: item ? { items: [item] } : null,
  }, null, 2) + "\n");
}

async function onStart(input) {
  const ti = input.tool_input || {};
  const m = String(ti.prompt || "").match(ID_RE);
  if (!m) return;
  await write(input, { id: m[0], status: "en_progreso" });
}

async function onStop(input) {
  const lines = transcriptLines(input);
  const texts = [];
  if (typeof input.last_assistant_message === "string") texts.push(input.last_assistant_message);
  for (const line of lines) {
    if (!line.includes("Gantt:")) continue;
    try { collectStrings(JSON.parse(line), texts); } catch { texts.push(line); }
  }
  const update = lastGantt(texts);
  if (update) { await write(input, update); return; }

  // Sin línea Gantt: buscar la tarea en el encargo (primer T-xx del transcript) y dejarla para revisar
  let id = null;
  for (const line of lines) { const m = line.match(ID_RE); if (m) { id = m[0]; break; } }
  if (id) await write(input, { id: id, status: "bloqueado", notes: "El agente terminó sin línea Gantt: revisar su resumen." });
}

if (require.main === module) {
  let buf = "";
  process.stdin.setEncoding("utf8");
  process.stdin.on("data", (c) => (buf += c));
  process.stdin.on("end", async () => {
    try {
      let input = {};
      try { input = JSON.parse(buf || "{}"); } catch { /* entrada vacía */ }
      if (process.argv[2] === "start") await onStart(input); else await onStop(input);
    } catch { /* silencio */ }
    process.exit(0);
  });
}
