using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace Stallions.Server.Tests.Helpers;

/// <summary>SqlException has no public constructor; this builds one carrying a given error number (e.g. 2601/2627 for a unique-index violation).</summary>
public static class SqlExceptionFactory
{
    public static SqlException WithNumber(int number)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        typeof(SqlError).GetField("_number", all)!.SetValue(error, number);

        var errors = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        var list = new List<object> { error };
        typeof(SqlErrorCollection).GetField("_errors", all)!.SetValue(errors, list);

        var ex = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        typeof(SqlException).GetField("_errors", all)!.SetValue(ex, errors);
        return ex;
    }
}
