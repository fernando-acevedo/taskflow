import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { COMMENT_MAX_LENGTH, CommentInput } from '../data-access/comments-api.service';
import { ActiveMention, Mentionable, activeMention, insertMention, mentionedIds, suggest } from './mentions';

/**
 * A text box for a comment, new or being edited. Typing `@` offers the project's members; picking one writes
 * `@Name` into the text. Stays as it is, keeping the text, until the save succeeds.
 */
@Component({
  selector: 'app-comment-editor',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'comments'">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ t(label()) }}</mat-label>
          <textarea
            #box
            matInput
            formControlName="body"
            rows="3"
            role="combobox"
            aria-autocomplete="list"
            aria-haspopup="listbox"
            [attr.aria-expanded]="suggestions().length > 0"
            [attr.aria-controls]="suggestions().length > 0 ? listId : null"
            [attr.aria-activedescendant]="suggestions().length > 0 ? listId + '-' + highlighted() : null"
            (input)="track(box)"
            (click)="track(box)"
            (keyup)="onKeyUp($event, box)"
            (keydown)="onKeyDown($event, box)"
            (blur)="close()"
          ></textarea>
          @if (form.controls.body.getError('api'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.body.hasError('required')) {
            <mat-error>{{ t('bodyRequired') }}</mat-error>
          } @else if (form.controls.body.hasError('maxlength')) {
            <mat-error>{{ t('bodyTooLong', { max: maxLength }) }}</mat-error>
          } @else {
            <mat-hint>{{ t('mentionHint') }}</mat-hint>
          }
        </mat-form-field>

        @if (suggestions().length > 0) {
          <ul class="suggestions" role="listbox" [id]="listId" [attr.aria-label]="t('mentionList')">
            @for (member of suggestions(); track member.userId; let index = $index) {
              <li
                role="option"
                [id]="listId + '-' + index"
                [class.active]="index === highlighted()"
                [attr.aria-selected]="index === highlighted()"
                (mousedown)="$event.preventDefault(); pick(member, box)"
              >
                {{ member.displayName }}
              </li>
            }
          </ul>
        }

        <div class="actions">
          @if (cancellable()) {
            <button mat-button type="button" [disabled]="saving()" (click)="cancelled.emit()">{{ t('cancel') }}</button>
          }
          <button mat-flat-button type="submit" [disabled]="saving()">{{ t(submitLabel()) }}</button>
        </div>
      </form>
    </ng-container>
  `,
  styles: `
    mat-form-field {
      width: 100%;
    }
    .suggestions {
      list-style: none;
      margin: 4px 0 0;
      padding: 4px 0;
      border: 1px solid var(--mat-sys-outline-variant, #c4c7c5);
      border-radius: 8px;
      background: var(--mat-sys-surface-container, #f3f3f3);
      max-height: 180px;
      overflow-y: auto;
    }
    .suggestions li {
      padding: 6px 12px;
      cursor: pointer;
    }
    .suggestions li.active,
    .suggestions li:hover {
      background: var(--mat-sys-secondary-container, #dde3ea);
    }
    .actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      margin-top: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentEditor implements OnInit {
  private static nextId = 0;

  readonly members = input.required<readonly Mentionable[]>();
  readonly save = input.required<SaveFn<CommentInput>>();
  readonly initialBody = input('');
  /** Keys under `comments`. */
  readonly label = input('newComment');
  readonly submitLabel = input('post');
  readonly cancellable = input(false);
  readonly saved = output();
  readonly cancelled = output();

  protected readonly listId = `comment-mentions-${CommentEditor.nextId++}`;
  protected readonly maxLength = COMMENT_MAX_LENGTH;
  protected readonly saving = signal(false);
  private readonly mention = signal<ActiveMention | null>(null);
  private readonly rawHighlighted = signal(0);
  private readonly query = signal('');
  protected readonly suggestions = computed(() =>
    this.mention() === null ? [] : suggest(this.members(), this.query()),
  );
  /** The highlighted row, kept inside the list when the suggestions shrink while it is open. */
  protected readonly highlighted = computed(() =>
    Math.max(0, Math.min(this.rawHighlighted(), this.suggestions().length - 1)),
  );

  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly form = this.fb.group({
    body: ['', [Validators.required, Validators.maxLength(COMMENT_MAX_LENGTH), notBlank]],
  });

  ngOnInit(): void {
    this.form.controls.body.setValue(this.initialBody());
  }

  protected track(box: HTMLTextAreaElement): void {
    const mention = activeMention(box.value, box.selectionStart);
    this.mention.set(mention);
    this.query.set(mention?.query ?? '');
    this.rawHighlighted.set(0);
  }

  protected onKeyUp(event: KeyboardEvent, box: HTMLTextAreaElement): void {
    // Moving the caret with the arrow keys can enter or leave a mention; the list keys are handled on keydown.
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      this.track(box);
    }
  }

  protected onKeyDown(event: KeyboardEvent, box: HTMLTextAreaElement): void {
    const options = this.suggestions();
    if (options.length === 0) {
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.rawHighlighted.set((this.highlighted() + 1) % options.length);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.rawHighlighted.set((this.highlighted() - 1 + options.length) % options.length);
        break;
      case 'Enter':
      case 'Tab':
        event.preventDefault();
        this.pick(options[this.highlighted()], box);
        break;
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        this.close();
        break;
    }
  }

  protected close(): void {
    this.mention.set(null);
    this.query.set('');
  }

  protected pick(member: Mentionable, box: HTMLTextAreaElement): void {
    const mention = this.mention();
    if (mention === null) {
      return;
    }
    const result = insertMention(box.value, mention, box.selectionStart, member.displayName);
    this.form.controls.body.setValue(result.text);
    this.form.controls.body.markAsDirty();
    this.close();
    box.focus();
    box.setSelectionRange(result.caret, result.caret);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const body = this.form.controls.body.value.trim();
    this.saving.set(true);
    this.save()({ body, mentionedUserIds: mentionedIds(body, this.members()) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (error) {
          // Up to 4000 characters must not be lost to a rejected save.
          applyApiFieldErrors(this.form, error);
        } else {
          this.form.reset({ body: '' });
          this.saved.emit();
        }
      });
  }
}

function notBlank(control: { value: unknown }): { required: true } | null {
  return typeof control.value === 'string' && control.value.trim() === '' ? { required: true } : null;
}
