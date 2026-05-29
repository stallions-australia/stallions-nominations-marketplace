using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Stallions.Server.Auth;
using Stallions.Server.Data;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Server.Services;
using Stallions.Shared.Enums;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Auth — Azure AD B2C JWT validation via Microsoft.Identity.Web
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
builder.Services.AddAuthorization(options =>
{
    // DB-backed role policies — role and Active status checked against the Users table.
    // JWT only proves identity; the database is the source of truth for roles.
    options.AddPolicy("StaffOnly",
        p => p.AddRequirements(new DbRoleRequirement(UserRole.Staff)));
    options.AddPolicy("StudFarmAdminOnly",
        p => p.AddRequirements(new DbRoleRequirement(UserRole.StudFarmAdmin)));
    options.AddPolicy("StudFarmOrStaff",
        p => p.AddRequirements(new DbRoleRequirement(UserRole.StudFarmAdmin, UserRole.Staff)));
    options.AddPolicy("BuyerOnly",
        p => p.AddRequirements(new DbRoleRequirement(UserRole.Buyer)));
});

// Register the handler that evaluates DbRoleRequirement
builder.Services.AddScoped<IAuthorizationHandler, DbRoleHandler>();

// Config options
builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection("Checkout"));

// Infrastructure
builder.Services.AddHttpContextAccessor();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

// Auth helpers
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IStudFarmRepository, StudFarmRepository>();
builder.Services.AddScoped<IStallionRepository, StallionRepository>();
builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
builder.Services.AddScoped<IListingRepository, ListingRepository>();
builder.Services.AddScoped<IBidRepository, BidRepository>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<INominationBindingRepository, NominationBindingRepository>();
builder.Services.AddScoped<IEnquiryRepository, EnquiryRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Blob Storage (uses DefaultAzureCredential; requires AZURE_STORAGE_ACCOUNT_NAME in appsettings.Development.json)
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

// Services (will be uncommented as Tasks 3–11 are completed)
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISeasonService, SeasonService>();
builder.Services.AddScoped<IStallionService, StallionService>();
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<IBidService, BidService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<INominationBindingService, NominationBindingService>();
builder.Services.AddScoped<IEnquiryService, EnquiryService>();
builder.Services.AddScoped<IAdminService, AdminService>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Use the client IP or authenticated user identifier as the partition key
        var key = context.User?.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);
    };
});

// CORS — allow Blazor WASM client origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Apply pending EF Core migrations on startup — safe to run on every deploy
// (EF is idempotent; already-applied migrations are skipped)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Serve Blazor WASM client static files (wwwroot of Client project)
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

// All non-API routes fall back to the Blazor app's index.html
app.MapFallbackToFile("index.html");

app.Run();
