using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;

namespace Stallions.Server.Tests.Helpers;

public static class DbContextFactory
{
    public static AppDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AppDbContext(options);
    }
}
