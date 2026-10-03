import { EntrySide } from '../core/api';
import { fromMinor, toMinor } from '../core/money';

export interface DraftLine {
  accountId: string;
  debit: string;
  credit: string;
}

export interface DraftSummary {
  debit: number;
  credit: number;
  difference: number;
  problem: string | null;
  lines: { accountId: string; side: EntrySide; amount: number }[];
}

export function emptyLine(): DraftLine {
  return { accountId: '', debit: '', credit: '' };
}

function isBlank(line: DraftLine): boolean {
  return !line.accountId && !line.debit.trim() && !line.credit.trim();
}

export function summarise(draft: DraftLine[]): DraftSummary {
  const lines: DraftSummary['lines'] = [];
  let debit = 0;
  let credit = 0;
  let problem: string | null = null;

  draft.forEach((line, index) => {
    if (isBlank(line)) {
      return;
    }

    const row = index + 1;
    const hasDebit = line.debit.trim() !== '';
    const hasCredit = line.credit.trim() !== '';
    const amount = toMinor(hasDebit ? line.debit : line.credit);

    if (!line.accountId) {
      problem ??= `Line ${row} needs an account.`;
    } else if (hasDebit === hasCredit) {
      problem ??= `Line ${row} needs a debit or a credit, not both.`;
    } else if (amount === null || amount === 0) {
      problem ??= `Line ${row} needs an amount above zero with at most two decimal places.`;
    } else {
      debit += hasDebit ? amount : 0;
      credit += hasCredit ? amount : 0;
      lines.push({
        accountId: line.accountId,
        side: hasDebit ? 'Debit' : 'Credit',
        amount: fromMinor(amount),
      });
    }
  });

  if (!problem && lines.length < 2) {
    problem = 'An entry needs at least two lines.';
  } else if (!problem && debit !== credit) {
    problem = `Out of balance by ${fromMinor(Math.abs(debit - credit)).toFixed(2)}.`;
  }

  return {
    debit: fromMinor(debit),
    credit: fromMinor(credit),
    difference: fromMinor(debit - credit),
    problem,
    lines,
  };
}
