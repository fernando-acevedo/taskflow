import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { LanguageService } from '../../../core/i18n/language.service';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { CommentInput, TaskComment } from '../data-access/comments-api.service';
import { CommentsPageActions } from '../data-access/store/comments.actions';
import { commentsFeature } from '../data-access/store/comments.feature';
import { relativeTime } from './comment-time';
import { CommentEditor } from './comment-editor';
import { BodySegment, segmentBody } from './mentions';

interface CommentView {
  readonly comment: TaskComment;
  readonly segments: readonly BodySegment[];
  readonly when: string;
  readonly canEdit: boolean;
  readonly canDelete: boolean;
  readonly save: SaveFn<CommentInput>;
}

/**
 * The comment thread of a task: oldest first, with a box for a new comment. Authors edit their own comments;
 * they, the project's owners and admins delete. An archived project, or a user who isn't a member, only reads.
 */
@Component({
  selector: 'app-task-comments',
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslocoDirective, CommentEditor],
  template: `
    <section class="thread" *transloco="let t; prefix: 'comments'" [attr.aria-label]="t('title')">
      <h3>{{ t('title') }}</h3>

      @if (loadError()) {
        <p class="note">{{ t('loadFailed') }}</p>
      } @else if (loading() && views().length === 0) {
        <p class="note">{{ t('loading') }}</p>
      } @else if (views().length === 0) {
        <p class="note empty">{{ t('empty') }}</p>
      }

      <ul class="comments">
        @for (view of views(); track view.comment.id) {
          <li class="comment">
            <div class="meta">
              <strong class="author">{{ view.comment.authorName }}</strong>
              <time [attr.datetime]="view.comment.createdAt" [title]="view.comment.createdAt | date: 'medium' : undefined : language.current()">
                {{ view.when }}
              </time>
              @if (view.comment.isEdited) {
                <span class="edited">{{ t('edited') }}</span>
              }
              <span class="spacer"></span>
              @if (view.canEdit && editingId() !== view.comment.id) {
                <button mat-icon-button type="button" [attr.aria-label]="t('edit')" (click)="editingId.set(view.comment.id)">
                  <mat-icon aria-hidden="true">edit</mat-icon>
                </button>
              }
              @if (view.canDelete) {
                <button mat-icon-button type="button" [attr.aria-label]="t('delete')" (click)="remove(view.comment)">
                  <mat-icon aria-hidden="true">delete</mat-icon>
                </button>
              }
            </div>
            @if (editingId() === view.comment.id) {
              <app-comment-editor
                [members]="mentionable()"
                [save]="view.save"
                [initialBody]="view.comment.body"
                label="editComment"
                submitLabel="save"
                [cancellable]="true"
                (saved)="editingId.set(null)"
                (cancelled)="editingId.set(null)"
              />
            } @else {
              <p class="body">
                @for (segment of view.segments; track $index) {
                  @if (segment.mention) {
                    <span class="mention">{{ segment.text }}</span>
                  } @else {
                    {{ segment.text }}
                  }
                }
              </p>
            }
          </li>
        }
      </ul>

      @if (canComment()) {
        <app-comment-editor [members]="mentionable()" [save]="saveNew" />
      } @else {
        <p class="note">{{ t('readOnly') }}</p>
      }
    </section>
  `,
  styles: `
    .thread {
      display: block;
      margin-top: 16px;
    }
    h3 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-medium);
    }
    .comments {
      list-style: none;
      margin: 0 0 12px;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .meta {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 40px;
      font-size: 0.875rem;
    }
    .meta time,
    .edited,
    .note {
      color: var(--mat-sys-on-surface-variant, #444);
    }
    .spacer {
      flex: 1;
    }
    .body {
      margin: 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .mention {
      font-weight: 500;
      color: var(--mat-sys-primary, #1a56c6);
      background: var(--mat-sys-primary-container, #dbe5ff);
      border-radius: 4px;
      padding: 0 2px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskComments implements OnInit {
  readonly projectId = input.required<string>();
  readonly taskId = input.required<string>();

  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  private readonly comments = this.store.selectSignal(commentsFeature.selectAll);
  protected readonly loading = this.store.selectSignal(commentsFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(commentsFeature.selectLoadError);
  private readonly members = this.store.selectSignal(projectsFeature.selectMembers);
  private readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  private readonly canWork = this.store.selectSignal(projectsFeature.selectCanEditTasks);
  private readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);
  private readonly user = this.store.selectSignal(authFeature.selectUser);
  private readonly now = signal(Date.now());

  protected readonly editingId = signal<string | null>(null);
  protected readonly canComment = this.canWork;
  /** Active members: the people a new mention can name. */
  protected readonly mentionable = computed(() => this.members().filter((member) => member.isActive));

  protected readonly views = computed<CommentView[]>(() => {
    const names = new Map(this.members().map((member) => [member.userId, member.displayName]));
    const me = this.user()?.id;
    const archived = this.project()?.isArchived ?? false;
    const canManage = this.isOwner() && !archived;
    const now = this.now();
    const locale = this.language.current();
    return this.comments().map((comment) => {
      const mine = comment.authorId === me;
      return {
        comment,
        segments: segmentBody(
          comment.body,
          comment.mentionedUserIds.flatMap((id) => names.get(id) ?? []),
        ),
        when: relativeTime(comment.createdAt, now, locale),
        canEdit: mine && this.canWork(),
        canDelete: (mine && this.canWork()) || canManage,
        save: this.saveFor(comment.id),
      };
    });
  });

  protected readonly saveNew: SaveFn<CommentInput> = (comment) =>
    this.submissions.dispatchAndWait(CommentsPageActions.createSubmitted({ comment, submissionId: nextSubmissionId() }));

  constructor() {
    // "5 minutes ago" must keep moving while the thread stays open.
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void {
    this.store.dispatch(CommentsPageActions.opened({ projectId: this.projectId(), taskId: this.taskId() }));
  }

  private saveFor(commentId: string): SaveFn<CommentInput> {
    return (changes) =>
      this.submissions.dispatchAndWait(
        CommentsPageActions.updateSubmitted({ commentId, changes, submissionId: nextSubmissionId() }),
      );
  }

  protected remove(comment: TaskComment): void {
    confirm(this.dialog, {
      title: 'comments.deleteTitle',
      message: 'comments.deleteMessage',
      confirm: 'comments.delete',
      params: { author: comment.authorName },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(CommentsPageActions.deleteConfirmed({ commentId: comment.id })));
  }
}
