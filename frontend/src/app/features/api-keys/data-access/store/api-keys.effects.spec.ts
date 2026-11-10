import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Subject, of, throwError } from 'rxjs';
import { AuthApiActions } from '@core/auth/store/auth.actions';
import { Notifier } from '@core/notifications/notifier';
import { ApiKey, ApiKeysApiService, CreatedApiKey } from '../api-keys-api.service';
import { ApiKeysApiActions, ApiKeysPageActions } from './api-keys.actions';
import { createKey, notify, revokeKey } from './api-keys.effects';
import { apiKeysFeature, initialApiKeysState } from './api-keys.feature';

const key: ApiKey = {
  id: 'k1',
  name: 'CI',
  prefix: 'tf_ab12',
  scope: 'Read',
  createdAt: '2026-11-01T00:00:00Z',
  expiresAt: '2027-02-01T00:00:00Z',
  revokedAt: null,
  lastUsedAt: null,
  isActive: true,
};

describe('api keys effects', () => {
  it('answers a creation with the secret and the submission id', () => {
    const created: CreatedApiKey = { ...key, secret: 'tf_ab12_SECRET' };
    const api = { create: vi.fn(() => of(created)) } as unknown as ApiKeysApiService;
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    createKey(actions$ as Actions, api).subscribe((action) => emitted.push(action));

    const input = { name: 'CI', scope: 'Read' as const, expiresAt: null };
    actions$.next(ApiKeysPageActions.createSubmitted({ key: input, submissionId: 7 }));

    expect(api.create).toHaveBeenCalledWith(input);
    expect(emitted).toEqual([
      ApiKeysApiActions.keyCreated({ secret: 'tf_ab12_SECRET', name: 'CI', submissionId: 7 }),
    ]);
  });

  it('reports a failed creation with the submission id', () => {
    const api = {
      create: vi.fn(() => throwError(() => ({ status: 400 }))),
    } as unknown as ApiKeysApiService;
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    createKey(actions$ as Actions, api).subscribe((action) => emitted.push(action));

    actions$.next(
      ApiKeysPageActions.createSubmitted({ key: { name: 'x', scope: 'Write', expiresAt: null }, submissionId: 3 }),
    );

    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toMatchObject({ type: ApiKeysApiActions.writeFailed.type, submissionId: 3 });
  });

  it('runs revocations one after another', () => {
    const answers = [new Subject<void>(), new Subject<void>()];
    const api = {
      revoke: vi.fn().mockReturnValueOnce(answers[0]).mockReturnValueOnce(answers[1]),
    } as unknown as ApiKeysApiService;
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    revokeKey(actions$ as Actions, api).subscribe((action) => emitted.push(action));

    actions$.next(ApiKeysPageActions.revokeConfirmed({ keyId: 'a' }));
    actions$.next(ApiKeysPageActions.revokeConfirmed({ keyId: 'b' }));
    expect(api.revoke).toHaveBeenCalledTimes(1);
    answers[0].next();
    answers[0].complete();
    expect(api.revoke).toHaveBeenCalledTimes(2);
    answers[1].next();
    answers[1].complete();

    expect(emitted).toEqual([ApiKeysApiActions.keyRevoked(), ApiKeysApiActions.keyRevoked()]);
  });

  it('notifies a revocation and a failure', () => {
    const notifier = { success: vi.fn(), error: vi.fn() } as unknown as Notifier;
    const actions$ = new Subject<Action>();
    notify(actions$ as Actions, notifier).subscribe();

    actions$.next(ApiKeysApiActions.keyRevoked());
    const error = { code: 'Demo.ActionDisabled', fields: {} };
    actions$.next(ApiKeysApiActions.writeFailed({ error }));

    expect(notifier.success).toHaveBeenCalledWith('profile.apiKeys.revoked');
    expect(notifier.error).toHaveBeenCalledWith(error);
  });
});

describe('api keys feature', () => {
  it('keeps the loaded keys and never the secret of a created one', () => {
    const loaded = apiKeysFeature.reducer(initialApiKeysState, ApiKeysApiActions.keysLoaded({ keys: [key] }));
    const created = apiKeysFeature.reducer(
      loaded,
      ApiKeysApiActions.keyCreated({ secret: 'tf_ab12_SECRET', name: 'CI', submissionId: 1 }),
    );

    expect(created.ids).toEqual(['k1']);
    expect(JSON.stringify(created)).not.toContain('SECRET');
  });

  it('forgets the keys when the session expires', () => {
    const loaded = apiKeysFeature.reducer(initialApiKeysState, ApiKeysApiActions.keysLoaded({ keys: [key] }));

    expect(apiKeysFeature.reducer(loaded, AuthApiActions.sessionExpired())).toEqual(initialApiKeysState);
  });
});
