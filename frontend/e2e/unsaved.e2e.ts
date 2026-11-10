import { expect, test } from '@playwright/test';
import { Api } from './support/api';
import { dialog, openBoard, signIn } from './support/ui';

test('Esc closes an untouched dialog at once and asks before discarding a modified one', async ({ page }) => {
  const api = await Api.signIn();
  const project = await api.createProjectWithClient();

  await signIn(page);
  await openBoard(page, project.id);

  await page.getByRole('button', { name: 'New task' }).click();
  const form = dialog(page);
  await expect(form.getByRole('heading', { name: 'New task' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(form).toBeHidden();

  await page.getByRole('button', { name: 'New task' }).click();
  await form.getByLabel('Title').fill('Half-written task');
  await page.keyboard.press('Escape');
  const confirm = page.getByRole('dialog', { name: 'Discard changes?' });
  await expect(confirm).toBeVisible();

  // Cancel keeps the form and what was typed.
  await confirm.getByRole('button', { name: 'Cancel' }).click();
  await expect(confirm).toBeHidden();
  await expect(form.getByLabel('Title')).toHaveValue('Half-written task');

  // Discard closes everything without saving.
  await page.keyboard.press('Escape');
  await page.getByRole('dialog', { name: 'Discard changes?' }).getByRole('button', { name: 'Discard' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText('Half-written task')).toHaveCount(0);
});
