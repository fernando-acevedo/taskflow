import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action, Store, createAction, props } from '@ngrx/store';
import { Subject } from 'rxjs';
import { ApiError } from '../../core/http/api-error';
import { Submissions } from './submission';

const submitted = createAction('[Test] Submitted', props<{ submissionId: number }>());
const saved = createAction('[Test] Saved', props<{ submissionId?: number }>());
const failed = createAction('[Test] Failed', props<{ error: ApiError; submissionId?: number }>());

describe('Submissions.dispatchAndWait', () => {
  let actions$: Subject<Action>;
  let submissions: Submissions;

  beforeEach(() => {
    actions$ = new Subject<Action>();
    TestBed.configureTestingModule({
      providers: [
        { provide: Actions, useValue: actions$ },
        // The real store sends dispatched actions through the actions stream, so the fake does the same.
        { provide: Store, useValue: { dispatch: (action: Action) => actions$.next(action) } },
      ],
    });
    submissions = TestBed.inject(Submissions);
  });

  it('emits null when the success action for the same submission arrives', () => {
    const results: (ApiError | null)[] = [];
    submissions.dispatchAndWait(submitted({ submissionId: 7 })).subscribe((result) => results.push(result));

    actions$.next(saved({ submissionId: 6 }));
    actions$.next(saved({ submissionId: 7 }));

    expect(results).toEqual([null]);
  });

  it("emits the error of the submission's failure action", () => {
    const error: ApiError = { code: 'Users.EmailTaken', fields: {} };
    const results: (ApiError | null)[] = [];
    submissions.dispatchAndWait(submitted({ submissionId: 8 })).subscribe((result) => results.push(result));

    actions$.next(failed({ error, submissionId: 8 }));

    expect(results).toEqual([error]);
  });
});
