import { EnvironmentInjector, Type, createEnvironmentInjector } from '@angular/core';

/** Public API of the chatbot feature: a lazy loader for the widget together with its state. */
export interface LoadedChatWidget {
  readonly component: Type<unknown>;
  readonly injector: EnvironmentInjector;
}

/**
 * The widget is loaded after the first render so it, its store slice and its effects stay out of the initial
 * bundle. The state is provided on a child injector, so it exists only once the widget does.
 */
export const loadChatWidget = async (parent: EnvironmentInjector): Promise<LoadedChatWidget> => {
  const [{ ChatWidget }, { chatbotProviders }] = await Promise.all([
    import('./ui/chat-widget'),
    import('./data-access/store/chatbot.providers'),
  ]);
  return { component: ChatWidget, injector: createEnvironmentInjector(chatbotProviders(), parent) };
};
