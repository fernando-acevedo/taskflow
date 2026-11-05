/** A person who can be mentioned: a project member. */
export interface Mentionable {
  readonly userId: string;
  readonly displayName: string;
}

export interface ActiveMention {
  /** Index of the `@`. */
  readonly start: number;
  /** What was typed after it, up to the caret. */
  readonly query: string;
}

export interface BodySegment {
  readonly text: string;
  readonly mention: boolean;
}

const MAX_QUERY_LENGTH = 40;

/** The `@query` the caret is in, if any: the `@` starts the text or follows whitespace, and the query has no line break. */
export function activeMention(text: string, caret: number): ActiveMention | null {
  for (let index = caret - 1; index >= 0 && caret - index <= MAX_QUERY_LENGTH + 1; index--) {
    const char = text[index];
    if (char === '\n') {
      return null;
    }
    if (char === '@') {
      const before = index === 0 ? ' ' : text[index - 1];
      return /\s/.test(before) ? { start: index, query: text.slice(index + 1, caret) } : null;
    }
  }
  return null;
}

/** Members whose name contains the query (any case, accents ignored), in the order given. */
export function suggest<T extends Mentionable>(members: readonly T[], query: string, limit = 6): T[] {
  const wanted = fold(query);
  return members.filter((member) => fold(member.displayName).includes(wanted)).slice(0, limit);
}

/** Replaces the `@query` with `@Name ` and says where the caret goes. */
export function insertMention(
  text: string,
  mention: ActiveMention,
  caret: number,
  name: string,
): { text: string; caret: number } {
  const inserted = `@${name} `;
  return { text: text.slice(0, mention.start) + inserted + text.slice(caret), caret: mention.start + inserted.length };
}

/** The members the body mentions by their `@Name`. The API needs these ids next to the text. */
export function mentionedIds(body: string, members: readonly Mentionable[]): string[] {
  // Longer names first, so "@Ana García" isn't also counted as a mention of "Ana".
  const taken: [number, number][] = [];
  const ids = new Set<string>();
  for (const member of [...members].sort((a, b) => b.displayName.length - a.displayName.length)) {
    for (const [start, end] of findMentions(body, member.displayName)) {
      if (!taken.some(([from, to]) => start < to && end > from)) {
        taken.push([start, end]);
        ids.add(member.userId);
      }
    }
  }
  return members.filter((member) => ids.has(member.userId)).map((member) => member.userId);
}

/** Splits the body so mentions of the given names can be highlighted. */
export function segmentBody(body: string, names: readonly string[]): BodySegment[] {
  const ranges = names
    .flatMap((name) => findMentions(body, name))
    .sort((a, b) => a[0] - b[0] || b[1] - a[1]);
  const segments: BodySegment[] = [];
  let position = 0;
  for (const [start, end] of ranges) {
    if (start < position) {
      continue;
    }
    if (start > position) {
      segments.push({ text: body.slice(position, start), mention: false });
    }
    segments.push({ text: body.slice(start, end), mention: true });
    position = end;
  }
  if (position < body.length) {
    segments.push({ text: body.slice(position), mention: false });
  }
  return segments;
}

/** `[start, end)` of each `@name` in the text that isn't followed by more letters of a longer word. */
function findMentions(body: string, name: string): [number, number][] {
  const needle = `@${name}`;
  const found: [number, number][] = [];
  for (let from = body.indexOf(needle); from >= 0; from = body.indexOf(needle, from + 1)) {
    const end = from + needle.length;
    const before = from === 0 ? ' ' : body[from - 1];
    if (/\s|[(¿¡"']/.test(before) && !/[\p{L}\p{N}_]/u.test(body[end] ?? ' ')) {
      found.push([from, end]);
    }
  }
  return found;
}

function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}
