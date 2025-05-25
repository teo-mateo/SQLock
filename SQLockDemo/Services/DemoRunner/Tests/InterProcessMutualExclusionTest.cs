using Data;
using Microsoft.Extensions.Logging;
using SQLock;
using System.Diagnostics;

namespace SQLockDemo.Services.DemoRunner.Tests;

public class InterProcessMutualExclusionTest(
    ISqlDistributedLockFactory lockFactory,
    VehicleManagementDbContext dbContext,
    ILoggerFactory loggerFactory)
    : SqlockTestBase(lockFactory, dbContext, loggerFactory)
{
    public override string Name => "Mutual-Exclusion-Inter-Process";
    public override string Description => "Demonstrates that locks work across process boundaries. Two separate processes will try to acquire the same lock; the second process should block until the first releases it 🔒.";

    public override async Task RunAsync()
    {
        try
        {
            // Generate a unique lock key for this test
            string testLockKey = $"inter_process_test_{Guid.NewGuid():N}".Substring(0, 32);
            int holdTimeMs = 5000; // First process will hold the lock for 5 seconds
            
            Logger.LogInformation("Using test lock key: {LockKey}", testLockKey);
            
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            Logger.LogInformation("[{ElapsedTime}ms] Starting first process to take and hold the lock...", stopwatch.ElapsedMilliseconds);
            
            // Get the executable path
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SQLockDemo.exe");
            Logger.LogInformation("Executable path: {ExePath}", exePath);
            
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
            Logger.LogInformation("[{ElapsedTime}ms] Started first process (PID: {ProcessId}) - will hold lock for {HoldTime}ms ⏱️", 
                stopwatch.ElapsedMilliseconds, process1.Id, holdTimeMs);
            
            // Wait a bit to ensure the first process has time to acquire the lock
            await Task.Delay(1000);
            
            Logger.LogInformation("[{ElapsedTime}ms] Starting second process to attempt to take the same lock... 🔄", stopwatch.ElapsedMilliseconds);
            
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
            Logger.LogInformation("[{ElapsedTime}ms] Started second process (PID: {ProcessId}) - should block until first process releases the lock 🔒", 
                stopwatch.ElapsedMilliseconds, process2.Id);
            
            // Start reading output from processes
            var process1OutputTask = Task.Run(() => ReadProcessOutputAsync(process1, "Process 1"));
            var process2OutputTask = Task.Run(() => ReadProcessOutputAsync(process2, "Process 2"));
            
            // Wait for both processes to complete
            Logger.LogInformation("[{ElapsedTime}ms] Waiting for both processes to complete... ⌛", stopwatch.ElapsedMilliseconds);
            
            await Task.WhenAll(
                Task.Run(() => process1.WaitForExit()),
                Task.Run(() => process2.WaitForExit()),
                process1OutputTask,
                process2OutputTask
            );
            
            stopwatch.Stop();
            var elapsedTime = stopwatch.ElapsedMilliseconds;
            
            // Verify the test results
            if (elapsedTime >= holdTimeMs)
            {
                Logger.LogInformation("✅ Test PASSED: Inter-Process Mutual Exclusion");
                Logger.LogInformation("Total test duration: {ElapsedTime}ms, which is greater than the first process hold time ({HoldTime}ms) ⏱️", 
                    elapsedTime, holdTimeMs);
                Logger.LogInformation("This confirms the second process had to wait for the first process to release the lock 🔓");
            }
            else
            {
                Logger.LogError("❌ Test FAILED: Inter-Process Mutual Exclusion");
                Logger.LogError("Test completed too quickly ({ElapsedTime}ms), suggesting the second process didn't wait for the lock ⚠️", 
                    elapsedTime);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Test FAILED: Inter-Process Mutual Exclusion ❌");
        }
    }
    
    private async Task ReadProcessOutputAsync(Process process, string processName)
    {
        while (!process.StandardOutput.EndOfStream)
        {
            string? line = await process.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
            {
                Logger.LogInformation("[{ProcessName}] {OutputLine}", processName, line);
            }
        }
    }
}
