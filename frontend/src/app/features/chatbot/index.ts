import { EnvironmentInjector, Type, createEnvironmentInjector } from '@angular/core';

/** Public API of the chatbot feature: a lazy loader for the widget together with its state. */
export interface LoadedChatWidget {
  readonly component: Type<unknown>;
  readonly injector: EnvironmentInjector;
}

/**
 * The widget is loaded after the first render so it, its store slice and its effects stay out of the initial
 * bundle. Resolves to `null` when the parent injector was destroyed meanwhile. The state is provided on a child injector, so it exists only once the widget does.
 */
export const loadChatWidget = async (parent: EnvironmentInjector): Promise<LoadedChatWidget | null> => {
  const [{ ChatWidget }, { chatbotProviders }] = await Promise.all([
    import('./ui/chat-widget'),
    import('./data-access/store/chatbot.providers'),
  ]);
  // The app (or a test's TestBed) can be gone by the time the chunks arrive: a child of a destroyed injector throws.
  if (parent.destroyed) {
    return null;
  }
  return { component: ChatWidget, injector: createEnvironmentInjector(chatbotProviders(), parent) };
};
