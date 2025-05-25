namespace SQLock;

public interface ISqlDistributedLockFactory
{
    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock" /> for the specified <paramref name="entityName" /> and
    /// <paramref name="id" />
    /// </summary>
    SqlDistributedLock TakeLock(string entityName, long id);

    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock" /> for the specified <paramref name="key" />
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    SqlDistributedLock TakeLock(string key);
}

public sealed class SqlDistributedLockFactory(string connectionString) : ISqlDistributedLockFactory
{
    public SqlDistributedLock TakeLock(string entityName, long id) => new(connectionString, $"{entityName}:{id}");

    public SqlDistributedLock TakeLock(string lockName) => new(connectionString, lockName);
}