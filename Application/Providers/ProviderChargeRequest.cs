using Domain.Accounts;
using Domain.Datatypes;

namespace Application.Providers;

// Reference = the ledger's own id for the top-up, so callbacks can be correlated to it.
public sealed record ProviderChargeRequest(TransactionId Reference, Amount Amount, Currency Currency);
