using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stallions.Server.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Used by `dotnet ef` tooling when a live connection string is not available.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.;Database=StallionsNominations;Trusted_Connection=True;")
            .Options;

        return new AppDbContext(options);
    }
}
