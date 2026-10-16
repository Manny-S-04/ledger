namespace Application.Auth;

public record AuthToken(string Value, DateTime ExpiresAt);
