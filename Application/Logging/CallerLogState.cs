namespace Application.Logging;

// Log state carrying caller info captured at compile time ([CallerMemberName] etc.).
public readonly record struct CallerLogState(string Message, string Member, string File, int Line, string? RequestId)
{
    public override string ToString() => Message;
}
