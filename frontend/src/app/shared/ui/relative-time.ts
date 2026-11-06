const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** "5 minutes ago" for the last week, a short date after that. */
export function relativeTime(iso: string, now: number, locale: string): string {
  const then = new Date(iso).getTime();
  const elapsed = now - then;
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  if (elapsed < MINUTE) {
    return formatter.format(0, 'second');
  }
  if (elapsed < HOUR) {
    return formatter.format(-Math.floor(elapsed / MINUTE), 'minute');
  }
  if (elapsed < DAY) {
    return formatter.format(-Math.floor(elapsed / HOUR), 'hour');
  }
  if (elapsed < 7 * DAY) {
    return formatter.format(-Math.floor(elapsed / DAY), 'day');
  }
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(then);
}
