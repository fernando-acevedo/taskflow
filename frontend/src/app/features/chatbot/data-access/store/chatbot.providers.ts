import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as chatbotEffects from './chatbot.effects';
import { chatbotFeature } from './chatbot.feature';

/** The chat's state and effects, registered when the lazy widget is created (not at the app root). */
export const chatbotProviders = () => [provideState(chatbotFeature), provideEffects(chatbotEffects)];
