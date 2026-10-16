using Application.DbResult;
using Application.Interfaces;
using Domain.Users;

namespace Application.Auth;

public class AuthenticateHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthenticateHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthenticateResult> Handle(AuthenticateCommand command)
    {
        Email email;
        try
        {
            email = Email.Create(command.Email);
        }
        catch (ArgumentException)
        {
            // A malformed email can't belong to a user: same result as unknown email / wrong password.
            return new AuthenticateResult.NotFound();
        }

        DbResult<User> userResult = await _userRepository.GetUserByEmail(email);
        switch (userResult)
        {
            case DbResult<User>.Failure { Error: DbError.NotFound }:
                return new AuthenticateResult.NotFound();
            case DbResult<User>.Failure:
                return new AuthenticateResult.Failed();
        }

        if (userResult is not DbResult<User>.Success success)
        {
            return new AuthenticateResult.Failed();
        }

        User user = success.Value;
        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return new AuthenticateResult.NotFound();
        }

        UserId? userId = UserId.TryCreate(user.Id);
        if (userId is null)
        {
            return new AuthenticateResult.Failed();
        }

        return new AuthenticateResult.Found(_tokenService.Create(userId.Value, user.Email));
    }
}
