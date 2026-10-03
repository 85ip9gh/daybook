import { fromMinor, toMinor } from '../core/money';

export interface DraftInvoiceLine {
  description: string;
  quantity: string;
  unitPrice: string;
  revenueAccountId: string;
}

export interface InvoiceSummary {
  amounts: (number | null)[];
  subtotal: number;
  tax: number;
  total: number;
  rate: number | null;
  problem: string | null;
  lines: { description: string; quantity: number; unitPrice: number; revenueAccountId: string }[];
}

const fourPlaces = /^\d{1,9}(\.\d{1,4})?$/;
const scale = 10_000n;
const half = (scale * scale) / 200n;
const perCent = (scale * scale) / 100n;

export function emptyInvoiceLine(): DraftInvoiceLine {
  return { description: '', quantity: '', unitPrice: '', revenueAccountId: '' };
}

function scaled(text: string): bigint | null {
  const trimmed = text.trim();
  if (!fourPlaces.test(trimmed)) {
    return null;
  }

  const [whole, fraction = ''] = trimmed.split('.');
  return BigInt(whole) * scale + BigInt(fraction.padEnd(4, '0'));
}

export function lineAmountMinor(quantity: string, unitPrice: string): number | null {
  const amount = scaled(quantity);
  const price = scaled(unitPrice);
  if (amount === null || price === null) {
    return null;
  }

  return Number((amount * price + half) / perCent);
}

export function taxMinor(subtotalMinor: number, ratePercent: string): number | null {
  const rate = scaled(ratePercent);
  if (rate === null || rate <= 0n || rate >= 100n * scale) {
    return null;
  }

  return Number((BigInt(subtotalMinor) * rate + half) / perCent);
}

function isBlank(line: DraftInvoiceLine): boolean {
  return (
    !line.description.trim() &&
    !line.quantity.trim() &&
    !line.unitPrice.trim() &&
    !line.revenueAccountId
  );
}

export function summariseInvoice(
  draft: DraftInvoiceLine[],
  taxPercent: string,
  taxAccountId: string,
): InvoiceSummary {
  const lines: InvoiceSummary['lines'] = [];
  const amounts: (number | null)[] = [];
  let subtotal = 0;
  let problem: string | null = null;

  draft.forEach((line, index) => {
    const amount = lineAmountMinor(line.quantity, line.unitPrice);
    amounts.push(amount === null ? null : fromMinor(amount));

    if (isBlank(line)) {
      return;
    }

    const row = index + 1;
    if (!line.description.trim()) {
      problem ??= `Line ${row} needs a description.`;
    } else if (amount === null || amount === 0) {
      problem ??= `Line ${row} needs a quantity and a price above zero, to at most four decimal places.`;
    } else if (!line.revenueAccountId) {
      problem ??= `Line ${row} needs a revenue account.`;
    } else {
      subtotal += amount;
      lines.push({
        description: line.description.trim(),
        quantity: Number(line.quantity),
        unitPrice: Number(line.unitPrice),
        revenueAccountId: line.revenueAccountId,
      });
    }
  });

  if (!problem && lines.length === 0) {
    problem = 'An invoice needs at least one line.';
  }

  let tax = 0;
  let rate: number | null = null;
  if (taxPercent.trim()) {
    const computed = taxMinor(subtotal, taxPercent);
    if (computed === null) {
      problem ??= 'The tax rate is a percentage above 0 and below 100.';
    } else if (!taxAccountId) {
      problem ??= 'Choose the account the tax is owed on.';
    } else {
      tax = computed;
      rate = Number(taxPercent) / 100;
    }
  }

  return {
    amounts,
    subtotal: fromMinor(subtotal),
    tax: fromMinor(tax),
    total: fromMinor(subtotal + tax),
    rate,
    problem,
    lines,
  };
}

export interface AllocationRow {
  invoiceId: string;
  outstanding: number;
  amount: string;
}

export interface PaymentSummary {
  total: number;
  problem: string | null;
  allocations: { invoiceId: string; amount: number }[];
}

export function summarisePayment(rows: AllocationRow[]): PaymentSummary {
  const allocations: PaymentSummary['allocations'] = [];
  let total = 0;
  let problem: string | null = null;

  for (const row of rows) {
    if (!row.amount.trim()) {
      continue;
    }

    const amount = toMinor(row.amount);
    if (amount === null || amount === 0) {
      problem ??= 'Each amount is above zero with at most two decimal places.';
    } else if (amount > Math.round(row.outstanding * 100)) {
      problem ??= 'An amount is more than its invoice has outstanding.';
    } else {
      total += amount;
      allocations.push({ invoiceId: row.invoiceId, amount: fromMinor(amount) });
    }
  }

  if (!problem && allocations.length === 0) {
    problem = 'Enter an amount against at least one invoice.';
  }

  return { total: fromMinor(total), problem, allocations };
}
