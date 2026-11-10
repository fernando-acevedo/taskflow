import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { Notifier } from './notifier';

describe('Notifier support code', () => {
  function setup() {
    const transloco = {
      translate: vi.fn((key: string, params?: { code: string }) =>
        key === 'common.supportCode' ? `Support code: ${params?.code}` : key === 'apiErrors.unexpected' ? 'Oops.' : key,
      ),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        { provide: TranslocoService, useValue: transloco },
      ],
    });
    return TestBed.inject(Notifier);
  }

  it('adds the first 8 characters of the trace id to an unexpected error', () => {
    expect(setup().errorMessage({ code: 'unexpected', fields: {}, traceId: '0123456789abcdef' })).toBe(
      'Oops. Support code: 01234567',
    );
  });

  it('adds nothing without a trace id, or to errors the user can act on', () => {
    const notifier = setup();
    expect(notifier.errorMessage({ code: 'unexpected', fields: {} })).toBe('Oops.');
    expect(notifier.errorMessage({ code: 'forbidden', fields: {}, traceId: '0123456789abcdef' })).not.toContain('Support');
  });
});
