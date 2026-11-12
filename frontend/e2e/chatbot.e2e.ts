import { expect, test, type Page } from '@playwright/test';

// The support bot picks one of several texts at random, so these specs never assert bot text: they follow the
// conversation through the ids of the quick replies (data-reply-id), which are fixed. Quick-reply labels are fixed
// too, so those can be asserted per language.

interface Locale {
  code: 'en' | 'es';
  forgotPhrase: string;
  forgotLabel: string;
  launcherLabel: RegExp;
}

const locales: Locale[] = [
  { code: 'en', forgotPhrase: 'I forgot my password', forgotLabel: 'I forgot my password', launcherLabel: /open support chat/i },
  { code: 'es', forgotPhrase: 'olvidé mi contraseña', forgotLabel: 'Olvidé mi contraseña', launcherLabel: /abrir el chat de soporte/i },
];

const reply = (page: Page, id: string) => page.locator(`[data-testid="chat-quick-reply"][data-reply-id="${id}"]`);

/** Opens the widget and waits for the greeting, which arrives asynchronously. */
async function openChat(page: Page): Promise<void> {
  await page.goto('/login');
  const launcher = page.getByTestId('chat-launcher');
  await expect(launcher).toBeVisible();
  await launcher.click();
  await expect(page.getByTestId('chat-panel')).toBeVisible();
  await expect(page.getByTestId('chat-message-bot').first()).toBeVisible();
}

async function expectReplies(page: Page, ids: string[]): Promise<void> {
  for (const id of ids) {
    await expect(reply(page, id)).toBeVisible();
  }
}

async function say(page: Page, text: string): Promise<void> {
  const botMessages = page.getByTestId('chat-message-bot');
  const before = await botMessages.count();
  await page.getByTestId('chat-input').fill(text);
  await page.getByTestId('chat-send').click();
  // The answer is a new bot message, once the typing indicator is gone.
  await expect(botMessages).toHaveCount(before + 1);
  await expect(page.getByTestId('chat-typing')).toBeHidden();
}

for (const { code, forgotPhrase, forgotLabel, launcherLabel } of locales) {
  test.describe(`chatbot (${code})`, () => {
    test.use({ locale: code === 'es' ? 'es-ES' : 'en-US' });

    test('walks the forgot-password flow from the login page', async ({ page }) => {
      await openChat(page);
      await expectReplies(page, ['cant_login', 'forgot', 'no_email', 'other']);

      await reply(page, 'forgot').click();
      await expectReplies(page, ['go', 'done']);

      // "Go" takes the visitor to the page and the conversation carries on.
      await reply(page, 'go').click();
      await expect(page).toHaveURL(/\/forgot-password/);
      await expectReplies(page, ['yes', 'no_email', 'link']);
      await expect(page.getByTestId('chat-panel')).toBeVisible();

      await reply(page, 'yes').click();
      await expect(page.getByTestId('chat-ended')).toBeVisible();

      await page.getByTestId('chat-start-over').click();
      await expect(page.getByTestId('chat-ended')).toBeHidden();
      await expectReplies(page, ['cant_login', 'forgot']);
    });

    test('understands a free-text forgot-password message', async ({ page }) => {
      await openChat(page);
      await say(page, forgotPhrase);
      await expectReplies(page, ['go', 'done']);
    });

    test('shows the texts in the browser language', async ({ page }) => {
      await page.goto('/login');
      const launcher = page.getByTestId('chat-launcher');
      await expect(launcher).toBeVisible();
      await expect(launcher).toHaveAccessibleName(launcherLabel);
      await launcher.click();
      await expect(reply(page, 'forgot')).toHaveText(forgotLabel);
    });

    test('hands over to a person after repeated unintelligible messages', async ({ page }) => {
      await openChat(page);
      await say(page, 'qwzx vbnm');
      await say(page, 'qwzx vbnm');
      await expect(page.getByTestId('chat-ended')).toBeHidden();

      await page.getByTestId('chat-input').fill('qwzx vbnm');
      await page.getByTestId('chat-send').click();
      await expect(page.getByTestId('chat-ended')).toBeVisible();
    });
  });
}
