import { createActionGroup, props } from '@ngrx/store';
import { Tenant } from '../tenant.models';

export const TenantActions = createActionGroup({
  source: 'Tenant',
  events: {
    'Tenant Switched': props<{ tenantId: string }>(),
  },
});

export const TenantApiActions = createActionGroup({
  source: 'Tenant API',
  events: {
    /** `preferredId` is the tenant remembered for this user (or the demo's); it applies only while still a member. */
    'Tenants Loaded': props<{ tenants: Tenant[]; preferredId: string | null }>(),
  },
});
