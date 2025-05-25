using Data;
using Microsoft.EntityFrameworkCore;
using SQLock;

namespace SQLockDemo.Services;

public class VehiclesService(VehicleManagementDbContext db, ISqlDistributedLockFactory lockFactory)
    : IVehiclesService
{
    public async Task SeedAsync()
    {
        if (!await db.Vehicles.AnyAsync())
        {
            var makesAndModels = new (string Make, string Model)[]
            {
                ("Toyota", "Corolla"),
                ("Honda", "Civic"),
                ("Ford", "Focus"),
                ("Volkswagen", "Golf"),
                ("BMW", "3 Series"),
                ("Audi", "A4"),
                ("Mercedes-Benz", "C-Class"),
                ("Hyundai", "Elantra"),
                ("Nissan", "Sentra"),
                ("Kia", "Forte")
            };

            List<Vehicle> vehicles = makesAndModels.Select((mm, i) => new Vehicle
            {
                Make = mm.Make,
                Model = mm.Model,
                Year = 2018 + i % 6,
                LicensePlate = $"ABC{i:000}",
                Mileage = 50000 + i * 1000
            }).ToList();

            db.Vehicles.AddRange(vehicles);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<Vehicle>> GetAllAsync()
    {
        return await db.Vehicles.ToListAsync();
    }

    public async Task SimulateRaceConditionAsync(long vehicleId)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VehicleManagementDbContext>();
        optionsBuilder.UseSqlServer(db.Database.GetDbConnection().ConnectionString);
        DbContextOptions<VehicleManagementDbContext> options = optionsBuilder.Options;

        Task task1 = Task.Run(async () =>
        {
            try
            {
                await using var db1 = new VehicleManagementDbContext(options);
                Vehicle v1 = await db1.Vehicles.FirstAsync(v => v.Id == vehicleId);
                v1.Mileage += 100;
                db1.Vehicles.Update(v1);
                await db1.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                Console.WriteLine("[Task1] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
            }
        });

        Task task2 = Task.Run(async () =>
        {
            try
            {
                await using var db2 = new VehicleManagementDbContext(options);
                Vehicle v2 = await db2.Vehicles.FirstAsync(v => v.Id == vehicleId);
                v2.Mileage += 200;
                db2.Vehicles.Update(v2);
                await db2.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                Console.WriteLine("[Task2] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
            }
        });

        await Task.WhenAll(task1, task2);
    }

    public async Task SimulateRaceConditionWithDistributedLockAsync(long vehicleId)
    {
        var semaphore = new SemaphoreSlim(0, 2);
        var optionsBuilder = new DbContextOptionsBuilder<VehicleManagementDbContext>();
        optionsBuilder.UseSqlServer(db.Database.GetDbConnection().ConnectionString);
        DbContextOptions<VehicleManagementDbContext> options = optionsBuilder.Options;

        Task task1 = Task.Run(async () =>
        {
            try
            {
                await using var db1 = new VehicleManagementDbContext(options);
                await using SqlDistributedLock sqlLock = await lockFactory.CreateLockAndTake("vehicle", vehicleId);
                if (!await sqlLock.TryTakeAsync())
                {
                    Console.WriteLine("[Task1] Could not acquire distributed lock.");
                    return;
                }

                Vehicle v1 = await db1.Vehicles.FirstAsync(v => v.Id == vehicleId);
                v1.Mileage += 100;
                db1.Vehicles.Update(v1);
                await db1.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                Console.WriteLine("[Task1] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
            }
        });

        Task task2 = Task.Run(async () =>
        {
            try
            {
                await using var db2 = new VehicleManagementDbContext(options);
                await using SqlDistributedLock sqlLock = await lockFactory.CreateLockAndTake("vehicle", vehicleId);
                await sqlLock.TakeAsync();

                Vehicle v2 = await db2.Vehicles.FirstAsync(v => v.Id == vehicleId);
                v2.Mileage += 200;
                db2.Vehicles.Update(v2);
                await db2.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                Console.WriteLine("[Task2] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
            }
        });

        await Task.WhenAll(task1, task2);
    }
}