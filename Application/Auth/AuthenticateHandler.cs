using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Auth;

public class AuthenticateHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthenticateHandler> _logger;

    public AuthenticateHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthenticateHandler> logger)
    {
        _logger = logger;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthenticateResult> Handle(AuthenticateCommand command)
    {
        _logger.Info("Authenticating user");
        Email? email = Email.TryCreate(command.Email);
        if (email is null)
        {
            // A malformed email can't belong to a user: same result as unknown email / wrong password.
            _logger.Info("result=NotFound");
            return new AuthenticateResult.NotFound();
        }

        DbResult<User> userResult = await _userRepository.GetUserByEmail(email);
        switch (userResult)
        {
            case DbResult<User>.Failure { Error: DbError.NotFound }:
                _logger.Info("result=NotFound");
                return new AuthenticateResult.NotFound();
            case DbResult<User>.Failure:
                _logger.Info("result=Failed");
                return new AuthenticateResult.Failed();
        }

        if (userResult is not DbResult<User>.Success success)
        {
            _logger.Info("result=Failed");
            return new AuthenticateResult.Failed();
        }

        User user = success.Value;
        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            _logger.Info("result=NotFound");
            return new AuthenticateResult.NotFound();
        }

        UserId? userId = UserId.TryCreate(user.Id);
        if (userId is null)
        {
            _logger.Info("result=Failed");
            return new AuthenticateResult.Failed();
        }

        _logger.Info("result=Found");
        return new AuthenticateResult.Found(_tokenService.Create(userId.Value, user.Email));
    }
}
