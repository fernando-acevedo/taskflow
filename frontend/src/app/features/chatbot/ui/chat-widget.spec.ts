import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { ChatWidgetActions } from '../data-access/store/chatbot.actions';
import { ChatMessage, chatbotFeature } from '../data-access/store/chatbot.feature';
import { ChatWidget } from './chat-widget';

const bot = (over: Partial<ChatMessage> = {}): ChatMessage => ({
  id: 1,
  from: 'bot',
  text: 'Hi, write to {support}',
  quickReplies: [],
  url: null,
  isFallback: false,
  ...over,
});

interface Options {
  messages?: ChatMessage[];
  sessionId?: string | null;
  locale?: string | null;
  ended?: boolean;
  outcome?: 'Completed' | 'Escalated' | null;
  lang?: string;
  sending?: boolean;
}

describe('ChatWidget', () => {
  let store: MockStore;
  let router: Router;

  function render(opts: Options = {}) {
    TestBed.configureTestingModule({
      imports: [
        ChatWidget,
        TranslocoTestingModule.forRoot({
          langs: { en: {}, es: {} },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: opts.lang ?? 'en' },
        }),
      ],
      providers: [
        provideRouter([]),
        provideMockStore({
          selectors: [
            { selector: chatbotFeature.selectMessages, value: opts.messages ?? [] },
            { selector: chatbotFeature.selectSending, value: opts.sending ?? false },
            { selector: chatbotFeature.selectEnded, value: opts.ended ?? false },
            { selector: chatbotFeature.selectOutcome, value: opts.outcome ?? null },
            { selector: chatbotFeature.selectLocale, value: opts.locale ?? null },
            { selector: chatbotFeature.selectSessionId, value: opts.sessionId ?? null },
          ],
        }),
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    router = TestBed.inject(Router);
    vi.spyOn(store, 'dispatch');
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ChatWidget);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const all = (id: string) => Array.from(el.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));
    const open = () => {
      q('chat-launcher')?.click();
      fixture.detectChanges();
    };
    return { fixture, q, all, open };
  }

  it('starts a session in the active language when opened', () => {
    const { open } = render({ lang: 'es' });
    open();
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.sessionRequested({ locale: 'es' }));
  });

  it('keeps the conversation when reopened in the same language', () => {
    const { open, q, fixture } = render({ sessionId: 's1', locale: 'en', messages: [bot()] });
    open();
    expect(store.dispatch).not.toHaveBeenCalled();
    q('chat-close')?.click();
    fixture.detectChanges();
    expect(q('chat-launcher')).not.toBeNull();
  });

  it('offers to restart when the UI language differs from the session language', () => {
    const { open, q, fixture } = render({ sessionId: 's1', locale: 'en', messages: [bot()], lang: 'en' });
    open();
    expect(q('chat-restart-language')).toBeNull();

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(q('chat-restart-language')).not.toBeNull();
    q('chat-restart-language')?.querySelector('button')?.click();
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.sessionRequested({ locale: 'es' }));
  });

  it('disables the restart button while a turn is in flight', () => {
    const { open, q, fixture } = render({ sessionId: 's1', locale: 'en', messages: [bot()], sending: true });
    open();
    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    const button = q('chat-restart-language')?.querySelector('button');
    expect(button?.disabled).toBe(true);
    button?.click();
    expect(store.dispatch).not.toHaveBeenCalled();
  });

  it('renders bot text as plain text, never as HTML', () => {
    const { open, q } = render({ sessionId: 's1', locale: 'en', messages: [bot({ text: '<b>x</b> a@b.c' })] });
    open();
    const text = q('chat-message-bot');
    expect(text?.querySelector('b')).toBeNull();
    expect(text?.textContent).toContain('<b>x</b> a@b.c');
  });

  it('sends the quick reply id and navigates for an internal url', () => {
    const { open, all } = render({
      sessionId: 's1',
      locale: 'en',
      messages: [bot({ quickReplies: [{ id: 'forgot', label: 'Reset it', url: '/forgot-password' }] })],
    });
    open();
    all('chat-quick-reply')[0].click();
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.quickReplySelected({ id: 'forgot', label: 'Reset it' }));
    expect(router.navigateByUrl).toHaveBeenCalledWith('/forgot-password');
  });

  it('does not navigate for a quick reply without url', () => {
    const { open, all } = render({
      sessionId: 's1',
      locale: 'en',
      messages: [bot({ quickReplies: [{ id: 'plain', label: 'Plain', url: null }] })],
    });
    open();
    all('chat-quick-reply')[0].click();
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.quickReplySelected({ id: 'plain', label: 'Plain' }));
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('opens an external url in a new tab with noopener', () => {
    const { open, all } = render({
      sessionId: 's1',
      locale: 'en',
      messages: [bot({ quickReplies: [{ id: 'help', label: 'Help', url: 'https://help.example.com' }] })],
    });
    open();
    const link = all('chat-quick-reply')[0] as HTMLAnchorElement;
    expect(link.tagName).toBe('A');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
    expect(link.getAttribute('href')).toBe('https://help.example.com/');
  });

  it('sends the typed text on submit and caps the input at 1000 characters', () => {
    const { open, q, fixture } = render({ sessionId: 's1', locale: 'en', messages: [bot()] });
    open();
    const input = q('chat-input') as HTMLInputElement;
    expect(input.getAttribute('maxlength')).toBe('1000');
    input.value = ' hello ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    input.form?.dispatchEvent(new Event('submit', { cancelable: true }));
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.messageSent({ text: 'hello' }));
  });

  it('shows the outcome and a Start over button when the conversation ended', () => {
    const { open, q, fixture } = render({ sessionId: 's1', locale: 'en', messages: [bot()], ended: true, outcome: 'Escalated' });
    open();
    expect(q('chat-ended')).not.toBeNull();
    expect(q('chat-input')).toBeNull();
    q('chat-start-over')?.click();
    fixture.detectChanges();
    expect(store.dispatch).toHaveBeenCalledWith(ChatWidgetActions.sessionRequested({ locale: 'en' }));
  });
});
