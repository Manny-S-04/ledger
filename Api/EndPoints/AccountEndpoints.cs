using System.Security.Claims;
using Api.Auth;
using Application.Accounts;
using Domain.Accounts;

namespace Api.EndPoints;

public static class AccountEndpoints
{
    private record CreateAccountRequest(int UserId, string? Currency);

    private record AccountResponse(
        int Id,
        int UserId,
        string Balance,
        string? Currency,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static AccountResponse From(Account account) =>
            new(account.Id,
                account.UserId,
                account.Balance.ToString(),
                account.Currency,
                account.CreatedAt,
                account.UpdatedAt);
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/accounts").RequireAuthorization();

        group.MapAccountGetEndpoints();
        group.MapAccountPostEndpoints();
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
        ClaimsPrincipal user,
        GetAccountsByEmailHandler handler)
    {
        if (!string.Equals(email.Trim(), user.GetEmail(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        GetAccountsByEmailResult result =
            await handler.Handle(new GetAccountsByEmailQuery(email));
        return result switch
        {
            GetAccountsByEmailResult.Found found => Results.Ok(found.Accounts.Select(AccountResponse.From)),
            GetAccountsByEmailResult.NotFound => Results.NotFound(),
            GetAccountsByEmailResult.InvalidEmail => Results.BadRequest(),
            GetAccountsByEmailResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }

    private static async Task<IResult> GetAccountsByUserId(
        int userId,
        ClaimsPrincipal user,
        GetAccountsByUserIdHandler handler)
    {
        if (userId != user.GetUserId())
        {
            return Results.Forbid();
        }

        GetAccountsByUserIdResult result =
            await handler.Handle(new GetAccountsByUserIdQuery(userId));
        return result switch
        {
            GetAccountsByUserIdResult.Found found => Results.Ok(found.Accounts.Select(AccountResponse.From)),
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
        ClaimsPrincipal user,
        CreateAccountHandler handler)
    {
        if (request.UserId != user.GetUserId())
        {
            return Results.Forbid();
        }

        CreateAccountResult result =
            await handler.Handle(new CreateAccountCommand(request.UserId, request.Currency));
        return result switch
        {
            CreateAccountResult.Created created => Results.Ok(AccountResponse.From(created.Account)),
            CreateAccountResult.InvalidUserId => Results.BadRequest(),
            CreateAccountResult.InvalidCurrency => Results.BadRequest(),
            CreateAccountResult.Failed => Results.StatusCode(500),
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
        ClaimsPrincipal user,
        DeleteAccountHandler handler)
    {
        DeleteAccountResult result =
            await handler.Handle(new DeleteAccountCommand(id, user.GetUserId()));
        return result switch
        {
            DeleteAccountResult.Deleted => Results.Ok(),
            DeleteAccountResult.NotFound => Results.NotFound(),
            DeleteAccountResult.Forbidden => Results.Forbid(),
            DeleteAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
    }
}
