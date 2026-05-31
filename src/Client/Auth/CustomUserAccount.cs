using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Stallions.Client.Auth;

/// <summary>
/// Extends RemoteUserAccount for B2C. No role claims are present in B2C tokens —
/// roles are determined from the database and accessed via UserStateService.
/// This class exists to satisfy the MSAL generic type constraint.
/// </summary>
public class CustomUserAccount : RemoteUserAccount
{
}
