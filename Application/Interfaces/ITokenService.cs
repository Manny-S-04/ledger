using Application.Auth;
using Domain.Users;

namespace Application.Interfaces;

// Seam for token issuing/validation: swap the implementation in DI (e.g. for JwtBearer / System.IdentityModel.Tokens.Jwt).
public interface ITokenService
{
    public AuthToken Create(UserId userId, Email email);
    public TokenClaims? Validate(string token);
}
