import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';

function flatten(value: unknown, prefix = ''): string[] {
  if (value !== null && typeof value === 'object') {
    return Object.entries(value).flatMap(([key, child]) => flatten(child, prefix ? `${prefix}.${key}` : key));
  }
  return [prefix];
}

describe('translation files', () => {
  const enKeys = new Set(flatten(en));
  const esKeys = new Set(flatten(es));

  it('have the same keys in English and Spanish', () => {
    expect([...enKeys].filter((key) => !esKeys.has(key))).toEqual([]);
    expect([...esKeys].filter((key) => !enKeys.has(key))).toEqual([]);
  });

  it('define the keys of the stale notice under common.stale', () => {
    for (const keys of [enKeys, esKeys]) {
      expect(keys.has('common.stale.reload')).toBe(true);
      expect(keys.has('common.stale.overwrite')).toBe(true);
    }
  });
});
