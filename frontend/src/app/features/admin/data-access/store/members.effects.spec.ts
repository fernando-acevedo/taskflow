import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Subject } from 'rxjs';
import { TenantMembersApiService } from '../tenant-members-api.service';
import { MembersApiActions, MembersPageActions } from './members.actions';
import { changeRole, setActive } from './members.effects';
import { initialMembersState, membersFeature } from './members.feature';
import { TenantActions } from '@core/tenant/store/tenant.actions';

describe('members effects', () => {
  it('runs every change, one after another, even when the next one comes before the first answer', () => {
    const answers = [new Subject<void>(), new Subject<void>()];
    const api = {
      setActive: vi.fn(() => answers[0]),
      changeRole: vi.fn(() => answers[1]),
    } as unknown as TenantMembersApiService;
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    setActive(actions$ as Actions, api).subscribe((action) => emitted.push(action));
    changeRole(actions$ as Actions, api).subscribe((action) => emitted.push(action));

    actions$.next(MembersPageActions.activeChanged({ userId: 'a', active: false }));
    actions$.next(MembersPageActions.roleChanged({ userId: 'b', role: 'Admin' }));
    answers[0].next();
    answers[0].complete();
    answers[1].next();
    answers[1].complete();

    expect(api.setActive).toHaveBeenCalledWith('a', false);
    expect(api.changeRole).toHaveBeenCalledWith('b', 'Admin');
    expect(emitted).toEqual([MembersApiActions.memberUpdated(), MembersApiActions.memberUpdated()]);
  });
});

describe('members feature', () => {
  it("forgets the tenant's members when the tenant is switched", () => {
    const loaded = membersFeature.reducer(
      initialMembersState,
      MembersApiActions.membersLoaded({
        members: [
          {
            userId: 'a',
            email: 'a@x.y',
            displayName: 'A',
            role: 'Owner',
            isActive: true,
            isInvitationPending: false,
            joinedAt: '',
          },
        ],
      }),
    );
    expect(loaded.ids).toEqual(['a']);

    expect(membersFeature.reducer(loaded, TenantActions.tenantSwitched({ tenantId: 'x' }))).toEqual(
      initialMembersState,
    );
  });
});
