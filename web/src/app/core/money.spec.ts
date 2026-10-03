import { formatMoney, fromMinor, toMinor } from './money';

describe('formatMoney', () => {
  it('groups thousands and always shows two places', () => {
    expect(formatMoney(1234567.5)).toBe('1,234,567.50');
    expect(formatMoney(0)).toBe('0.00');
  });

  it('writes a negative in parentheses and never with a sign', () => {
    expect(formatMoney(-42.1)).toBe('(42.10)');
  });

  it('leaves a zero blank only when asked to', () => {
    expect(formatMoney(0, true)).toBe('');
    expect(formatMoney(0.01, true)).toBe('0.01');
  });

  it('is blank for a missing value', () => {
    expect(formatMoney(null)).toBe('');
    expect(formatMoney(undefined)).toBe('');
  });
});

describe('toMinor', () => {
  it('reads an amount as whole cents without floating point', () => {
    expect(toMinor('12.5')).toBe(1250);
    expect(toMinor('0.07')).toBe(7);
    expect(toMinor(' 1999 ')).toBe(199900);
  });

  it('keeps sums exact where adding floats would drift', () => {
    expect(0.1 + 0.2).not.toBe(0.3);
    expect(toMinor('0.1')! + toMinor('0.2')!).toBe(toMinor('0.3'));
  });

  it.each(['', 'abc', '-5', '1.005', '1,000.00', '.5', '5.'])('refuses %j', (text) => {
    expect(toMinor(text)).toBeNull();
  });

  it('round trips through fromMinor', () => {
    expect(fromMinor(toMinor('27300.29')!)).toBe(27300.29);
  });
});
