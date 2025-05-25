using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SQLock;
using SQLockDemo.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) => { config.AddJsonFile("appsettings.json", false, true); })
    .ConfigureServices((context, services) =>
    {
        string connectionString = context.Configuration.GetConnectionString("DefaultConnection")!;
        services.AddDbContext<VehicleManagementDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<ISqlDistributedLockFactory>(_ => new SqlDistributedLockFactory(connectionString));

        services.AddScoped<IVehiclesService, VehiclesService>();
        services.AddScoped<DemoRunner>();
    })
    .Build();

var configuration = host.Services.GetRequiredService<IConfiguration>();
string connectionString = configuration.GetConnectionString("DefaultConnection")!;
Console.WriteLine($"Connection string: {connectionString}");

bool canConnect = SQLHelper.TestConnection(connectionString);
Console.WriteLine(canConnect ? "Database connection successful!" : "Failed to connect to database.");

if (args.Contains("--demo"))
{
    Console.WriteLine("Running SQLock demo tests...");
    using IServiceScope scope = host.Services.CreateScope();
    var demoRunner = scope.ServiceProvider.GetRequiredService<DemoRunner>();
    await demoRunner.RunTestsAsync();
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

// Handle --take command to take a specific lock and hold it for a specified time
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
    
    await TakeLockAndHold(host.Services, lockKey, holdTimeMs);
    return;
}

Console.WriteLine("Hello, World!");

// Helper method to take a lock and hold it for the specified time
static async Task TakeLockAndHold(IServiceProvider services, string lockKey, int holdTimeMs)
{
    Console.WriteLine($"Taking lock '{lockKey}' and holding for {holdTimeMs}ms...");
    
    using var scope = services.CreateScope();
    var lockFactory = scope.ServiceProvider.GetRequiredService<ISqlDistributedLockFactory>();
    
    var stopwatch = new System.Diagnostics.Stopwatch();
    stopwatch.Start();
    
    try
    {
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Attempting to take lock '{lockKey}'...");
        await using var lock1 = await lockFactory.CreateLockAndTake(lockKey);
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Successfully acquired lock '{lockKey}'");
        
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Holding lock for {holdTimeMs}ms...");
        await Task.Delay(holdTimeMs);
        
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Releasing lock '{lockKey}'...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    finally
    {
        stopwatch.Stop();
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Operation completed");
    }
}