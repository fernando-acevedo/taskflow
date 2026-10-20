import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it('keeps in-app paths', () => {
    expect(safeReturnUrl('/profile?tab=1')).toBe('/profile?tab=1');
  });

  it.each([undefined, null, '', 'https://evil.example', '//evil.example', '/\\evil.example', 'profile'])(
    'falls back to the home page for %s',
    (value) => {
      expect(safeReturnUrl(value)).toBe('/');
    },
  );
});
