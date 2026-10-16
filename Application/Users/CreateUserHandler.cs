using Application.DbResult;
using Application.Interfaces;
using Domain.Users;

namespace Application.Users;

public class CreateUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    
    public CreateUserHandler(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }
    
    public async Task<CreateUserResult> Handle(CreateUserCommand command)
    {
        if (string.IsNullOrEmpty(command.Email))
        {
            return new CreateUserResult.InvalidEmail();
        }
        var email = Email.Create(command.Email);

        var exists = await _userRepository.Exists(email);

        switch (exists)
        {
            case DbResult<bool>.Success { Value: true }:
                return new CreateUserResult.AlreadyExists();
            case DbResult<bool>.Failure:
                return new CreateUserResult.Failed();
            case DbResult<bool>.Success:
                break;
            case var _:
                return new CreateUserResult.Failed();
        }

        var user = User.Create(email, _passwordHasher.Hash(command.PasswordHash));

        var createUser = await _userRepository.Create(user);
        return createUser switch
        {
            DbResult<UserId>.Success userId => new CreateUserResult.Created(userId.Value.Value),
            _ => new CreateUserResult.Failed()
        };
    }
}