import {
  DraftInvoiceLine,
  emptyInvoiceLine,
  lineAmountMinor,
  summariseInvoice,
  summarisePayment,
  taxMinor,
} from './invoice.model';

function line(
  description: string,
  quantity: string,
  unitPrice: string,
  account = 'rev',
): DraftInvoiceLine {
  return { description, quantity, unitPrice, revenueAccountId: account };
}

describe('lineAmountMinor', () => {
  it('multiplies exactly and rounds half away from zero, as the server does', () => {
    expect(lineAmountMinor('3', '19.99')).toBe(5997);
    expect(lineAmountMinor('1.5', '0.33')).toBe(50);
    expect(lineAmountMinor('0.5', '0.05')).toBe(3);
    expect(lineAmountMinor('0.5', '0.07')).toBe(4);
    expect(lineAmountMinor('0.0001', '0.01')).toBe(0);
  });

  it('does not lose cents on large values', () => {
    expect(lineAmountMinor('99999.9999', '99999.9999')).toBe(999999998000);
  });

  it.each(['', 'x', '-1', '1.00001'])('refuses the quantity %j', (quantity) => {
    expect(lineAmountMinor(quantity, '10')).toBeNull();
  });
});

describe('taxMinor', () => {
  it('rounds once on the subtotal', () => {
    expect(taxMinor(6047, '14')).toBe(847);
    expect(taxMinor(5997, '14')).toBe(840);
    expect(taxMinor(18000, '14')).toBe(2520);
  });

  it.each(['0', '100', '-5', 'x'])('refuses the rate %j', (rate) => {
    expect(taxMinor(1000, rate)).toBeNull();
  });
});

describe('summariseInvoice', () => {
  it('totals the lines and the tax', () => {
    const summary = summariseInvoice(
      [line('Hours', '3', '19.99'), line('Seat', '1.5', '0.33')],
      '14',
      'tax',
    );

    expect(summary.problem).toBeNull();
    expect(summary.amounts).toEqual([59.97, 0.5]);
    expect([summary.subtotal, summary.tax, summary.total]).toEqual([60.47, 8.47, 68.94]);
    expect(summary.rate).toBe(0.14);
    expect(summary.lines).toHaveLength(2);
  });

  it('leaves tax out when no rate is given', () => {
    const summary = summariseInvoice([line('Hours', '2', '50')], '', '');

    expect([summary.tax, summary.total, summary.rate]).toEqual([0, 100, null]);
    expect(summary.problem).toBeNull();
  });

  it.each([
    [[emptyInvoiceLine()], '', '', 'An invoice needs at least one line.'],
    [[line('', '1', '10')], '', '', 'Line 1 needs a description.'],
    [
      [line('Hours', '0', '10')],
      '',
      '',
      'Line 1 needs a quantity and a price above zero, to at most four decimal places.',
    ],
    [[line('Hours', '1', '10', '')], '', '', 'Line 1 needs a revenue account.'],
    [
      [line('Hours', '1', '10')],
      '120',
      'tax',
      'The tax rate is a percentage above 0 and below 100.',
    ],
    [[line('Hours', '1', '10')], '14', '', 'Choose the account the tax is owed on.'],
  ])('reports what is wrong', (lines, rate, account, expected) => {
    expect(summariseInvoice(lines, rate, account).problem).toBe(expected);
  });
});

describe('summarisePayment', () => {
  const rows = [
    { invoiceId: 'a', outstanding: 100, amount: '' },
    { invoiceId: 'b', outstanding: 80.1, amount: '' },
  ];

  it('totals what is being paid and skips invoices left blank', () => {
    const summary = summarisePayment([
      { ...rows[0], amount: '100' },
      { ...rows[1], amount: '' },
    ]);

    expect(summary).toEqual({
      total: 100,
      problem: null,
      allocations: [{ invoiceId: 'a', amount: 100 }],
    });
  });

  it('allows paying an invoice exactly in full', () => {
    expect(summarisePayment([{ ...rows[1], amount: '80.10' }]).problem).toBeNull();
  });

  it.each([
    [[{ ...rows[0], amount: '100.01' }], 'An amount is more than its invoice has outstanding.'],
    [[{ ...rows[0], amount: '0' }], 'Each amount is above zero with at most two decimal places.'],
    [
      [{ ...rows[0], amount: '1.005' }],
      'Each amount is above zero with at most two decimal places.',
    ],
    [rows, 'Enter an amount against at least one invoice.'],
  ])('reports what is wrong', (allocations, expected) => {
    expect(summarisePayment(allocations).problem).toBe(expected);
  });
});
