using Domain.Accounts;
using Domain.Datatypes;

namespace Ledger.Tests.Accounts;

internal static class AccountTestData
{
    public static Account Account(int id, int userId, string currency = "GBP", decimal balance = 0m) =>
        global::Domain.Accounts.Account.Create(id, userId, Amount.Create(balance), Currency.TryCreate(currency)!,
            DateTime.UtcNow, DateTime.UtcNow);
}
