namespace CardDemo.Domain.Common;

/// <summary>
/// Marks an entity that carries an optimistic-concurrency version, per the
/// safe-target persistence design (09-DotNet-Target-Architecture.md#concurrency).
/// The infrastructure layer increments <see cref="RowVersion"/> on every update
/// and configures it as the EF Core concurrency token.
/// </summary>
public interface IVersioned
{
    long RowVersion { get; set; }
}
