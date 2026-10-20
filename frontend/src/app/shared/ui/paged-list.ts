import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { ApiError } from '../../core/http/api-error';
import { Notifier } from '../../core/notifications/notifier';
import { EmptyState } from './empty-state';

/**
 * The card of a paged list page: progress bar, then the load error with a retry button, the empty state
 * (projected with the `empty` attribute) or the rows (the rest of the content), and the paginator.
 */
@Component({
  selector: 'app-paged-list',
  imports: [MatButtonModule, MatCardModule, MatPaginatorModule, MatProgressBarModule, TranslocoDirective, EmptyState],
  template: `
    <mat-card appearance="outlined" *transloco="let t">
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (loadError(); as error) {
        <app-empty-state icon="cloud_off" [message]="notifier.errorMessage(error)">
          <button mat-button type="button" (click)="retry.emit()">{{ t('common.retry') }}</button>
        </app-empty-state>
      } @else if (!loading() && empty()) {
        <ng-content select="[empty]" />
      } @else {
        <ng-content />
      }

      <mat-paginator
        [length]="totalCount()"
        [pageIndex]="page() - 1"
        [pageSize]="pageSize()"
        [pageSizeOptions]="pageSizeOptions()"
        (page)="pageChange.emit($event)"
      />
    </mat-card>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PagedList {
  protected readonly notifier = inject(Notifier);

  readonly loading = input(false);
  /** Shows the progress bar: loading, or saving a change to one of the rows. */
  readonly busy = input(false);
  readonly loadError = input<ApiError | null>(null);
  readonly empty = input(false);
  readonly totalCount = input(0);
  /** 1-based, like the API. */
  readonly page = input(1);
  readonly pageSize = input(20);
  readonly pageSizeOptions = input<number[]>([10, 25, 50]);

  readonly retry = output();
  readonly pageChange = output<PageEvent>();
}
