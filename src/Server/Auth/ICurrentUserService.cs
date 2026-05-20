namespace Stallions.Server.Auth;

public interface ICurrentUserService
{
    string? EntraObjectId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    string? EntraRole { get; }
    bool IsAuthenticated { get; }
}
