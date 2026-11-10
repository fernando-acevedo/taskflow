import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Observable, filter, merge, of, exhaustMap } from 'rxjs';
import { ConfirmDialogData, confirm } from './confirm-dialog';

/** Implemented by the components of full-page forms, so `unsavedChangesGuard` can ask before leaving them. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

const discardChanges: ConfirmDialogData = {
  title: 'common.unsaved.title',
  message: 'common.unsaved.message',
  confirm: 'common.unsaved.discard',
};

/** Asks whether to throw away the changes. Emits once: true when the user chose to discard them. */
export function confirmDiscard(dialog: MatDialog): Observable<boolean> {
  return confirm(dialog, discardChanges);
}

/** `canDeactivate` for a page implementing `HasUnsavedChanges`: asks before navigating away from a modified form. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (page) =>
  page.hasUnsavedChanges() ? confirmDiscard(inject(MatDialog)) : true;

/**
 * Makes the browser warn before the tab is closed or reloaded while `hasChanges()` is true. Call it from the
 * constructor of the page (an injection context); the listener goes away with the page.
 */
export function warnBeforeUnload(hasChanges: () => boolean): void {
  const view = inject(DOCUMENT).defaultView;
  if (!view) {
    return;
  }
  const listener = (event: BeforeUnloadEvent): void => {
    if (hasChanges()) {
      event.preventDefault();
    }
  };
  view.addEventListener('beforeunload', listener);
  inject(DestroyRef).onDestroy(() => view.removeEventListener('beforeunload', listener));
}

/**
 * For a form dialog: Esc or a click on the backdrop closes it at once while `isModified()` is false, and asks first
 * when it is true. Call it from the dialog's constructor. The Cancel button (`mat-dialog-close`) is unaffected.
 */
export function confirmCloseWhenModified(dialogRef: MatDialogRef<unknown, unknown>, isModified: () => boolean): void {
  const dialog = inject(MatDialog);
  dialogRef.disableClose = true;

  const escape$ = dialogRef.keydownEvents().pipe(
    filter((event) => event.key === 'Escape' && !event.defaultPrevented),
  );
  merge(dialogRef.backdropClick(), escape$)
    .pipe(exhaustMap(() => (isModified() ? confirmDiscard(dialog) : of(true))))
    .subscribe((close) => {
      if (close) {
        dialogRef.close();
      }
    });
}
