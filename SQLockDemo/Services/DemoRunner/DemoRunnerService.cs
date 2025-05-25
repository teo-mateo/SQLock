using System.Data;
using System.Diagnostics;
using Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLock;
using System.IO;

namespace SQLockDemo.Services.DemoRunner;

public class DemoRunnerService(
    ISqlDistributedLockFactory lockFactory,
    VehicleManagementDbContext dbContext,
    ILogger<DemoRunnerService> logger)
{
    public async Task RunTestsAsync()
    {
        logger.LogInformation("Starting SQLock demo tests...");
        
        // Test 1: Single-thread happy path
        await SingleThreadHappyPathTestAsync();
        
        // Test 2: Mutual exclusion between threads
        await MutualExclusionThreadsTestAsync();
        
        // Test 3: Inter-process mutual exclusion
        await InterProcessMutualExclusionTestAsync();
    }

    private async Task SingleThreadHappyPathTestAsync()
    {
        const string testLockName = "demo_test_lock";
        logger.LogInformation("\n=== Test: Single-Thread Happy Path ===");
        logger.LogInformation("Description: Basic mechanics work. TakeAsync returns, DMV shows lock in sys.dm_tran_locks; after DisposeAsync no lock remains");
        
        try
        {
            // Step 1: Create and acquire lock
            logger.LogInformation("Step 1: Acquiring lock '{LockName}'...", testLockName);
            await using SqlDistributedLock lck = lockFactory.CreateLock(testLockName);
            await lck.TakeAsync();
            logger.LogInformation("✅ Lock acquired successfully");
            
            // Step 2: Verify lock exists in DB
            logger.LogInformation("Step 2: Checking lock existence in database...");
            bool lockExists = await CheckLockExistsAsync(testLockName);
            
            if (lockExists)
            {
                logger.LogInformation("✅ Lock found in sys.dm_tran_locks");
            }
            else
            {
                logger.LogError("❌ Lock not found in sys.dm_tran_locks!");
                return;
            }

            // Step 3: Release lock
            logger.LogInformation("Step 3: Releasing lock...");
            await lck.DisposeAsync();
            logger.LogInformation("✅ Lock released");

            // Step 4: Verify lock removal
            logger.LogInformation("Step 4: Checking lock cleanup...");
            lockExists = await CheckLockExistsAsync(testLockName);
            
            if (!lockExists)
            {
                logger.LogInformation("✅ Lock successfully removed from sys.dm_tran_locks");
                logger.LogInformation("Test PASSED: Single-Thread Happy Path ✅");
            }
            else
            {
                logger.LogError("❌ Lock still present in sys.dm_tran_locks after disposal!");
                logger.LogInformation("Test FAILED: Single-Thread Happy Path ❌");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test FAILED: Single-Thread Happy Path ❌");
        }
    }
    
    private async Task MutualExclusionThreadsTestAsync()
    {
        const string testLockName = "mutual_exclusion_test_lock";
        logger.LogInformation("\n=== Test: Mutual Exclusion (Threads) ===");
        logger.LogInformation("Description: Only one thread in the same process can own the lock at a time.");
        logger.LogInformation("Two tasks call TakeAsync on the same resource; measure that second caller blocks until first disposes.");
        
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
                logger.LogInformation("Task 1: Acquiring lock '{LockName}'...", testLockName);
                await using var lock1 = await lockFactory.CreateLockAndTake(testLockName);
                
                var firstAcquireTime = stopwatch.ElapsedMilliseconds;
                logger.LogInformation("Task 1: Lock acquired at {Time}ms", firstAcquireTime);
                
                // Signal that the first lock is acquired
                firstLockAcquired.SetResult(true);
                
                // Wait for Task 2 to start trying to acquire the lock
                await secondLockStarted.Task;
                
                // Hold the lock for a while
                logger.LogInformation("Task 1: Holding lock for 2 seconds...");
                await Task.Delay(2000);
                
                // Release the lock
                logger.LogInformation("Task 1: Releasing lock...");
                var firstReleaseTime = stopwatch.ElapsedMilliseconds;
                logger.LogInformation("Task 1: Lock released at {Time}ms", firstReleaseTime);
                
                // Signal that the first lock is released
                firstLockReleased.SetResult(true);
            });
            
            // Task 2: Second thread tries to acquire the same lock
            Task task2 = Task.Run(async () =>
            {
                // Wait for first lock to be acquired
                await firstLockAcquired.Task;
                
                logger.LogInformation("Task 2: Attempting to acquire the same lock...");
                var secondStartTime = stopwatch.ElapsedMilliseconds;
                logger.LogInformation("Task 2: Attempt started at {Time}ms", secondStartTime);
                
                // Signal that the second lock attempt has started
                secondLockStarted.SetResult(true);
                
                // Try to acquire the lock (this should block until Task 1 releases it)
                await using var lock2 = await lockFactory.CreateLockAndTake(testLockName);
                
                var secondAcquireTime = stopwatch.ElapsedMilliseconds;
                logger.LogInformation("Task 2: Lock acquired at {Time}ms", secondAcquireTime);
                
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
                logger.LogInformation("✅ Test PASSED: Task 2 successfully waited for Task 1 to release the lock");
            }
            else
            {
                logger.LogError("❌ Test FAILED: Task 2 did not properly wait for Task 1 to release the lock");
            }
            
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test FAILED: Mutual Exclusion (Threads) ❌");
        }
    }

    private async Task InterProcessMutualExclusionTestAsync()
    {
        logger.LogInformation("\n=== Test: Inter-Process Mutual Exclusion ===");
        logger.LogInformation("Description: Demonstrates that locks work across process boundaries.");
        logger.LogInformation("Two separate processes will try to acquire the same lock; the second process should block until the first releases it.");
        
        try
        {
            // Generate a unique lock key for this test
            string testLockKey = $"inter_process_test_{Guid.NewGuid():N}".Substring(0, 32);
            int holdTimeMs = 5000; // First process will hold the lock for 5 seconds
            
            logger.LogInformation("Using test lock key: {LockKey}", testLockKey);
            
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            logger.LogInformation("[{ElapsedTime}ms] Starting first process to take and hold the lock...", stopwatch.ElapsedMilliseconds);
            
            // Get the executable path
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SQLockDemo.exe");
            logger.LogInformation("Executable path: {ExePath}", exePath);
            
            // Start the first process that will take the lock and hold it
            var process1 = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--take {testLockKey} --hold {holdTimeMs}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process1.Start();
            logger.LogInformation("[{ElapsedTime}ms] Started first process (PID: {ProcessId}) - will hold lock for {HoldTime}ms", 
                stopwatch.ElapsedMilliseconds, process1.Id, holdTimeMs);
            
            // Wait a bit to ensure the first process has time to acquire the lock
            await Task.Delay(1000);
            
            logger.LogInformation("[{ElapsedTime}ms] Starting second process to attempt to take the same lock...", stopwatch.ElapsedMilliseconds);
            
            // Start the second process that will try to acquire the same lock
            var process2 = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--take {testLockKey} --hold 1000",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process2.Start();
            logger.LogInformation("[{ElapsedTime}ms] Started second process (PID: {ProcessId}) - should block until first process releases the lock", 
                stopwatch.ElapsedMilliseconds, process2.Id);
            
            // Start reading output from processes
            var process1OutputTask = Task.Run(() => ReadProcessOutputAsync(process1, "Process 1"));
            var process2OutputTask = Task.Run(() => ReadProcessOutputAsync(process2, "Process 2"));
            
            // Wait for both processes to complete
            logger.LogInformation("[{ElapsedTime}ms] Waiting for both processes to complete...", stopwatch.ElapsedMilliseconds);
            
            await Task.WhenAll(
                Task.Run(() => process1.WaitForExit()),
                Task.Run(() => process2.WaitForExit()),
                process1OutputTask,
                process2OutputTask
            );
            
            var elapsedTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Stop();
            
            // Verify the test results
            if (elapsedTime >= holdTimeMs)
            {
                logger.LogInformation("✅ Test PASSED: Inter-Process Mutual Exclusion");
                logger.LogInformation("Total test duration: {ElapsedTime}ms, which is greater than the first process hold time ({HoldTime}ms)", 
                    elapsedTime, holdTimeMs);
                logger.LogInformation("This confirms the second process had to wait for the first process to release the lock");
            }
            else
            {
                logger.LogError("❌ Test FAILED: Inter-Process Mutual Exclusion");
                logger.LogError("Test completed too quickly ({ElapsedTime}ms), suggesting the second process didn't wait for the lock", 
                    elapsedTime);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test FAILED: Inter-Process Mutual Exclusion ❌");
        }
    }
    
    private async Task ReadProcessOutputAsync(Process process, string processName)
    {
        while (!process.StandardOutput.EndOfStream)
        {
            string? line = await process.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
            {
                logger.LogInformation("[{ProcessName}] {OutputLine}", processName, line);
            }
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

        using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }
}
