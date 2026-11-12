import { EnvironmentInjector } from '@angular/core';
import { loadChatWidget } from './index';

describe('loadChatWidget', () => {
  it('resolves to null instead of throwing when the parent injector is destroyed while the chunks load', async () => {
    const parent = { destroyed: true } as EnvironmentInjector;

    await expect(loadChatWidget(parent)).resolves.toBeNull();
  });
});
