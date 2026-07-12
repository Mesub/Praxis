namespace Praxis.Shared;

/// Audit fields are never set by hand — the SaveChanges interceptor stamps them (M0.4).
/// If you find yourself assigning CreatedAt in a handler, the interceptor is not wired up.
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    /// Optimistic concurrency. Maps to PostgreSQL's xmin system column.
    public uint Version { get; set; }
}

/// Phase 0 is single-tenant, but the shape is here so that Institution/Enterprise tenancy
/// (CANON §5.8) is an interceptor change rather than a migration of every table.
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
}
