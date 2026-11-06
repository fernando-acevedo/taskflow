import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoDirective } from '@jsverse/transloco';
import { THEME_PREFERENCES, ThemePreference } from './theme';
import { ThemeService } from './theme.service';

const ICONS: Record<ThemePreference, string> = { System: 'brightness_auto', Light: 'light_mode', Dark: 'dark_mode' };

/** Toolbar button with a menu of the three theme options; the current one is marked. */
@Component({
  selector: 'app-theme-menu',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'theme'">
      <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="t('label')">
        <mat-icon aria-hidden="true">{{ icons[theme.preference()] }}</mat-icon>
      </button>
      <mat-menu #menu="matMenu" xPosition="before">
        @for (option of options; track option) {
          <button
            mat-menu-item
            type="button"
            role="menuitemradio"
            [attr.aria-checked]="theme.preference() === option"
            (click)="theme.use(option)"
          >
            <mat-icon aria-hidden="true">{{ theme.preference() === option ? 'check' : icons[option] }}</mat-icon>
            <span>{{ t(option) }}</span>
          </button>
        }
      </mat-menu>
    </ng-container>
  `,
})
export class ThemeMenu {
  protected readonly theme = inject(ThemeService);
  protected readonly options = THEME_PREFERENCES;
  protected readonly icons = ICONS;
}
