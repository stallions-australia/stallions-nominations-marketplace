using System.Security.Claims;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace Stallions.Client.Auth;

/// <summary>
/// Maps the 'roles' array from the Entra ID token into ClaimTypes.Role so that
/// AuthorizeView Roles="Staff" / Roles="StudFarmAdmin" works correctly in Blazor WASM.
///
/// Without this, MSAL delivers roles in the JWT but the Blazor auth state builder
/// has no knowledge of the array — it needs an explicit typed account + factory pair.
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
        var user = await base.CreateUserAsync(account, options);

        if (user.Identity?.IsAuthenticated == true)
        {
            var identity = (ClaimsIdentity)user.Identity;

            foreach (var role in account.Roles)
            {
                // Avoid duplicating claims that the base factory may have already added
                if (!identity.HasClaim(ClaimTypes.Role, role))
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        return user;
    }
}
