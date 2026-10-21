import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { ProjectMember, ProjectRole } from '../data-access/projects-api.service';
import { ProjectPageActions } from '../data-access/store/projects.actions';
import { projectsFeature } from '../data-access/store/projects.feature';

@Component({
  selector: 'app-members-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  templateUrl: './members-page.html',
  styles: `
    :host {
      display: grid;
      gap: 16px;
      max-width: 840px;
    }
    .add {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: 12px;
      padding: 16px;
    }
    .add .email {
      flex: 1 1 260px;
    }
    .add .role {
      flex: 0 1 160px;
    }
    .add button {
      margin-top: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MembersPage {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly members = this.store.selectSignal(projectsFeature.selectMembers);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);
  protected readonly saving = this.store.selectSignal(projectsFeature.selectSaving);
  protected readonly me = this.store.selectSignal(authFeature.selectUser);

  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    role: this.fb.control<ProjectRole>('Member'),
  });

  /** The email stays in the field until the member is added, so a typo can be corrected. */
  protected add(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, role } = this.form.getRawValue();
    this.submissions.dispatchAndWait(
      ProjectPageActions.memberAddSubmitted({ email: email.trim(), role, submissionId: nextSubmissionId() }),
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        if (error) {
          applyApiFieldErrors(this.form, error);
        } else {
          this.form.reset();
        }
      });
  }

  protected changeRole(member: ProjectMember, role: ProjectRole): void {
    this.store.dispatch(ProjectPageActions.memberRoleChanged({ userId: member.userId, role }));
  }

  protected remove(member: ProjectMember): void {
    confirm(this.dialog, {
      title: 'members.removeTitle',
      message: 'members.removeMessage',
      confirm: 'members.remove',
      params: { name: member.displayName },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(ProjectPageActions.memberRemovalConfirmed({ userId: member.userId })));
  }

  protected leave(): void {
    confirm(this.dialog, {
      title: 'members.leaveTitle',
      message: 'members.leaveMessage',
      confirm: 'members.leave',
      params: { name: this.project()?.name },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(ProjectPageActions.leaveConfirmed()));
  }
}
