using Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SQLockDemo.Services
{
    public interface IVehiclesService
    {
        Task SeedAsync();
        Task<int> CountAsync();
        Task<List<Vehicle>> GetAllAsync();
        Task SimulateRaceConditionAsync(long vehicleId);
        Task SimulateRaceConditionWithDistributedLockAsync(long vehicleId);
    }
}
