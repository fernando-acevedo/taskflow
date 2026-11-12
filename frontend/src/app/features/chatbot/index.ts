/** Public API of the chatbot feature: the root registration and a lazy loader for the widget. */
export * as chatbotEffects from './data-access/store/chatbot.effects';
export { chatbotFeature } from './data-access/store/chatbot.feature';
/** The widget is loaded after the first render so it stays out of the initial bundle. */
export const loadChatWidget = () => import('./ui/chat-widget').then((m) => m.ChatWidget);
