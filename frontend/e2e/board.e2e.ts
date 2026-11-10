import { expect, test } from '@playwright/test';
import { Api, unique } from './support/api';
import { column, dragCard, moveViaMenu, openBoard, signIn } from './support/ui';

test('moves a card with the "Move to" menu and keeps it after a reload', async ({ page }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();
  const title = unique('Menu move');
  await api.createTask(project.id, title);

  await signIn(page);
  await openBoard(page, project.id);
  await expect(column(page, 'To do').getByText(title)).toBeVisible();

  await moveViaMenu(page, title, 'In progress');
  await expect(column(page, 'In progress').getByText(title)).toBeVisible();
  await expect(column(page, 'To do').getByText(title)).toHaveCount(0);

  await page.reload();
  await expect(column(page, 'In progress').getByText(title)).toBeVisible();
  await expect(column(page, 'To do').getByText(title)).toHaveCount(0);
});

test('drags a card to another column and keeps it after a reload', async ({ page }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();
  const title = unique('Drag move');
  await api.createTask(project.id, title);

  await signIn(page);
  await openBoard(page, project.id);
  await expect(column(page, 'To do').getByText(title)).toBeVisible();

  await dragCard(page, title, 'Review');
  await expect(column(page, 'Review').getByText(title)).toBeVisible();

  await page.reload();
  await expect(column(page, 'Review').getByText(title)).toBeVisible();
  await expect(column(page, 'To do').getByText(title)).toHaveCount(0);
});
