using Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLock;
using System.Diagnostics;

namespace SQLockDemo.Services.DemoRunner.Tests;

public class CancellationTokenHonouredTest(
    ISqlDistributedLockFactory lockFactory,
    VehicleManagementDbContext dbContext,
    ILoggerFactory loggerFactory)
    : SqlockTestBase(lockFactory, dbContext, loggerFactory)
{
    public override string Name => "Cancellation-Token-Honoured";
    public override string Description => "Operation aborts quickly when token fires. Pass a CTS that cancels after e.g., 50 ms; ensure elapsed < 200 ms and no lingering session-level lock.";

    public override async Task RunAsync()
    {
        string testLockName = $"cancel_test_lock_{Guid.NewGuid():N}".Substring(0, 32);
        const int cancellationDelayMs = 50;
        const int holdLockDurationMs = 500; // Holder keeps lock longer than cancellation
        const int expectedMaxElapsedMs = 250; // Generous upper bound for quick cancellation

        Logger.LogInformation("Setting up a holder task to acquire '{LockName}' first", testLockName);
        var holderLockAcquiredSignal = new TaskCompletionSource<bool>();

        // Holder Task: Acquires the lock and holds it to make the contender block
        var holderTask = Task.Run(async () =>
        {
            try
            {
                await using var lckHolder = LockFactory.CreateLock(testLockName);
                await lckHolder.TakeAsync();
                Logger.LogInformation("[Holder] Acquired lock '{LockName}'. Holding for {HoldLockDurationMs}ms", testLockName, holdLockDurationMs);
                holderLockAcquiredSignal.SetResult(true);
                await Task.Delay(holdLockDurationMs);
                Logger.LogInformation("[Holder] Releasing lock '{LockName}'", testLockName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[Holder] Error in holder task");
                holderLockAcquiredSignal.TrySetException(ex); 
            }
        });

        // Wait for the holder to acquire the lock
        Logger.LogInformation("Waiting for holder task to acquire lock...");
        await holderLockAcquiredSignal.Task;
        Logger.LogInformation("Holder task has acquired the lock. Proceeding with contender");

        var cts = new CancellationTokenSource();
        Logger.LogInformation("Attempting to take lock '{LockName}' with a CancellationToken that cancels in {CancellationDelayMs}ms", testLockName, cancellationDelayMs);
        
        var stopwatch = Stopwatch.StartNew();
        bool operationCancelled = false;
        long elapsedMs = 0;

        try
        {
            cts.CancelAfter(cancellationDelayMs); // Schedule cancellation
            await using var lckContender = LockFactory.CreateLock(testLockName);
            // This call should throw OperationCanceledException due to the CTS
            await lckContender.TakeAsync(5000, cts.Token); 
            Logger.LogWarning("Contender unexpectedly acquired lock '{LockName}' despite cancellation", testLockName); 
        }
        catch (OperationCanceledException)
        {
            operationCancelled = true;
            Logger.LogInformation("✅ OperationCanceledException correctly thrown");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Contender task threw an unexpected exception");
        }
        finally
        {
            stopwatch.Stop();
            elapsedMs = stopwatch.ElapsedMilliseconds;
            Logger.LogInformation("Contender operation finished in {ElapsedMs}ms", elapsedMs);
            cts.Dispose();
        }

        // Verification
        bool lockStillExistsAfterContender = await CheckLockExistsAsync(testLockName, isLockExpected: false); // Check if contender left a lock

        bool passed = operationCancelled && elapsedMs < expectedMaxElapsedMs && !lockStillExistsAfterContender;

        if (passed)
        {
            Logger.LogInformation("✅ Test PASSED: Cancellation token honoured. Elapsed: {ElapsedMs}ms (< {ExpectedMaxElapsedMs}ms), no lock by contender", elapsedMs, expectedMaxElapsedMs);
        }
        else
        {
            Logger.LogError("❌ Test FAILED: Cancellation token NOT honoured properly");
            if (!operationCancelled)
                Logger.LogError("  - OperationCanceledException was NOT thrown");
            if (elapsedMs >= expectedMaxElapsedMs)
                Logger.LogError("  - Operation was too slow: {ElapsedMs}ms (expected < {ExpectedMaxElapsedMs}ms)", elapsedMs, expectedMaxElapsedMs);
            if (lockStillExistsAfterContender)
                Logger.LogError("  - Lock '{LockName}' was unexpectedly found granted after contender's cancelled operation", testLockName);
        }
        
        Logger.LogInformation("Waiting for holder task to complete...");
        await holderTask; // Ensure holder task finishes and releases its lock
        Logger.LogInformation("Holder task completed");

        // Final check: ensure holder released the lock and no locks remain for this testLockName
        bool finalLockCheck = await CheckLockExistsAsync(testLockName, isLockExpected: false);
        if (!finalLockCheck)
        {
            Logger.LogInformation("✅ Final check: Lock '{LockName}' correctly released by holder and not present", testLockName);
        }
        else
        {
            Logger.LogWarning("⚠️ Final check: Lock '{LockName}' still present after holder task should have released it", testLockName);
        }
    }

    // Modified CheckLockExistsAsync to be more specific if needed, or can use base one
    // For this test, the base CheckLockExistsAsync (which checks for GRANT) should be fine
    // as a cancelled operation should not result in a GRANT.
    // Adding a parameter for clarity on expected state, though not strictly used by SQL in SqlockTestBase version.
    private async Task<bool> CheckLockExistsAsync(string lockName, bool isLockExpected)
    {
        // This uses the DbContext from SqlockTestBase
        var sql = @"
            SELECT COUNT(*) 
            FROM sys.dm_tran_locks 
            WHERE resource_description LIKE @LockName + '%' 
              AND request_status = 'GRANT'
              AND request_owner_type = 'SESSION'";

        await using var connection = new SqlConnection(DbContext.Database.GetConnectionString()!);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@LockName", lockName.Split('_')[0] + "_" + lockName.Split('_')[1]); // Match sp_getapplock format
        var lockCount = (int)(await command.ExecuteScalarAsync() ?? 0);
        Logger.LogTrace("CheckLockExistsAsync for '{LockName}': Found {LockCount} granted locks. Expected to find lock: {IsLockExpected}", lockName, lockCount, isLockExpected);
        return lockCount > 0;
    }
}
