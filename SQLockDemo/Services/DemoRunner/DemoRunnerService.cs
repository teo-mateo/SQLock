using Microsoft.Extensions.Logging;

namespace SQLockDemo.Services.DemoRunner;

public class DemoRunnerService(
    IEnumerable<ISqlockTest> tests,
    ILogger<DemoRunnerService> logger)
{
    public async Task RunTestsAsync(string? singleTestName = null)
    {
        ISqlockTest[] testsToRun;

        if (!string.IsNullOrWhiteSpace(singleTestName))
        {
            ISqlockTest? matchedTest = tests.FirstOrDefault(t => t.Name.Equals(singleTestName, StringComparison.OrdinalIgnoreCase));
            if (matchedTest != null)
            {
                testsToRun = [matchedTest];
            }
            else
            {
                logger.LogError("Test '{TestName}' not found. Available tests: {AvailableTests}", 
                                 singleTestName, string.Join(", ", tests.Select(t => t.Name)));
                return;
            }
        }
        else
        {
            testsToRun = tests.ToArray();
        }
        
        if (!testsToRun.Any())
        {
            logger.LogWarning("No tests found to run");
            return;
        }

        foreach (ISqlockTest test in testsToRun)
        {
            logger.LogInformation("\n=== Running Test: {TestName} ===", test.Name);
            logger.LogInformation("Description: {TestDescription}", test.Description);
            try
            {
                await test.RunAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Test FAILED: {TestName} ❌", test.Name);
            }
        }
    }
}
