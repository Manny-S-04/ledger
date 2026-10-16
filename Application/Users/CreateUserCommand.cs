using Domain.Users;

namespace Application.Users;

public record CreateUserCommand(string Email, string Password);