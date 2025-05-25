using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace SQLock;

/// <summary>
/// Implements a distributed lock using SQL Server's sp_getapplock (Session scope).
/// </summary>
public class SqlDistributedLock : IAsyncDisposable, IDisposable
{
    private readonly DbConnection _connection;
    private readonly string _lockName;
    private bool _lockAcquired;

    public SqlDistributedLock(string connectionString, string lockName)
        : this(new SqlConnection(connectionString), lockName)
    {
    }

    private SqlDistributedLock(DbConnection connection, string lockName)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        if (string.IsNullOrWhiteSpace(lockName))
            throw new ArgumentException("lockName cannot be null or whitespace.", nameof(lockName));
        _lockName = lockName;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lockAcquired)
        {
            await using DbCommand cmd = _connection.CreateCommand();
            cmd.CommandText = "sp_releaseapplock";
            cmd.CommandType = CommandType.StoredProcedure;

            DbParameter resourceParam = cmd.CreateParameter();
            resourceParam.ParameterName = "@Resource";
            resourceParam.Value = _lockName;
            cmd.Parameters.Add(resourceParam);

            DbParameter lockOwnerParam = cmd.CreateParameter();
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

    /// <summary>
    ///     Attempts to take the distributed lock and throws if not acquired.
    /// </summary>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    public async Task TakeAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
    {
        if (_lockAcquired)
            throw new InvalidOperationException($"Lock '{_lockName}' is already acquired.");
        
        bool acquired = await TakeInternal(timeoutMs, cancellationToken);
        if (!acquired)
            throw new InvalidOperationException($"Failed to acquire lock '{_lockName}' within {timeoutMs}ms.");
    }

    /// <summary>
    ///     Attempts to acquire the distributed lock.
    /// </summary>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>true if the lock was acquired, false if the lock could not be acquired.</returns>
    public Task<bool> TryTakeAsync(int timeoutMs = 30000, CancellationToken cancellationToken = default)
    {
        if (_lockAcquired)
            throw new InvalidOperationException($"Lock '{_lockName}' is already acquired.");
        
        return TakeInternal(timeoutMs, cancellationToken);
    }

    /// <summary>
    ///     Shared internal logic for lock acquisition.
    /// </summary>
    private async Task<bool> TakeInternal(int timeoutMs, CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken);

        await using DbCommand cmd = _connection.CreateCommand();
        cmd.CommandTimeout = (int)Math.Ceiling(timeoutMs / 1000.0); // seconds
        cmd.CommandText = "sp_getapplock";
        cmd.CommandType = CommandType.StoredProcedure;

        DbParameter resourceParam = cmd.CreateParameter();
        resourceParam.ParameterName = "@Resource";
        resourceParam.Value = _lockName;
        cmd.Parameters.Add(resourceParam);

        DbParameter lockModeParam = cmd.CreateParameter();
        lockModeParam.ParameterName = "@LockMode";
        lockModeParam.Value = "Exclusive";
        cmd.Parameters.Add(lockModeParam);

        DbParameter lockOwnerParam = cmd.CreateParameter();
        lockOwnerParam.ParameterName = "@LockOwner";
        lockOwnerParam.Value = "Session";
        cmd.Parameters.Add(lockOwnerParam);

        DbParameter timeoutParam = cmd.CreateParameter();
        timeoutParam.ParameterName = "@LockTimeout";
        timeoutParam.Value = timeoutMs;
        cmd.Parameters.Add(timeoutParam);

        DbParameter resultParam = cmd.CreateParameter();
        resultParam.ParameterName = "@Result";
        resultParam.Direction = ParameterDirection.ReturnValue;
        cmd.Parameters.Add(resultParam);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
        {
            // If cancellation was requested and SqlException occurred,
            // it's highly probable the SqlException is due to the cancellation.
            // Throw OperationCanceledException to conform to standard cancellation pattern.
            // Check for specific error numbers if more precise handling is needed, e.g., error 0 for timeout/cancel.
            if (ex.Number == 0 || ex.Message.Contains("Operation cancelled by user", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("cancelled by user", StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException("The database operation was canceled.", ex, cancellationToken);
            }
            throw; // Re-throw if not the specific cancellation-related SqlException
        }
        
        var result = (int)resultParam.Value!;
        _lockAcquired = result >= 0;

        if (!_lockAcquired)
            await _connection.CloseAsync();

        return _lockAcquired;
    }
}