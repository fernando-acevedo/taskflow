import { HealthReport, SystemInfo } from '../status-api.service';
import { StatusApiActions, StatusPageActions } from './status.actions';
import { initialStatusState, statusFeature } from './status.feature';

const health: HealthReport = {
  status: 'Healthy',
  totalDurationMs: 12,
  checks: [{ name: 'database', status: 'Healthy', durationMs: 10 }],
};

const info: SystemInfo = {
  name: 'TaskFlow API',
  version: '1.0.0',
  environment: 'Development',
  culture: 'es',
  message: 'Bienvenido a la API de TaskFlow.',
};

describe('statusFeature', () => {
  const { reducer, selectOverallStatus } = statusFeature;

  it('starts loading and clears the previous error when status is requested', () => {
    const failed = { ...initialStatusState, error: 'HTTP 0' };

    const state = reducer(failed, StatusPageActions.statusRequested());

    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('stores health and info when the API answers', () => {
    const state = reducer(
      { ...initialStatusState, loading: true },
      StatusApiActions.statusLoaded({ health, info, checkedAt: '2026-10-16T20:00:00Z' }),
    );

    expect(state).toEqual({ health, info, loading: false, error: null, checkedAt: '2026-10-16T20:00:00Z' });
    expect(selectOverallStatus.projector(state.health, state.error)).toBe('Healthy');
  });

  it('reports Unreachable when the API call fails', () => {
    const state = reducer(
      { ...initialStatusState, health, info, loading: true },
      StatusApiActions.statusLoadFailed({ error: 'HTTP 0', checkedAt: '2026-10-16T20:00:00Z' }),
    );

    expect(state.health).toBeNull();
    expect(selectOverallStatus.projector(state.health, state.error)).toBe('Unreachable');
  });
});
