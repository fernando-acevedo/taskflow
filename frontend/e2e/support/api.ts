import { randomUUID } from 'node:crypto';

/** The API under test (the same one the frontend was built against). */
export const apiUrl = (process.env.API_URL ?? 'http://localhost:5090').replace(/\/+$/, '');

export const admin = {
  email: process.env.E2E_ADMIN_EMAIL ?? 'admin@taskflow.local',
  password: process.env.E2E_ADMIN_PASSWORD ?? 'Admin12345',
};

/** A name that no other test (or earlier run) uses, so tests never depend on each other's data. */
export function unique(prefix: string): string {
  return `${prefix} ${randomUUID().slice(0, 8)}`;
}

export interface ApiTask {
  id: string;
  title: string;
  status: string;
  position: number;
}

/** Sets up test data through the HTTP API, signed in as the seeded admin (who owns what they create). */
export class Api {
  private constructor(private readonly token: string) {}

  static async signIn(): Promise<Api> {
    const response = await fetch(`${apiUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(admin),
    });
    if (!response.ok) {
      throw new Error(`Admin login failed (${response.status}). Is the API at ${apiUrl} seeded with ${admin.email}?`);
    }
    const body = (await response.json()) as { accessToken: string };
    return new Api(body.accessToken);
  }

  private async send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await fetch(`${apiUrl}${path}`, {
      method,
      headers: { Authorization: `Bearer ${this.token}`, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`${method} ${path} failed (${response.status}): ${await response.text()}`);
    }
    return (await response.json()) as T;
  }

  createClient(name = unique('E2E client')): Promise<{ id: string; name: string }> {
    return this.send('POST', '/api/clients', { name });
  }

  createProject(clientId: string, name = unique('E2E project')): Promise<{ id: string; name: string }> {
    return this.send('POST', '/api/projects', { clientId, name });
  }

  /** A client and a project in it. */
  async createProjectWithClient(): Promise<{ id: string; name: string; clientName: string }> {
    const client = await this.createClient();
    const project = await this.createProject(client.id);
    return { ...project, clientName: client.name };
  }

  createTask(projectId: string, title: string): Promise<ApiTask> {
    return this.send('POST', `/api/projects/${projectId}/tasks`, { title });
  }
}
