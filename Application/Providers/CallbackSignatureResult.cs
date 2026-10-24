namespace Application.Providers;

public enum CallbackSignatureResult
{
    Valid,
    Missing,    // timestamp or signature header absent
    Malformed,  // timestamp not unix seconds, or signature not hex
    Mismatch,   // signature doesn't match the body: not from the provider, or the body was changed
    Stale       // signature fine but the timestamp is outside the tolerance: a replay or a badly skewed clock
}
