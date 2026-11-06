/** Today in the browser's time zone as `yyyy-MM-dd`, the format of date inputs and of the API. */
export function localToday(now = new Date()): string {
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}
