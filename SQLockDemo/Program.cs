using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SQLock;
using SQLockDemo.Services;
using SQLockDemo.Services.DemoRunner;
using SQLockDemo.Services.DemoRunner.Tests;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) => { config.AddJsonFile("appsettings.json", false, true); })
    .ConfigureServices((hostContext, services) =>
    {
        string connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection")!;
        services.AddDbContext<Data.VehicleManagementDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<ISqlDistributedLockFactory>(_ => new SqlDistributedLockFactory(connectionString));

        services.AddScoped<IVehiclesService, VehiclesService>();
        
        // Register test classes
        services.AddTransient<ISqlockTest, SingleThreadHappyPathTest>();
        services.AddTransient<ISqlockTest, MutualExclusionThreadTest>();
        services.AddTransient<ISqlockTest, InterProcessMutualExclusionTest>();
        services.AddTransient<ISqlockTest, TimeoutRespectedTest>();
        
        // Register the DemoRunnerService
        services.AddScoped<DemoRunnerService>();
    })
    .Build();

var configuration = host.Services.GetRequiredService<IConfiguration>();
string connectionString = configuration.GetConnectionString("DefaultConnection")!;
Console.WriteLine($"Connection string: {connectionString}");

bool canConnect = SQLHelper.TestConnection(connectionString);
Console.WriteLine(canConnect ? "Database connection successful!" : "Failed to connect to database.");

// Parse arguments
string? demoTestName = null;
bool runDemo = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--demo")
    {
        runDemo = true;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
        {
            demoTestName = args[i + 1];
            i++; // Consume the test name argument
        }
        break; // Found --demo, no need to check further args for it
    }
}

if (runDemo)
{
    Console.WriteLine(string.IsNullOrWhiteSpace(demoTestName)
        ? "Running all SQLock demo tests..."
        : $"Running SQLock demo test: {demoTestName}...");
    using IServiceScope scope = host.Services.CreateScope();
    var demoRunner = scope.ServiceProvider.GetRequiredService<DemoRunnerService>();
    await demoRunner.RunTestsAsync(demoTestName);
    return;
}

if (args.Contains("--seed"))
{
    Console.WriteLine("Seeding database with vehicles...");
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    await vehiclesService.SeedAsync();
    Console.WriteLine("Seeding complete.");
    return;
}

if (args.Contains("--getall"))
{
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    List<Vehicle> vehicles = await vehiclesService.GetAllAsync();
    foreach (Vehicle v in vehicles)
    {
        string ts = BitConverter.ToString(v.Timestamp).Replace("-", "");
        Console.WriteLine(
            $"Id: {v.Id}, Make: {v.Make}, Model: {v.Model}, Year: {v.Year}, LicensePlate: {v.LicensePlate}, Mileage: {v.Mileage}, Timestamp: {ts}");
    }

    return;
}

// --sim command to simulate race conditions -- expected to result in concurrency conflicts
if (args.Contains("--sim"))
{
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    List<Vehicle> vehicles = await vehiclesService.GetAllAsync();
    if (vehicles.Count == 0)
    {
        Console.WriteLine("No vehicles found. Please seed the database first.");
        return;
    }

    var rand = new Random();
    for (var i = 0; i < 100; i++)
    {
        Vehicle randomVehicle = vehicles[rand.Next(vehicles.Count)];
        long vehicleId = randomVehicle.Id;
        Console.WriteLine($"Simulating race condition on Vehicle Id: {vehicleId}");
        await vehiclesService.SimulateRaceConditionAsync(vehicleId);
    }

    return;
}

// --simlock command to simulate race conditions with distributed locks -- expected to succeed
if (args.Contains("--simlock"))
{
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    List<Vehicle> vehicles = await vehiclesService.GetAllAsync();
    if (vehicles.Count == 0)
    {
        Console.WriteLine("No vehicles found. Please seed the database first.");
        return;
    }

    var rand = new Random();
    for (var i = 0; i < 100; i++)
    {
        Vehicle randomVehicle = vehicles[rand.Next(vehicles.Count)];
        long vehicleId = randomVehicle.Id;
        Console.WriteLine($"Simulating race condition with distributed lock on Vehicle Id: {vehicleId}");
        await vehiclesService.SimulateRaceConditionWithDistributedLockAsync(vehicleId);
    }

    return;
}

// --take command to take a specific lock and hold it for a specified time
if (args.Contains("--take"))
{
    // Get the lock key
    int keyIndex = Array.IndexOf(args, "--take");
    if (keyIndex >= args.Length - 1)
    {
        Console.WriteLine("Error: No lock key specified after --take");
        return;
    }
    string lockKey = args[keyIndex + 1];
    
    // Get the hold time (default to 5000ms if not specified)
    int holdTimeMs = 5000;
    if (args.Contains("--hold"))
    {
        int holdIndex = Array.IndexOf(args, "--hold");
        if (holdIndex < args.Length - 1 && int.TryParse(args[holdIndex + 1], out int parsedHoldTime))
        {
            holdTimeMs = parsedHoldTime;
        }
        else
        {
            Console.WriteLine("Warning: Invalid hold time specified, using default of 5000ms");
        }
    }
    
    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await TakeLockAndHold(scope.ServiceProvider, lockKey, holdTimeMs, logger);
    return;
}

// Helper method to take a lock and hold it for the specified time
static async Task TakeLockAndHold(IServiceProvider services, string lockKey, int holdTimeMs, ILogger logger)
{
    logger.LogInformation("Taking lock '{LockKey}' and holding for {HoldTime}ms...", lockKey, holdTimeMs);
    
    var lockFactory = services.GetRequiredService<ISqlDistributedLockFactory>();
    
    var stopwatch = new System.Diagnostics.Stopwatch();
    stopwatch.Start();
    
    try
    {
        logger.LogInformation("[{ElapsedTime}ms] Attempting to take lock '{LockKey}'...", stopwatch.ElapsedMilliseconds, lockKey);
        await using SqlDistributedLock lock1 = await lockFactory.CreateLockAndTake(lockKey);
        logger.LogInformation("[{ElapsedTime}ms] Successfully acquired lock '{LockKey}'", stopwatch.ElapsedMilliseconds, lockKey);
        
        logger.LogInformation("[{ElapsedTime}ms] Holding lock for {HoldTime}ms...", stopwatch.ElapsedMilliseconds, holdTimeMs);
        await Task.Delay(holdTimeMs);
        
        logger.LogInformation("[{ElapsedTime}ms] Releasing lock '{LockKey}'...", stopwatch.ElapsedMilliseconds, lockKey);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error while taking lock: {ErrorMessage}", ex.Message);
    }
    finally
    {
        stopwatch.Stop();
        logger.LogInformation("[{ElapsedTime}ms] Operation completed", stopwatch.ElapsedMilliseconds);
    }
}