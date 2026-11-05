import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { HoursBar } from './hours-bar';

describe('HoursBar', () => {
  function render(actual: number, estimated: number | null): HTMLElement {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        HoursBar,
        TranslocoTestingModule.forRoot({
          langs: { en: { time: { of: '{{ actual }} of {{ estimate }}', noEstimate: 'logged', overBy: 'over by {{ time }}' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    });
    const fixture = TestBed.createComponent(HoursBar);
    fixture.componentRef.setInput('actual', actual);
    fixture.componentRef.setInput('estimated', estimated);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const width = (element: HTMLElement) => element.querySelector<HTMLElement>('.fill')?.style.width;

  it('fills the bar by the share of the estimate and writes the numbers as text', () => {
    const element = render(150, 300);

    expect(width(element)).toBe('50%');
    expect(element.querySelector('.text')?.textContent?.trim()).toBe('2 h 30 min of 5 h');
    expect(element.querySelector('.over-text')).toBeNull();
  });

  it('caps the bar and flags the overrun with text and an icon when over the estimate', () => {
    const element = render(300, 240);

    expect(width(element)).toBe('100%');
    expect(element.querySelector('.fill')?.classList).toContain('over');
    expect(element.querySelector('.over-text')?.textContent).toContain('over by 1 h');
    expect(element.querySelector('.over-text mat-icon')).not.toBeNull();
  });

  it('shows only the total, without a bar, when there is no estimate', () => {
    const element = render(90, null);

    expect(element.querySelector('.track')).toBeNull();
    expect(element.querySelector('.text')?.textContent?.trim()).toBe('1 h 30 min logged');
  });

  it('is not over when the logged time equals the estimate', () => {
    expect(render(60, 60).querySelector('.over-text')).toBeNull();
  });
});
