import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { CommentEditor } from './comment-editor';

describe('CommentEditor', () => {
  it('keeps the highlighted suggestion inside the list when it shrinks', () => {
    TestBed.configureTestingModule({
      imports: [
        CommentEditor,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    });
    const save = vi.fn(() => of(null));
    const fixture = TestBed.createComponent(CommentEditor);
    fixture.componentRef.setInput('members', [
      { userId: 'u1', displayName: 'Ana' },
      { userId: 'u2', displayName: 'Anabel' },
      { userId: 'u3', displayName: 'Andrea' },
    ]);
    fixture.componentRef.setInput('save', save);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const box = element.querySelector('textarea')!;
    const type = (text: string): void => {
      box.value = text;
      box.setSelectionRange(text.length, text.length);
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    type('@An');
    expect(element.querySelectorAll('.suggestions li')).toHaveLength(3);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    fixture.detectChanges();
    expect(element.querySelector('.suggestions li.active')?.textContent).toContain('Andrea');

    // Without track() resetting it, a narrower list must not leave the index past its end.
    fixture.componentRef.setInput('members', [{ userId: 'u1', displayName: 'Ana' }]);
    fixture.detectChanges();
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    box.dispatchEvent(enter);
    fixture.detectChanges();

    expect(enter.defaultPrevented).toBe(true);
    expect(box.value).toContain('@Ana');
  });
});
