/**
 * Reads the values an emailed link carries in its fragment (`#email=…&token=…`). The fragment never reaches a
 * server (no access logs, no `Referer`). The router has already percent-decoded it, so decoding it again (as
 * `URLSearchParams` does) would turn a `+` into a space. No value can contain `&`: emails are limited to
 * Identity's user-name characters and the tokens are base64 or base64url.
 */
export function readEmailedLink(fragment: string | null): Map<string, string> {
  const link = new Map<string, string>();
  for (const pair of (fragment ?? '').split('&')) {
    const separator = pair.indexOf('=');
    if (separator > 0) {
      link.set(pair.slice(0, separator), pair.slice(separator + 1));
    }
  }
  return link;
}
