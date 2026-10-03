import { Pipe, PipeTransform } from '@angular/core';

const formatter = new Intl.NumberFormat('en-CA', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const amountPattern = /^\d{1,13}(\.\d{1,2})?$/;

export function formatMoney(value: number | null | undefined, blankZero = false): string {
  if (value === null || value === undefined || (blankZero && value === 0)) {
    return '';
  }

  const text = formatter.format(Math.abs(value));
  return value < 0 ? `(${text})` : text;
}

export function toMinor(text: string | null | undefined): number | null {
  const trimmed = (text ?? '').trim();
  if (!amountPattern.test(trimmed)) {
    return null;
  }

  const [whole, fraction = ''] = trimmed.split('.');
  return Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
}

export function fromMinor(minor: number): number {
  return minor / 100;
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, blankZero = false): string {
    return formatMoney(value, blankZero);
  }
}
