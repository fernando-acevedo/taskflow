import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { User } from '../../../../core/auth/auth.models';
import { ApiError, PagedResult } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { NewUserInput, UserQuery } from '../admin-users-api.service';

export const UsersPageActions = createActionGroup({
  source: 'Users Page',
  events: {
    Opened: emptyProps(),
    'Query Changed': props<{ query: Partial<UserQuery> }>(),
    'Create Submitted': props<{ user: NewUserInput } & Required<Submission>>(),
    'Invitation Resend Clicked': props<{ id: string }>(),
    'Active Changed': props<{ id: string; active: boolean }>(),
    'Admin Changed': props<{ id: string; admin: boolean }>(),
  },
});

export const AdminUsersApiActions = createActionGroup({
  source: 'Admin Users API',
  events: {
    'Users Loaded': props<{ page: PagedResult<User> }>(),
    'Users Load Failed': props<{ error: ApiError }>(),
    'User Created': props<Submission>(),
    'Invitation Sent': emptyProps(),
    'User Updated': emptyProps(),
    'User Update Failed': props<{ error: ApiError } & Submission>(),
  },
});
