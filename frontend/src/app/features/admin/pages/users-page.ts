import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { PageEvent } from '@angular/material/paginator';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { LanguageService } from '../../../core/i18n/language.service';
import { tenantFeature } from '../../../core/tenant/store/tenant.feature';
import { TenantRole } from '../../../core/tenant/tenant.models';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { EmptyState } from '../../../shared/ui/empty-state';
import { PagedList } from '../../../shared/ui/paged-list';
import { SearchField } from '../../../shared/ui/search-field';
import { NewMemberInput, TenantMember } from '../data-access/tenant-members-api.service';
import { MembersPageActions } from '../data-access/store/members.actions';
import { membersFeature } from '../data-access/store/members.feature';
import { MemberActions, memberActions } from '../ui/member-permissions';
import { MemberFormDialog } from '../ui/member-form-dialog';

@Component({
  selector: 'app-users-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    TranslocoDirective,
    EmptyState,
    PagedList,
    SearchField,
  ],
  templateUrl: './users-page.html',
  styles: `
    :host {
      display: block;
      max-width: 960px;
      margin: 0 auto;
      padding: 32px 16px;
    }

    .support {
      margin-top: 32px;
    }

    .support h2 {
      margin: 0;
      font: var(--mat-sys-title-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  protected readonly members = this.store.selectSignal(membersFeature.selectAll);
  protected readonly loading = this.store.selectSignal(membersFeature.selectLoading);
  protected readonly saving = this.store.selectSignal(membersFeature.selectSaving);
  protected readonly loadError = this.store.selectSignal(membersFeature.selectLoadError);
  protected readonly supportAccess = this.store.selectSignal(membersFeature.selectSupportAccess);
  protected readonly me = this.store.selectSignal(authFeature.selectUser);
  protected readonly isOwner = this.store.selectSignal(tenantFeature.selectIsOwner);
  private readonly platformAdmin = this.store.selectSignal(authFeature.selectIsAdmin);

  // The members endpoint isn't paged (a tenant is small), so search and paging happen here.
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly filtered = computed(() => {
    const needle = this.search().trim().toLocaleLowerCase();
    return needle
      ? this.members().filter(
          (m) =>
            m.displayName.toLocaleLowerCase().includes(needle) ||
            m.email.toLocaleLowerCase().includes(needle),
        )
      : this.members();
  });
  protected readonly visible = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  constructor() {
    this.store.dispatch(MembersPageActions.opened());
    effect(() => {
      if (this.isOwner()) {
        untracked(() => this.store.dispatch(MembersPageActions.supportAccessRequested()));
      }
    });
  }

  protected actionsFor(member: TenantMember): MemberActions {
    return memberActions(member, {
      meId: this.me()?.id ?? null,
      actorIsOwner: this.isOwner(),
      platformAdmin: this.platformAdmin(),
    });
  }

  protected searchChanged(search: string): void {
    this.search.set(search);
    this.page.set(1);
  }

  protected retry(): void {
    this.store.dispatch(MembersPageActions.opened());
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
  }

  protected openForm(): void {
    const save: SaveFn<NewMemberInput> = (member) =>
      this.submissions.dispatchAndWait(
        MembersPageActions.addSubmitted({ member, submissionId: nextSubmissionId() }),
      );
    this.dialog.open<MemberFormDialog, { save: SaveFn<NewMemberInput>; isOwner: boolean }>(
      MemberFormDialog,
      {
        data: { save, isOwner: this.isOwner() },
        width: '480px',
      },
    );
  }

  protected resendInvitation(member: TenantMember): void {
    this.store.dispatch(MembersPageActions.invitationResendClicked({ userId: member.userId }));
  }

  protected changeRole(member: TenantMember, role: TenantRole): void {
    this.store.dispatch(MembersPageActions.roleChanged({ userId: member.userId, role }));
  }

  protected remove(member: TenantMember): void {
    confirm(this.dialog, {
      title: 'users.removeTitle',
      message: 'users.removeMessage',
      confirm: 'users.removeMember',
      params: { name: member.displayName },
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.store.dispatch(MembersPageActions.removeClicked({ userId: member.userId })),
      );
  }

  protected setActive(member: TenantMember, active: boolean): void {
    if (active) {
      this.store.dispatch(MembersPageActions.activeChanged({ userId: member.userId, active }));
      return;
    }
    confirm(this.dialog, {
      title: 'users.deactivateTitle',
      message: 'users.deactivateMessage',
      confirm: 'users.deactivate',
      params: { name: member.displayName },
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.store.dispatch(MembersPageActions.activeChanged({ userId: member.userId, active })),
      );
  }
}
