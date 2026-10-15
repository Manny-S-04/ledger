using Application.Accounts;

namespace Api.EndPoints;

public static class AccountEndpoints
{
    private record CreateAccountRequest(int UserId, decimal Balance, string? Currency);
    private record UpdateAccountRequest(int UserId, decimal Balance, string? Currency);

    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/accounts");

        group.MapAccountGetEndpoints();
        group.MapAccountPostEndpoints();
        group.MapAccountPutEndpoints();
        group.MapAccountDeleteEndpoints();

        return app;
    }

    private static RouteGroupBuilder MapAccountGetEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapGet("/{email}", GetAccountsByEmail);
        group.MapGet("/user/{userId:int}", GetAccountsByUserId);

        return group;
    }

    private static async Task<IResult> GetAccountsByEmail(
        string email,
        GetAccountsByEmailHandler handler)
    {
        GetAccountsByEmailResult result =
            await handler.Handle(new GetAccountsByEmailQuery(email));
        return result switch
        {
            GetAccountsByEmailResult.Found found => Results.Ok(found.Accounts),
            GetAccountsByEmailResult.NotFound => Results.NotFound(),
            GetAccountsByEmailResult.InvalidEmail => Results.BadRequest(),
            GetAccountsByEmailResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }

    private static async Task<IResult> GetAccountsByUserId(
        int userId,
        GetAccountsByUserIdHandler handler)
    {
        GetAccountsByUserIdResult result =
            await handler.Handle(new GetAccountsByUserIdQuery(userId));
        return result switch
        {
            GetAccountsByUserIdResult.Found found => Results.Ok(found.Accounts),
            GetAccountsByUserIdResult.NotFound => Results.NotFound(),
            GetAccountsByUserIdResult.InvalidUserId => Results.BadRequest(),
            GetAccountsByUserIdResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }

    private static RouteGroupBuilder MapAccountPostEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateAccount);

        return group;
    }

    private static async Task<IResult> CreateAccount(
        CreateAccountRequest request,
        CreateAccountHandler handler)
    {
        CreateAccountResult result =
            await handler.Handle(
                new CreateAccountCommand(request.UserId, request.Balance, request.Currency));
        return result switch
        {
            CreateAccountResult.Created created => Results.Ok(created.Account),
            CreateAccountResult.InvalidUserId => Results.BadRequest(),
            CreateAccountResult.InvalidAmount => Results.BadRequest(),
            CreateAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }

    private static RouteGroupBuilder MapAccountPutEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapPut("/{id:int}", UpdateAccount);

        return group;
    }

    private static async Task<IResult> UpdateAccount(
        int id,
        UpdateAccountRequest request,
        UpdateAccountHandler handler)
    {
        UpdateAccountResult result =
            await handler.Handle(
                new UpdateAccountCommand(id, request.UserId, request.Balance, request.Currency));
        return result switch
        {
            UpdateAccountResult.Updated => Results.Ok(),
            UpdateAccountResult.InvalidAmount => Results.BadRequest(),
            UpdateAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }

    private static RouteGroupBuilder MapAccountDeleteEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapDelete("/{id:int}", DeleteAccount);

        return group;
    }

    private static async Task<IResult> DeleteAccount(
        int id,
        DeleteAccountHandler handler)
    {
        DeleteAccountResult result =
            await handler.Handle(new DeleteAccountCommand(id));
        return result switch
        {
            DeleteAccountResult.Deleted => Results.Ok(),
            DeleteAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }
}
