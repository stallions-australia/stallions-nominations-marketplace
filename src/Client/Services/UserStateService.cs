using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

/// <summary>
/// Fetches the current user's profile (including DB role) from the API once after
/// authentication and caches it for the session. Components use this instead of
/// JWT role claims, since B2C tokens carry no role information.
/// </summary>
public class UserStateService
{
    private readonly UserApiService _api;

    public UserStateService(UserApiService api) => _api = api;

    public UserDto? CurrentUser { get; private set; }

    public string? Role => CurrentUser?.Role;
    public bool IsStaff        => Role == "Staff";
    public bool IsStudFarmAdmin => Role == "StudFarmAdmin";
    public bool IsBuyer        => Role == "Buyer";
    public bool IsLoaded       => CurrentUser is not null;

    /// <summary>
    /// Fetches the user profile from the API if not already loaded.
    /// Safe to call multiple times — only hits the network once per session.
    /// </summary>
    public async Task LoadAsync()
    {
        if (CurrentUser is null)
            CurrentUser = await _api.GetMeAsync();
    }

    /// <summary>
    /// Clears the cached user — call on sign-out.
    /// </summary>
    public void Clear() => CurrentUser = null;
}
