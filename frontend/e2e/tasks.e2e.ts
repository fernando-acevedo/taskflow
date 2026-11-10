import { expect, test } from '@playwright/test';
import { Api, unique } from './support/api';
import { card, column, createTaskInUi, dialog, openBoard, signIn } from './support/ui';

test('creates a project and a task, then edits the task', async ({ page }) => {
  const api = await Api.signIn();
  const client = await api.createClient();
  const projectName = unique('E2E project');
  const taskTitle = unique('Write brief');
  const editedTitle = `${taskTitle} (edited)`;

  await signIn(page);
  await page.goto('/projects');
  await page.getByRole('button', { name: 'New project' }).first().click();
  const form = dialog(page);
  await form.getByRole('combobox', { name: /Client/ }).click();
  await page.getByRole('option', { name: client.name }).click();
  await form.getByLabel('Name').fill(projectName);
  await form.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(form).toBeHidden();

  // Creating a project opens it, on its board.
  await expect(page.getByRole('heading', { name: projectName, level: 1 })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Board', selected: true })).toBeVisible();

  await createTaskInUi(page, taskTitle);
  await expect(column(page, 'To do').getByText(taskTitle)).toBeVisible();

  await card(page, taskTitle).click();
  const edit = dialog(page);
  await expect(edit.getByRole('heading', { name: 'Edit task' })).toBeVisible();
  await edit.getByLabel('Title').fill(editedTitle);
  await edit.getByRole('combobox', { name: 'Priority' }).click();
  await page.getByRole('option', { name: 'High' }).click();
  await edit.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(edit).toBeHidden();

  await expect(column(page, 'To do').getByText(editedTitle)).toBeVisible();
  await expect(column(page, 'To do').getByText('High', { exact: true })).toBeVisible();

  await page.reload();
  await expect(column(page, 'To do').getByText(editedTitle)).toBeVisible();
});

test('shows tasks created through the API', async ({ page }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();
  const title = unique('From the API');
  await api.createTask(project.id, title);

  await signIn(page);
  await openBoard(page, project.id);
  await expect(column(page, 'To do').getByText(title)).toBeVisible();
});
