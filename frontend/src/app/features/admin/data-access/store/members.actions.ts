import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '@shared/data-access/api-error';
import { TenantRole } from '@core/tenant/tenant.models';
import { Submission } from '@shared/data-access/submission';
import { NewMemberInput, SupportAccess, TenantMember } from '../tenant-members-api.service';

export const MembersPageActions = createActionGroup({
  source: 'Members Page',
  events: {
    Opened: emptyProps(),
    'Support Access Requested': emptyProps(),
    'Add Submitted': props<{ member: NewMemberInput } & Required<Submission>>(),
    'Invitation Resend Clicked': props<{ userId: string }>(),
    'Role Changed': props<{ userId: string; role: TenantRole }>(),
    'Remove Clicked': props<{ userId: string }>(),
    'Active Changed': props<{ userId: string; active: boolean }>(),
  },
});

export const MembersApiActions = createActionGroup({
  source: 'Members API',
  events: {
    'Members Loaded': props<{ members: TenantMember[] }>(),
    'Members Load Failed': props<{ error: ApiError }>(),
    'Member Added': props<{ invitationSent: boolean } & Submission>(),
    'Invitation Sent': emptyProps(),
    'Member Updated': emptyProps(),
    'Member Removed': emptyProps(),
    'Member Update Failed': props<{ error: ApiError } & Submission>(),
    'Support Access Loaded': props<{ items: SupportAccess[] }>(),
  },
});
