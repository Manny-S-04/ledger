using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Users;

public class GetUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<GetUserHandler> _logger;
    
    public GetUserHandler(IUserRepository userRepository, ILogger<GetUserHandler> logger)
    {
        _logger = logger;
        _userRepository = userRepository;
    }
    
    public async Task<GetUserResult> Handle()
    {
        _logger.Info("hit");
        var userResult = await _userRepository.GetAllUsers();
        GetUserResult result = userResult switch
        {
            DbResult<IEnumerable<User>>.Success users => new GetUserResult.Success(users.Value),
            DbResult<IEnumerable<User>>.Failure error => new GetUserResult.FailedWithError(error.Error),
            _ => new GetUserResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}