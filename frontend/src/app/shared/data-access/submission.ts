import { Injectable, inject } from '@angular/core';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, filter, map, take } from 'rxjs';
import { ApiError } from '../../core/http/api-error';

/**
 * Tags a form submission so the form can wait for its own outcome: effects copy `submissionId` from the
 * submitted action to the success or failure action they answer with.
 */
export interface Submission {
  readonly submissionId?: number;
}

/** What a form dialog gets to save its values: the outcome as `Submissions.dispatchAndWait` reports it. */
export type SaveFn<T> = (input: T) => Observable<ApiError | null>;

let lastSubmissionId = 0;

export function nextSubmissionId(): number {
  return ++lastSubmissionId;
}

/** Lets dialogs and forms stay open, keeping what was typed, until the API accepted what they submitted. */
@Injectable({ providedIn: 'root' })
export class Submissions {
  private readonly store = inject(Store);
  private readonly actions$ = inject(Actions);

  /** Dispatches `submitted` and emits once when the API answered it: `null` on success, or the error. */
  dispatchAndWait(submitted: Action & Required<Submission>): Observable<ApiError | null> {
    return new Observable<ApiError | null>((subscriber) => {
      const subscription = this.actions$
        .pipe(
          filter(
            (action: Action & Submission & { error?: ApiError }) =>
              action.type !== submitted.type && action.submissionId === submitted.submissionId,
          ),
          take(1),
          // Failure actions carry `error`; success actions don't.
          map((action) => action.error ?? null),
        )
        .subscribe(subscriber);
      this.store.dispatch(submitted);
      return subscription;
    });
  }
}
