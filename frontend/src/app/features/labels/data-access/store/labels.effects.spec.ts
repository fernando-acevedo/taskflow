import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { isStale } from '@shared/data-access/api-error';
import { Notifier } from '@core/notifications/notifier';
import { LabelsApiService } from '../labels-api.service';
import { LabelsApiActions, LabelsPageActions } from './labels.actions';
import { createLabel, deleteLabel, notify, refreshLabels, updateLabel } from './labels.effects';
import { labelsFeature } from './labels.feature';

const current = { id: 'l1', name: 'Bug', color: '#D32F2F', rowVersion: 'rv1' };

function storeWith(projectId: string | null): Store {
  return {
    select: (selector: unknown) =>
      of(
        selector === labelsFeature.selectProjectId
          ? projectId
          : selector === labelsFeature.selectEntities
            ? { l1: current }
            : undefined,
      ),
  } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const input = { name: 'Bug', color: '#D32F2F' };
const label = { id: 'l1', ...input, rowVersion: 'rv2' };

describe('labels effects', () => {
  it('creates a label in the open project and answers with the submission id', () => {
    const api = { create: vi.fn(() => of(label)) } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.createSubmitted({ label: input, submissionId: 3 })) as Actions;

    const emitted = run(createLabel(actions$, storeWith('p1'), api));

    expect(api.create).toHaveBeenCalledWith('p1', input);
    expect(emitted).toEqual([LabelsApiActions.labelSaved({ label, message: 'labels.created', submissionId: 3 })]);
  });

  it('does not create a label without an open project', () => {
    const api = { create: vi.fn() } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.createSubmitted({ label: input, submissionId: 3 })) as Actions;

    const emitted = run(createLabel(actions$, storeWith(null), api));

    expect(api.create).not.toHaveBeenCalled();
    expect(emitted).toEqual([LabelsApiActions.labelSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 3 })]);
  });

  it('reports a name already taken to the form', () => {
    const api = {
      update: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Labels.NameTaken' } }))),
    } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.updateSubmitted({ labelId: 'l1', changes: input, rowVersion: 'rv1', submissionId: 5 })) as Actions;

    const emitted = run(updateLabel(actions$, storeWith('p1'), api));

    expect(api.update).toHaveBeenCalledWith('p1', 'l1', input, 'rv1');
    expect(emitted).toEqual([
      LabelsApiActions.labelSaveFailed({ error: { code: 'Labels.NameTaken', fields: {} }, submissionId: 5 }),
    ]);
  });

  it('sends the stored row version and answers a stale edit with the submission id', () => {
    const api = {
      update: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Concurrency.Stale' } }))),
    } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.updateSubmitted({ labelId: 'l1', changes: input, rowVersion: 'rv1', submissionId: 6 })) as Actions;

    const [failed] = run(updateLabel(actions$, storeWith('p1'), api));

    expect(api.update).toHaveBeenCalledWith('p1', 'l1', input, 'rv1');
    expect(failed.type).toBe(LabelsApiActions.labelSaveFailed.type);
    expect(isStale((failed as ReturnType<typeof LabelsApiActions.labelSaveFailed>).error)).toBe(true);
  });

  it('sends the version the dialog opened with, not the one a background reload put in the store', () => {
    const api = { update: vi.fn(() => of(label)) } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.updateSubmitted({ labelId: 'l1', changes: input, rowVersion: 'rv-opened', submissionId: 10 })) as Actions;

    run(updateLabel(actions$, storeWith('p1'), api));

    expect(api.update).toHaveBeenCalledWith('p1', 'l1', input, 'rv-opened');
  });

  it('does not update a label the store does not hold', () => {
    const api = { update: vi.fn() } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.updateSubmitted({ labelId: 'gone', changes: input, rowVersion: 'rv1', submissionId: 7 })) as Actions;

    const emitted = run(updateLabel(actions$, storeWith('p1'), api));

    expect(api.update).not.toHaveBeenCalled();
    expect(emitted).toEqual([LabelsApiActions.labelSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 7 })]);
  });

  it('refreshes the labels for the dialog, tagged with its submission id', () => {
    const api = { list: vi.fn(() => of([label])) } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.refreshRequested({ submissionId: 8 })) as Actions;

    const emitted = run(refreshLabels(actions$, storeWith('p1'), api));

    expect(api.list).toHaveBeenCalledWith('p1');
    expect(emitted).toEqual([LabelsApiActions.labelsRefreshed({ projectId: 'p1', labels: [label], submissionId: 8 })]);
  });

  it('keeps a stale edit out of the snackbar but shows other errors', () => {
    const notifier = { success: vi.fn(), error: vi.fn() } as unknown as Notifier;
    const stale = { code: 'Concurrency.Stale', fields: {} };
    const other = { code: 'unexpected', fields: {} };
    const actions$ = of(
      LabelsApiActions.labelSaveFailed({ error: stale, submissionId: 1 }),
      LabelsApiActions.labelSaveFailed({ error: other, submissionId: 2 }),
    ) as Actions;

    run(notify(actions$, notifier));

    expect(notifier.error).toHaveBeenCalledTimes(1);
    expect(notifier.error).toHaveBeenCalledWith(other);
  });

  it('deletes a label', () => {
    const api = { delete: vi.fn(() => of(undefined)) } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.deleteConfirmed({ labelId: 'l1' })) as Actions;

    const emitted = run(deleteLabel(actions$, storeWith('p1'), api));

    expect(api.delete).toHaveBeenCalledWith('p1', 'l1');
    expect(emitted).toEqual([LabelsApiActions.labelDeleted({ labelId: 'l1' })]);
  });
});
