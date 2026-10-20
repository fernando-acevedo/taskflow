import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { User } from '../../../../core/auth/auth.models';
import { ApiError, PagedResult } from '../../../../core/http/api-error';
import { UserQuery } from '../admin-users-api.service';

export const UsersPageActions = createActionGroup({
  source: 'Users Page',
  events: {
    Opened: emptyProps(),
    'Query Changed': props<{ query: Partial<UserQuery> }>(),
    'Active Changed': props<{ id: string; active: boolean }>(),
    'Admin Changed': props<{ id: string; admin: boolean }>(),
  },
});

export const AdminUsersApiActions = createActionGroup({
  source: 'Admin Users API',
  events: {
    'Users Loaded': props<{ page: PagedResult<User> }>(),
    'Users Load Failed': props<{ error: ApiError }>(),
    'User Updated': emptyProps(),
    'User Update Failed': props<{ error: ApiError }>(),
  },
});
