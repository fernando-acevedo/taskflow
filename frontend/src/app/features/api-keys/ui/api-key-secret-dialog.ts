import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

export interface ApiKeySecretData {
  readonly name: string;
  readonly secret: string;
}

/**
 * Shows a new key's secret once. It lives only in this dialog's data: it is never stored, and closing the
 * dialog is the end of it.
 */
@Component({
  selector: 'app-api-key-secret-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'profile.apiKeys'">
      <h2 mat-dialog-title>{{ t('secretTitle') }}</h2>
      <mat-dialog-content>
        <p class="warning" role="alert">{{ t('secretWarning') }}</p>
        <p class="name">{{ data.name }}</p>
        <div class="secret">
          <code [attr.aria-label]="t('secretLabel')" tabindex="0">{{ data.secret }}</code>
          <button mat-stroked-button type="button" (click)="copy()">
            <mat-icon aria-hidden="true">{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
            {{ copied() ? t('copied') : t('copy') }}
          </button>
        </div>
        <p class="status" role="status">
          @if (copied()) {
            {{ t('copied') }}
          } @else if (copyFailed()) {
            {{ t('copyFailed') }}
          }
        </p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-flat-button type="button" mat-dialog-close>{{ t('done') }}</button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styles: `
    .warning {
      margin: 0 0 12px;
      padding: 8px 12px;
      border-radius: 8px;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .name {
      margin: 0 0 8px;
      font: var(--mat-sys-title-small);
    }
    .secret {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
    }
    code {
      flex: 1 1 240px;
      padding: 8px 12px;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-highest);
      overflow-wrap: anywhere;
      user-select: all;
    }
    .status {
      min-height: 1.5em;
      margin: 8px 0 0;
      font: var(--mat-sys-body-small);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiKeySecretDialog {
  protected readonly data = inject<ApiKeySecretData>(MAT_DIALOG_DATA);
  protected readonly copied = signal(false);
  protected readonly copyFailed = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.secret);
      this.copied.set(true);
      this.copyFailed.set(false);
    } catch {
      this.copied.set(false);
      this.copyFailed.set(true);
    }
  }
}
