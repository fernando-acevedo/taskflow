import { TenantRole } from '@core/tenant/tenant.models';
import { TenantMember } from '../data-access/tenant-members-api.service';

export interface MemberActionContext {
  readonly meId: string | null;
  /** Owner of the tenant, or the super-admin (who counts as Owner). */
  readonly actorIsOwner: boolean;
  /** The platform's super-admin, who alone can activate or deactivate accounts. */
  readonly platformAdmin: boolean;
}

export interface MemberActions {
  readonly resendInvitation: boolean;
  /** The roles the member can be given, other than the current one. */
  readonly roles: readonly TenantRole[];
  readonly remove: boolean;
  readonly toggleActive: boolean;
  readonly any: boolean;
}

const NONE: MemberActions = {
  resendInvitation: false,
  roles: [],
  remove: false,
  toggleActive: false,
  any: false,
};

/** Only an owner may hand out the Owner role. */
export function assignableRoles(actorIsOwner: boolean): TenantRole[] {
  return actorIsOwner ? ['Member', 'Admin', 'Owner'] : ['Member', 'Admin'];
}

/**
 * What the signed-in user may do to a row. The API enforces the same rules: nobody changes their own role, and an
 * admin has no actions on an owner.
 */
export function memberActions(member: TenantMember, context: MemberActionContext): MemberActions {
  if (member.userId === context.meId || (member.role === 'Owner' && !context.actorIsOwner)) {
    return NONE;
  }

  const roles = assignableRoles(context.actorIsOwner).filter((role) => role !== member.role);
  return {
    resendInvitation: member.isInvitationPending,
    roles,
    remove: true,
    toggleActive: context.platformAdmin,
    any: true,
  };
}
