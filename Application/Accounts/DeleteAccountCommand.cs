namespace Application.Accounts;

public record DeleteAccountCommand(int AccountId, int RequestingUserId);
