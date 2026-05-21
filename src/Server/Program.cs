using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Stallions.Server.Auth;
using Stallions.Server.Data;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Options;
using Stallions.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Auth — Entra ID JWT validation via Microsoft.Identity.Web
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthorization();

// Config options
builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection("Checkout"));

// Infrastructure
builder.Services.AddHttpContextAccessor();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

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

// Services (will be uncommented as Tasks 3–11 are completed)
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISeasonService, SeasonService>();
builder.Services.AddScoped<IStallionService, StallionService>();
builder.Services.AddScoped<IListingService, ListingService>();
// builder.Services.AddScoped<IBidService, BidService>();
// builder.Services.AddScoped<ICheckoutService, CheckoutService>();
// builder.Services.AddScoped<INominationBindingService, NominationBindingService>();
// builder.Services.AddScoped<IEnquiryService, EnquiryService>();
// builder.Services.AddScoped<IAdminService, AdminService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
