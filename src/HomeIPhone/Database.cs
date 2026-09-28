using Microsoft.EntityFrameworkCore;

namespace HomeIPhone;

public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
{
    public DbSet<Phone> Phones => Set<Phone>();
    public DbSet<DiscoveryCandidate> Discovered => Set<DiscoveryCandidate>();
    public DbSet<TftpEvent> TftpEvents => Set<TftpEvent>();
    public DbSet<PhoneEvent> Events => Set<PhoneEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Phone>().OwnsOne(p => p.Configuration);
        model.Entity<TftpEvent>().HasIndex(e => new { e.MacAddress, e.TimestampUtc });
        model.Entity<PhoneEvent>().HasIndex(e => new { e.MacAddress, e.TimestampUtc });
    }
}

// All read/modify/write operations share this gate; contexts never outlive an operation.
public sealed class DatabaseGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
