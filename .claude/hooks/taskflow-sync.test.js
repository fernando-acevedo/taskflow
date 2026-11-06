// Run with: node --test .claude/hooks
"use strict";
const test = require("node:test");
const assert = require("node:assert");
const fs = require("node:fs");
const os = require("node:os");
const http = require("node:http");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const sync = require("./taskflow-sync");

const hook = path.join(__dirname, "gantt-sync.js");
const SECRET = "tf_super-secret-value";

function server(status) {
  const calls = [];
  const srv = http.createServer((req, res) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      calls.push({ url: req.url, auth: req.headers.authorization, body: JSON.parse(body) });
      res.writeHead(status, { "Content-Type": "application/json" });
      res.end(JSON.stringify(status === 200
        ? { applied: true, items: [] }
        : { applied: false, items: [{ externalKey: "T-1", outcome: "Failed", errorCode: "Tasks.DependencyCycle" }] }));
    });
  });
  return new Promise((resolve) => srv.listen(0, () => resolve({ srv, calls, url: "http://127.0.0.1:" + srv.address().port })));
}

test("maps a Gantt task to a sync item", () => {
  const item = sync.toItem({ id: "T-5", title: "x".repeat(250), agent: "backend-dev", status: "bloqueado", start: "2026-11-01", end: "2026-11-03", deps: ["T-4"], notes: "waiting" });
  assert.equal(item.externalKey, "T-5");
  assert.equal(item.title.length, 200);
  assert.deepEqual(item.labels, ["agent:backend-dev"]);
  assert.equal(item.description, "x".repeat(250));
  assert.doesNotMatch(item.description, /Agente/);
  assert.deepEqual(sync.toItem({ id: "T-9", title: "t" }).labels, []);
  assert.equal(sync.toItem({ id: "T-9", title: "t", agent: "yo" }).description, null);
  assert.equal(item.isBlocked, true);
  assert.equal(item.status, undefined);
  assert.equal(item.statusNote, "waiting");
  assert.deepEqual([item.startDate, item.dueDate, item.dependsOn], ["2026-11-01", "2026-11-03", ["T-4"]]);
  assert.equal(sync.toItem({ id: "T-6", title: "t", status: "en_progreso" }).status, "InProgress");
  assert.equal(sync.toItem({ id: "T-7", title: "t", status: "hecho" }).status, "Done");
  assert.equal(sync.toItem({ id: "T-8", title: "t", status: "pendiente" }).status, "ToDo");
});

test("plan splits beyond 200 and sends dependencies in a second pass", () => {
  const items = Array.from({ length: 250 }, (_, i) => ({ externalKey: "T-" + i, title: "t", dependsOn: ["T-0"] }));
  const batches = sync.plan(items, 200);
  assert.equal(batches.length, 4);
  assert.ok(batches.slice(0, 2).flat().every((i) => !("dependsOn" in i)));
  assert.ok(batches.slice(2).flat().every((i) => Array.isArray(i.dependsOn)));
  assert.equal(sync.plan(items.slice(0, 200), 200).length, 1);
});

test("post sends the bearer key and reports failures without the key", async () => {
  const ok = await server(200);
  const good = await sync.post({ url: ok.url, key: SECRET, project: "p1" }, [{ externalKey: "T-1", title: "t" }]);
  assert.equal(good.ok, true);
  assert.equal(ok.calls[0].url, "/api/projects/p1/tasks/sync");
  assert.equal(ok.calls[0].auth, "Bearer " + SECRET);
  ok.srv.close();

  const bad = await server(422);
  const failed = await sync.post({ url: bad.url, key: SECRET, project: "p1" }, [{ externalKey: "T-1", title: "t" }]);
  assert.equal(failed.ok, false);
  assert.match(failed.detail, /T-1: Tasks.DependencyCycle/);
  assert.ok(!JSON.stringify(failed).includes(SECRET));
  bad.srv.close();

  const down = await sync.post({ url: "http://127.0.0.1:1", key: SECRET, project: "p1" }, []);
  assert.equal(down.ok, false);
  assert.ok(!JSON.stringify(down).includes(SECRET));
});

test("the hook dry run prints the payload, not the key, and sends nothing", () => {
  const input = JSON.stringify({ last_assistant_message: 'Gantt: {"id":"T-85","status":"hecho","notes":"ok"}' });
  const env = { ...process.env, TASKFLOW_URL: "http://127.0.0.1:1", TASKFLOW_API_KEY: SECRET, TASKFLOW_PROJECT: "p1" };
  const run = spawnSync(process.execPath, [hook, "stop", "--dry-run"], { input, env, encoding: "utf8" });
  assert.equal(run.status, 0);
  assert.ok(!run.stdout.includes(SECRET) && !run.stderr.includes(SECRET));
  const out = JSON.parse(run.stdout);
  assert.equal(out.payload.items[0].externalKey, "T-85");
  assert.equal(out.payload.items[0].status, "Done");
  assert.equal(out.payload.items[0].statusNote, "ok");
});

test("the hook falls back to the local file when the API is down", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "gantt-"));
  fs.mkdirSync(path.join(root, "docs"));
  const seed = '{ version: 1, project: "t", example: false, tasks: [\n {"id":"T-1","title":"One","agent":"yo","status":"pendiente","start":"2026-11-01","end":"2026-11-02","deps":[],"notes":""}\n ] }';
  fs.writeFileSync(path.join(root, "docs", "panel-gantt.html"), "var SEED = " + seed + ";");
  const input = JSON.stringify({ last_assistant_message: 'Gantt: {"id":"T-1","status":"hecho","notes":"done"}' });
  const env = { ...process.env, CLAUDE_PROJECT_DIR: root, TASKFLOW_URL: "http://127.0.0.1:1", TASKFLOW_API_KEY: SECRET, TASKFLOW_PROJECT: "p1" };
  const run = spawnSync(process.execPath, [hook, "stop"], { input, env, encoding: "utf8" });
  assert.equal(run.status, 0);
  assert.match(run.stdout, /TaskFlow fall/);
  assert.ok(!run.stdout.includes(SECRET));
  assert.match(fs.readFileSync(path.join(root, "docs", "gantt-updates.js"), "utf8"), /"id":"T-1","status":"hecho"/);
  fs.rmSync(root, { recursive: true, force: true });
});

test("rejects an http: TASKFLOW_URL unless it is localhost or 127.0.0.1", () => {
  const env = (url) => ({ TASKFLOW_URL: url, TASKFLOW_API_KEY: SECRET, TASKFLOW_PROJECT: "p" });
  assert.equal(sync.config(env("http://taskflow.example.com")), null);
  assert.equal(sync.insecureUrl(env("http://taskflow.example.com")), true);
  assert.notEqual(sync.config(env("https://taskflow.example.com")), null);
  assert.notEqual(sync.config(env("http://localhost:5080")), null);
  assert.notEqual(sync.config(env("http://127.0.0.1:5080")), null);
});
