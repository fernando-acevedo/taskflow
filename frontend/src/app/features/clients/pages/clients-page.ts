import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { Observable, filter, map } from 'rxjs';
import { Notifier } from '@core/notifications/notifier';
import { SaveFn, Submissions, nextSubmissionId } from '@shared/data-access/submission';
import { confirm } from '@shared/ui/confirm-dialog';
import { EmptyState } from '@shared/ui/empty-state';
import { PagedList } from '@core/ui/paged-list';
import { SearchField } from '@shared/ui/search-field';
import { Client, ClientInput } from '../data-access/clients-api.service';
import { ClientsPageActions } from '../data-access/store/clients.actions';
import { clientsFeature } from '../data-access/store/clients.feature';
import { ClientFormData, ClientFormDialog } from '../ui/client-form-dialog';

@Component({
  selector: 'app-clients-page',
  imports: [MatButtonModule, MatIconModule, MatSlideToggleModule, TranslocoDirective, EmptyState, PagedList, SearchField],
  templateUrl: './clients-page.html',
  styleUrl: './clients-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientsPage {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly clients = this.store.selectSignal(clientsFeature.selectAll);
  protected readonly query = this.store.selectSignal(clientsFeature.selectQuery);
  protected readonly totalCount = this.store.selectSignal(clientsFeature.selectTotalCount);
  protected readonly loading = this.store.selectSignal(clientsFeature.selectLoading);
  protected readonly saving = this.store.selectSignal(clientsFeature.selectSaving);
  protected readonly loadError = this.store.selectSignal(clientsFeature.selectLoadError);

  constructor() {
    this.store.dispatch(ClientsPageActions.opened());
  }

  protected search(search: string): void {
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { search } }));
  }

  protected retry(): void {
    this.store.dispatch(ClientsPageActions.opened());
  }

  protected showArchived(includeArchived: boolean): void {
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { includeArchived } }));
  }

  protected changePage(event: PageEvent): void {
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { page: event.pageIndex + 1, pageSize: event.pageSize } }));
  }

  protected openForm(client: Client | null = null): void {
    // The version the dialog opened with; it changes only on Reload or Save anyway (a successful refresh).
    let rowVersion = client?.rowVersion ?? '';
    const save: SaveFn<ClientInput> = (input) =>
      this.submissions.dispatchAndWait(
        client
          ? ClientsPageActions.updateSubmitted({ id: client.id, client: input, rowVersion, submissionId: nextSubmissionId() })
          : ClientsPageActions.createSubmitted({ client: input, submissionId: nextSubmissionId() }),
      );
    const refresh = (): Observable<Client | null> =>
      this.submissions
        .dispatchAndWait(ClientsPageActions.refreshRequested({ submissionId: nextSubmissionId() }))
        .pipe(
          map((error) => {
            if (error) {
              return null;
            }
            const latest = this.clients().find((current) => current.id === client?.id) ?? null;
            if (latest) {
              rowVersion = latest.rowVersion;
            } else {
              this.notifier.info('common.gone');
              ref.close(); // the dialog is open by the time a refresh can answer
            }
            return latest;
          }),
        );
    const ref: MatDialogRef<ClientFormDialog> = this.dialog.open<ClientFormDialog, ClientFormData>(ClientFormDialog, {
      data: { client, save, refresh },
      width: '480px',
    });
  }

  protected archive(client: Client): void {
    confirm(this.dialog, {
      title: 'clients.archiveTitle',
      message: 'clients.archiveMessage',
      confirm: 'clients.archive',
      params: { name: client.name },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(ClientsPageActions.archiveConfirmed({ id: client.id })));
  }

  /** While a save's reload is in flight the row's version may be behind the server's. */
  protected busy = computed(() => this.loading() || this.saving());

  protected restore(client: Client): void {
    this.store.dispatch(ClientsPageActions.restoreClicked({ id: client.id }));
  }
}
