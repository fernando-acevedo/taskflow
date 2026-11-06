import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { TenantActions } from '../../../../core/tenant/store/tenant.actions';
import { ApiError } from '../../../../core/http/api-error';
import { Label } from '../labels-api.service';
import { LabelsApiActions, LabelsPageActions } from './labels.actions';

export interface LabelsState extends EntityState<Label> {
  projectId: string | null;
  loading: boolean;
  loadError: ApiError | null;
  saving: boolean;
}

const adapter = createEntityAdapter<Label>({ sortComparer: (a, b) => a.name.localeCompare(b.name) });

export const initialLabelsState: LabelsState = adapter.getInitialState({
  projectId: null,
  loading: false,
  loadError: null,
  saving: false,
});

export const labelsFeature = createFeature({
  name: 'labels',
  reducer: createReducer(
    initialLabelsState,
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      TenantActions.tenantSwitched,
      (): LabelsState => initialLabelsState,
    ),
    on(LabelsPageActions.opened, (state, { projectId }): LabelsState =>
      state.projectId === projectId
        ? { ...state, loading: true, loadError: null }
        : adapter.removeAll({ ...state, projectId, loading: true, loadError: null }),
    ),
    on(LabelsApiActions.labelsLoaded, (state, { projectId, labels }): LabelsState =>
      projectId === state.projectId ? adapter.setAll(labels, { ...state, loading: false }) : state,
    ),
    on(LabelsApiActions.labelsLoadFailed, (state, { error }): LabelsState => ({ ...state, loading: false, loadError: error })),
    on(
      LabelsPageActions.createSubmitted,
      LabelsPageActions.updateSubmitted,
      LabelsPageActions.deleteConfirmed,
      (state): LabelsState => ({ ...state, saving: true }),
    ),
    on(LabelsApiActions.labelSaved, (state, { label }): LabelsState => adapter.upsertOne(label, { ...state, saving: false })),
    on(LabelsApiActions.labelDeleted, (state, { labelId }): LabelsState =>
      adapter.removeOne(labelId, { ...state, saving: false }),
    ),
    on(LabelsApiActions.labelSaveFailed, (state): LabelsState => ({ ...state, saving: false })),
  ),
  extraSelectors: ({ selectLabelsState }) => adapter.getSelectors(selectLabelsState),
});
