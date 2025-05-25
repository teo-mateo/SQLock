namespace SQLock;

public interface ISqlDistributedLockFactory
{
    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock" /> for the specified <paramref name="entityName" /> and immediately tries to take it
    /// <paramref name="id" />
    /// </summary>
    Task<SqlDistributedLock> CreateLockAndTake(string entityName, long id);

    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock" /> for the specified <paramref name="key"/> and immediately tries to take it
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    Task<SqlDistributedLock> CreateLockAndTake(string key);

    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock" /> for the specified <paramref name="key" />
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    SqlDistributedLock CreateLock(string key);
}

public sealed class SqlDistributedLockFactory(string connectionString) : ISqlDistributedLockFactory
{
    public async Task<SqlDistributedLock> CreateLockAndTake(string entityName, long id)
    {
        var lck = new SqlDistributedLock(connectionString, $"{entityName}:{id}");
        await lck.TakeAsync();
        return lck;
    }

    public async Task<SqlDistributedLock> CreateLockAndTake(string lockName)
    {
        var lck = new SqlDistributedLock(connectionString, lockName);
        await lck.TakeAsync();
        return lck;
    }

    public SqlDistributedLock CreateLock(string key) => new(connectionString, key);
}