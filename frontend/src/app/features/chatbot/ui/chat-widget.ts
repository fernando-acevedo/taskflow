import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  afterRenderEffect,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ChatQuickReplyDto } from '@core/api/types';
import { LanguageService } from '@core/i18n/language.service';
import { CHAT_MAX_LENGTH } from '../data-access/chatbot-api.service';
import { ChatWidgetActions } from '../data-access/store/chatbot.actions';
import { chatbotFeature } from '../data-access/store/chatbot.feature';
import { ChatLink, classifyLink } from './chat-link';

/**
 * The support bot: a launcher at the bottom-right corner and a non-modal panel. It is mounted once by the root
 * component, so it is there on the sign-in pages and inside the shell alike.
 *
 * Language: the server fixes it per session. Opening the panel starts a session in the active language; if the
 * user switches the UI language later, the panel offers "Restart in <language>" instead of mixing languages.
 */
@Component({
  selector: 'app-chat-widget',
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './chat-widget.scss',
  template: `
    <ng-container *transloco="let t; prefix: 'chatbot'">
      @if (!isOpen()) {
        <button
          #launcher
          type="button"
          class="launcher"
          data-testid="chat-launcher"
          [attr.aria-label]="t('open')"
          aria-haspopup="dialog"
          (click)="open()"
        >
          <mat-icon aria-hidden="true">support_agent</mat-icon>
        </button>
      } @else {
        <section
          class="panel"
          role="dialog"
          aria-modal="false"
          aria-labelledby="chat-title"
          data-testid="chat-panel"
          (keydown.escape)="close()"
        >
          <header class="header">
            <h2 id="chat-title" tabindex="-1">{{ t('title') }}</h2>
            <button mat-icon-button type="button" data-testid="chat-close" [attr.aria-label]="t('close')" (click)="close()">
              <mat-icon aria-hidden="true">close</mat-icon>
            </button>
          </header>

          @if (localeMismatch()) {
            <div class="notice" data-testid="chat-restart-language">
              <span>{{ t('languageChanged') }}</span>
              <button mat-button type="button" [disabled]="sending()" (click)="start()">{{ t('restartIn', { language: languageName() }) }}</button>
            </div>
          }

          <div class="messages" role="log" aria-live="polite" [attr.aria-label]="t('conversation')" #list data-testid="chat-messages">
            @for (message of messages(); track message.id) {
              <div class="message" [class.user]="message.from === 'user'" [class.bot]="message.from === 'bot'" [attr.data-testid]="'chat-message-' + message.from">
                <span class="visually-hidden">{{ message.from === 'user' ? t('you') : t('bot') }}:</span>
                <p class="text">{{ message.text }}</p>
                @if (linkOf(message.url); as link) {
                  @if (link.kind === 'internal') {
                    <button mat-stroked-button type="button" class="link" data-testid="chat-link" (click)="go(link.url)">{{ t('openLink') }}</button>
                  } @else {
                    <a mat-stroked-button class="link" data-testid="chat-link" [href]="link.url" target="_blank" rel="noopener noreferrer">
                      {{ t('openLink') }}<span class="visually-hidden"> {{ t('opensInNewTab') }}</span>
                    </a>
                  }
                }
                @if (message.quickReplies.length > 0) {
                  <div class="quick-replies" role="group" [attr.aria-label]="t('quickReplies')">
                    @for (reply of message.quickReplies; track reply.id) {
                      @let external = externalUrl(reply.url);
                      @if (external) {
                        <a
                          mat-stroked-button
                          class="quick-reply"
                          data-testid="chat-quick-reply"
                          [attr.data-reply-id]="reply.id"
                          [href]="external"
                          target="_blank"
                          rel="noopener noreferrer"
                          (click)="pick(reply)"
                        >
                          {{ reply.label }}<span class="visually-hidden"> {{ t('opensInNewTab') }}</span>
                        </a>
                      } @else {
                        <button
                          mat-stroked-button
                          type="button"
                          class="quick-reply"
                          data-testid="chat-quick-reply"
                          [attr.data-reply-id]="reply.id"
                          [disabled]="sending()"
                          (click)="pick(reply)"
                        >
                          {{ reply.label }}
                        </button>
                      }
                    }
                  </div>
                }
              </div>
            }
            @if (sending()) {
              <p class="typing" data-testid="chat-typing">{{ t('typing') }}</p>
            }
          </div>

          @if (ended()) {
            <div class="ended" data-testid="chat-ended">
              @if (outcome() === 'Escalated') {
                <p>{{ t('outcome.escalated') }}</p>
              } @else {
                <p>{{ t('outcome.completed') }}</p>
              }
              <button mat-flat-button type="button" data-testid="chat-start-over" [disabled]="sending()" (click)="start()">{{ t('startOver') }}</button>
            </div>
          } @else if (messages().length === 0 && !sending()) {
            <div class="ended">
              <button mat-flat-button type="button" data-testid="chat-start-over" [disabled]="sending()" (click)="start()">{{ t('startOver') }}</button>
            </div>
          } @else {
            <form class="composer" (submit)="submit($event)">
              <input
                #input
                class="input"
                type="text"
                autocomplete="off"
                data-testid="chat-input"
                [attr.maxlength]="maxLength"
                [attr.aria-label]="t('inputLabel')"
                [placeholder]="t('inputPlaceholder')"
                [disabled]="sending()"
                [value]="draft()"
                (input)="draft.set($any($event.target).value)"
              />
              <button mat-icon-button type="submit" data-testid="chat-send" [attr.aria-label]="t('send')" [disabled]="sending() || draft().trim() === ''">
                <mat-icon aria-hidden="true">send</mat-icon>
              </button>
            </form>
          }
        </section>
      }
    </ng-container>
  `,
})
export class ChatWidget {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  private readonly injector = inject(Injector);

  protected readonly maxLength = CHAT_MAX_LENGTH;
  protected readonly isOpen = signal(false);
  protected readonly draft = signal('');
  protected readonly messages = this.store.selectSignal(chatbotFeature.selectMessages);
  protected readonly sending = this.store.selectSignal(chatbotFeature.selectSending);
  protected readonly ended = this.store.selectSignal(chatbotFeature.selectEnded);
  protected readonly outcome = this.store.selectSignal(chatbotFeature.selectOutcome);
  private readonly sessionLocale = this.store.selectSignal(chatbotFeature.selectLocale);
  private readonly sessionId = this.store.selectSignal(chatbotFeature.selectSessionId);

  /** The session speaks another language than the UI now does. */
  protected readonly localeMismatch = computed(
    () => this.sessionLocale() !== null && this.sessionLocale() !== this.language.current(),
  );
  protected readonly languageName = computed(() => {
    const current = this.language.current();
    const name = new Intl.DisplayNames([current], { type: 'language' }).of(current) ?? current;
    return name.charAt(0).toLocaleUpperCase(current) + name.slice(1);
  });

  private readonly list = viewChild<ElementRef<HTMLElement>>('list');
  private readonly input = viewChild<ElementRef<HTMLElement>>('input');
  private readonly launcher = viewChild<ElementRef<HTMLElement>>('launcher');

  constructor() {
    // New messages scroll into view (the log region announces them).
    afterRenderEffect(() => {
      this.messages();
      const element = this.list()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    });
    // Back to the composer when the bot's answer arrives and unlocks it.
    afterRenderEffect(() => {
      if (!this.sending() && this.isOpen()) {
        this.input()?.nativeElement?.focus();
      }
    });
  }

  protected open(): void {
    this.isOpen.set(true);
    if (this.sessionId() === null && !this.sending()) {
      this.start();
    }
  }

  protected close(): void {
    this.isOpen.set(false);
    afterNextRender(() => this.launcher()?.nativeElement?.focus(), { injector: this.injector });
  }

  /** A new session in the active language: first open, "Start over" and "Restart in <language>". */
  protected start(): void {
    if (this.sending()) {
      return;
    }
    this.draft.set('');
    this.store.dispatch(ChatWidgetActions.sessionRequested({ locale: this.language.current() }));
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const text = this.draft().trim();
    if (text === '' || this.sending()) {
      return;
    }
    this.draft.set('');
    this.store.dispatch(ChatWidgetActions.messageSent({ text }));
  }

  /** The id always goes to the bot; an internal path also navigates (an external one opens through its anchor). */
  protected pick(reply: ChatQuickReplyDto): void {
    if (this.sending()) {
      return;
    }
    this.store.dispatch(ChatWidgetActions.quickReplySelected({ id: reply.id, label: reply.label }));
    const link = classifyLink(reply.url);
    if (link?.kind === 'internal') {
      this.go(link.url);
    }
  }

  protected go(url: string): void {
    void this.router.navigateByUrl(url);
  }

  protected linkOf(url: string | null): ChatLink | null {
    return classifyLink(url);
  }

  protected externalUrl(url: string | null): string | null {
    const link = classifyLink(url);
    return link?.kind === 'external' ? link.url : null;
  }
}
