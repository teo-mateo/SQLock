using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SQLock
{
    /// <summary>
    /// Implements a distributed lock using SQL Server's sp_getapplock (Session scope only).
    /// </summary>
    public class SqlDistributedLock : IAsyncDisposable
    {
        private readonly DbConnection _connection;
        private readonly string _lockName;
        private bool _lockAcquired;

        public SqlDistributedLock(DbConnection connection, string entityName, long id)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Lock base name cannot be null or whitespace.", nameof(entityName));
            _lockName = $"{entityName}:{id}";
        }

        public async Task<bool> AcquireAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "sp_getapplock";
            cmd.CommandType = CommandType.StoredProcedure;

            var resourceParam = cmd.CreateParameter();
            resourceParam.ParameterName = "@Resource";
            resourceParam.Value = _lockName;
            cmd.Parameters.Add(resourceParam);

            var lockModeParam = cmd.CreateParameter();
            lockModeParam.ParameterName = "@LockMode";
            lockModeParam.Value = "Exclusive";
            cmd.Parameters.Add(lockModeParam);

            var lockOwnerParam = cmd.CreateParameter();
            lockOwnerParam.ParameterName = "@LockOwner";
            lockOwnerParam.Value = "Session";
            cmd.Parameters.Add(lockOwnerParam);

            var timeoutParam = cmd.CreateParameter();
            timeoutParam.ParameterName = "@LockTimeout";
            timeoutParam.Value = timeoutMs;
            cmd.Parameters.Add(timeoutParam);

            var resultParam = cmd.CreateParameter();
            resultParam.ParameterName = "@Result";
            resultParam.Direction = ParameterDirection.ReturnValue;
            cmd.Parameters.Add(resultParam);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            var result = (int)resultParam.Value!;
            _lockAcquired = result >= 0;
            return _lockAcquired;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_lockAcquired)
                return;
            
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "sp_releaseapplock";
            cmd.CommandType = CommandType.StoredProcedure;

            var resourceParam = cmd.CreateParameter();
            resourceParam.ParameterName = "@Resource";
            resourceParam.Value = _lockName;
            cmd.Parameters.Add(resourceParam);

            var lockOwnerParam = cmd.CreateParameter();
            lockOwnerParam.ParameterName = "@LockOwner";
            lockOwnerParam.Value = "Session";
            cmd.Parameters.Add(lockOwnerParam);

            await cmd.ExecuteNonQueryAsync();
            _lockAcquired = false;
        }
    }
}
