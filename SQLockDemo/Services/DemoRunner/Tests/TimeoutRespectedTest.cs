using Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SQLock;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace SQLockDemo.Services.DemoRunner.Tests;

public class TimeoutRespectedTest(
    ISqlDistributedLockFactory lockFactory,
    VehicleManagementDbContext dbContext,
    ILoggerFactory loggerFactory)
    : SqlockTestBase(lockFactory, dbContext, loggerFactory)
{
    public override string Name => "Timeout-Respected";
    public override string Description => "TryAcquireAsync returns false (or AcquireAsync throws) after the requested period. Stopwatch elapsed ≈ requested timeout; DMV confirms lock was never granted.";

    public override async Task RunAsync()
    {
        string testLockName = $"timeout_test_lock_{Guid.NewGuid():N}".Substring(0, 32);
        int holdTimeMs = 4000; // The first lock will be held for 4 seconds
        int timeoutMs = 1500;  // The second lock will timeout after 1.5 seconds

        Logger.LogInformation("\n=== Test: Timeout Respected ===");
        Logger.LogInformation("Description: TryAcquireAsync returns false (or AcquireAsync throws) after the requested period. Stopwatch elapsed ≈ requested timeout; DMV confirms lock was never granted");

        // Start a task that acquires the lock and holds it
        var acquiredSignal = new TaskCompletionSource<bool>();
        var holdTask = Task.Run(async () =>
        {
            await using var lck = LockFactory.CreateLock(testLockName);
            await lck.TakeAsync();
            Logger.LogInformation("[Holder] Lock acquired, holding for {HoldTimeMs}ms...", holdTimeMs);
            acquiredSignal.SetResult(true);
            await Task.Delay(holdTimeMs);
            Logger.LogInformation("[Holder] Releasing lock...");
        });

        // Wait for the first task to acquire the lock
        await acquiredSignal.Task;
        await Task.Delay(100); // Ensure the lock is fully established in DMV

        // Try to acquire the lock with a timeout
        var stopwatch = Stopwatch.StartNew();
        bool acquired = false;
        Exception? acquireException = null;
        try
        {
            await using var lck2 = LockFactory.CreateLock(testLockName);
            acquired = await lck2.TryTakeAsync(timeoutMs);
        }
        catch (Exception ex)
        {
            acquireException = ex;
        }
        stopwatch.Stop();

        var elapsed = stopwatch.ElapsedMilliseconds;
        Logger.LogInformation("[Contender] TryAcquireAsync returned: {Acquired}, elapsed: {Elapsed}ms (timeout: {TimeoutMs}ms)", acquired, elapsed, timeoutMs);

        // DMV: confirm lock was never granted
        bool lockExists = await CheckLockExistsAsync(testLockName);
        if (!acquired && !lockExists && Math.Abs(elapsed - timeoutMs) < 500)
        {
            Logger.LogInformation("✅ Test PASSED: Timeout respected, lock was not granted, elapsed ≈ timeout");
        }
        else
        {
            if (acquired)
                Logger.LogError("❌ Test FAILED: Lock was unexpectedly acquired!");
            if (lockExists)
                Logger.LogError("❌ Test FAILED: DMV shows lock was granted!");
            if (Math.Abs(elapsed - timeoutMs) >= 500)
                Logger.LogError("❌ Test FAILED: Elapsed ({Elapsed}ms) not close to timeout ({TimeoutMs}ms)", elapsed, timeoutMs);
            if (acquireException != null)
                Logger.LogError(acquireException, "❌ Test FAILED: Exception thrown during TryAcquireAsync!");
        }

        await holdTask;
    }

    private async Task<bool> CheckLockExistsAsync(string lockName)
    {
        var sql = @"
            SELECT COUNT(*) 
            FROM sys.dm_tran_locks 
            WHERE resource_description LIKE @LockName + '%' 
              AND request_status = 'GRANT'
              AND request_owner_type = 'SESSION'";

        await using var connection = new SqlConnection(DbContext.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@LockName", lockName);
        var lockCount = (int)(await command.ExecuteScalarAsync() ?? throw new Exception("Failed to retrieve lock count"));
        return lockCount > 0;
    }
}
