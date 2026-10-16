import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { HealthReport, SystemInfo } from '../status-api.service';

export const StatusPageActions = createActionGroup({
  source: 'Status Page',
  events: {
    'Status Requested': emptyProps(),
  },
});

export const StatusApiActions = createActionGroup({
  source: 'Status API',
  events: {
    'Status Loaded': props<{ health: HealthReport; info: SystemInfo; checkedAt: string }>(),
    'Status Load Failed': props<{ error: string; checkedAt: string }>(),
  },
});
