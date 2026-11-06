import { TenantMember } from '../data-access/tenant-members-api.service';
import { MemberActionContext, assignableRoles, memberActions } from './member-permissions';

function member(overrides: Partial<TenantMember> = {}): TenantMember {
  return {
    userId: 'm1',
    email: 'm@tests.local',
    displayName: 'Mia',
    role: 'Member',
    isActive: true,
    isInvitationPending: false,
    joinedAt: '2026-11-01T00:00:00Z',
    ...overrides,
  };
}

const owner: MemberActionContext = { meId: 'me', actorIsOwner: true, platformAdmin: false };
const admin: MemberActionContext = { meId: 'me', actorIsOwner: false, platformAdmin: false };
const superAdmin: MemberActionContext = { meId: 'me', actorIsOwner: true, platformAdmin: true };

describe('memberActions', () => {
  it('lets an owner give any other role and remove a member', () => {
    const actions = memberActions(member(), owner);

    expect(actions.roles).toEqual(['Admin', 'Owner']);
    expect(actions.remove).toBe(true);
    expect(actions.toggleActive).toBe(false);
  });

  it('lets an owner act on another owner', () => {
    expect(memberActions(member({ role: 'Owner' }), owner).roles).toEqual(['Member', 'Admin']);
  });

  it('never offers the Owner role to an admin', () => {
    expect(memberActions(member(), admin).roles).toEqual(['Admin']);
    expect(assignableRoles(false)).not.toContain('Owner');
  });

  it('gives an admin no actions on an owner', () => {
    expect(memberActions(member({ role: 'Owner' }), admin).any).toBe(false);
  });

  it('gives nobody actions on themselves', () => {
    expect(memberActions(member({ userId: 'me' }), owner).any).toBe(false);
    expect(memberActions(member({ userId: 'me', role: 'Admin' }), admin).roles).toEqual([]);
  });

  it('offers resend only while the invitation is pending', () => {
    expect(memberActions(member({ isInvitationPending: true }), admin).resendInvitation).toBe(true);
    expect(memberActions(member(), admin).resendInvitation).toBe(false);
  });

  it('offers activation to the super-admin only', () => {
    expect(memberActions(member(), superAdmin).toggleActive).toBe(true);
    expect(memberActions(member(), owner).toggleActive).toBe(false);
    expect(memberActions(member(), admin).toggleActive).toBe(false);
  });
});
