import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '../../http/api-error';
import { Tenant } from '../tenant.models';

export const TenantActions = createActionGroup({
  source: 'Tenant',
  events: {
    'Tenant Switched': props<{ tenantId: string }>(),
    /** The shell's button after the list of tenants failed to load. */
    'Retry Clicked': emptyProps(),
  },
});

export const TenantApiActions = createActionGroup({
  source: 'Tenant API',
  events: {
    /** `preferredId` is the tenant remembered for this user (or the demo's); it applies only while still a member. */
    'Tenants Loaded': props<{ tenants: Tenant[]; preferredId: string | null }>(),
    'Tenants Load Failed': props<{ error: ApiError }>(),
  },
});
