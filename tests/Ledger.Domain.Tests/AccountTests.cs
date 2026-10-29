namespace Ledger.Domain.Tests;

public class AccountTests
{
    [Fact]
    public void Opening_an_account_trims_its_fields_and_normalises_the_currency()
    {
        var account = Account.Open(" 1000 ", " Cash ", AccountType.Asset, "cad");

        Assert.Equal("1000", account.Code);
        Assert.Equal("Cash", account.Name);
        Assert.Equal("CAD", account.Currency);
        Assert.True(account.IsActive);
        Assert.False(account.IsBank);
    }

    [Fact]
    public void Only_an_asset_account_can_be_a_bank_account()
    {
        var bank = Account.Open("1000", "Operating account", AccountType.Asset, "CAD", isBank: true);
        var exception = Assert.Throws<LedgerRuleException>(() => Account.Open("4000", "Sales", AccountType.Revenue, "CAD", isBank: true));

        Assert.True(bank.IsBank);
        Assert.Equal("bank_account_not_asset", exception.Code);
    }

    [Theory]
    [InlineData("", "Cash", "CAD", "account_code_invalid")]
    [InlineData("123456789012345678901", "Cash", "CAD", "account_code_invalid")]
    [InlineData("1000", " ", "CAD", "account_name_invalid")]
    [InlineData("1000", "Cash", "CA", "currency_invalid")]
    [InlineData("1000", "Cash", "C4D", "currency_invalid")]
    public void Opening_an_account_with_bad_input_is_rejected(string code, string name, string currency, string expected)
    {
        var exception = Assert.Throws<LedgerRuleException>(() => Account.Open(code, name, AccountType.Asset, currency));

        Assert.Equal(expected, exception.Code);
    }

    [Fact]
    public void Opening_an_account_with_an_unknown_type_is_rejected()
    {
        var exception = Assert.Throws<LedgerRuleException>(() => Account.Open("1000", "Cash", (AccountType)9, "CAD"));

        Assert.Equal("account_type_invalid", exception.Code);
    }

    [Theory]
    [InlineData(AccountType.Asset, EntrySide.Debit)]
    [InlineData(AccountType.Expense, EntrySide.Debit)]
    [InlineData(AccountType.Liability, EntrySide.Credit)]
    [InlineData(AccountType.Equity, EntrySide.Credit)]
    [InlineData(AccountType.Revenue, EntrySide.Credit)]
    public void Each_account_type_has_its_normal_side(AccountType type, EntrySide expected)
    {
        Assert.Equal(expected, type.NormalSide());
    }
}
