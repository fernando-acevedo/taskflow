import { classifyLink } from './chat-link';

describe('classifyLink', () => {
  it('tells app paths from http(s) addresses', () => {
    expect(classifyLink('/forgot-password')).toEqual({ kind: 'internal', url: '/forgot-password' });
    expect(classifyLink('https://help.example.com/a')).toEqual({ kind: 'external', url: 'https://help.example.com/a' });
  });

  it('drops anything else', () => {
    for (const url of [null, '', 'javascript:alert(1)', '//evil.example', 'data:text/html,x', 'mailto:a@b.c', 'help']) {
      expect(classifyLink(url)).toBeNull();
    }
  });
});
