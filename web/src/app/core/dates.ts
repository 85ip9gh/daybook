function pad(value: number): string {
  return String(value).padStart(2, '0');
}

export function isoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function today(): string {
  return isoDate(new Date());
}

export function firstOfMonth(iso: string): string {
  return `${iso.slice(0, 7)}-01`;
}

export function monthLabel(yearMonth: string): string {
  const [year, month] = yearMonth.split('-').map(Number);
  return new Date(year, month - 1, 1).toLocaleDateString('en-CA', {
    month: 'long',
    year: 'numeric',
  });
}

export function previousMonth(iso: string): string {
  const [year, month] = iso.split('-').map(Number);
  const date = new Date(year, month - 2, 1);
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}`;
}
