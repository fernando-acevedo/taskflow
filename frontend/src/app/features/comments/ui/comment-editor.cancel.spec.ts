import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { CommentEditor } from './comment-editor';

describe('CommentEditor cancel', () => {
  function setup(confirmed: boolean) {
    TestBed.configureTestingModule({
      imports: [
        CommentEditor,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    });
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(confirmed) } as never);
    const fixture = TestBed.createComponent(CommentEditor);
    fixture.componentRef.setInput('members', []);
    fixture.componentRef.setInput('save', () => of(null));
    fixture.componentRef.setInput('cancellable', true);
    const cancelled = vi.fn();
    fixture.componentInstance.cancelled.subscribe(cancelled);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const type = (text: string): void => {
      const box = element.querySelector('textarea')!;
      box.value = text;
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const cancel = (): void => element.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
    return { open, cancelled, type, cancel };
  }

  it('cancels at once while the text is untouched', () => {
    const { open, cancelled, cancel } = setup(false);

    cancel();

    expect(open).not.toHaveBeenCalled();
    expect(cancelled).toHaveBeenCalledOnce();
  });

  it('asks before discarding a modified text, and keeps editing if the user declines', () => {
    const { open, cancelled, type, cancel } = setup(false);

    type('half a thought');
    cancel();

    expect(open).toHaveBeenCalledOnce();
    expect(cancelled).not.toHaveBeenCalled();
  });

  it('cancels once the user confirms the discard', () => {
    const { cancelled, type, cancel } = setup(true);

    type('half a thought');
    cancel();

    expect(cancelled).toHaveBeenCalledOnce();
  });
});
