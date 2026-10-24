using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;

namespace Application.Accounts;

public sealed record TopupAccountCommand(UserId UserId, AccountId AccountId, Amount Amount, Currency Currency);
