using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Services;

/// <summary>
/// Fetches the current user's profile (including DB role) from the API once after
/// authentication and caches it for the session. Components use this instead of
/// JWT role claims, since B2C tokens carry no role information.
///
/// Implements the Blazor state container pattern: raises OnChange whenever CurrentUser
/// is updated so that subscribed components (e.g. NavBar) can call StateHasChanged.
/// </summary>
public class UserStateService
{
    private readonly UserApiService _api;

    public UserStateService(UserApiService api) => _api = api;

    public UserDto? CurrentUser { get; private set; }

    public string? Role   => CurrentUser?.Role;
    public string? Status => CurrentUser?.Status;
    public bool IsStaff        => Role == "Staff";
    public bool IsStudFarmAdmin => Role == "StudFarmAdmin";
    public bool IsBuyer        => Role == "Buyer";
    public bool IsVerified     => Status == "Active";
    public bool IsPendingVerification => Status == "PendingVerification";
    public bool IsLoaded       => CurrentUser is not null;

    public int? AcceptedTermsVersion => CurrentUser?.AcceptedTermsVersion;
    public bool SuppressBidConfirmation => CurrentUser?.SuppressBidConfirmation ?? false;

    /// <summary>True when this verified buyer must accept a newer T&C version.</summary>
    public bool NeedsTermsAcceptance(int currentVersion) =>
        IsBuyer && IsVerified && (AcceptedTermsVersion is null || AcceptedTermsVersion < currentVersion);

    /// <summary>Update cached state after a successful accept-terms call (avoids a full reload).</summary>
    public void MarkTermsAccepted(int version)
    {
        if (CurrentUser is not null)
        {
            CurrentUser.AcceptedTermsVersion = version;
            OnChange?.Invoke();
        }
    }

    /// <summary>Update cached state after a successful suppress-bid-confirmation call.</summary>
    public void MarkBidConfirmationSuppressed()
    {
        if (CurrentUser is not null)
        {
            CurrentUser.SuppressBidConfirmation = true;
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Raised whenever CurrentUser changes (loaded or cleared).
    /// Components subscribe and call StateHasChanged in response.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Fetches the user profile from the API if not already loaded.
    /// Safe to call multiple times — only hits the network once per session.
    /// </summary>
    public async Task LoadAsync()
    {
        if (CurrentUser is null)
        {
            CurrentUser = await _api.GetMeAsync();
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Clears the cached user — call on sign-out.
    /// </summary>
    public void Clear()
    {
        CurrentUser = null;
        OnChange?.Invoke();
    }
}
