using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class Vehicle
{
    public long Id { get; init; }
    [MaxLength(500)]
    public string Make { get; init; } = string.Empty;
    [MaxLength(500)]
    public string Model { get; init; } = string.Empty;
    public int Year { get; init; }
    [MaxLength(20)]
    public required string LicensePlate { get; init; } = string.Empty;
    public int Mileage { get; set; }

    [Timestamp] public byte[] Timestamp { get; init; } = null!;
}

public class VehicleManagementDbContext(DbContextOptions<VehicleManagementDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles { get; init; } = null!;
}