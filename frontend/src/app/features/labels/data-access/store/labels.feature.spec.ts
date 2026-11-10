import { ShellActions } from '../../../../core/auth/store/auth.actions';
import { LabelsApiActions, LabelsPageActions } from './labels.actions';
import { initialLabelsState, labelsFeature } from './labels.feature';

const { reducer } = labelsFeature;
const bug = { id: 'l1', name: 'Bug', color: '#D32F2F', rowVersion: 'rv1' };
const api = { id: 'l2', name: 'Api', color: '#1976D2', rowVersion: 'rv2' };

function loaded() {
  const opened = reducer(initialLabelsState, LabelsPageActions.opened({ projectId: 'p1' }));
  return reducer(opened, LabelsApiActions.labelsLoaded({ projectId: 'p1', labels: [bug, api] }));
}

describe('labelsFeature', () => {
  it('loads the labels of the open project, sorted by name', () => {
    const state = loaded();

    expect(state.loading).toBe(false);
    expect(state.ids).toEqual(['l2', 'l1']);
  });

  it('ignores labels loaded for another project', () => {
    const opened = reducer(initialLabelsState, LabelsPageActions.opened({ projectId: 'p2' }));

    expect(reducer(opened, LabelsApiActions.labelsLoaded({ projectId: 'p1', labels: [bug] })).ids).toEqual([]);
  });

  it("clears the previous project's labels when another project opens", () => {
    expect(reducer(loaded(), LabelsPageActions.opened({ projectId: 'p2' })).ids).toEqual([]);
  });

  it('keeps the new row version a save returns', () => {
    const saved = reducer(loaded(), LabelsApiActions.labelSaved({ label: { ...bug, rowVersion: 'rv9' }, message: 'labels.saved' }));
    expect(saved.entities['l1']?.rowVersion).toBe('rv9');
  });

  it('replaces the labels with the refreshed ones of the open project only', () => {
    const fresh = { ...bug, name: 'Theirs', rowVersion: 'rv5' };
    const refreshed = reducer(loaded(), LabelsApiActions.labelsRefreshed({ projectId: 'p1', labels: [fresh] }));
    expect(refreshed.ids).toEqual(['l1']);
    expect(refreshed.entities['l1']?.rowVersion).toBe('rv5');

    expect(reducer(loaded(), LabelsApiActions.labelsRefreshed({ projectId: 'p2', labels: [fresh] })).ids).toHaveLength(2);
  });

  it('upserts a saved label and removes a deleted one', () => {
    const renamed = { ...bug, name: 'Zed' };
    const saved = reducer(loaded(), LabelsApiActions.labelSaved({ label: renamed, message: 'labels.saved' }));
    expect(saved.ids).toEqual(['l2', 'l1']);
    expect(saved.entities['l1']?.name).toBe('Zed');

    expect(reducer(saved, LabelsApiActions.labelDeleted({ labelId: 'l1' })).ids).toEqual(['l2']);
  });

  it('tracks saving until the API answers', () => {
    const saving = reducer(loaded(), LabelsPageActions.deleteConfirmed({ labelId: 'l1' }));
    expect(saving.saving).toBe(true);

    const failed = reducer(saving, LabelsApiActions.labelSaveFailed({ error: { code: 'x', fields: {} } }));
    expect(failed.saving).toBe(false);
  });

  it('resets on logout', () => {
    expect(reducer(loaded(), ShellActions.logoutClicked())).toEqual(initialLabelsState);
  });
});
