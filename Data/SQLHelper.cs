using System.Data;
using Microsoft.Data.SqlClient;

namespace Data;

public static class SQLHelper
{
    public static bool TestConnection(string connectionString)
    {
        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            return conn.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }
}