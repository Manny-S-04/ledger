namespace Application.Accounts;

// Result of the close transaction (T-031), decided under the account row lock.
public enum CloseAccountOutcome
{
    Closed,
    AlreadyClosed,
    HasBalance,
    PendingTopups
}
