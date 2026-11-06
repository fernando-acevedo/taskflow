import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiError, toApiError } from '../../../core/http/api-error';
import { LanguageService } from '../../../core/i18n/language.service';
import { Notifier } from '../../../core/notifications/notifier';
import { TenantApiService } from '../../../core/tenant/tenant-api.service';
import { TenantSummary } from '../../../core/tenant/tenant.models';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { EmptyState } from '../../../shared/ui/empty-state';

/**
 * Platform view for the super-admin: every tenant, and a form to create one. It is a small admin screen, so it
 * talks to the API service directly instead of keeping an NgRx feature of its own.
 */
@Component({
  selector: 'app-tenants-page',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
    EmptyState,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'tenants'">
      <header class="page-header">
        <div>
          <h1>{{ t('title') }}</h1>
          <p>{{ t('subtitle') }}</p>
        </div>
      </header>

      <mat-card appearance="outlined" class="create">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <h2>{{ t('createTitle') }}</h2>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" autocomplete="off" required />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('slug') }}</mat-label>
            <input matInput formControlName="slug" autocomplete="off" required />
            <mat-hint>{{ t('slugHint') }}</mat-hint>
            @if (form.controls.slug.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.slug.invalid) {
              <mat-error>{{ t('slugRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('ownerEmail') }}</mat-label>
            <input matInput type="email" formControlName="ownerEmail" autocomplete="off" />
            <mat-hint>{{ t('ownerHint') }}</mat-hint>
            @if (form.controls.ownerEmail.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.ownerEmail.invalid) {
              <mat-error>{{ t('ownerEmail') }}</mat-error>
            }
          </mat-form-field>
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="saving()">{{ t('submit') }}</button>
          </div>
        </form>
      </mat-card>

      <mat-card appearance="outlined">
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        @if (loadError(); as error) {
          <app-empty-state icon="cloud_off" [message]="notifier.errorMessage(error)">
            <button mat-button type="button" (click)="load()">{{ t('retry') }}</button>
          </app-empty-state>
        } @else if (!loading() && tenants().length === 0) {
          <app-empty-state icon="apartment" [message]="t('empty')" />
        } @else {
          <ul class="rows">
            @for (tenant of tenants(); track tenant.id) {
              <li>
                <div class="main">
                  <strong>{{ tenant.name }}</strong>
                  <code>{{ tenant.slug }}</code>
                  @if (tenant.isDemo) {
                    <span class="chip">{{ t('demo') }}</span>
                  }
                  <span class="meta">{{
                    t('createdOn', {
                      when:
                        (tenant.createdAt | date: 'mediumDate' : undefined : language.current()),
                    })
                  }}</span>
                </div>
              </li>
            }
          </ul>
        }
      </mat-card>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
      max-width: 960px;
      margin: 0 auto;
      padding: 32px 16px;
    }

    .create {
      margin-bottom: 24px;
      padding: 16px;
    }

    h2 {
      margin: 0 0 12px;
      font: var(--mat-sys-title-medium);
    }

    mat-form-field {
      width: 100%;
    }

    .actions {
      display: flex;
      justify-content: flex-end;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantsPage {
  private readonly api = inject(TenantApiService);
  protected readonly notifier = inject(Notifier);
  protected readonly language = inject(LanguageService);

  protected readonly tenants = signal<TenantSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadError = signal<ApiError | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    slug: [
      '',
      [
        Validators.required,
        Validators.pattern(/^[a-z0-9]+(-[a-z0-9]+)*$/),
        Validators.maxLength(63),
      ],
    ],
    ownerEmail: ['', [Validators.email, Validators.maxLength(256)]],
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.list().subscribe({
      next: (tenants) => {
        this.tenants.set(tenants);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(toApiError(error));
        this.loading.set(false);
      },
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, slug, ownerEmail } = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .create({
        name: name.trim(),
        slug: slug.trim(),
        ...(ownerEmail.trim() ? { ownerEmail: ownerEmail.trim() } : {}),
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.notifier.success(
            created.owner?.invitationSent ? 'tenants.createdInvited' : 'tenants.created',
          );
          this.form.reset();
          this.load();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const apiError = toApiError(error);
          applyApiFieldErrors(this.form, apiError);
          this.notifier.error(apiError);
        },
      });
  }
}
