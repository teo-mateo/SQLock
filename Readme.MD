# SQLock - SQL Server Distributed Locking Library

## Overview
SQLock is a .NET 8.0 library that provides distributed locking capabilities using SQL Server. It enables coordination between multiple processes, services, or application instances by leveraging SQL Server's `sp_getapplock` stored procedure to ensure exclusive access to shared resources.

## Key Features
- **SQL Server Integration**: Works seamlessly with SQL Server via Microsoft.Data.SqlClient
- **Session-Level Locks**: Uses `sp_getapplock` for distributed, session-level locks
- **Simple API**: Familiar async/await pattern with `TakeAsync` (throws on failure) and `TryTakeAsync` (returns bool)
- **Automatic Cleanup**: Locks are automatically released on disposal
- **Cancellation Support**: Honors cancellation tokens for responsive application behavior
- **Timeout Control**: Configurable timeouts for lock acquisition attempts
- **Connection Flexibility**: Supports connection string or DbConnection

## Projects
- **SQLock**: Core library implementing the distributed locking mechanism
- **SQLockDemo**: Console application demonstrating lock usage and testing various scenarios
- **Data**: Entity Framework Core data project for the demo application

## How It Works
The library uses SQL Server's `sp_getapplock` stored procedure to acquire exclusive locks. Each lock is identified by a unique resource name. When a lock is requested:

1. A connection to SQL Server is established
2. The lock is requested with a specified timeout
3. If the lock is acquired, operations can proceed safely
4. If the lock cannot be acquired within the timeout, either an exception is thrown (`TakeAsync`) or `false` is returned (`TryTakeAsync`)
5. Locks are automatically released when the lock object is disposed

## Demo Application
The SQLockDemo project demonstrates the library's functionality and includes comprehensive tests to verify correct behavior.

### Command Line Parameters
- `--seed` : Seeds the database with 10,000 vehicles if empty
- `--getall` : Lists all vehicles in the database
- `--sim` : Runs 100 race condition simulations (without distributed lock)
- `--simlock` : Runs 100 race condition simulations (with distributed lock)
- `--demo [test-name]` : Runs all tests or a specific test if a name is provided

### Available Tests
The demo includes the following test scenarios:

1. **Single-Thread-Happy-Path**: Verifies basic lock mechanics - acquisition, verification in `sys.dm_tran_locks`, and proper release.
2. **Mutual-Exclusion-Thread**: Demonstrates that only one thread can hold a lock at a time, with others waiting for release.
3. **Timeout-Respected**: Confirms that lock acquisition respects the specified timeout and returns appropriately when a lock cannot be acquired.
4. **Inter-Process-Mutual-Exclusion**: Shows locks working across different processes, not just threads within the same process.
5. **Cancellation-Token-Honoured**: Verifies that operations abort quickly when a cancellation token is triggered, with no lingering locks.

To run a specific test:
```
dotnet run --project SQLockDemo -- --demo "Single-Thread-Happy-Path"
```

To run all tests:
```
dotnet run --project SQLockDemo -- --demo
```

## Usage Examples

### Basic Usage
```csharp
// Create a lock for a specific resource
await using var sqlLock = lockFactory.CreateLock("vehicle_123");

// Acquire the lock (throws if not acquired within default timeout)
await sqlLock.TakeAsync();

// Perform exclusive operations...

// Lock is automatically released when disposed
```

### With Timeout
```csharp
await using var sqlLock = lockFactory.CreateLock("vehicle_123");

// Try to acquire with a 5-second timeout
if (await sqlLock.TryTakeAsync(timeoutMs: 5000))
{
    // Lock acquired, perform operations...
}
else
{
    // Could not acquire lock within timeout
}
```

### With Cancellation
```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
await using var sqlLock = lockFactory.CreateLock("vehicle_123");

try
{
    // Will throw OperationCanceledException if token is canceled
    await sqlLock.TakeAsync(cancellationToken: cts.Token);
    
    // Perform operations...
}
catch (OperationCanceledException)
{
    // Handle cancellation
}
```

## Prerequisites
- .NET 8.0
- SQL Server instance
- Microsoft.Data.SqlClient NuGet package

## About
This project demonstrates a robust solution for distributed locking in microservices architectures or any multi-process scenarios using SQL Server as the coordination mechanism. The comprehensive test suite ensures reliability across various edge cases and scenarios.

## Future Work / TODOs
The following test scenarios and features are planned for future implementation:

| # | Scenario | Description | Test Type | Implementation Details |
|---|----------|-------------|-----------|------------------------|
| 6 | 🔵 Re-entrant acquisition | Same connection returns result 1 (already owned). | Unit (mock ADO) or integration | Call AcquireAsync twice on one instance; expect no exception and only one lock row in DMV. |
| 7 | 🔵 Disposal always frees connection | No open sessions after GC when lock never acquired. | Integration | TryAcquireAsync → false, forget to dispose; run GC/Wait, assert session count unchanged. |
| 8 | Exception pathway | Any error inside AcquireLockInternal still disposes/ closes connection. | Integration | Tamper @LockTimeout to an impossible negative → expect SqlException; afterwards DMV shows no open session; connection-pool count unchanged. |
| 9 | Factory resolves from DI | DI wiring produces a usable lock. | Unit + Integration | Build a minimal Host with AddSqlDistributedLockFactory<TContext>(); resolve factory; create and acquire lock. |
| 10 | Throughput/perf smoke | Lock adds minimal overhead under contention. | Benchmark (BenchmarkDotNet) | Measure median latency for 1 → 32 concurrent callers; ensure it matches expectations (< N ms). |
| 11 | Crash resilience | Lock disappears when process dies abruptly. | Manual / scripted | Start app, acquire lock, kill -9 PID; check DMV until lock row vanishes (SQL detects broken SPID). |