using System.Security.Claims;

namespace Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
}
