// src/Server/Data/DbErrors.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Stallions.Server.Data;

public static class DbErrors
{
    /// <summary>SQL Server: 2601 = duplicate key in a unique index, 2627 = unique constraint violation.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
