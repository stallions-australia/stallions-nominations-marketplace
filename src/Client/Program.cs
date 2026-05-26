using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stallions.Client;
using Stallions.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Entra ID (MSAL) auth
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add(builder.Configuration["ApiScope"]!);
});

// Typed API services — BaseAddressAuthorizationMessageHandler attaches the Entra ID access
// token when the user is signed in; unauthenticated requests to public endpoints pass through.
// When hosted on the same App Service, ApiBaseUrl is intentionally blank in
// appsettings.Production.json so we fall back to the host's base address.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
var apiBase = string.IsNullOrWhiteSpace(apiBaseUrl)
    ? new Uri(builder.HostEnvironment.BaseAddress)
    : new Uri(apiBaseUrl);

// Public browse services — no auth token. These call [AllowAnonymous] endpoints and
// must NOT use BaseAddressAuthorizationMessageHandler, which throws when there is no
// token (i.e. the user hasn't signed in yet).
builder.Services.AddHttpClient<ListingApiService>(c => c.BaseAddress = apiBase);
builder.Services.AddHttpClient<StallionApiService>(c => c.BaseAddress = apiBase);
builder.Services.AddHttpClient<StudFarmApiService>(c => c.BaseAddress = apiBase);

// Authenticated services — always require a Bearer token.
builder.Services.AddHttpClient<BidApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<CheckoutApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<EnquiryApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<UserApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<AdminApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
builder.Services.AddHttpClient<StaffApiService>(c => c.BaseAddress = apiBase)
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();

await builder.Build().RunAsync();
