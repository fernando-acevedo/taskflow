export interface ChatLink {
  readonly kind: 'internal' | 'external';
  readonly url: string;
}

/**
 * Classifies a url from the bot: an app path ("/forgot-password") or an http(s) address. Anything else
 * (javascript:, protocol-relative "//host", data:) is dropped, so a bad script cannot plant a dangerous link.
 */
export function classifyLink(url: string | null | undefined): ChatLink | null {
  if (!url) {
    return null;
  }
  if (url.startsWith('/') && !url.startsWith('//') && !url.includes('\\')) {
    return { kind: 'internal', url };
  }
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? { kind: 'external', url: parsed.href } : null;
  } catch {
    return null;
  }
}
