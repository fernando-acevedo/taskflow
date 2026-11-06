import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { TranslatedTitleStrategy } from './translated-title.strategy';

@Component({ template: '' })
class Blank {}

describe('TranslatedTitleStrategy', () => {
  const announce = vi.fn(() => Promise.resolve());

  beforeEach(() => {
    announce.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { titles: { a: 'Alpha', b: 'Beta' } }, es: { titles: { a: 'Alfa', b: 'Beta' } } },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
        }),
      ],
      providers: [
        provideRouter([
          { path: 'a', title: 'titles.a', component: Blank },
          { path: 'b', title: 'titles.b', component: Blank },
          { path: 'none', component: Blank },
        ]),
        { provide: TitleStrategy, useExisting: TranslatedTitleStrategy },
        { provide: LiveAnnouncer, useValue: { announce } },
      ],
    });
  });

  it('translates the route title, follows the language and announces later navigations only', async () => {
    const harness = await RouterTestingHarness.create('/a');
    const title = TestBed.inject(Title);
    expect(title.getTitle()).toBe('Alpha · TaskFlow');
    expect(announce).not.toHaveBeenCalled();

    TestBed.inject(TranslocoService).setActiveLang('es');
    expect(title.getTitle()).toBe('Alfa · TaskFlow');

    await harness.navigateByUrl('/b');
    expect(title.getTitle()).toBe('Beta · TaskFlow');
    expect(announce).toHaveBeenCalledWith('Beta', 'polite');
  });

  it('does not announce query-string-only changes', async () => {
    const harness = await RouterTestingHarness.create('/a');
    await harness.navigateByUrl('/a?x=1');
    expect(announce).not.toHaveBeenCalled();

    await harness.navigateByUrl('/b');
    expect(announce).toHaveBeenCalledTimes(1);
  });

  it('falls back to the app name when a route has no title', async () => {
    await RouterTestingHarness.create('/none');

    expect(TestBed.inject(Title).getTitle()).toBe('TaskFlow');
  });
});
