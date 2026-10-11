// tests/Server.Tests/Helpers/TaxTestData.cs
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;

namespace Stallions.Server.Tests.Helpers;

/// <summary>Business details on the seeded settings row (call Database.EnsureCreated() first).</summary>
public static class TaxTestData
{
    public static PlatformSettings CompleteBusinessDetails(AppDbContext db, bool withBank = true)
    {
        var s = db.PlatformSettings.Single();
        s.BusinessLegalName = "Stallions Australia Pty Ltd";
        s.BusinessAbn = "12345678901";
        s.BusinessAddress = "1 Racecourse Road, Scone NSW 2337";
        s.BusinessEmail = "accounts@stallions.example";
        s.BankAccountName = withBank ? "Stallions Australia Pty Ltd" : null;
        s.BankBsb = withBank ? "062000" : null;
        s.BankAccountNumber = withBank ? "12345678" : null;
        db.SaveChanges();
        return s;
    }

    public static void ClearBusinessDetails(AppDbContext db)
    {
        var s = db.PlatformSettings.Single();
        s.BusinessLegalName = null;
        s.BusinessAbn = null;
        s.BusinessAddress = null;
        s.BusinessEmail = null;
        s.BankAccountName = null;
        s.BankBsb = null;
        s.BankAccountNumber = null;
        db.SaveChanges();
    }
}
