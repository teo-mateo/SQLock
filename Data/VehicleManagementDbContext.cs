using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Data
{
    public class Vehicle
    {
        public long Id { get; set; }
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string LicensePlate { get; set; } = string.Empty;
        public int Mileage { get; set; }
        [Timestamp]
        public byte[] Timestamp { get; set; } = null!;
    }

    public class VehicleManagementDbContext : DbContext
    {
        public VehicleManagementDbContext(DbContextOptions<VehicleManagementDbContext> options)
            : base(options)
        {
        }

        public DbSet<Vehicle> Vehicles { get; set; } = null!;
    }
}
