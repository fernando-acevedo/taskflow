import { Params } from '@angular/router';
import { TimeFilters } from '../time-tracking-api.service';

const DATE = /^\d{4}-\d{2}-\d{2}$/;

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

/** The report's filters, read from the query string (anything the user typed in the URL). */
export function parseTimeFilters(params: Params): TimeFilters {
  const from = text(params['from']);
  const to = text(params['to']);
  return {
    from: from && DATE.test(from) ? from : null,
    to: to && DATE.test(to) ? to : null,
    userId: text(params['userId']),
  };
}

/** Query params for `filters`, with null for the ones not set so that a merge removes them from the URL. */
export function toTimeQueryParams(filters: TimeFilters): Params {
  return { from: filters.from, to: filters.to, userId: filters.userId };
}
