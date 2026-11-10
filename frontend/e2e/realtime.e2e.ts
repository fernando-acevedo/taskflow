import { expect, test } from '@playwright/test';
import { Api, unique } from './support/api';
import { column, createTaskInUi, moveViaMenu, newSignedInPage, openBoard, signIn } from './support/ui';

test('changes in one session show up in another without reloading', async ({ page, browser }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();
  const title = unique('Live task');

  await signIn(page);
  await openBoard(page, project.id);
  const watcher = await newSignedInPage(browser);
  await openBoard(watcher.page, project.id);
  // Both are subscribed before anything changes.
  await expect(page.locator('.live', { hasText: 'Live' })).toBeVisible();
  await expect(watcher.page.locator('.live', { hasText: 'Live' })).toBeVisible();

  await createTaskInUi(page, title);
  await expect(column(watcher.page, 'To do').getByText(title)).toBeVisible();

  await moveViaMenu(page, title, 'Review');
  await expect(column(page, 'Review').getByText(title)).toBeVisible();
  await expect(column(watcher.page, 'Review').getByText(title)).toBeVisible();
  await expect(column(watcher.page, 'To do').getByText(title)).toHaveCount(0);

  await watcher.context.close();
});
