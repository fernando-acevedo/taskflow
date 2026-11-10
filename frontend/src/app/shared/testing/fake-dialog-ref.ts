import { NEVER } from 'rxjs';

/** A stand-in for `MatDialogRef` in form-dialog specs: records `close` and never emits Esc or backdrop clicks. */
export function fakeDialogRef(close: () => void = () => undefined) {
  return { close, disableClose: false, keydownEvents: () => NEVER, backdropClick: () => NEVER };
}
