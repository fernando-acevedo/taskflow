import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from './core/i18n/language.service';
import { SUPPORTED_LANGUAGES } from './core/i18n/languages';

@Component({
  selector: 'app-root',
  imports: [MatButtonToggleModule, MatToolbarModule, RouterOutlet, TranslocoDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly language = inject(LanguageService);
  protected readonly languages = SUPPORTED_LANGUAGES;
}
