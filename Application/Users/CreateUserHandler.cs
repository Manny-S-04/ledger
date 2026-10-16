using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Users;

public class CreateUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateUserHandler> _logger;
    
    public CreateUserHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, ILogger<CreateUserHandler> logger)
    {
        _logger = logger;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }
    
    public async Task<CreateUserResult> Handle(CreateUserCommand command)
    {
        _logger.Info("hit");
        if (string.IsNullOrEmpty(command.Email))
        {
            _logger.Info("result=InvalidEmail");
            return new CreateUserResult.InvalidEmail();
        }
        var email = Email.Create(command.Email);

        var exists = await _userRepository.Exists(email);

        switch (exists)
        {
            case DbResult<bool>.Success { Value: true }:
                _logger.Info("result=AlreadyExists");
                return new CreateUserResult.AlreadyExists();
            case DbResult<bool>.Failure:
                _logger.Info("result=Failed");
                return new CreateUserResult.Failed();
            case DbResult<bool>.Success:
                break;
            case var _:
                _logger.Info("result=Failed");
                return new CreateUserResult.Failed();
        }

        var user = User.Create(email, _passwordHasher.Hash(command.PasswordHash));

        var createUser = await _userRepository.Create(user);
        CreateUserResult result = createUser switch
        {
            DbResult<UserId>.Success userId => new CreateUserResult.Created(userId.Value.Value),
            _ => new CreateUserResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}