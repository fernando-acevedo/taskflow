import { passwordStrength } from './password-strength';

describe('passwordStrength', () => {
  it('is 0 for an empty password', () => {
    expect(passwordStrength('')).toBe(0);
  });

  it('rates short, repeated or sequential passwords as weak', () => {
    expect(passwordStrength('abc')).toBe(1);
    expect(passwordStrength('aaaaaaaaaaaaaaaa')).toBe(1);
    expect(passwordStrength('123456789')).toBe(1);
    expect(passwordStrength('abcdefghij')).toBe(1);
  });

  it('rewards length and character variety', () => {
    expect(passwordStrength('password')).toBe(1);
    expect(passwordStrength('Passw0rd')).toBe(2);
    expect(passwordStrength('Passw0rdLonger')).toBe(3);
    expect(passwordStrength('Passw0rd!Longer')).toBe(4);
  });
});
