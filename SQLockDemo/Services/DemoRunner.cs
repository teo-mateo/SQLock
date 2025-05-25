using System.Data;
using Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLock;

namespace SQLockDemo.Services;

public class DemoRunner
{
    private readonly ISqlDistributedLockFactory _lockFactory;
    private readonly VehicleManagementDbContext _dbContext;
    private readonly ILogger<DemoRunner> _logger;

    public DemoRunner(
        ISqlDistributedLockFactory lockFactory,
        VehicleManagementDbContext dbContext,
        ILogger<DemoRunner> logger)
    {
        _lockFactory = lockFactory;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RunTestsAsync()
    {
        _logger.LogInformation("Starting SQLock demo tests...");
        
        // Test 1: Single-thread happy path
        await SingleThreadHappyPathTestAsync();
    }

    private async Task SingleThreadHappyPathTestAsync()
    {
        const string testLockName = "demo_test_lock";
        _logger.LogInformation("\n=== Test: Single-Thread Happy Path ===");
        _logger.LogInformation("Description: Basic mechanics work. AcquireAsync returns, DMV shows lock in sys.dm_tran_locks; after DisposeAsync no lock remains");
        
        try
        {
            // Step 1: Create and acquire lock
            _logger.LogInformation("Step 1: Acquiring lock '{LockName}'...", testLockName);
            await using SqlDistributedLock lck = _lockFactory.TakeLock(testLockName);
            await lck.AcquireAsync();
            _logger.LogInformation("✅ Lock acquired successfully");
            
            // Step 2: Verify lock exists in DB
            _logger.LogInformation("Step 2: Checking lock existence in database...");
            bool lockExists = await CheckLockExistsAsync(testLockName);
            
            if (lockExists)
            {
                _logger.LogInformation("✅ Lock found in sys.dm_tran_locks");
            }
            else
            {
                _logger.LogError("❌ Lock not found in sys.dm_tran_locks!");
                return;
            }

            // Step 3: Release lock
            _logger.LogInformation("Step 3: Releasing lock...");
            await lck.DisposeAsync();
            _logger.LogInformation("✅ Lock released");

            // Step 4: Verify lock removal
            _logger.LogInformation("Step 4: Checking lock cleanup...");
            lockExists = await CheckLockExistsAsync(testLockName);
            
            if (!lockExists)
            {
                _logger.LogInformation("✅ Lock successfully removed from sys.dm_tran_locks");
                _logger.LogInformation("Test PASSED: Single-Thread Happy Path ✅");
            }
            else
            {
                _logger.LogError("❌ Lock still present in sys.dm_tran_locks after disposal!");
                _logger.LogInformation("Test FAILED: Single-Thread Happy Path ❌");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test FAILED: Single-Thread Happy Path ❌");
        }
    }

    private async Task<bool> CheckLockExistsAsync(string lockName)
    {
        // Use raw SQL to check if the lock exists in sys.dm_tran_locks
        var sql = @"
            SELECT COUNT(1) 
            FROM sys.dm_tran_locks 
            WHERE resource_type = 'APPLICATION' 
              AND request_mode = 'X'  -- Exclusive lock
              AND request_status = 'GRANT'
              AND request_owner_type = 'SESSION'";

        using var connection = new SqlConnection(_dbContext.Database.GetConnectionString());
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }
}
