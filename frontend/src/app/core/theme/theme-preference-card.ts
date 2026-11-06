import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { TranslocoDirective } from '@jsverse/transloco';
import { THEME_PREFERENCES } from './theme';
import { ThemeService } from './theme.service';

/** The appearance card of the profile page. */
@Component({
  selector: 'app-theme-preference',
  imports: [MatButtonToggleModule, MatCardModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'theme'">
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ t('title') }}</mat-card-title>
          <mat-card-subtitle>{{ t('hint') }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <mat-button-toggle-group
            hideSingleSelectionIndicator
            [attr.aria-label]="t('title')"
            [value]="theme.preference()"
            (change)="theme.use($event.value)"
          >
            @for (option of options; track option) {
              <mat-button-toggle [value]="option">{{ t(option) }}</mat-button-toggle>
            }
          </mat-button-toggle-group>
        </mat-card-content>
      </mat-card>
    </ng-container>
  `,
  styles: `
    mat-card {
      margin-top: 16px;
    }
  `,
})
export class ThemePreferenceCard {
  protected readonly theme = inject(ThemeService);
  protected readonly options = THEME_PREFERENCES;
}
