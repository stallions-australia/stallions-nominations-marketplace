using System.Security.Claims;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace Stallions.Client.Auth;

/// <summary>
/// Pass-through factory required by the MSAL pipeline. No role mapping is performed —
/// roles are DB-backed and accessed via UserStateService after login.
/// </summary>
public class CustomAccountClaimsPrincipalFactory
    : AccountClaimsPrincipalFactory<CustomUserAccount>
{
    public CustomAccountClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
        : base(accessor) { }

    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        CustomUserAccount account,
        RemoteAuthenticationUserOptions options)
    {
        return await base.CreateUserAsync(account, options);
    }
}
