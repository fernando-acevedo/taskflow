import { Action } from '@ngrx/store';
import { Subject } from 'rxjs';
import { AdminUsersApiService } from '../admin-users-api.service';
import { AdminUsersApiActions, UsersPageActions } from './admin-users.actions';
import { updateUser } from './admin-users.effects';

describe('admin users effects', () => {
  it('runs every change, one after another, even when the next one comes before the first answer', () => {
    const answers = [new Subject<void>(), new Subject<void>()];
    const api = {
      setActive: vi.fn(() => answers[0]),
      setAdmin: vi.fn(() => answers[1]),
    } as unknown as AdminUsersApiService;
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    updateUser(actions$, api).subscribe((action) => emitted.push(action));

    actions$.next(UsersPageActions.activeChanged({ id: 'a', active: false }));
    actions$.next(UsersPageActions.adminChanged({ id: 'b', admin: true }));
    expect(api.setAdmin).not.toHaveBeenCalled();

    answers[0].next();
    answers[0].complete();
    answers[1].next();
    answers[1].complete();

    expect(api.setActive).toHaveBeenCalledWith('a', false);
    expect(api.setAdmin).toHaveBeenCalledWith('b', true);
    expect(emitted).toEqual([AdminUsersApiActions.userUpdated(), AdminUsersApiActions.userUpdated()]);
  });
});
