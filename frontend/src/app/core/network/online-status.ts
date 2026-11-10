import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/** Whether the browser reports a network connection, following the window's `online` / `offline` events. */
@Injectable({ providedIn: 'root' })
export class OnlineStatus {
  private readonly view = inject(DOCUMENT).defaultView;
  private readonly state = signal(this.view?.navigator.onLine ?? true);
  readonly online = this.state.asReadonly();

  constructor() {
    const view = this.view;
    if (!view) {
      return;
    }
    const goOnline = (): void => this.state.set(true);
    const goOffline = (): void => this.state.set(false);
    view.addEventListener('online', goOnline);
    view.addEventListener('offline', goOffline);
    inject(DestroyRef).onDestroy(() => {
      view.removeEventListener('online', goOnline);
      view.removeEventListener('offline', goOffline);
    });
  }
}
