import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ShellActions } from './core/auth/store/auth.actions';
import { authFeature } from './core/auth/store/auth.feature';
import { LanguageService } from './core/i18n/language.service';
import { SUPPORTED_LANGUAGES } from './core/i18n/languages';

@Component({
  selector: 'app-root',
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatMenuModule,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslocoDirective,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly store = inject(Store);
  protected readonly language = inject(LanguageService);
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly user = this.store.selectSignal(authFeature.selectUser);
  protected readonly isAdmin = this.store.selectSignal(authFeature.selectIsAdmin);

  protected logout(): void {
    this.store.dispatch(ShellActions.logoutClicked());
  }
}
