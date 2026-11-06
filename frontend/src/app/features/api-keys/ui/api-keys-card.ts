import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Actions, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { LanguageService } from '../../../core/i18n/language.service';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { ApiKey, NewApiKeyInput } from '../data-access/api-keys-api.service';
import { ApiKeysApiActions, ApiKeysPageActions } from '../data-access/store/api-keys.actions';
import { apiKeysFeature } from '../data-access/store/api-keys.feature';
import { ApiKeyFormData, ApiKeyFormDialog } from './api-key-form-dialog';
import { ApiKeySecretData, ApiKeySecretDialog } from './api-key-secret-dialog';

export type ApiKeyStatus = 'active' | 'revoked' | 'expired';

export function apiKeyStatus(key: ApiKey): ApiKeyStatus {
  if (key.revokedAt) {
    return 'revoked';
  }
  return key.isActive ? 'active' : 'expired';
}

/** The "API keys" section of the profile page: the user's keys, creating one and revoking one. */
@Component({
  selector: 'app-api-keys-card',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'profile.apiKeys'">
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ t('title') }}</mat-card-title>
          <mat-card-subtitle>{{ t('intro') }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (loadError()) {
            <p role="alert">
              {{ t('loadFailed') }}
              <button mat-button type="button" (click)="reload()">{{ t('retry') }}</button>
            </p>
          } @else if (loading() && keys().length === 0) {
            <p role="status">{{ t('loading') }}</p>
          } @else if (keys().length === 0) {
            <p class="empty">{{ t('empty') }}</p>
          } @else {
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{{ t('name') }}</th>
                    <th scope="col">{{ t('prefix') }}</th>
                    <th scope="col">{{ t('scope') }}</th>
                    <th scope="col">{{ t('expires') }}</th>
                    <th scope="col">{{ t('lastUsed') }}</th>
                    <th scope="col">{{ t('status') }}</th>
                    <th scope="col"><span class="visually-hidden">{{ t('actions') }}</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (key of keys(); track key.id) {
                    @let status = statusOf(key);
                    <tr>
                      <th scope="row">{{ key.name }}</th>
                      <td><code>{{ key.prefix }}…</code></td>
                      <td>{{ t('scopes.' + key.scope) }}</td>
                      <td>{{ key.expiresAt | date: 'mediumDate' : undefined : language.current() }}</td>
                      <td>
                        @if (key.lastUsedAt) {
                          {{ key.lastUsedAt | date: 'medium' : undefined : language.current() }}
                        } @else {
                          {{ t('neverUsed') }}
                        }
                      </td>
                      <td><span class="chip" [attr.data-status]="status">{{ t('statuses.' + status) }}</span></td>
                      <td>
                        @if (status === 'active') {
                          <button mat-button type="button" [attr.aria-label]="t('revokeLabel', { name: key.name })" (click)="revoke(key)">
                            {{ t('revoke') }}
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </mat-card-content>
        <mat-card-actions>
          <button mat-flat-button type="button" (click)="create()">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ t('create') }}
          </button>
        </mat-card-actions>
      </mat-card>
    </ng-container>
  `,
  styles: `
    mat-card {
      margin-top: 16px;
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      padding: 8px;
      text-align: left;
      white-space: nowrap;
    }
    thead th {
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
    }
    tbody th {
      font-weight: 500;
      white-space: normal;
      overflow-wrap: anywhere;
    }
    .chip[data-status='revoked'],
    .chip[data-status='expired'] {
      background: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class ApiKeysCard {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  protected readonly keys = this.store.selectSignal(apiKeysFeature.selectAll);
  protected readonly loading = this.store.selectSignal(apiKeysFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(apiKeysFeature.selectLoadError);
  protected readonly statusOf = apiKeyStatus;

  constructor() {
    this.store.dispatch(ApiKeysPageActions.opened());

    // The secret goes from the action stream straight into the dialog: no state keeps it.
    inject(Actions)
      .pipe(ofType(ApiKeysApiActions.keyCreated), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(({ name, secret }) => {
        this.dialog.open<ApiKeySecretDialog, ApiKeySecretData>(ApiKeySecretDialog, {
          data: { name, secret },
          width: '520px',
          maxWidth: 'calc(100vw - 32px)',
          disableClose: true,
        });
      });
  }

  protected reload(): void {
    this.store.dispatch(ApiKeysPageActions.opened());
  }

  protected create(): void {
    const save: SaveFn<NewApiKeyInput> = (key) =>
      this.submissions.dispatchAndWait(
        ApiKeysPageActions.createSubmitted({ key, submissionId: nextSubmissionId() }),
      );
    this.dialog.open<ApiKeyFormDialog, ApiKeyFormData>(ApiKeyFormDialog, {
      data: { save },
      width: '480px',
      maxWidth: 'calc(100vw - 32px)',
    });
  }

  protected revoke(key: ApiKey): void {
    confirm(this.dialog, {
      title: 'profile.apiKeys.revokeTitle',
      message: 'profile.apiKeys.revokeMessage',
      confirm: 'profile.apiKeys.revoke',
      params: { name: key.name },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(ApiKeysPageActions.revokeConfirmed({ keyId: key.id })));
  }
}
