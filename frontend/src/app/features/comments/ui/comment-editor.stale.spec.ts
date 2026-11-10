import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { ApiError } from '@shared/data-access/api-error';
import en from '../../../../../public/i18n/en.json';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { CommentEditor } from './comment-editor';

const stale = { code: 'Concurrency.Stale', fields: {} };

describe('CommentEditor when someone else changed the comment (stale)', () => {
  function render(save: () => Observable<ApiError | null>, refresh: (() => Observable<string | null>) | null) {
    TestBed.configureTestingModule({
      imports: [
        CommentEditor,
        TranslocoTestingModule.forRoot({ langs: { en }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    });
    const fixture = TestBed.createComponent(CommentEditor);
    fixture.componentRef.setInput('members', []);
    fixture.componentRef.setInput('save', save);
    fixture.componentRef.setInput('initialBody', 'Original');
    fixture.componentRef.setInput('refresh', refresh);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const box = element.querySelector('textarea')!;
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    return {
      fixture,
      element,
      box,
      saved,
      typeAndSubmit: (text: string) => {
        box.value = text;
        box.dispatchEvent(new Event('input'));
        element.querySelector('form')?.dispatchEvent(new Event('submit'));
        fixture.detectChanges();
      },
      press: (index: number) => {
        element.querySelectorAll<HTMLButtonElement>('app-stale-notice button')[index].click();
        fixture.detectChanges();
      },
    };
  }

  it('shows the notice inline and keeps what was typed', () => {
    const view = render(() => of(stale), null);

    view.typeAndSubmit('Mine');

    expect(view.element.querySelector('app-stale-notice [role="alert"]')).not.toBeNull();
    expect(view.box.value).toBe('Mine');
    expect(view.saved).not.toHaveBeenCalled();
  });

  it('Reload brings the current text of the comment', () => {
    const view = render(() => of(stale), () => of('Theirs'));
    view.typeAndSubmit('Mine');

    view.press(0);

    expect(view.box.value).toBe('Theirs');
    expect(view.element.querySelector('app-stale-notice')).toBeNull();
  });

  it('Save anyway refreshes the row version and saves the typed text again', () => {
    const save = vi.fn().mockReturnValueOnce(of(stale)).mockReturnValueOnce(of(null));
    const refresh = vi.fn(() => of('Theirs'));
    const view = render(save, refresh);
    view.typeAndSubmit('Mine');

    view.press(1);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledTimes(2);
    expect(save).toHaveBeenLastCalledWith({ body: 'Mine', mentionedUserIds: [] });
    expect(view.saved).toHaveBeenCalled();
  });

  it('keeps the notice when the comment cannot be fetched', () => {
    const view = render(() => of(stale), () => of(null));
    view.typeAndSubmit('Mine');

    view.press(0);

    expect(view.element.querySelector('app-stale-notice')).not.toBeNull();
    expect(view.box.value).toBe('Mine');
  });

  it('focuses Reload when the stale notice appears and has no accessibility violations', async () => {
    const view = render(() => of(stale), null);
    view.typeAndSubmit('Mine');
    TestBed.tick();

    expect(document.activeElement?.textContent?.trim()).toBe('Reload');
    await expectNoAxeViolations(view.fixture);
  });
});
