using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api.Auth;

// Validates "Authorization: Bearer <token>" via ITokenService. Replacing this scheme with
// AddJwtBearer(...) later leaves endpoints (.RequireAuthorization()) and ClaimsPrincipal usage unchanged.
public sealed class TokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";

    private readonly ITokenService _tokenService;

    public TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Application.Auth.TokenClaims? claims = _tokenService.Validate(header["Bearer ".Length..].Trim());
        if (claims is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Email, claims.Email)
            ],
            SchemeName);

        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
