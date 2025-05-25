using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLock
{
    /// <summary>
    /// Implements a distributed lock using SQL Server's sp_getapplock (Session scope only).
    /// </summary>
    public class SqlDistributedLock : IAsyncDisposable, IDisposable
    {
        private readonly DbConnection _connection;
        private readonly string _lockName;
        private bool _lockAcquired;
        
        public SqlDistributedLock(string connectionString, string entityName, long id)
            : this(new SqlConnection(connectionString), entityName, id)
        {
        }

        private SqlDistributedLock(DbConnection connection, string entityName, long id)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name cannot be null or whitespace.", nameof(entityName));
            _lockName = $"{entityName}:{id}";
        }
        
        /// <summary>
        /// Attempts to acquire the distributed lock and throws if not acquired.
        /// </summary>
        /// <param name="timeoutMs">The timeout in milliseconds.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        public async Task AcquireAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
        {
            bool acquired = await AcquireLockInternal(timeoutMs, cancellationToken);
            if (!acquired)
                throw new InvalidOperationException($"Failed to acquire lock '{_lockName}' within {timeoutMs}ms.");
        }

        /// <summary>
        /// Attempts to acquire the distributed lock.
        /// </summary>
        /// <param name="timeoutMs">The timeout in milliseconds.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>true if the lock was acquired, false if the lock could not be acquired.</returns>
        public Task<bool> TryAcquireAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
            => AcquireLockInternal(timeoutMs, cancellationToken);

        /// <summary>
        /// Shared internal logic for lock acquisition.
        /// </summary>
        private async Task<bool> AcquireLockInternal(int timeoutMs, CancellationToken cancellationToken)
        {
            await _connection.OpenAsync(cancellationToken);

            await using var cmd = _connection.CreateCommand();
            cmd.CommandTimeout = (int)Math.Ceiling(timeoutMs / 1000.0);   // seconds
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

            if (!_lockAcquired)
                await _connection.CloseAsync();

            return _lockAcquired;
        }

        public async ValueTask DisposeAsync()
        {
            if (_lockAcquired)
            {
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

            await _connection.DisposeAsync();
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
