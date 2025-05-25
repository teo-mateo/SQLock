using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
namespace SQLock;

public interface ISqlDistributedLockFactory
{
    /// <summary>
    /// Creates a new <see cref="SqlDistributedLock"/> for the specified <paramref name="entityName"/> and <paramref name="id"/>
    /// </summary>
    SqlDistributedLock CreateLock(string entityName, long id);
}

public sealed class SqlDistributedLockFactory(string connectionString) : ISqlDistributedLockFactory
{
    public SqlDistributedLock CreateLock(string entityName, long id) =>
        new(connectionString, entityName, id);
}