using Application.Auth;
using Application.Users;

namespace Api.EndPoints;

public static class UserEndpoints
{
    private record CreateUserRequest(string Email, string Password);
    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string Token, DateTime ExpiresAt);
    public static IEndpointRouteBuilder MapUserEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users");
        
        group.MapUserGetEndpoints();
        group.MapUserPostEndpoints();

        return app;
    }

    private static RouteGroupBuilder MapUserGetEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapGet("/", GetUsers).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetUsers(GetUserHandler handler) {
        GetUserResult userResult = await handler.Handle();
        return userResult switch
        {
            GetUserResult.Success users => Results.Ok(users.Users),
            GetUserResult.FailedWithError =>
                // handle error
                Results.StatusCode(400),
            GetUserResult.Failed => Results.StatusCode(500),
            _ => Results.Ok()
        };
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
        CreateUserHandler handler)
    {
        CreateUserResult userResult =
            await handler.Handle(
                    new CreateUserCommand(request.Email, request.Password)
                    );
        return userResult switch
        {
            CreateUserResult.Created => Results.Ok(),
            CreateUserResult.InvalidEmail => Results.BadRequest(),
            CreateUserResult.AlreadyExists => Results.Conflict(),
            CreateUserResult.Failed => Results.StatusCode(400),
            _ => Results.StatusCode(500)
        };
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AuthenticateHandler handler)
    {
        AuthenticateResult result =
            await handler.Handle(new AuthenticateCommand(request.Email, request.Password));
        return result switch
        {
            AuthenticateResult.Found found =>
                Results.Ok(new LoginResponse(found.Token.Value, found.Token.ExpiresAt)),
            AuthenticateResult.NotFound => Results.Unauthorized(),
            AuthenticateResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }
}
