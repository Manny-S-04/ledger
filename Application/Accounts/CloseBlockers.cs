using Domain.Accounts;

namespace Application.Accounts;

// What may stop an account closing (T-031), read before the provider is asked and before any lock is taken.
public sealed record CloseBlockers(bool AlreadyClosed, bool HasPending, IReadOnlyList<ExpiredTopup> Expired);

// An expired top-up and the provider's charge id, when the provider accepted the charge (T-031.4).
public sealed record ExpiredTopup(TransactionId Id, string? ProviderReference);
