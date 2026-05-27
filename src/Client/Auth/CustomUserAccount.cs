using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Stallions.Client.Auth;

/// <summary>
/// Extends RemoteUserAccount to capture the 'roles' claim array from the
/// Entra ID token. Without this, the default deserialiser ignores the JSON
/// array and AuthorizeView Roles="..." never sees the values.
/// </summary>
public class CustomUserAccount : RemoteUserAccount
{
    [JsonPropertyName("roles")]
    public string[] Roles { get; set; } = [];
}
