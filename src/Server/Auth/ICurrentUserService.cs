namespace Stallions.Server.Auth;

public interface ICurrentUserService
{
    string? EntraObjectId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
}
