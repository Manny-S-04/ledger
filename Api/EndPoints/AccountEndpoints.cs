using Infrastructure.Providers;
using Application.Interfaces;
using System.Text.Json;
using Application.Providers;
using System.Globalization;
using System.Security.Claims;
using Api.Auth;
using Application.Accounts;
using Domain.Accounts;

using Application.Logging;
using Application.DbResult;
using Domain.Users;
using Domain.Datatypes;

namespace Api.EndPoints;

public static class AccountEndpoints
{
    private const string LogCategory = "Api.EndPoints.AccountEndpoints";

    private record CreateAccountRequest(string? Currency);

    private record TopUpAccountRequest(int AccountId, decimal Amount, string Currency);

    // Mirrors the provider's callback payload (FakeProvider CallbackPayload). Only Reference and Status are used.
    private record TopUpAccountCallbackRequest(
        string? ProviderReference,
        string? Reference,
        string? Status,
        string? FailureReason,
        decimal Amount,
        string? Currency,
        long Sequence);

    private record AccountResponse(
        int Id,
        int UserId,
        string Balance,
        string Currency,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static AccountResponse From(Account account) =>
            new(account.Id,
                account.UserId,
                account.Balance.ToString(),
                account.Currency.Code,
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
        GetAccountsByEmailHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        logger.Info($"Get accounts by email request received, callerUserId={user.GetUserId()}");
        if (!string.Equals(email.Trim(), user.GetEmail(), StringComparison.OrdinalIgnoreCase))
        {
            logger.Info("result=OwnershipMismatch status=403");
            return Results.Forbid();
        }

        GetAccountsByEmailResult result =
            await handler.Handle(new GetAccountsByEmailQuery(email));
        IResult response = result switch
        {
            GetAccountsByEmailResult.Found found => Results.Ok(found.Accounts.Select(AccountResponse.From)),
            GetAccountsByEmailResult.NotFound => Results.NotFound(),
            GetAccountsByEmailResult.InvalidEmail => Results.BadRequest(),
            GetAccountsByEmailResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }

    private static async Task<IResult> GetAccountsByUserId(
        ClaimsPrincipal user,
        GetAccountsByUserIdHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        int userId = user.GetUserId();
        logger.Info($"Get accounts request received for userId={userId}");

        GetAccountsByUserIdResult result =
            await handler.Handle(new GetAccountsByUserIdQuery(userId));
        IResult response = result switch
        {
            GetAccountsByUserIdResult.Found found => Results.Ok(found.Accounts.Select(AccountResponse.From)),
            GetAccountsByUserIdResult.InvalidUserId => Results.BadRequest(),
            GetAccountsByUserIdResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }

    private static RouteGroupBuilder MapAccountPostEndpoints(
        this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateAccount);
        group.MapPost("/topup", TopUpAccount);
        // The provider has no JWT: this route is authenticated by the callback signature instead.
        group.MapPost("/topup/callback", TopUpAccountCallback).AllowAnonymous();

        return group;
    }

    private static readonly JsonSerializerOptions CallbackJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static async Task<IResult> TopUpAccountCallback(
        HttpRequest httpRequest,
        ICallbackSignatureVerifier verifier,
        TopupAccountCallbackHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);

        // Verify against the exact bytes received; re-serialised JSON could differ from what was signed.
        using MemoryStream buffer = new MemoryStream();
        await httpRequest.Body.CopyToAsync(buffer);
        byte[] body = buffer.ToArray();

        CallbackSignatureResult signature = verifier.Verify(
            httpRequest.Headers[HmacCallbackSignatureVerifier.TimestampHeader].FirstOrDefault(),
            httpRequest.Headers[HmacCallbackSignatureVerifier.SignatureHeader].FirstOrDefault(),
            body);
        if (signature != CallbackSignatureResult.Valid)
        {
            logger.Warn($"result=BadSignature({signature}) status=401");
            return Results.Unauthorized();
        }

        TopUpAccountCallbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TopUpAccountCallbackRequest>(body, CallbackJson);
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null)
        {
            logger.Info("result=InvalidBody status=400");
            return Results.BadRequest();
        }

        logger.Info($"Top up callback received for reference={request.Reference}, status={request.Status}");

        if (!int.TryParse(request.Reference, NumberStyles.None, CultureInfo.InvariantCulture, out int referenceValue)
            || TransactionId.TryCreate(referenceValue) is not TransactionId transactionId)
        {
            logger.Info("result=InvalidReference status=400");
            return Results.BadRequest();
        }

        ProviderCallbackStatus? status = request.Status switch
        {
            "Succeeded" => ProviderCallbackStatus.Succeeded,
            "Failed" => ProviderCallbackStatus.Failed,
            _ => null,
        };
        if (status is not ProviderCallbackStatus parsedStatus)
        {
            logger.Info("result=InvalidStatus status=400");
            return Results.BadRequest();
        }

        TopupAccountCallbackResult result = await handler.Handle(new TopupAccountCallbackCommand(transactionId, parsedStatus));

        IResult response = result switch
        {
            TopupAccountCallbackResult.Credited => Results.Ok(),
            TopupAccountCallbackResult.MarkedFailed => Results.Ok(),
            // Duplicates and late callbacks are handled silently: 200 so the provider stops retrying.
            TopupAccountCallbackResult.AlreadyProcessed => Results.Ok(),
            TopupAccountCallbackResult.LateSuccessOnExpired => Results.Ok(),
            TopupAccountCallbackResult.NotFound => Results.NotFound(),
            _ => Results.StatusCode(500),
        };

        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }

    private static async Task<IResult> TopUpAccount(
        TopUpAccountRequest request,
        ClaimsPrincipal user,
        TopupAccountHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        logger.Info($"Top up account request received for accountId={request.AccountId}, callerUserId={user.GetUserId()}");
        
        if (UserId.TryCreate(user.GetUserId()) is not UserId parsedUserId)
        {
            logger.Info("result=NoUserClaim status=401");
            return Results.Unauthorized();
        }
        
        if (AccountId.TryCreate(request.AccountId) is not AccountId parsedAccountId)
        {
            logger.Info("result=InvalidAccountId status=400");
            return Results.BadRequest();
        }
        
        if (Amount.TryCreate(request.Amount) is not Amount parsedAmount)
        {
            logger.Info("result=InvalidAmount status=400");
            return Results.BadRequest();
        }
        
        if (Currency.TryCreate(request.Currency) is not Currency parsedCurrency)
        {
            logger.Info("result=InvalidCurrency status=400");
            return Results.BadRequest();
        }
        
        UserId userId = parsedUserId;
        AccountId accountId = parsedAccountId;
        Amount amount = parsedAmount;
        Currency currency = parsedCurrency;

        TopupAccountResult result = await handler.Handle(new TopupAccountCommand(userId, accountId, amount, currency));

        IResult response = result switch
        {
            TopupAccountResult.Pending => Results.StatusCode(200),
            TopupAccountResult.InvalidCurrency => Results.BadRequest(), // is this the correct code?
            TopupAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }

    private static async Task<IResult> CreateAccount(
        CreateAccountRequest request,
        ClaimsPrincipal user,
        CreateAccountHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        int callerUserId = user.GetUserId();
        logger.Info($"Create account request received, callerUserId={callerUserId}");

        CreateAccountResult result =
            await handler.Handle(new CreateAccountCommand(callerUserId, request.Currency));
        IResult response = result switch
        {
            CreateAccountResult.Created created => Results.Ok(AccountResponse.From(created.Account)),
            CreateAccountResult.InvalidUserId => Results.BadRequest(),
            CreateAccountResult.InvalidCurrency => Results.BadRequest(),
            CreateAccountResult.AlreadyExists => Results.Conflict(),
            CreateAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
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
        DeleteAccountHandler handler,
        ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(LogCategory);
        logger.Info($"Delete account request received for accountId={id}, callerUserId={user.GetUserId()}");
        DeleteAccountResult result =
            await handler.Handle(new DeleteAccountCommand(id, user.GetUserId()));
        IResult response = result switch
        {
            DeleteAccountResult.Deleted => Results.Ok(),
            DeleteAccountResult.NotFound => Results.NotFound(),
            DeleteAccountResult.Forbidden => Results.Forbid(),
            DeleteAccountResult.HasBalance => Results.Conflict(),
            DeleteAccountResult.Failed => Results.StatusCode(500),
            _ => Results.StatusCode(500)
        };
        logger.Info($"result={result.GetType().Name} status={ResultStatus.Of(response)}");
        return response;
    }
}
