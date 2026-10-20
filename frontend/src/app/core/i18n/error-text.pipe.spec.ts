import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { ErrorTextPipe } from './error-text.pipe';

describe('ErrorTextPipe', () => {
  let pipe: ErrorTextPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { errors: { PasswordTooShort: 'Too short.', unexpected: 'Something went wrong.' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
    });
    pipe = TestBed.runInInjectionContext(() => new ErrorTextPipe());
  });

  it('translates a known code', () => {
    expect(pipe.transform('PasswordTooShort')).toBe('Too short.');
  });

  it('falls back to the generic message for a code without a translation', () => {
    expect(pipe.transform('PasswordRequiresUniqueChars')).toBe('Something went wrong.');
  });
});
