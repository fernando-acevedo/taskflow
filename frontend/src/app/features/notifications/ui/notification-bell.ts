import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { LanguageService } from '../../../core/i18n/language.service';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { relativeTime } from '../../../shared/ui/relative-time';
import { AppNotification } from '../data-access/notifications-api.service';
import { NotificationBellActions } from '../data-access/store/notifications.actions';
import { notificationsFeature } from '../data-access/store/notifications.feature';

/** The bell in the toolbar: unread badge, and a menu with the latest notifications. */
@Component({
  selector: 'app-notification-bell',
  imports: [MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, TranslocoDirective, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'notifications'">
      <button
        mat-icon-button
        type="button"
        class="bell"
        [matMenuTriggerFor]="menu"
        (menuOpened)="opened()"
        [attr.aria-label]="count() > 0 ? t('bellUnread', { count: count() }) : t('bell')"
      >
        <mat-icon aria-hidden="true" [matBadge]="count() > 99 ? '99+' : count()" [matBadgeHidden]="count() === 0" matBadgeSize="small">
          notifications
        </mat-icon>
      </button>
      <mat-menu #menu="matMenu" xPosition="before" class="notification-menu">
        <div class="menu-header" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()" role="presentation">
          <h2>{{ t('title') }}</h2>
          <button mat-button type="button" [disabled]="count() === 0" (click)="markAll()">{{ t('markAllRead') }}</button>
        </div>
        @if (loadError(); as error) {
          <p class="status" role="alert">{{ notifier.errorMessage(error) }}</p>
        } @else if (items().length === 0) {
          @if (loading()) {
            <p class="status">{{ t('loading') }}</p>
          } @else {
            <app-empty-state icon="notifications_none" [message]="t('empty')" />
          }
        } @else {
          @for (row of rows(); track row.item.id) {
            @let item = row.item;
            <button mat-menu-item type="button" class="item" [class.unread]="!item.readAt" (click)="open(item)">
              <span class="text">
                <span class="message">{{ t('messages.' + item.type, row.params) }}</span>
                <span class="meta">
                  {{ item.data.projectName }} · {{ row.when }}
                  @if (!item.readAt) {
                    <span class="visually-hidden"> · {{ t('unread') }}</span>
                  }
                </span>
              </span>
            </button>
          }
        }
      </mat-menu>
    </ng-container>
  `,
  styles: `
    .menu-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 4px 8px 4px 16px;

      h2 {
        margin: 0;
        font: var(--mat-sys-title-small);
      }
    }
    .status {
      margin: 0;
      padding: 16px;
      color: var(--mat-sys-on-surface-variant);
    }
    .item {
      height: auto;
      min-height: 56px;
      padding-block: 8px;
      white-space: normal;
    }
    .text {
      display: flex;
      flex-direction: column;
      gap: 2px;
      max-width: 340px;
    }
    .message {
      font: var(--mat-sys-body-medium);
    }
    .meta {
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-on-surface-variant);
    }
    .unread {
      background: var(--mat-sys-secondary-container);

      .message {
        font-weight: 600;
      }
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class NotificationBell {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  protected readonly notifier = inject(Notifier);

  protected readonly count = this.store.selectSignal(notificationsFeature.selectUnreadCount);
  protected readonly items = this.store.selectSignal(notificationsFeature.selectItems);
  protected readonly loading = this.store.selectSignal(notificationsFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(notificationsFeature.selectLoadError);

  private readonly now = signal(Date.now());

  protected opened(): void {
    this.now.set(Date.now());
    this.store.dispatch(NotificationBellActions.opened());
  }

  protected markAll(): void {
    this.store.dispatch(NotificationBellActions.markAllClicked({ at: new Date().toISOString() }));
  }

  protected open(item: AppNotification): void {
    if (!item.readAt) {
      this.store.dispatch(NotificationBellActions.notificationClicked({ id: item.id, at: new Date().toISOString() }));
    }
    void this.router.navigate(['/projects', item.projectId, 'board'], {
      queryParams: item.taskId ? { task: item.taskId } : {},
    });
  }

  /** One view model per item, recomputed only when the list, the language or the opening time changes. */
  protected readonly rows = computed(() => {
    const lang = this.language.current();
    const now = this.now();
    const dateFormat = new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short' });
    return this.items().map((item) => {
      const { actorName, taskTitle, dueDate } = item.data;
      return {
        item,
        when: relativeTime(item.createdAt, now, lang),
        params: {
          actor: actorName ?? '',
          task: taskTitle ?? '',
          // yyyy-MM-dd is a calendar date: parsing it as local time keeps it from shifting a day.
          dueDate: dueDate ? dateFormat.format(new Date(`${dueDate}T00:00:00`)) : '',
        },
      };
    });
  });
}
