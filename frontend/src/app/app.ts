import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ShellActions } from '@core/auth/store/auth.actions';
import { authFeature } from '@core/auth/store/auth.feature';
import { TenantSwitcher } from '@core/tenant/tenant-switcher';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { DemoService } from '@core/demo/demo.service';
import { LanguageService } from '@core/i18n/language.service';
import { SUPPORTED_LANGUAGES } from '@core/i18n/languages';
import { ThemeMenu } from '@core/theme/theme-menu';
import { OfflineBanner } from '@core/network/offline-banner';
import { NotificationBell } from '@features/notifications';

const languageNames = new Map<string, string>(
  SUPPORTED_LANGUAGES.map((language) => {
    const name = new Intl.DisplayNames([language], { type: 'language' }).of(language) ?? language;
    return [language, name.charAt(0).toLocaleUpperCase(language) + name.slice(1)];
  }),
);

@Component({
  selector: 'app-root',
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatMenuModule,
    MatToolbarModule,
    NotificationBell,
    OfflineBanner,
    ThemeMenu,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TenantSwitcher,
    TranslocoDirective,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly store = inject(Store);
  protected readonly language = inject(LanguageService);
  protected readonly demo = inject(DemoService).status;
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly user = this.store.selectSignal(authFeature.selectUser);
  /** The platform's super-admin. */
  protected readonly isAdmin = this.store.selectSignal(authFeature.selectIsAdmin);
  /** Owner or Admin of the current tenant. */
  protected readonly canManage = this.store.selectSignal(tenantFeature.selectCanManage);

  /** A skip link must move focus too: the router would treat the bare #main anchor as a navigation. */
  protected skipToContent(event: Event, main: HTMLElement): void {
    event.preventDefault();
    main.focus();
  }

  /** The language's own name ("Español"), so a screen reader doesn't spell out "ES". */
  protected languageName(language: string): string {
    return languageNames.get(language) ?? language;
  }

  protected logout(): void {
    this.store.dispatch(ShellActions.logoutClicked());
  }
}
