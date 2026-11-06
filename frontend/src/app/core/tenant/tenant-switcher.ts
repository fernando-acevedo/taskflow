import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { TenantActions } from './store/tenant.actions';
import { tenantFeature } from './store/tenant.feature';

/**
 * The shell's workspace picker. With several tenants it is a menu (the current one is checked, each shows the
 * user's role in it); with one it is just the name; with none it renders nothing.
 */
@Component({
  selector: 'app-tenant-switcher',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      @if (current(); as tenant) {
        @if (tenants().length > 1) {
          <button
            mat-button
            type="button"
            class="switcher"
            [matMenuTriggerFor]="menu"
            [attr.aria-label]="t('tenant.switch') + ': ' + tenant.name"
          >
            <mat-icon aria-hidden="true">apartment</mat-icon>
            <span class="name">{{ tenant.name }}</span>
            <span class="role">{{ t('tenant.roles.' + tenant.role) }}</span>
            <mat-icon aria-hidden="true">arrow_drop_down</mat-icon>
          </button>
          <mat-menu #menu="matMenu" xPosition="before">
            @for (option of tenants(); track option.id) {
              <button
                mat-menu-item
                type="button"
                role="menuitemradio"
                [attr.aria-checked]="option.id === tenant.id"
                (click)="select(option.id)"
              >
                <mat-icon aria-hidden="true">{{ option.id === tenant.id ? 'check' : '' }}</mat-icon>
                <span>{{ option.name }} ({{ t('tenant.roles.' + option.role) }})</span>
              </button>
            }
          </mat-menu>
        } @else {
          <span class="switcher single" [attr.title]="t('tenant.current')">
            <mat-icon aria-hidden="true">apartment</mat-icon>
            <span class="name">{{ tenant.name }}</span>
          </span>
        }
      }
    </ng-container>
  `,
  styles: `
    :host {
      display: contents;
    }

    .switcher {
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }

    .single {
      padding: 0 8px;
    }

    .role {
      font: var(--mat-sys-label-small);
      opacity: 0.8;
    }

    @media (max-width: 720px) {
      .name,
      .role {
        display: none;
      }
    }
  `,
})
export class TenantSwitcher {
  private readonly store = inject(Store);
  protected readonly tenants = this.store.selectSignal(tenantFeature.selectTenants);
  protected readonly current = this.store.selectSignal(tenantFeature.selectCurrent);

  protected select(tenantId: string): void {
    if (tenantId !== this.current()?.id) {
      this.store.dispatch(TenantActions.tenantSwitched({ tenantId }));
    }
  }
}
