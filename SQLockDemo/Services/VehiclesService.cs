using Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Threading;
using SQLock;
using System.Data.Common;

namespace SQLockDemo.Services
{
    public class VehiclesService(VehicleManagementDbContext db) : IVehiclesService
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

                var vehicles = makesAndModels.Select((mm, i) => new Vehicle
                {
                    Make = mm.Make,
                    Model = mm.Model,
                    Year = 2018 + (i % 6),
                    LicensePlate = $"ABC{i:000}",
                    Mileage = 50000 + i * 1000
                }).ToList();

                db.Vehicles.AddRange(vehicles);
                await db.SaveChangesAsync();
            }
        }

        public async Task<int> CountAsync() => await db.Vehicles.CountAsync();

        public async Task<List<Vehicle>> GetAllAsync() => await db.Vehicles.ToListAsync();

        public async Task SimulateRaceConditionAsync(long vehicleId)
        {
            var semaphore = new SemaphoreSlim(0, 2);
            var optionsBuilder = new DbContextOptionsBuilder<VehicleManagementDbContext>();
            optionsBuilder.UseSqlServer(db.Database.GetDbConnection().ConnectionString);
            var options = optionsBuilder.Options;

            var task1 = Task.Run(async () =>
            {
                try
                {
                    await using var db1 = new VehicleManagementDbContext(options);
                    Vehicle v1 = await db1.Vehicles.FirstAsync(v => v.Id == vehicleId);
                    v1.Mileage += 100;
                    semaphore.Release();
                    await semaphore.WaitAsync();
                    await Task.Delay(500); // Simulate work
                    db1.Vehicles.Update(v1);
                    await db1.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    Console.WriteLine("[Task1] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
                }
            });

            var task2 = Task.Run(async () =>
            {
                try
                {
                    await using var db2 = new VehicleManagementDbContext(options);
                    Vehicle v2 = await db2.Vehicles.FirstAsync(v => v.Id == vehicleId);
                    v2.Mileage += 200;
                    semaphore.Release();
                    await semaphore.WaitAsync();
                    await Task.Delay(1000); // Simulate longer work
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
            var options = optionsBuilder.Options;

            var task1 = Task.Run(async () =>
            {
                try
                {
                    await using var db1 = new VehicleManagementDbContext(options);
                    await using DbConnection lockConn1 = db1.Database.GetDbConnection();
                    await lockConn1.OpenAsync();
                    await using var sqlLock = new SqlDistributedLock(lockConn1, "vehicle", vehicleId);
                    if (!await sqlLock.TryAcquireAsync())
                    {
                        Console.WriteLine("[Task1] Could not acquire distributed lock.");
                        return;
                    }
                    Vehicle v1 = await db1.Vehicles.FirstAsync(v => v.Id == vehicleId);
                    v1.Mileage += 100;
                    semaphore.Release();
                    await semaphore.WaitAsync();
                    await Task.Delay(500); // Simulate work
                    db1.Vehicles.Update(v1);
                    await db1.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    Console.WriteLine("[Task1] DbUpdateConcurrencyException: Concurrency conflict detected during update.");
                }
            });

            var task2 = Task.Run(async () =>
            {
                try
                {
                    await using var db2 = new VehicleManagementDbContext(options);
                    await using DbConnection lockConn2 = db2.Database.GetDbConnection();
                    await lockConn2.OpenAsync();
                    await using var sqlLock = new SqlDistributedLock(lockConn2, "vehicle", vehicleId);
                    if (!await sqlLock.TryAcquireAsync())
                    {
                        Console.WriteLine("[Task2] Could not acquire distributed lock.");
                        return;
                    }
                    Vehicle v2 = await db2.Vehicles.FirstAsync(v => v.Id == vehicleId);
                    v2.Mileage += 200;
                    semaphore.Release();
                    await semaphore.WaitAsync();
                    await Task.Delay(1000); // Simulate longer work
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
}
