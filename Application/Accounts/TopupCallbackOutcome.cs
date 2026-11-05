namespace Application.Accounts;

// What the repository did with a callback for a transaction that exists.
public enum TopupCallbackOutcome
{
    Credited,         // was pending, provider said Succeeded: marked done and the stored amount credited
    MarkedFailed,     // was pending, provider said Failed: marked failed, nothing credited
    AlreadyProcessed, // was done or failed already (a duplicate or late callback): nothing changed
    LateSuccessOnExpired // was expired, provider now says Succeeded (T-027): credited, marked done, was_late set,
                         // and a topup_credited_late audit event written
}
