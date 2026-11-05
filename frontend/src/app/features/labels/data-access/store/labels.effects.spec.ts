import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { LabelsApiService } from '../labels-api.service';
import { LabelsApiActions, LabelsPageActions } from './labels.actions';
import { createLabel, deleteLabel, updateLabel } from './labels.effects';
import { labelsFeature } from './labels.feature';

function storeWith(projectId: string | null): Store {
  return { select: (selector: unknown) => of(selector === labelsFeature.selectProjectId ? projectId : undefined) } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const input = { name: 'Bug', color: '#D32F2F' };
const label = { id: 'l1', ...input };

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
    const actions$ = of(LabelsPageActions.updateSubmitted({ labelId: 'l1', changes: input, submissionId: 5 })) as Actions;

    const emitted = run(updateLabel(actions$, storeWith('p1'), api));

    expect(api.update).toHaveBeenCalledWith('p1', 'l1', input);
    expect(emitted).toEqual([
      LabelsApiActions.labelSaveFailed({ error: { code: 'Labels.NameTaken', fields: {} }, submissionId: 5 }),
    ]);
  });

  it('deletes a label', () => {
    const api = { delete: vi.fn(() => of(undefined)) } as unknown as LabelsApiService;
    const actions$ = of(LabelsPageActions.deleteConfirmed({ labelId: 'l1' })) as Actions;

    const emitted = run(deleteLabel(actions$, storeWith('p1'), api));

    expect(api.delete).toHaveBeenCalledWith('p1', 'l1');
    expect(emitted).toEqual([LabelsApiActions.labelDeleted({ labelId: 'l1' })]);
  });
});
