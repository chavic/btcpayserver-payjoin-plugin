using BTCPayServer.Plugins.Payjoin.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Payjoin;

public class PayjoinPluginDbContext : DbContext
{
    private readonly bool _designTime;

    public PayjoinPluginDbContext(DbContextOptions<PayjoinPluginDbContext> options, bool designTime = false)
        : base(options)
    {
        _designTime = designTime;
    }

    public DbSet<PluginData> PluginRecords { get; set; } = null!;

    public DbSet<PayjoinReceiverSessionData> ReceiverSessions { get; set; } = null!;

    public DbSet<PayjoinReceiverSessionEventData> ReceiverSessionEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.Plugins.Payjoin");
        modelBuilder.Entity<PluginData>()
            .ToTable("PluginRecords");
        modelBuilder.Entity<PayjoinReceiverSessionData>(entity =>
        {
            entity.ToTable("ReceiverSessions");
            entity.HasKey(x => x.InvoiceId);
            entity.Property(x => x.InvoiceId).HasColumnType("text");
            entity.Property(x => x.StoreId).HasColumnType("text");
            entity.Property(x => x.ReceiverAddress).HasColumnType("text");
            entity.Property(x => x.OhttpRelayUrl).HasColumnType("text");
            entity.HasMany(x => x.Events)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PayjoinReceiverSessionEventData>(entity =>
        {
            entity.ToTable("ReceiverSessionEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Event).HasColumnType("text");
            entity.HasIndex(x => new { x.InvoiceId, x.Sequence }).IsUnique();
        });
    }
}
