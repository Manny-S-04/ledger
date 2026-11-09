using Application.Providers;
using Domain.Accounts;

namespace Application.Accounts;

// EventId and Sequence come from the provider's callback (T-034): repeated event ids and lower sequences are ignored.
public sealed record TopupAccountCallbackCommand(TransactionId TransactionId, ProviderCallbackStatus Status, string EventId, long Sequence);
