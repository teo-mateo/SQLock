using Data;
using Microsoft.Extensions.Logging;
using SQLock;

namespace SQLockDemo.Services.DemoRunner;

public abstract class SqlockTestBase : ISqlockTest
{
    protected readonly ISqlDistributedLockFactory LockFactory;
    protected readonly VehicleManagementDbContext DbContext;
    protected readonly ILogger Logger;

    protected SqlockTestBase(
        ISqlDistributedLockFactory lockFactory,
        VehicleManagementDbContext dbContext,
        ILoggerFactory loggerFactory)
    {
        LockFactory = lockFactory;
        DbContext = dbContext;
        Logger = loggerFactory.CreateLogger(GetType());
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Task RunAsync();
}