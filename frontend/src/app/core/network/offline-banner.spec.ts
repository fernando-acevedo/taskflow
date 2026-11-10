import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { OfflineBanner } from './offline-banner';
import { OnlineStatus } from './online-status';

@Component({ selector: 'app-host', imports: [OfflineBanner], template: '<app-offline-banner />' })
class Host {}

describe('OfflineBanner', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { app: { offline: 'You are offline' } } }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const region = (root: HTMLElement) => root.querySelector<HTMLElement>('[role="status"]')!;

  afterEach(() => vi.restoreAllMocks());

  it('shows nothing while online', () => {
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    const fixture = render();

    expect(region(fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });

  it('announces the loss of connection and clears when it returns', async () => {
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    const fixture = render();

    window.dispatchEvent(new Event('offline'));
    await fixture.whenStable();
    expect(TestBed.inject(OnlineStatus).online()).toBe(false);
    expect(region(fixture.nativeElement as HTMLElement).textContent).toContain('You are offline');

    window.dispatchEvent(new Event('online'));
    await fixture.whenStable();
    expect(region(fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });

  it('starts offline when the browser already is', () => {
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
    const fixture = render();

    expect(region(fixture.nativeElement as HTMLElement).textContent).toContain('You are offline');
  });
});
