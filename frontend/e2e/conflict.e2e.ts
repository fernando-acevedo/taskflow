import { expect, test } from '@playwright/test';
import { Api, unique } from './support/api';
import { card, column, dialog, newSignedInPage, openBoard, signIn } from './support/ui';

test('a second editor gets the conflict notice and can reload or save anyway', async ({ page, browser }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();
  const original = unique('Shared task');
  await api.createTask(project.id, original);

  await signIn(page);
  await openBoard(page, project.id);
  const other = await newSignedInPage(browser);
  await openBoard(other.page, project.id);

  // Both open the same task.
  await card(page, original).click();
  await card(other.page, original).click();
  const first = dialog(page);
  const second = dialog(other.page);
  await expect(first.getByLabel('Title')).toHaveValue(original);
  await expect(second.getByLabel('Title')).toHaveValue(original);

  // The first saves.
  const firstTitle = `${original} / first`;
  await first.getByLabel('Title').fill(firstTitle);
  await first.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(first).toBeHidden();

  // The second saves from the old version: conflict, with both ways out.
  await second.getByLabel('Title').fill(`${original} / second`);
  await second.getByRole('button', { name: 'Save', exact: true }).click();
  const notice = second.getByRole('alert').filter({ hasText: 'Someone else changed this task' });
  await expect(notice).toBeVisible();
  await expect(notice.getByRole('button', { name: 'Reload' })).toBeVisible();
  await expect(notice.getByRole('button', { name: 'Save anyway' })).toBeVisible();

  // Reload brings in the first editor's version.
  await notice.getByRole('button', { name: 'Reload' }).click();
  await expect(notice).toBeHidden();
  await expect(second.getByLabel('Title')).toHaveValue(firstTitle);

  // The first edits again, so the second is stale once more; "Save anyway" then overwrites.
  await card(page, firstTitle).click();
  await first.getByLabel('Title').fill(`${original} / interleaved`);
  await first.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(first).toBeHidden();

  const lastTitle = `${original} / last`;
  await second.getByLabel('Title').fill(lastTitle);
  await second.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(notice).toBeVisible();
  await notice.getByRole('button', { name: 'Save anyway' }).click();
  await expect(second).toBeHidden();

  await page.reload();
  await expect(column(page, 'To do').getByText(lastTitle)).toBeVisible();
  await other.context.close();
});
