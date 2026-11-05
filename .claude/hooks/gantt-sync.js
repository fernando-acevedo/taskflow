#!/usr/bin/env node
// Sincroniza el Panel Gantt con los subagentes, sin usar el modelo (0 tokens). Escribe en docs/gantt-updates.js,
// que docs/panel-gantt.html (abierto desde el disco) relee cada 5 s y fusiona por id.
//
//   node gantt-sync.js start   → hook PreToolUse del tool Agent: la tarea T-xx del encargo pasa a "en_progreso".
//   node gantt-sync.js stop    → hook SubagentStop: aplica la última línea `Gantt: {...}` del agente; si no hay,
//                                marca la tarea "bloqueado" para que la revises.
// Nunca falla en voz alta: ante cualquier error sale con 0 y no bloquea nada.
"use strict";
const fs = require("fs");
const path = require("path");

const STATUSES = ["pendiente", "en_progreso", "bloqueado", "hecho", "todo", "pending", "in_progress", "blocked", "done"];
const GANTT_RE = /Gantt:\s*(\{[^\n]*?\})(?=\s*(?:$|\n|`|"|\\n))/g;
const ID_RE = /\bT-\d{2,}\b/;

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

function write(input, update) {
  const root = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
  const file = path.join(root, "docs", "gantt-updates.js");
  update.at = new Date().toISOString();
  if (!fs.existsSync(file)) {
    fs.writeFileSync(file, "// Generado por .claude/hooks/gantt-sync.js. Lo lee docs/panel-gantt.html abierto desde el disco.\n");
  }
  fs.appendFileSync(file, "(window.GANTT_UPDATES = window.GANTT_UPDATES || []).push(" + JSON.stringify(update) + ");\n");
  process.stdout.write(JSON.stringify({ systemMessage: "Panel Gantt: " + update.id + " → " + update.status }));
}

function onStart(input) {
  const ti = input.tool_input || {};
  const m = String(ti.prompt || "").match(ID_RE);
  if (!m) return;
  write(input, { id: m[0], status: "en_progreso" });
}

function onStop(input) {
  const lines = transcriptLines(input);
  const texts = [];
  if (typeof input.last_assistant_message === "string") texts.push(input.last_assistant_message);
  for (const line of lines) {
    if (!line.includes("Gantt:")) continue;
    try { collectStrings(JSON.parse(line), texts); } catch { texts.push(line); }
  }
  const update = lastGantt(texts);
  if (update) { write(input, update); return; }

  // Sin línea Gantt: buscar la tarea en el encargo (primer T-xx del transcript) y dejarla para revisar
  let id = null;
  for (const line of lines) { const m = line.match(ID_RE); if (m) { id = m[0]; break; } }
  if (id) write(input, { id: id, status: "bloqueado", notes: "El agente terminó sin línea Gantt: revisar su resumen." });
}

let buf = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (c) => (buf += c));
process.stdin.on("end", () => {
  try {
    let input = {};
    try { input = JSON.parse(buf || "{}"); } catch { /* entrada vacía */ }
    if (process.argv[2] === "start") onStart(input); else onStop(input);
  } catch { /* silencio */ }
  process.exit(0);
});
