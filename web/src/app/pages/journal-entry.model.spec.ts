import { DraftLine, emptyLine, summarise } from './journal-entry.model';

function line(accountId: string, debit: string, credit: string): DraftLine {
  return { accountId, debit, credit };
}

describe('summarise', () => {
  it('accepts a balanced entry and turns it into request lines', () => {
    const summary = summarise([
      line('bank', '115.00', ''),
      line('sales', '', '100'),
      line('tax', '', '15'),
    ]);

    expect(summary.problem).toBeNull();
    expect(summary.debit).toBe(115);
    expect(summary.credit).toBe(115);
    expect(summary.lines).toEqual([
      { accountId: 'bank', side: 'Debit', amount: 115 },
      { accountId: 'sales', side: 'Credit', amount: 100 },
      { accountId: 'tax', side: 'Credit', amount: 15 },
    ]);
  });

  it('says by how much an entry is out of balance', () => {
    const summary = summarise([line('bank', '12.50', ''), line('fees', '', '12.00')]);

    expect(summary.problem).toBe('Out of balance by 0.50.');
    expect(summary.difference).toBe(0.5);
  });

  it('balances amounts that floating point would not', () => {
    const summary = summarise([
      line('a', '0.10', ''),
      line('b', '0.20', ''),
      line('c', '', '0.30'),
    ]);

    expect(summary.problem).toBeNull();
  });

  it('ignores blank lines but still needs two real ones', () => {
    expect(summarise([emptyLine(), emptyLine()]).problem).toBe(
      'An entry needs at least two lines.',
    );
    expect(
      summarise([line('bank', '5', ''), emptyLine(), line('sales', '', '5')]).problem,
    ).toBeNull();
  });

  it.each([
    [line('', '5', ''), 'Line 1 needs an account.'],
    [line('bank', '5', '5'), 'Line 1 needs a debit or a credit, not both.'],
    [line('bank', '', ''), 'Line 1 needs a debit or a credit, not both.'],
    [line('bank', '0', ''), 'Line 1 needs an amount above zero with at most two decimal places.'],
    [
      line('bank', '1.005', ''),
      'Line 1 needs an amount above zero with at most two decimal places.',
    ],
    [line('bank', 'ten', ''), 'Line 1 needs an amount above zero with at most two decimal places.'],
  ])('names the first line that is wrong: %j', (bad, expected) => {
    expect(summarise([bad, line('sales', '', '5')]).problem).toBe(expected);
  });
});
