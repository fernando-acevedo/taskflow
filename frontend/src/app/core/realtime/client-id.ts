/** The header that tells the API which tab made a request, so the realtime events it causes can skip that tab. */
export const CLIENT_ID_HEADER = 'X-Client-Id';

let id: string | null = null;

/** A v4 UUID from `getRandomValues`, for contexts where `crypto.randomUUID` is missing (it needs a secure context). */
function randomUuid(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** A random id for this tab, created on first use and kept in memory only (a reload is a new tab, as far as echoes go). */
export function clientId(): string {
  id ??= randomUuid();
  return id;
}

/** For tests: forget the id so the next call generates one again. */
export function resetClientIdForTests(): void {
  id = null;
}
