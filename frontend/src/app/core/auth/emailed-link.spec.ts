import { readEmailedLink } from './emailed-link';

describe('readEmailedLink', () => {
  it('keeps the values exactly as the router decoded them', () => {
    // What the router hands over for #email=ana%2Btest%40example.com&token=CfDJ8a%2Bb%2Fc%3D%3D
    const link = readEmailedLink('email=ana+test@example.com&token=CfDJ8a+b/c==');

    expect(link.get('email')).toBe('ana+test@example.com');
    expect(link.get('token')).toBe('CfDJ8a+b/c==');
  });

  it('is empty without a fragment', () => {
    expect(readEmailedLink(null).size).toBe(0);
  });
});
