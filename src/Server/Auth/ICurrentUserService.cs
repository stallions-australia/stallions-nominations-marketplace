namespace Stallions.Server.Auth;

public interface ICurrentUserService
{
    string? ObjectId { get; }       // B2C object ID (was EntraObjectId)
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    // Roles removed — roles come from the database, not the JWT
}
