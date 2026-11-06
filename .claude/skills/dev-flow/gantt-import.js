#!/usr/bin/env node
// Initial import: sends the whole SEED of docs/panel-gantt.html to a TaskFlow project with POST .../tasks/sync, in batches
// of at most 200 (the API's limit). Safe to repeat: tasks are matched by externalKey (the T-xx id) and updated in place.
//
//   TASKFLOW_URL=https://... TASKFLOW_API_KEY=tf_... TASKFLOW_PROJECT=<project id> node gantt-import.js
//   node gantt-import.js --dry-run            prints the payload of every batch and sends nothing (needs no variables)
//   node gantt-import.js --batch-size 50      smaller batches (default and maximum 200)
//
// The API key (a Write key) is read from the environment and never printed.
"use strict";
const path = require("path");
const sync = require("../../hooks/taskflow-sync");

async function main(argv) {
  const dryRun = argv.includes("--dry-run");
  const sizeAt = argv.indexOf("--batch-size");
  const size = sizeAt >= 0 ? Number(argv[sizeAt + 1]) : sync.MAX_BATCH;
  if (!Number.isInteger(size) || size < 1 || size > sync.MAX_BATCH) throw new Error("--batch-size must be between 1 and " + sync.MAX_BATCH);

  const root = path.resolve(__dirname, "../../..");
  const items = sync.loadSeed(root).tasks.map(sync.toItem);
  const batches = sync.plan(items, size);

  if (dryRun) {
    batches.forEach((batch, index) => console.log(JSON.stringify({ batch: index + 1, of: batches.length, items: batch }, null, 2)));
    console.error(`dry run: ${items.length} tasks in ${batches.length} batch(es), nothing sent`);
    return 0;
  }

  const cfg = sync.config();
  if (!cfg) {
    console.error("Set TASKFLOW_URL, TASKFLOW_API_KEY and TASKFLOW_PROJECT (or use --dry-run).");
    return 2;
  }
  for (let i = 0; i < batches.length; i++) {
    const result = await sync.post(cfg, batches[i], 30000);
    console.log(`batch ${i + 1}/${batches.length}: ${result.ok ? "ok" : "failed HTTP " + result.status + " " + result.detail}`);
    if (!result.ok) return 1;
  }
  console.log(`imported ${items.length} tasks`);
  return 0;
}

main(process.argv.slice(2)).then((code) => { process.exitCode = code; }, (error) => {
  console.error(error.message);
  process.exitCode = 1;
});
