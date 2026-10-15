namespace Application.Accounts;

public record CreateAccountCommand(int UserId, string? Currency);
