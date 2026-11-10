import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { HasUnsavedChanges, confirmCloseWhenModified, unsavedChangesGuard, warnBeforeUnload } from './unsaved-changes';

let modified = false;

@Component({ selector: 'app-form-dialog', template: '<p>form</p>' })
class FormDialog {
  constructor() {
    confirmCloseWhenModified(inject(MatDialogRef), () => modified);
  }
}

@Component({ selector: 'app-page', template: '' })
class Page {
  constructor() {
    warnBeforeUnload(() => modified);
  }
}

const overlay = () => document.querySelector<HTMLElement>('.cdk-overlay-container')!;
const dialogs = () => document.querySelectorAll('mat-dialog-container');
const pressEscape = () =>
  document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }));
/** Waits for the confirmation to render its buttons (zoneless: nothing runs change detection by itself). */
const shown = () =>
  vi.waitFor(() => {
    TestBed.tick();
    expect(buttons().length).toBe(2);
  });
/** The buttons of the topmost dialog: Cancel / Keep first, the confirming one last. */
const buttons = () => Array.from(dialogs()[dialogs().length - 1]?.querySelectorAll<HTMLElement>('mat-dialog-actions button') ?? []);

describe('unsaved changes', () => {
  beforeEach(() => {
    modified = false;
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    });
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  describe('unsavedChangesGuard', () => {
    const run = (page: HasUnsavedChanges) =>
      TestBed.runInInjectionContext(() => unsavedChangesGuard(page, null as never, null as never, null as never));

    it('lets a page without changes go', () => {
      expect(run({ hasUnsavedChanges: () => false })).toBe(true);
      expect(dialogs().length).toBe(0);
    });

    it('asks first when the page has changes, and goes only if the user discards them', async () => {
      const dialog = TestBed.inject(MatDialog);
      const open = vi.spyOn(dialog, 'open');

      const result = run({ hasUnsavedChanges: () => true });

      expect(isObservable(result)).toBe(true);
      expect(open).toHaveBeenCalledOnce();
      expect(open.mock.calls[0]?.[1]?.data).toMatchObject({ title: 'common.unsaved.title', confirm: 'common.unsaved.discard' });

      open.mockReturnValueOnce({ afterClosed: () => of(false) } as never);
      expect(await firstValueFrom(run({ hasUnsavedChanges: () => true }) as never)).toBe(false);
      open.mockReturnValueOnce({ afterClosed: () => of(true) } as never);
      expect(await firstValueFrom(run({ hasUnsavedChanges: () => true }) as never)).toBe(true);
    });
  });

  describe('warnBeforeUnload', () => {
    it('blocks the unload only while the page is modified, and stops with the page', () => {
      const fixture = TestBed.createComponent(Page);
      const unload = () => {
        const event = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(event);
        return event.defaultPrevented;
      };

      expect(unload()).toBe(false);
      modified = true;
      expect(unload()).toBe(true);

      fixture.destroy();
      expect(unload()).toBe(false);
    });
  });

  describe('confirmCloseWhenModified', () => {
    function open() {
      const ref = TestBed.inject(MatDialog).open(FormDialog);
      return { ref, close: vi.spyOn(ref, 'close') };
    }

    it('asks before Esc closes a modified form, and keeps it open when the user cancels', async () => {
      modified = true;
      const { ref, close } = open();
      expect(ref.disableClose).toBe(true);

      pressEscape();
      await vi.waitFor(() => expect(dialogs().length).toBe(2)); // the form and the confirmation
      await shown();
      buttons()[0]?.click();
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(close).not.toHaveBeenCalled();
    });

    it('closes after the user confirms the discard (backdrop click)', async () => {
      modified = true;
      const { close } = open();

      overlay().querySelector<HTMLElement>('.cdk-overlay-backdrop')!.click();
      await vi.waitFor(() => expect(dialogs().length).toBe(2));
      expect(close).not.toHaveBeenCalled();
      await shown();
      buttons()[1]?.click();

      await vi.waitFor(() => expect(close).toHaveBeenCalledOnce());
    });

    it('closes at once, without asking, when the form is untouched', async () => {
      const { close } = open();

      pressEscape();

      await vi.waitFor(() => expect(close).toHaveBeenCalledOnce());
      expect(dialogs().length).toBeLessThanOrEqual(1);
    });
  });
});
