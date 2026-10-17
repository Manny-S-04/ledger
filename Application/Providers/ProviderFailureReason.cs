namespace Application.Providers;

// Why the provider declined a charge (T-019.3: insufficient funds is a provider decline reason).
public enum ProviderFailureReason
{
    InsufficientFunds,
    CardDeclined,
    ProcessingError
}
