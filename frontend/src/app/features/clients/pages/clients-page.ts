import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { EmptyState } from '../../../shared/ui/empty-state';
import { PagedList } from '../../../shared/ui/paged-list';
import { SearchField } from '../../../shared/ui/search-field';
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
    const save: SaveFn<ClientInput> = (input) =>
      this.submissions.dispatchAndWait(
        client
          ? ClientsPageActions.updateSubmitted({ id: client.id, client: input, submissionId: nextSubmissionId() })
          : ClientsPageActions.createSubmitted({ client: input, submissionId: nextSubmissionId() }),
      );
    this.dialog.open<ClientFormDialog, ClientFormData>(ClientFormDialog, { data: { client, save }, width: '480px' });
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

  protected restore(client: Client): void {
    this.store.dispatch(ClientsPageActions.restoreClicked({ id: client.id }));
  }
}
