import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { admin } from './api';

/** Signs in through the login form and waits for the app shell. */
export async function signIn(page: Page): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(admin.email);
  await page.getByLabel('Password').fill(admin.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('button', { name: 'Account menu' })).toBeVisible();
}

/** A second, independent browser session (its own storage), already signed in. */
export async function newSignedInPage(browser: Browser): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext({ locale: 'en-US' });
  const page = await context.newPage();
  await signIn(page);
  return { context, page };
}

export async function openBoard(page: Page, projectId: string): Promise<void> {
  await page.goto(`/projects/${projectId}/board`);
  await expect(page.getByRole('button', { name: 'New task' })).toBeVisible();
}

/** The board column named like its status label ("To do", "In progress", "Review", "Done"). */
export function column(page: Page, status: string) {
  return page.getByRole('region', { name: new RegExp(`^${status}`) });
}

/** A card's open button. The card text starts with the title. */
export function card(page: Page, title: string) {
  return page.getByRole('button', { name: new RegExp(`^${title.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')}`) }).first();
}

/** Resolves when the API has answered a task move, so a reload right after sees what was persisted. */
function moveSaved(page: Page) {
  return page.waitForResponse((r) => r.request().method() === 'PUT' && /\/tasks\/[^/]+\/position$/.test(r.url()));
}

/** Opens a card's "Move to" menu and picks an item; returns once the move is saved. */
export async function moveViaMenu(page: Page, title: string, target: string): Promise<void> {
  await page.getByRole('button', { name: `Move “${title}”` }).click();
  const saved = moveSaved(page);
  await page.getByRole('menuitem', { name: target }).click();
  expect((await saved).ok()).toBe(true);
}

/** Drags a card onto a column with real mouse events (the CDK drag needs several moves to start); returns once the move is saved. */
export async function dragCard(page: Page, title: string, status: string): Promise<void> {
  const from = await card(page, title).boundingBox();
  const to = await column(page, status).boundingBox();
  if (!from || !to) {
    throw new Error('Card or column is not visible');
  }
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(from.x + from.width / 2 + 10, from.y + from.height / 2 + 10, { steps: 5 });
  await page.mouse.move(to.x + to.width / 2, to.y + 80, { steps: 20 });
  const saved = moveSaved(page);
  await page.mouse.up();
  expect((await saved).ok()).toBe(true);
}

export function dialog(page: Page) {
  return page.getByRole('dialog');
}

/** Creates a task from the board's "New task" dialog. */
export async function createTaskInUi(page: Page, title: string): Promise<void> {
  await page.getByRole('button', { name: 'New task' }).click();
  const form = dialog(page);
  await form.getByLabel('Title').fill(title);
  await form.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(form).toBeHidden();
}
