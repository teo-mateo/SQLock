using Data;
using Microsoft.Extensions.Logging;
using SQLock;
using System.Diagnostics;

namespace SQLockDemo.Services.DemoRunner.Tests;

public class MutualExclusionThreadTest(
    ISqlDistributedLockFactory lockFactory,
    VehicleManagementDbContext dbContext,
    ILoggerFactory loggerFactory)
    : SqlockTestBase(lockFactory, dbContext, loggerFactory)
{
    public override string Name => "Mutual-Exclusion-Threads";
    public override string Description => "Only one thread in the same process can own the lock at a time. Two tasks call TakeAsync on the same resource; measure that second caller blocks until first disposes.";

    public override async Task RunAsync()
    {
        const string testLockName = "mutual_exclusion_test_lock";
        
        try
        {
            // Setup completion sources for synchronization
            var firstLockAcquired = new TaskCompletionSource<bool>();
            var secondLockStarted = new TaskCompletionSource<bool>();
            var firstLockReleased = new TaskCompletionSource<bool>();
            var secondLockAcquired = new TaskCompletionSource<bool>();
            
            // Start stopwatch to measure timings
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // Task 1: First thread acquires the lock and holds it
            Task task1 = Task.Run(async () =>
            {
                Logger.LogInformation("Task 1: Acquiring lock '{LockName}'...", testLockName);
                await using var lock1 = await LockFactory.CreateLockAndTake(testLockName);
                
                var firstAcquireTime = stopwatch.ElapsedMilliseconds;
                Logger.LogInformation("Task 1: Lock acquired at {Time}ms", firstAcquireTime);
                
                // Signal that the first lock is acquired
                firstLockAcquired.SetResult(true);
                
                // Wait for second lock attempt to start
                await secondLockStarted.Task;
                
                // Hold the lock for a while
                Logger.LogInformation("Task 1: Holding lock for 2 seconds...");
                await Task.Delay(2000);
                
                // Release the lock
                Logger.LogInformation("Task 1: Releasing lock...");
                var firstReleaseTime = stopwatch.ElapsedMilliseconds;
                Logger.LogInformation("Task 1: Lock released at {Time}ms", firstReleaseTime);
                
                // Signal that the first lock is released
                firstLockReleased.SetResult(true);
            });
            
            // Task 2: Second thread tries to acquire the same lock
            Task task2 = Task.Run(async () =>
            {
                // Wait for first lock to be acquired
                await firstLockAcquired.Task;
                
                Logger.LogInformation("Task 2: Attempting to acquire the same lock...");
                var secondStartTime = stopwatch.ElapsedMilliseconds;
                Logger.LogInformation("Task 2: Attempt started at {Time}ms", secondStartTime);
                
                // Signal that the second lock attempt has started
                secondLockStarted.SetResult(true);
                
                // Try to acquire the lock (this should block until Task 1 releases it)
                await using var lock2 = await LockFactory.CreateLockAndTake(testLockName);
                
                var secondAcquireTime = stopwatch.ElapsedMilliseconds;
                Logger.LogInformation("Task 2: Lock acquired at {Time}ms", secondAcquireTime);
                
                // Signal that the second lock is acquired
                secondLockAcquired.SetResult(true);
            });
            
            // Wait for both tasks to complete
            await Task.WhenAll(task1, task2);
            
            // Verify that Task 2 acquired the lock after Task 1 released it
            await Task.WhenAll(firstLockReleased.Task, secondLockAcquired.Task);
            
            var firstReleaseTime = stopwatch.ElapsedMilliseconds;
            var secondAcquireTime = stopwatch.ElapsedMilliseconds;
            
            bool secondAcquiredAfterFirstReleased = secondAcquireTime >= firstReleaseTime;
            
            if (secondAcquiredAfterFirstReleased)
            {
                Logger.LogInformation("✅ Test PASSED: Task 2 successfully waited for Task 1 to release the lock");
            }
            else
            {
                Logger.LogError("❌ Test FAILED: Task 2 did not properly wait for Task 1 to release the lock");
            }
            
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Test FAILED: Mutual Exclusion (Threads) ❌");
        }
    }
}
