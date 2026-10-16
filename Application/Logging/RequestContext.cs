namespace Application.Logging;

// Per-request id that flows with the async call chain (set by middleware in Api).
// Read on the producer side when a log is written, so the background consumer doesn't need it.
public static class RequestContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? RequestId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
