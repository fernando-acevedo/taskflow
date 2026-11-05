/** Longest time a single entry can hold: a full day. */
export const MAX_ENTRY_MINUTES = 1440;

/** Minutes as "2 h 30 min", "2 h", "45 min" or "0 min". The units are the same in English and Spanish. */
export function formatMinutes(minutes: number): string {
  const total = Math.max(0, Math.round(minutes));
  const hours = Math.floor(total / 60);
  const rest = total % 60;
  if (hours === 0) {
    return `${rest} min`;
  }
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

/**
 * Reads what a person typed as a duration, in minutes, or null when it isn't one. Accepts "2:30", "2h 30m",
 * "2 h", "45m", and decimal hours with a point or a comma ("2.5"). A bare number is hours.
 */
export function parseDuration(text: string): number | null {
  const value = text.trim().toLowerCase();
  if (value === '') {
    return null;
  }
  const clock = /^(\d{1,3}):([0-5]?\d)$/.exec(value);
  if (clock) {
    return Number(clock[1]) * 60 + Number(clock[2]);
  }
  const units = /^(?:(\d+(?:[.,]\d+)?)\s*h)?\s*(?:(\d+)\s*(?:m|min))?$/.exec(value);
  if (units && (units[1] !== undefined || units[2] !== undefined)) {
    return Math.round(Number((units[1] ?? '0').replace(',', '.')) * 60 + Number(units[2] ?? '0'));
  }
  const hours = /^(\d+(?:[.,]\d+)?)$/.exec(value);
  return hours ? Math.round(Number(hours[1].replace(',', '.')) * 60) : null;
}

/** Minutes as the text a form field shows for editing: "2:30". Empty for null. */
export function toClockText(minutes: number | null): string {
  if (minutes === null) {
    return '';
  }
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`;
}
