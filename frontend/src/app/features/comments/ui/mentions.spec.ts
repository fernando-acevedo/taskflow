import { activeMention, insertMention, mentionedIds, segmentBody, suggest } from './mentions';

const members = [
  { userId: 'u1', displayName: 'Ana García' },
  { userId: 'u2', displayName: 'Ana' },
  { userId: 'u3', displayName: 'Bruno Díaz' },
];

describe('mentions', () => {
  it('finds the @query the caret is in', () => {
    expect(activeMention('hola @an', 8)).toEqual({ start: 5, query: 'an' });
    expect(activeMention('@', 1)).toEqual({ start: 0, query: '' });
  });

  it('ignores an @ inside a word (an email) and across lines', () => {
    expect(activeMention('ana@example', 11)).toBeNull();
    expect(activeMention('@ana\nhola', 9)).toBeNull();
  });

  it('suggests by name, ignoring case and accents', () => {
    expect(suggest(members, 'DIA').map((m) => m.userId)).toEqual(['u3']);
    expect(suggest(members, 'garcia').map((m) => m.userId)).toEqual(['u1']);
    expect(suggest(members, '').length).toBe(3);
  });

  it('inserts the picked name in place of the query', () => {
    const text = 'hola @an y adiós';
    const result = insertMention(text, { start: 5, query: 'an' }, 8, 'Ana García');
    expect(result.text).toBe('hola @Ana García  y adiós');
    expect(result.caret).toBe(5 + '@Ana García '.length);
  });

  it('lists the members the body mentions, not a longer name that merely starts the same', () => {
    expect(mentionedIds('Hola @Ana García, mira esto', members)).toEqual(['u1']);
    expect(mentionedIds('Hola @Ana, mira esto', members)).toEqual(['u2']);
    expect(mentionedIds('mail ana@Ana.com', members)).toEqual([]);
  });

  it('segments the body so mentions can be highlighted', () => {
    expect(segmentBody('Hi @Ana García and @Bruno Díaz!', ['Ana García', 'Bruno Díaz'])).toEqual([
      { text: 'Hi ', mention: false },
      { text: '@Ana García', mention: true },
      { text: ' and ', mention: false },
      { text: '@Bruno Díaz', mention: true },
      { text: '!', mention: false },
    ]);
  });

  it('returns the whole body as one segment when nobody is mentioned', () => {
    expect(segmentBody('plain', [])).toEqual([{ text: 'plain', mention: false }]);
  });
});
