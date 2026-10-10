using Application.DbResult;
using Application.Interfaces;
using Domain.Users;

namespace Application.Users;

public class GetUserHandler
{
    private readonly IUserRepository _userRepository;
    
    public GetUserHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<GetUserResult> Handle()
    {
        var userResult = await _userRepository.GetAllUsers();
        return userResult switch
        {
            DbResult<IEnumerable<User>>.Success users => new GetUserResult.Success(users.Value),
            DbResult<IEnumerable<User>>.Failure error => new GetUserResult.FailedWithError(error.Error),
            _ => new GetUserResult.Failed()
        };
    }
}