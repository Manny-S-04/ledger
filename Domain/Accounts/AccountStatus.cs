namespace Domain.Accounts;

// T-031: an account is never deleted; closing it keeps its id and history.
public enum AccountStatus
{
    Open,
    Closed
}
