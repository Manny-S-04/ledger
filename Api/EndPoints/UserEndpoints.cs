using Application.Auth;
using Application.Users;

using Application.Logging;

namespace Api.EndPoints;

public static class UserEndpoints
{
    private const string LogCategory = "Api.EndPoints.UserEndpoints";

    private record CreateUserRequest(string Email, string Password);
    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string Token, DateTime ExpiresAt);
    public static IEndpointRouteBuilder MapUserEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users");
        
        group.MapUserPostEndpoints();

        return app;
    }

    private static RouteGroupBuilder MapUserPostEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateUser);
        group.MapPost("/auth/login", Login);

        return group;
    }

        
    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        CreateUserHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        logger.Info("hit");
        CreateUserResult userResult =
            await handler.Handle(
                    new CreateUserCommand(request.Email, request.Password)
                    );
        IResult response = userResult switch
        {
            CreateUserResult.Created => Results.Ok(),
            CreateUserResult.InvalidEmail => Results.BadRequest(),
            CreateUserResult.AlreadyExists => Results.Conflict(),
            CreateUserResult.Failed => Results.StatusCode(400),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={userResult.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AuthenticateHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        logger.Info("hit");
        AuthenticateResult result =
            await handler.Handle(new AuthenticateCommand(request.Email, request.Password));
        IResult response = result switch
        {
            AuthenticateResult.Found found =>
                Results.Ok(new LoginResponse(found.Token.Value, found.Token.ExpiresAt)),
            AuthenticateResult.NotFound => Results.Unauthorized(),
            AuthenticateResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }
}
