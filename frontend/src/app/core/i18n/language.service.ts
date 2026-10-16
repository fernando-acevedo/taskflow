import { DOCUMENT, Injectable, Signal, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { LANGUAGE_STORAGE_KEY, Language } from './languages';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  readonly current: Signal<Language> = toSignal(
    this.transloco.langChanges$.pipe(map((language) => language as Language)),
    { initialValue: this.transloco.getActiveLang() as Language },
  );

  constructor() {
    effect(() => {
      this.document.documentElement.lang = this.current();
    });
  }

  use(language: Language): void {
    this.transloco.setActiveLang(language);
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage can be unavailable (private mode); the choice still applies to this session.
    }
  }
}
