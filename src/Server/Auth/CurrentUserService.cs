using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Stallions.Server.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? ObjectId =>
        User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? User?.FindFirst("oid")?.Value;

    public string? Email =>
        User?.FindFirst("emails")?.Value              // B2C multi-value array claim
        ?? User?.FindFirst("email")?.Value            // standard OIDC claim
        ?? User?.FindFirst("preferred_username")?.Value; // Entra External ID access tokens

    public string? DisplayName =>
        User?.FindFirst("name")?.Value
        ?? User?.FindFirst(ClaimTypes.Name)?.Value;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
