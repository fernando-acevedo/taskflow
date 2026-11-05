import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { CommentInput, TaskComment } from '../comments-api.service';

export const CommentsPageActions = createActionGroup({
  source: 'Comments Page',
  events: {
    /** The thread of a task was shown. */
    Opened: props<{ projectId: string; taskId: string }>(),
    'Create Submitted': props<{ comment: CommentInput } & Required<Submission>>(),
    'Update Submitted': props<{ commentId: string; changes: CommentInput } & Required<Submission>>(),
    'Delete Confirmed': props<{ commentId: string }>(),
  },
});

export const CommentsApiActions = createActionGroup({
  source: 'Comments API',
  events: {
    'Comments Loaded': props<{ taskId: string; comments: TaskComment[] }>(),
    'Comments Load Failed': props<{ error: ApiError }>(),
    'Comment Saved': props<{ comment: TaskComment; message: string } & Submission>(),
    'Comment Deleted': props<{ commentId: string }>(),
    'Comment Save Failed': props<{ error: ApiError } & Submission>(),
  },
});
