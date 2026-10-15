namespace Application.Accounts;

public record UpdateAccountCommand(int AccountId, int UserId, decimal Balance, string? Currency);
