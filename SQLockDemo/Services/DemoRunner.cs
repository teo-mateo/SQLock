using System.Data;
using System.Diagnostics;
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
        
        // Test 2: Mutual exclusion between threads
        await MutualExclusionThreadsTestAsync();
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
    
    private async Task MutualExclusionThreadsTestAsync()
    {
        const string testLockName = "mutual_exclusion_test_lock";
        _logger.LogInformation("\n=== Test: Mutual Exclusion (Threads) ===");
        _logger.LogInformation("Description: Only one thread in the same process can own the lock at a time.");
        _logger.LogInformation("Two tasks call AcquireAsync on the same resource; measure that second caller blocks until first disposes.");
        
        try
        {
            // Variables to track execution
            var firstLockAcquired = new TaskCompletionSource<bool>();
            var secondLockStarted = new TaskCompletionSource<bool>();
            var firstLockReleased = new TaskCompletionSource<bool>();
            var secondLockAcquired = new TaskCompletionSource<bool>();
            
            // Timing measurements
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // Task 1: First thread acquires the lock and holds it
            Task task1 = Task.Run(async () =>
            {
                _logger.LogInformation("Task 1: Acquiring lock '{LockName}'...", testLockName);
                await using var lock1 = _lockFactory.TakeLock(testLockName);
                
                await lock1.AcquireAsync();
                var firstAcquireTime = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation("Task 1: Lock acquired at {Time}ms", firstAcquireTime);
                
                // Signal that the first lock is acquired
                firstLockAcquired.SetResult(true);
                
                // Wait for Task 2 to start trying to acquire the lock
                await secondLockStarted.Task;
                
                // Hold the lock for a while
                _logger.LogInformation("Task 1: Holding lock for 2 seconds...");
                await Task.Delay(2000);
                
                // Release the lock
                _logger.LogInformation("Task 1: Releasing lock...");
                var firstReleaseTime = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation("Task 1: Lock released at {Time}ms", firstReleaseTime);
                
                // Signal that the first lock is released
                firstLockReleased.SetResult(true);
            });
            
            // Task 2: Second thread tries to acquire the same lock
            Task task2 = Task.Run(async () =>
            {
                // Wait for first lock to be acquired
                await firstLockAcquired.Task;
                
                _logger.LogInformation("Task 2: Attempting to acquire the same lock...");
                var secondStartTime = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation("Task 2: Attempt started at {Time}ms", secondStartTime);
                
                // Signal that the second lock attempt has started
                secondLockStarted.SetResult(true);
                
                // Try to acquire the lock (this should block until Task 1 releases it)
                await using var lock2 = _lockFactory.TakeLock(testLockName);
                await lock2.AcquireAsync();
                
                var secondAcquireTime = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation("Task 2: Lock acquired at {Time}ms", secondAcquireTime);
                
                // Signal that the second lock is acquired
                secondLockAcquired.SetResult(true);
                
                // Hold the lock briefly to ensure we can see it in the logs
                await Task.Delay(500);
            });
            
            // Wait for both tasks to complete
            await Task.WhenAll(task1, task2);
            
            // Wait for all signals to be set
            await Task.WhenAll(
                firstLockAcquired.Task,
                secondLockStarted.Task,
                firstLockReleased.Task,
                secondLockAcquired.Task);
            
            // Check if the second task acquired the lock after the first task released it
            bool secondAcquiredAfterFirstReleased = secondLockAcquired.Task.Result && 
                                                   firstLockReleased.Task.Result &&
                                                   secondLockAcquired.Task.Status == TaskStatus.RanToCompletion &&
                                                   firstLockReleased.Task.Status == TaskStatus.RanToCompletion;
            
            if (secondAcquiredAfterFirstReleased)
            {
                _logger.LogInformation("✅ Test PASSED: Task 2 successfully waited for Task 1 to release the lock");
            }
            else
            {
                _logger.LogError("❌ Test FAILED: Task 2 did not properly wait for Task 1 to release the lock");
            }
            
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test FAILED: Mutual Exclusion (Threads) ❌");
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
