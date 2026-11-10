import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { LanguageService } from '../../../core/i18n/language.service';
import { Submissions } from '../../../shared/data-access/submission';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { CommentsPageActions } from '../data-access/store/comments.actions';
import { commentsFeature } from '../data-access/store/comments.feature';
import { aComment } from '../data-access/store/testing';
import { TaskComments } from './task-comments';

interface Setup {
  me?: string;
  owner?: boolean;
  admin?: boolean;
  canWork?: boolean;
  archived?: boolean;
  loading?: boolean;
}

describe('TaskComments', () => {
  const dialog = { open: vi.fn() };
  const dispatchAndWait = vi.fn(() => of(null));
  let store: MockStore;
  let fixture: ComponentFixture<TaskComments>;

  const members = [
    { userId: 'u1', email: 'ana@x.test', displayName: 'Ana', isActive: true, role: 'Owner', joinedAt: '' },
    { userId: 'u2', email: 'bruno@x.test', displayName: 'Bruno', isActive: true, role: 'Member', joinedAt: '' },
    { userId: 'u3', email: 'carla@x.test', displayName: 'Carla', isActive: false, role: 'Member', joinedAt: '' },
  ];
  const comments = [
    aComment({ id: 'c1', authorId: 'u1', authorName: 'Ana', body: 'Please look, @Bruno', mentionedUserIds: ['u2'] }),
    aComment({ id: 'c2', authorId: 'u2', authorName: 'Bruno', body: 'Done', isEdited: true, createdAt: '2026-11-02T10:00:00Z' }),
  ];

  function render(setup: Setup = {}): HTMLElement {
    const { me = 'u1', owner = false, admin = false, canWork = true, archived = false, loading = false } = setup;
    dialog.open.mockReset();
    dispatchAndWait.mockClear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        TaskComments,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: commentsFeature.selectAll, value: comments },
            { selector: commentsFeature.selectLoading, value: loading },
            { selector: commentsFeature.selectLoadError, value: null },
            { selector: projectsFeature.selectMembers, value: members },
            { selector: projectsFeature.selectCurrent, value: { isArchived: archived } },
            { selector: projectsFeature.selectCanEditTasks, value: canWork && !archived },
            { selector: projectsFeature.selectIsOwner, value: owner },
            { selector: authFeature.selectIsAdmin, value: admin },
            { selector: authFeature.selectUser, value: { id: me } },
          ],
        }),
        { provide: MatDialog, useValue: dialog },
        { provide: Submissions, useValue: { dispatchAndWait } },
        { provide: LanguageService, useValue: { current: signal('en') } },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    fixture = TestBed.createComponent(TaskComments);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('taskId', 't1');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const labelsOf = (element: HTMLElement, selector: string): (string | null)[] =>
    Array.from(element.querySelectorAll(`.comment ${selector}`)).map((node) => node.getAttribute('aria-label'));

  it('loads the thread of its task', () => {
    render();

    expect(store.dispatch).toHaveBeenCalledWith(CommentsPageActions.opened({ projectId: 'p1', taskId: 't1' }));
  });

  it('shows author, "(edited)" only where it applies, and highlights the mentions', () => {
    const element = render();

    const items = element.querySelectorAll('.comment');
    expect(items).toHaveLength(2);
    expect(items[0].querySelector('.author')?.textContent).toBe('Ana');
    expect(items[0].querySelector('.edited')).toBeNull();
    expect(items[1].querySelector('.edited')?.textContent?.trim()).toBe('comments.edited');
    expect(items[0].querySelector('.mention')?.textContent).toBe('@Bruno');
  });

  it('lets the author edit and delete, and nobody else edit', () => {
    const element = render({ me: 'u1' });

    const items = element.querySelectorAll('.comment');
    expect(items[0].querySelectorAll('button')).toHaveLength(2);
    expect(items[1].querySelectorAll('button')).toHaveLength(0);
  });

  it('lets an owner delete, but not edit, other people’s comments', () => {
    const element = render({ me: 'u3', owner: true });

    expect(labelsOf(element, 'button')).toEqual(['comments.delete', 'comments.delete']);
  });

  it('does not let an admin who is not the owner delete other people’s comments', () => {
    const element = render({ me: 'u3', admin: true });

    expect(labelsOf(element, 'button')).toEqual([]);
  });

  it('is read-only in an archived project', () => {
    const element = render({ me: 'u1', owner: true, archived: true });

    expect(element.querySelectorAll('.comment button')).toHaveLength(0);
    expect(element.querySelector('app-comment-editor')).toBeNull();
  });

  it('deletes only after the confirmation', () => {
    const element = render({ me: 'u1' });
    const del = (): void => element.querySelector<HTMLButtonElement>('.comment button[aria-label="comments.delete"]')!.click();

    dialog.open.mockReturnValueOnce({ afterClosed: () => of(false) });
    del();
    expect(store.dispatch).not.toHaveBeenCalledWith(CommentsPageActions.deleteConfirmed({ commentId: 'c1' }));

    dialog.open.mockReturnValueOnce({ afterClosed: () => of(true) });
    del();
    expect(store.dispatch).toHaveBeenCalledWith(CommentsPageActions.deleteConfirmed({ commentId: 'c1' }));
  });

  it('posts a new comment with the members it mentions', () => {
    const element = render();
    const box = element.querySelector<HTMLTextAreaElement>('app-comment-editor textarea')!;

    box.value = 'Thanks @Bruno';
    box.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('app-comment-editor form')!.dispatchEvent(new Event('submit'));

    expect(dispatchAndWait).toHaveBeenCalledWith(
      expect.objectContaining({
        type: CommentsPageActions.createSubmitted.type,
        comment: { body: 'Thanks @Bruno', mentionedUserIds: ['u2'] },
      }),
    );
  });

  it('offers only active members while typing @', () => {
    const element = render();
    const box = element.querySelector<HTMLTextAreaElement>('app-comment-editor textarea')!;

    box.value = '@';
    box.setSelectionRange(1, 1);
    box.dispatchEvent(new Event('input'));
    TestBed.tick();

    expect(Array.from(element.querySelectorAll('[role="option"]')).map((o) => o.textContent?.trim())).toEqual(['Ana', 'Bruno']);
  });

  it('does not post an empty comment', () => {
    const element = render();

    element.querySelector<HTMLFormElement>('app-comment-editor form')!.dispatchEvent(new Event('submit'));

    expect(dispatchAndWait).not.toHaveBeenCalled();
  });
  describe('row version of an edit', () => {
    function startEditing(): HTMLElement {
      const element = render({ me: 'u1' });
      element.querySelector<HTMLButtonElement>('.comment button[aria-label="comments.edit"]')!.click();
      fixture.detectChanges();
      return element;
    }

    function submitText(element: HTMLElement, text: string): void {
      const box = element.querySelector<HTMLTextAreaElement>('app-comment-editor textarea')!;
      box.value = text;
      box.dispatchEvent(new Event('input'));
      element.querySelector<HTMLFormElement>('app-comment-editor form')!.dispatchEvent(new Event('submit'));
    }

    it('saves with the version the editor opened with, even if the thread was reloaded meanwhile', () => {
      const element = startEditing();
      store.overrideSelector(commentsFeature.selectAll, [aComment({ id: 'c1', authorId: 'u1', rowVersion: 'rv-theirs' }), comments[1]]);
      store.refreshState();
      fixture.detectChanges();

      submitText(element, 'Mine');

      expect(dispatchAndWait).toHaveBeenCalledWith(
        expect.objectContaining({ type: CommentsPageActions.updateSubmitted.type, commentId: 'c1', rowVersion: 'rv1' }),
      );
    });
  });
});
