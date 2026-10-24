namespace Domain.Users;

public sealed record Email
{
    // 254 is the maximum length of an address (T-018.1).
    private const int MaxLength = 254;

    public string EmailAddress { get; }

    private Email(string emailAddress)
    {
        EmailAddress = emailAddress;
    }

    // Returns null when the value isn't a valid email (same pattern as UserId.TryCreate).
    public static Email? TryCreate(string? emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return null;

        string normalized = Normalize(emailAddress);
        if (normalized.Length > MaxLength)
            return null;

        return IsValidFormat(normalized) ? new Email(normalized) : null;
    }

    private static string Normalize(string emailAddress) => emailAddress.Trim().ToLowerInvariant();
    
    private static bool IsValidFormat(string emailAddress)
    {
        int atCount = 0;
        int atPosition = -1;
        foreach (var c in emailAddress)
        {
            if (c == '@')
            {
                atCount++;
                atPosition = emailAddress.IndexOf('@');
            }
        }
        
        if (atCount != 1) return false;
        
        var localPart = emailAddress[..atPosition];
        var domainPart = emailAddress[(atPosition + 1)..];
        
        if (string.IsNullOrEmpty(localPart) || string.IsNullOrEmpty(domainPart))
            return false;
            
        if (localPart.Contains("..") || domainPart.Contains(".."))
            return false;
            
        if (!domainPart.Contains('.') || domainPart.StartsWith('.') || domainPart.EndsWith('.'))
            return false;
            
        foreach (var c in localPart)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '+' && c != '-')
                return false;
        }
        
        foreach (var c in domainPart)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
                return false;
        }
        
        return true;
    }
}
