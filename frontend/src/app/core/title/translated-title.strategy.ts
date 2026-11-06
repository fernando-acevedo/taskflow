import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, distinctUntilChanged, switchMap, take } from 'rxjs';

const APP_NAME = 'TaskFlow';

/**
 * Route `title`s are translation keys. The document title follows the active language, and each navigation after
 * the first is announced to screen readers (a single-page app otherwise says nothing when the page changes).
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly announcer = inject(LiveAnnouncer);
  private navigated = false;
  private lastPath = '';
  private readonly key$ = new BehaviorSubject<string | undefined>(undefined);

  constructor() {
    super();
    this.key$
      .pipe(
        distinctUntilChanged(),
        switchMap((key) => (key ? this.transloco.selectTranslate(key) : [''])),
      )
      .subscribe((page) => this.title.setTitle(page ? `${page} · ${APP_NAME}` : APP_NAME));
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);
    this.key$.next(key);
    // The first title is read with the page itself; later ones are announced, but not when only the query string
    // or fragment changed (the task list filters navigate that way).
    const path = snapshot.url.split(/[?#]/)[0];
    if (this.navigated && key && path !== this.lastPath) {
      this.transloco
        .selectTranslate(key)
        .pipe(take(1))
        .subscribe((page: string) => void this.announcer.announce(page, 'polite'));
    }
    this.navigated = true;
    this.lastPath = path;
  }
}
