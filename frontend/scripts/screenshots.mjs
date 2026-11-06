// Takes the README screenshots into docs/screenshots (1440x900, light theme, installed Google Chrome).
// Run with the API (Demo__Enabled=true) and the frontend (`npm start`) already running, and a seeded demo:
//   npm run screenshots
// Env: FRONTEND_URL (default http://localhost:4200), API_URL (default http://localhost:5080).
// Demo credentials come from GET {API_URL}/api/demo/status.
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const frontend = process.env.FRONTEND_URL ?? 'http://localhost:4200';
const api = process.env.API_URL ?? 'http://localhost:5080';
const out = resolve(dirname(fileURLToPath(import.meta.url)), '../../docs/screenshots');
mkdirSync(out, { recursive: true });

const status = await (await fetch(`${api}/api/demo/status`)).json();
const admin = status.accounts?.find((a) => a.role === 'Admin');
if (!status.enabled || !admin) {
  throw new Error('The demo is not enabled on the API (Demo__Enabled=true).');
}

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
const page = await context.newPage();
const extraContexts = [];
const shot = async (name) => {
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${out}/${name}-light.png` });
  console.log('saved', name);
};

try {
  // Login page in Spanish, with the demo panel (its own context, so the language preset stays out of the rest).
  const spanish = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
  extraContexts.push(spanish);
  await spanish.addInitScript(() => localStorage.setItem('taskflow.language', 'es'));
  const esPage = await spanish.newPage();
  await esPage.goto(`${frontend}/login`);
  await esPage.locator('.demo-panel').waitFor();
  await esPage.waitForLoadState('networkidle');
  await esPage.waitForTimeout(800);
  await esPage.screenshot({ path: `${out}/login-es-light.png` });
  console.log('saved login-es');

  // Sign in (English).
  await page.goto(`${frontend}/login`);
  await page.getByLabel('Email').fill(admin.email);
  await page.getByLabel('Password').fill(admin.password);
  await page.locator('form button[type=submit]').click();
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
  await page.locator('app-notification-bell').waitFor();
  await page.waitForSelector('mat-progress-bar', { state: 'detached' }).catch(() => {});
  await shot('dashboard');

  // The project with the most cards on its board.
  await page.goto(`${frontend}/projects`);
  await page.waitForLoadState('networkidle');
  const links = page.locator('a[href^="/projects/"]');
  await links.first().waitFor();
  const base = (await links.first().getAttribute('href')).split('/').slice(0, 3).join('/');
  await page.goto(`${frontend}${base}/board`);
  await page.locator('app-task-card').first().waitFor();
  await shot('board');

  await page.setViewportSize({ width: 1440, height: 1800 });
  // Cards don't show comment or time counts, so open the first card instead of relying on a seeded title.
  await page.locator('app-task-card').first().click();
  await page.locator('mat-dialog-container').waitFor();
  await page.waitForTimeout(500);
  await shot('task-dialog');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.keyboard.press('Escape');
  await page.locator('mat-dialog-container').waitFor({ state: 'detached' });

  await page.goto(`${frontend}${base}/hours`);
  await page.waitForTimeout(500);
  await shot('time-report');

  // Notifications: the demo member's bell menu when there are any; the demo seeds none, so fall back to the
  // project list (saved as projects-light.png).
  const member = status.accounts.find((a) => a.role === 'Member');
  const memberContext = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
  extraContexts.push(memberContext);
  const m = await memberContext.newPage();
  await m.goto(`${frontend}/login`);
  await m.getByLabel('Email').fill(member.email);
  await m.getByLabel('Password').fill(member.password);
  await m.locator('form button[type=submit]').click();
  await m.waitForURL((url) => !url.pathname.startsWith('/login'));
  await m.locator('app-notification-bell').waitFor();
  await m.waitForLoadState('networkidle');
  await m.locator('app-notification-bell button.bell').click();
  await m.locator('.notification-menu').waitFor();
  await m.waitForLoadState('networkidle');
  await m.waitForTimeout(800);
  if ((await m.locator('.notification-menu button.item').count()) > 0) {
    await m.screenshot({ path: `${out}/notifications-light.png` });
    console.log('saved notifications');
  } else {
    await m.keyboard.press('Escape');
    await m.goto(`${frontend}/projects`);
    await m.locator('a[href^="/projects/"]').first().waitFor();
    await m.waitForLoadState('networkidle');
    await m.waitForTimeout(800);
    await m.screenshot({ path: `${out}/projects-light.png` });
    console.log('saved projects (no notifications in the demo data)');
  }
} finally {
  await Promise.all(extraContexts.map((c) => c.close().catch(() => {})));
  await context.close().catch(() => {});
  await browser.close();
}
