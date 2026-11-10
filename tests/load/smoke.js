// k6 load smoke for the TaskFlow API (T-128). Not part of CI; see README.md.
//
//   k6 run -e TF_API=http://localhost:5090 -e TF_EMAIL=admin@taskflow.load -e TF_PASSWORD=... tests/load/smoke.js
//   k6 run -e TF_PROFILE=board ... tests/load/smoke.js
//
// Environment: TF_API (default http://localhost:5080), TF_EMAIL, TF_PASSWORD (required), TF_TENANT (optional tenant id,
// sent as X-Tenant), TF_PROFILE (smooth | board, default smooth), TF_VUS, TF_DURATION, TF_BOARD_TASKS,
// TF_PROJECT_ID (reuse a project instead of creating a client and a project; the user needs Work access).
import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const API = (__ENV.TF_API || 'http://localhost:5080').replace(/\/$/, '');
const PROFILE = __ENV.TF_PROFILE || 'smooth';
const BOARD_TASKS = Number(__ENV.TF_BOARD_TASKS || 12);
const CLIENT_ID = `k6-${Math.random().toString(36).slice(2, 10)}`;

// 409 (stale rowversion) and 412 (If-Match mismatch) are the expected answer to a concurrent edit: they are not errors.
const expectedConflict = http.expectedStatuses({ min: 200, max: 399 }, 409, 412);

const conflicts = new Counter('tf_conflicts');
const columnPositionsValid = new Rate('tf_column_positions_valid');

const scenarios = {
  smooth: {
    executor: 'constant-vus',
    exec: 'mixed',
    vus: Number(__ENV.TF_VUS || 10),
    duration: __ENV.TF_DURATION || '1m',
  },
  board: {
    // Several VUs moving tasks of the same column at once; teardown checks positions are 0..n-1.
    executor: 'constant-vus',
    exec: 'boardContention',
    vus: Number(__ENV.TF_VUS || 8),
    duration: __ENV.TF_DURATION || '30s',
  },
};

export const options = {
  scenarios: { [PROFILE]: scenarios[PROFILE] || fail(`Unknown TF_PROFILE "${PROFILE}" (smooth | board)`) },
  thresholds: {
    'http_req_duration{kind:read}': ['p(95)<500'],
    'http_req_duration{kind:write}': ['p(95)<800'],
    http_req_failed: ['rate<0.01'],
    ...(PROFILE === 'board' ? { tf_column_positions_valid: ['rate==1'] } : {}),
  },
};

function headers(token, extra) {
  const h = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
    'X-Client-Id': CLIENT_ID,
    'Accept-Language': 'en',
  };
  if (__ENV.TF_TENANT) h['X-Tenant'] = __ENV.TF_TENANT;
  return Object.assign(h, extra || {});
}

function read(token, url, name) {
  return http.get(`${API}${url}`, { headers: headers(token), tags: { kind: 'read', name } });
}

function write(token, method, url, body, name, extraHeaders) {
  return http.request(method, `${API}${url}`, body === null ? null : JSON.stringify(body), {
    headers: headers(token, extraHeaders),
    tags: { kind: 'write', name },
    responseCallback: expectedConflict,
  });
}

const etag = (rowVersion) => `"${rowVersion}"`;

function noteConflict(res) {
  if (res.status === 409 || res.status === 412) conflicts.add(1);
}

export function setup() {
  const tenantHeader = __ENV.TF_TENANT ? { 'X-Tenant': __ENV.TF_TENANT } : {};
  const login = http.post(
    `${API}/api/auth/login`,
    JSON.stringify({ email: __ENV.TF_EMAIL, password: __ENV.TF_PASSWORD }),
    { headers: Object.assign({ 'Content-Type': 'application/json' }, tenantHeader), tags: { name: 'login' } },
  );
  if (login.status !== 200) fail(`Login failed (${login.status}): check TF_EMAIL / TF_PASSWORD / TF_API. ${login.body}`);
  // One login: /api/auth is limited per IP (10 a minute by default), so the VUs share this token.
  const token = login.json('accessToken');

  let projectId = __ENV.TF_PROJECT_ID;
  if (!projectId) {
    const client = http.post(`${API}/api/clients`, JSON.stringify({ name: `k6 ${CLIENT_ID}` }), { headers: headers(token) });
    if (client.status !== 201) fail(`Could not create a client (${client.status}); the user must be a tenant admin. ${client.body}`);
    const project = http.post(
      `${API}/api/projects`,
      JSON.stringify({ clientId: client.json('id'), name: `k6 load ${CLIENT_ID}` }),
      { headers: headers(token) },
    );
    if (project.status !== 201) fail(`Could not create a project (${project.status}). ${project.body}`);
    projectId = project.json('id');
  }

  const data = { token, projectId, taskIds: [] };
  if (PROFILE === 'board') {
    for (let i = 0; i < BOARD_TASKS; i++) {
      const task = http.post(`${API}/api/projects/${projectId}/tasks`, JSON.stringify({ title: `board ${i}` }), { headers: headers(token) });
      if (task.status !== 201) fail(`Could not create a task (${task.status}). ${task.body}`);
      data.taskIds.push(task.json('id'));
    }
  }
  return data;
}

// Smooth profile: browse, create a task, move it twice, comment, read the activity with a cursor.
export function mixed(data) {
  const { token, projectId } = data;
  const base = `/api/projects/${projectId}`;

  check(read(token, '/api/projects?pageSize=20', 'GET /api/projects'), { 'projects 200': (r) => r.status === 200 });
  check(read(token, `${base}/tasks`, 'GET /tasks'), { 'tasks 200': (r) => r.status === 200 });

  const created = write(token, 'POST', `${base}/tasks`, { title: `k6 task ${__VU}-${__ITER}`, priority: 'Medium' }, 'POST /tasks');
  if (!check(created, { 'task created 201': (r) => r.status === 201 })) return;
  const taskId = created.json('id');
  let rowVersion = created.json('rowVersion');

  for (const status of ['InProgress', 'Review']) {
    const moved = write(token, 'PUT', `${base}/tasks/${taskId}/position`, { status, position: 0 }, 'PUT /tasks/{id}/position', {
      'If-Match': etag(rowVersion),
    });
    noteConflict(moved);
    if (moved.status !== 200) break; // a stale version: the next iteration starts again with a fresh task
    // The answer is { task, reordered }; the task carries the new rowversion for the next If-Match.
    rowVersion = moved.json('task.rowVersion');
  }

  const comment = write(token, 'POST', `${base}/tasks/${taskId}/comments`, { body: `k6 comment ${__ITER}` }, 'POST /comments');
  check(comment, { 'comment 200': (r) => r.status === 200 });

  // Activity with a cursor: first page, then the next one when there is one.
  const first = read(token, `${base}/activity?pageSize=10`, 'GET /activity');
  if (check(first, { 'activity 200': (r) => r.status === 200 })) {
    const next = first.json('nextCursor');
    if (next) {
      const second = read(token, `${base}/activity?pageSize=10&cursor=${encodeURIComponent(next)}`, 'GET /activity?cursor');
      check(second, { 'activity page 2 200': (r) => r.status === 200 });
    }
  }
  check(read(token, `${base}/tasks/${taskId}/activity?pageSize=10`, 'GET /tasks/{id}/activity'), { 'task activity 200': (r) => r.status === 200 });

  sleep(0.5 + Math.random());
}

// Board profile: every VU drops random tasks at random positions of the same column (To Do).
export function boardContention(data) {
  const { token, projectId, taskIds } = data;
  const base = `/api/projects/${projectId}/tasks`;
  const taskId = taskIds[Math.floor(Math.random() * taskIds.length)];

  const current = read(token, `${base}/${taskId}`, 'GET /tasks/{id}');
  if (!check(current, { 'task 200': (r) => r.status === 200 })) return;

  const moved = write(token, 'PUT', `${base}/${taskId}/position`,
    { status: 'ToDo', position: Math.floor(Math.random() * taskIds.length) }, 'PUT /tasks/{id}/position',
    { 'If-Match': current.headers['Etag'] || etag(current.json('rowVersion')) });
  noteConflict(moved);
  check(moved, { 'move 200/409/412': (r) => [200, 409, 412].includes(r.status) });
  sleep(0.05);
}

export function teardown(data) {
  if (PROFILE !== 'board') return;
  const res = http.get(`${API}/api/projects/${data.projectId}/tasks?status=ToDo`, { headers: headers(data.token) });
  const positions = res.status === 200 ? res.json().map((t) => t.position).sort((a, b) => a - b) : [];
  const valid = res.status === 200 && positions.length === data.taskIds.length && positions.every((p, i) => p === i);
  columnPositionsValid.add(valid);
  if (valid) console.log(`Column positions are 0..${positions.length - 1} without duplicates.`);
  else console.error(`INVALID column positions (status ${res.status}): [${positions.join(', ')}], expected 0..${data.taskIds.length - 1}`);
}

