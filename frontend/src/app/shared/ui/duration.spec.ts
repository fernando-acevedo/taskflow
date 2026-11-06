import { formatMinutes, parseDuration, toClockText } from './duration';

describe('formatMinutes', () => {
  it.each([
    [0, '0 min'],
    [45, '45 min'],
    [60, '1 h'],
    [150, '2 h 30 min'],
    [1440, '24 h'],
    [-5, '0 min'],
  ])('shows %i minutes as "%s"', (minutes, text) => {
    expect(formatMinutes(minutes)).toBe(text);
  });
});

describe('parseDuration', () => {
  it.each([
    ['2:30', 150],
    ['0:45', 45],
    ['2h 30m', 150],
    ['2h', 120],
    ['45m', 45],
    ['45 min', 45],
    ['2.5', 150],
    ['2,5', 150],
    ['1.25 h', 75],
    ['3', 180],
  ])('reads "%s" as %i minutes', (text, minutes) => {
    expect(parseDuration(text)).toBe(minutes);
  });

  it.each(['', '  ', 'abc', '2:75', '-1', '1h x'])('rejects "%s"', (text) => {
    expect(parseDuration(text)).toBeNull();
  });
});

describe('toClockText', () => {
  it('writes minutes as hours:minutes for editing', () => {
    expect(toClockText(150)).toBe('2:30');
    expect(toClockText(5)).toBe('0:05');
    expect(toClockText(null)).toBe('');
  });
});
