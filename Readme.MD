#### This project
Is a .NET 8.0 library and demo for distributed locking using SQL Server.

#### Key Features
- Works with SQL Server (via Microsoft.Data.SqlClient)
- Uses sp_getapplock for distributed, session-level locks
- Simple, familiar API: AcquireAsync (throws on failure) and TryAcquireAsync (returns bool)
- Lock key is based on entity name and ID
- Automatic lock release on disposal
- Supports connection string or DbConnection

#### Projects
- **SQLock**: Library for distributed SQL-based locks
- **SQLockDemo**: Console demo for lock usage and concurrency scenarios
- **Data**: Entity Framework Core data project

#### How It Works
The library uses the sp_getapplock stored procedure to acquire exclusive locks in SQL Server. Each lock is identified by a key (entity name + ID). Locks are acquired with a timeout (default 30s). If not acquired, AcquireAsync throws and TryAcquireAsync returns false. Locks are released on Dispose/DisposeAsync.

#### Demo Usage
- `--seed` : Seeds the database with 10,000 vehicles if empty
- `--getall` : Lists all vehicles in the database
- `--sim` : Runs 100 race condition simulations (without distributed lock)
- `--simlock` : Runs 100 race condition simulations (with distributed lock)

#### Example
```csharp
await using var sqlLock = new SqlDistributedLock(connectionString, "vehicle", vehicleId);
if (await sqlLock.TryAcquireAsync())
{
    // Do work
}

// Or throw on failure:
await sqlLock.AcquireAsync();
```

#### Prerequisites
- .NET 8.0
- SQL Server instance
- Microsoft.Data.SqlClient NuGet package

#### About
This project demonstrates distributed locking for microservices or multi-process scenarios using only SQL Server. The demo simulates concurrent updates to a Vehicles table, showing the effect of locking.