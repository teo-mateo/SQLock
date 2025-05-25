using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System;
using Data;
using SQLockDemo.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection")!;
        services.AddDbContext<VehicleManagementDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IVehiclesService, VehiclesService>();
    })
    .Build();

var configuration = host.Services.GetRequiredService<IConfiguration>();
string connectionString = configuration.GetConnectionString("DefaultConnection")!;
Console.WriteLine($"Connection string: {connectionString}");

bool canConnect = SQLHelper.TestConnection(connectionString);
Console.WriteLine(canConnect ? "Database connection successful!" : "Failed to connect to database.");

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
    var vehicles = await vehiclesService.GetAllAsync();
    foreach (var v in vehicles)
    {
        var ts = BitConverter.ToString(v.Timestamp).Replace("-", "");
        Console.WriteLine($"Id: {v.Id}, Make: {v.Make}, Model: {v.Model}, Year: {v.Year}, LicensePlate: {v.LicensePlate}, Mileage: {v.Mileage}, Timestamp: {ts}");
    }
    return;
}

if (args.Contains("--sim"))
{
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    var vehicles = await vehiclesService.GetAllAsync();
    if (vehicles.Count == 0)
    {
        Console.WriteLine("No vehicles found. Please seed the database first.");
        return;
    }
    var rand = new Random();
    var randomVehicle = vehicles[rand.Next(vehicles.Count)];
    long vehicleId = randomVehicle.Id;
    Console.WriteLine($"Simulating race condition on Vehicle Id: {vehicleId}");
    await vehiclesService.SimulateRaceConditionAsync(vehicleId);
    return;
}

if (args.Contains("--simlock"))
{
    using IServiceScope scope = host.Services.CreateScope();
    var vehiclesService = scope.ServiceProvider.GetRequiredService<IVehiclesService>();
    var vehicles = await vehiclesService.GetAllAsync();
    if (vehicles.Count == 0)
    {
        Console.WriteLine("No vehicles found. Please seed the database first.");
        return;
    }
    var rand = new Random();
    var randomVehicle = vehicles[rand.Next(vehicles.Count)];
    long vehicleId = randomVehicle.Id;
    Console.WriteLine($"Simulating race condition with distributed lock on Vehicle Id: {vehicleId}");
    await vehiclesService.SimulateRaceConditionWithDistributedLockAsync(vehicleId);
    return;
}

Console.WriteLine("Hello, World!");
