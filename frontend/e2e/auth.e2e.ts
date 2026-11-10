import { expect, test } from '@playwright/test';
import { unique } from './support/api';
import { signIn } from './support/ui';

test('signs in and out', async ({ page }) => {
  await page.goto('/projects');
  await expect(page).toHaveURL(/\/login/);

  await signIn(page);
  await page.goto('/projects');
  await expect(page.getByRole('heading', { name: 'Projects', level: 1 })).toBeVisible();

  await page.getByRole('button', { name: 'Account menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();

  // The session is gone: a protected page sends the visitor back to the login form.
  await page.goto('/projects');
  await expect(page).toHaveURL(/\/login/);
});

test('rejects a wrong password', async ({ page }) => {
  await page.goto('/login');
  // An unknown address, so the shared admin account never collects failed attempts (and a lockout).
  await page.getByLabel('Email').fill(`${unique('nobody').replace(' ', '-')}@e2e.local`);
  await page.getByLabel('Password').fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});
