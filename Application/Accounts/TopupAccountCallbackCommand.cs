using Application.Providers;
using Domain.Accounts;

namespace Application.Accounts;

public sealed record TopupAccountCallbackCommand(TransactionId TransactionId, ProviderCallbackStatus Status);
