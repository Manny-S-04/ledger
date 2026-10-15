namespace Application.Accounts;

public record CreateAccountCommand(int UserId, decimal Balance, string? Currency);
