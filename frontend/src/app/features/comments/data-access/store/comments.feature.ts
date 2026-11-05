import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ApiError } from '../../../../core/http/api-error';
import { TaskComment } from '../comments-api.service';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';

export interface CommentsState extends EntityState<TaskComment> {
  projectId: string | null;
  /** The task whose thread is loaded. */
  taskId: string | null;
  loading: boolean;
  loadError: ApiError | null;
}

const adapter = createEntityAdapter<TaskComment>({ sortComparer: (a, b) => a.createdAt.localeCompare(b.createdAt) });

export const initialCommentsState: CommentsState = adapter.getInitialState({
  projectId: null,
  taskId: null,
  loading: false,
  loadError: null,
});

export const commentsFeature = createFeature({
  name: 'comments',
  reducer: createReducer(
    initialCommentsState,
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): CommentsState => initialCommentsState),
    on(CommentsPageActions.opened, (state, { projectId, taskId }): CommentsState =>
      state.taskId === taskId
        ? { ...state, loading: true, loadError: null }
        : adapter.removeAll({ ...state, projectId, taskId, loading: true, loadError: null }),
    ),
    on(CommentsApiActions.commentsLoaded, (state, { taskId, comments }): CommentsState =>
      taskId === state.taskId ? adapter.setAll(comments, { ...state, loading: false }) : state,
    ),
    on(CommentsApiActions.commentsLoadFailed, (state, { error }): CommentsState => ({ ...state, loading: false, loadError: error })),
    on(CommentsApiActions.commentSaved, (state, { comment }): CommentsState =>
      comment.taskId === state.taskId ? adapter.upsertOne(comment, state) : state,
    ),
    on(CommentsApiActions.commentDeleted, (state, { commentId }): CommentsState => adapter.removeOne(commentId, state)),
  ),
  extraSelectors: ({ selectCommentsState }) => adapter.getSelectors(selectCommentsState),
});
