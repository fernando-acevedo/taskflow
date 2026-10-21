import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { PageEvent } from '@angular/material/paginator';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { ADMIN_ROLE, User } from '../../../core/auth/auth.models';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { EmptyState } from '../../../shared/ui/empty-state';
import { PagedList } from '../../../shared/ui/paged-list';
import { SearchField } from '../../../shared/ui/search-field';
import { NewUserInput } from '../data-access/admin-users-api.service';
import { UsersPageActions } from '../data-access/store/admin-users.actions';
import { adminUsersFeature } from '../data-access/store/admin-users.feature';
import { UserFormDialog } from '../ui/user-form-dialog';

@Component({
  selector: 'app-users-page',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslocoDirective, EmptyState, PagedList, SearchField],
  templateUrl: './users-page.html',
  styles: `
    :host {
      display: block;
      max-width: 960px;
      margin: 0 auto;
      padding: 32px 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);

  protected readonly users = this.store.selectSignal(adminUsersFeature.selectAll);
  protected readonly query = this.store.selectSignal(adminUsersFeature.selectQuery);
  protected readonly totalCount = this.store.selectSignal(adminUsersFeature.selectTotalCount);
  protected readonly loading = this.store.selectSignal(adminUsersFeature.selectLoading);
  protected readonly saving = this.store.selectSignal(adminUsersFeature.selectSaving);
  protected readonly loadError = this.store.selectSignal(adminUsersFeature.selectLoadError);
  protected readonly me = this.store.selectSignal(authFeature.selectUser);

  constructor() {
    this.store.dispatch(UsersPageActions.opened());
  }

  protected isAdmin(user: User): boolean {
    return user.roles.includes(ADMIN_ROLE);
  }

  protected search(search: string): void {
    this.store.dispatch(UsersPageActions.queryChanged({ query: { search } }));
  }

  protected retry(): void {
    this.store.dispatch(UsersPageActions.opened());
  }

  protected changePage(event: PageEvent): void {
    this.store.dispatch(UsersPageActions.queryChanged({ query: { page: event.pageIndex + 1, pageSize: event.pageSize } }));
  }

  protected openForm(): void {
    const save: SaveFn<NewUserInput> = (user) =>
      this.submissions.dispatchAndWait(UsersPageActions.createSubmitted({ user, submissionId: nextSubmissionId() }));
    this.dialog.open<UserFormDialog, SaveFn<NewUserInput>>(UserFormDialog, { data: save, width: '480px' });
  }

  protected resendInvitation(user: User): void {
    this.store.dispatch(UsersPageActions.invitationResendClicked({ id: user.id }));
  }

  protected setActive(user: User, active: boolean): void {
    if (active) {
      this.store.dispatch(UsersPageActions.activeChanged({ id: user.id, active }));
      return;
    }
    confirm(this.dialog, {
      title: 'users.deactivateTitle',
      message: 'users.deactivateMessage',
      confirm: 'users.deactivate',
      params: { name: user.displayName },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(UsersPageActions.activeChanged({ id: user.id, active })));
  }

  protected setAdmin(user: User, admin: boolean): void {
    this.store.dispatch(UsersPageActions.adminChanged({ id: user.id, admin }));
  }
}
