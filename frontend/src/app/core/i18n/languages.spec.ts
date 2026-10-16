import { resolveInitialLanguage } from './languages';

describe('resolveInitialLanguage', () => {
  it('prefers the saved language', () => {
    expect(resolveInitialLanguage('es', ['en-US'])).toBe('es');
  });

  it('ignores a saved value that is not supported', () => {
    expect(resolveInitialLanguage('fr', ['en-US'])).toBe('en');
  });

  it('uses Spanish for any es-* browser language', () => {
    expect(resolveInitialLanguage(null, ['es-AR', 'en'])).toBe('es');
  });

  it('falls back to English for other browser languages', () => {
    expect(resolveInitialLanguage(null, ['pt-BR'])).toBe('en');
    expect(resolveInitialLanguage(null, [])).toBe('en');
  });
});
