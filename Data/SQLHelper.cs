using System.Data;
using Microsoft.Data.SqlClient;

namespace Data
{
    public static class SQLHelper
    {
        public static SqlConnection GetOpenConnection(string connectionString)
        {
            var conn = new SqlConnection(connectionString);
            conn.Open();
            return conn;
        }

        public static int ExecuteNonQuery(string connectionString, string commandText, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection(connectionString);
            using var cmd = new SqlCommand(commandText, conn);
            if (parameters != null && parameters.Length > 0)
                cmd.Parameters.AddRange(parameters);
            return cmd.ExecuteNonQuery();
        }

        public static object? ExecuteScalar(string connectionString, string commandText, params SqlParameter[] parameters)
        {
            using var conn = GetOpenConnection(connectionString);
            using var cmd = new SqlCommand(commandText, conn);
            if (parameters != null && parameters.Length > 0)
                cmd.Parameters.AddRange(parameters);
            return cmd.ExecuteScalar();
        }

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
}
