using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Stallions.Server.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? EntraObjectId =>
        User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? User?.FindFirst("oid")?.Value;

    public string? Email =>
        User?.FindFirst(ClaimTypes.Email)?.Value
        ?? User?.FindFirst("preferred_username")?.Value;

    public string? DisplayName =>
        User?.FindFirst("name")?.Value
        ?? User?.FindFirst(ClaimTypes.Name)?.Value;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList().AsReadOnly()
        ?? (IReadOnlyList<string>)[];

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
