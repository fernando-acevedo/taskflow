import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { Notifier } from '../../../core/notifications/notifier';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { EmptyState } from '../../../shared/ui/empty-state';
import { Client, ClientInput } from '../data-access/clients-api.service';
import { ClientsPageActions } from '../data-access/store/clients.actions';
import { clientsFeature } from '../data-access/store/clients.feature';
import { ClientFormDialog } from '../ui/client-form-dialog';

@Component({
  selector: 'app-clients-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSlideToggleModule,
    TranslocoDirective,
    EmptyState,
  ],
  templateUrl: './clients-page.html',
  styleUrl: './clients-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientsPage {
  private readonly store = inject(Store);
  private readonly dialog = inject(MatDialog);
  protected readonly notifier = inject(Notifier);

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
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { search: search.trim() } }));
  }

  protected showArchived(includeArchived: boolean): void {
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { includeArchived } }));
  }

  protected changePage(event: PageEvent): void {
    this.store.dispatch(ClientsPageActions.queryChanged({ query: { page: event.pageIndex + 1, pageSize: event.pageSize } }));
  }

  protected openForm(client: Client | null = null): void {
    this.dialog
      .open<ClientFormDialog, Client | null, ClientInput>(ClientFormDialog, { data: client, width: '480px' })
      .afterClosed()
      .pipe(filter((input) => input !== undefined))
      .subscribe((input) =>
        this.store.dispatch(
          client
            ? ClientsPageActions.updateSubmitted({ id: client.id, client: input })
            : ClientsPageActions.createSubmitted({ client: input }),
        ),
      );
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
