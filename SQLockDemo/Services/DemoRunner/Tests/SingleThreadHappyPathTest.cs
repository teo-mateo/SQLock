using Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLock;

namespace SQLockDemo.Services.DemoRunner.Tests;

public class SingleThreadHappyPathTest : SqlockTestBase
{
    public SingleThreadHappyPathTest(
        ISqlDistributedLockFactory lockFactory,
        VehicleManagementDbContext dbContext,
        ILoggerFactory loggerFactory)
        : base(lockFactory, dbContext, loggerFactory)
    {
    }

    public override string Name => "Single-Thread Happy Path";
    public override string Description => "Basic mechanics work. TakeAsync returns, DMV shows lock in sys.dm_tran_locks; after DisposeAsync no lock remains";

    public override async Task RunAsync()
    {
        const string testLockName = "demo_test_lock";
        
        try
        {
            // Step 1: Acquire lock
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            Logger.LogInformation("Step 1: Acquiring lock '{LockName}'...", testLockName);
            await using SqlDistributedLock lck = LockFactory.CreateLock(testLockName);
            await lck.TakeAsync();
            Logger.LogInformation("✅ Lock acquired successfully");
            
            // Step 2: Verify lock exists in DB
            Logger.LogInformation("Step 2: Checking lock existence in database...");
            bool lockExists = await CheckLockExistsAsync(testLockName);
            
            if (lockExists)
            {
                Logger.LogInformation("✅ Lock found in sys.dm_tran_locks");
            }
            else
            {
                Logger.LogError("❌ Lock not found in sys.dm_tran_locks!");
                return;
            }

            // Step 3: Release lock
            Logger.LogInformation("Step 3: Releasing lock...");
            // ReSharper disable once DisposeOnUsingVariable
            await lck.DisposeAsync(); 
            Logger.LogInformation("✅ Lock released");

            // Step 4: Verify lock removal
            Logger.LogInformation("Step 4: Checking lock cleanup...");
            lockExists = await CheckLockExistsAsync(testLockName);
            
            if (!lockExists)
            {
                Logger.LogInformation("✅ Lock successfully removed from sys.dm_tran_locks");
                Logger.LogInformation("Test PASSED: Single-Thread Happy Path ✅");
            }
            else
            {
                Logger.LogError("❌ Lock still present in sys.dm_tran_locks after disposal!");
                Logger.LogInformation("Test FAILED: Single-Thread Happy Path ❌");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Test FAILED: Single-Thread Happy Path ❌");
        }
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
