import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { authFeature } from '../../../core/auth/store/auth.feature';

@Component({
  selector: 'app-home-page',
  imports: [RouterLink, MatButtonModule, MatCardModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('home.greeting', { name: user()?.displayName }) }}</h1>
      <p class="intro">{{ t('home.intro') }}</p>
      <mat-card appearance="outlined">
        <mat-card-content>
          <h2>{{ t('home.projectsTitle') }}</h2>
          <p>{{ t('home.projectsText') }}</p>
        </mat-card-content>
        <mat-card-actions>
          <a mat-flat-button routerLink="/projects">{{ t('home.openProjects') }}</a>
          <a mat-button routerLink="/profile">{{ t('nav.profile') }}</a>
        </mat-card-actions>
      </mat-card>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
      max-width: 720px;
      margin: 0 auto;
      padding: 32px 16px;
    }
    h1 {
      font: var(--mat-sys-headline-medium);
      margin: 0 0 8px;
    }
    h2 {
      font: var(--mat-sys-title-medium);
      margin: 8px 0;
    }
    .intro {
      color: var(--mat-sys-on-surface-variant);
      margin: 0 0 24px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  protected readonly user = inject(Store).selectSignal(authFeature.selectUser);
}
