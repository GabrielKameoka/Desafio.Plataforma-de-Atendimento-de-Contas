using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<DemoUser> Users => Set<DemoUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemoUser>(entity =>
        {
            entity.ToTable("demo_users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).ValueGeneratedNever();
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(user => user.IsActive).IsRequired().HasDefaultValue(true);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("demo_clients");
            entity.HasKey(client => client.Id);
            entity.Property(client => client.Id).ValueGeneratedNever();
            entity.Property(client => client.DisplayName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ServiceRequest>(entity =>
        {
            entity.ToTable("service_requests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).ValueGeneratedNever();
            entity.Property(request => request.Protocol).HasMaxLength(40).IsRequired();
            entity.HasIndex(request => request.Protocol).IsUnique();
            entity.Property(request => request.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(request => request.Description).HasMaxLength(4000).IsRequired();
            entity.Property(request => request.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(request => request.OpenedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(request => request.DeadlineAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(request => request.Version).IsConcurrencyToken();
            entity.HasOne(request => request.Client).WithMany()
                .HasForeignKey(request => request.ClientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(request => request.Assignee).WithMany()
                .HasForeignKey(request => request.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(request => new { request.Status, request.DeadlineAtUtc });
            entity.HasIndex(request => request.AssigneeId);
            entity.Navigation(request => request.Interactions).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(request => request.AuditEvents).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Interaction>(entity =>
        {
            entity.ToTable("interactions");
            entity.HasKey(interaction => interaction.Id);
            entity.Property(interaction => interaction.Id).ValueGeneratedNever();
            entity.Property(interaction => interaction.Text).HasMaxLength(4000).IsRequired();
            entity.Property(interaction => interaction.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne(interaction => interaction.Request).WithMany(request => request.Interactions)
                .HasForeignKey(interaction => interaction.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(interaction => interaction.Author).WithMany()
                .HasForeignKey(interaction => interaction.AuthorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(interaction => new { interaction.RequestId, interaction.CreatedAtUtc, interaction.Id });
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Id).ValueGeneratedNever();
            entity.Property(audit => audit.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(audit => audit.PreviousValue).HasMaxLength(1000);
            entity.Property(audit => audit.NewValue).HasMaxLength(1000);
            entity.Property(audit => audit.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(audit => audit.OccurredAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne(audit => audit.Request).WithMany(request => request.AuditEvents)
                .HasForeignKey(audit => audit.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(audit => audit.Author).WithMany()
                .HasForeignKey(audit => audit.AuthorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(audit => new { audit.RequestId, audit.OccurredAtUtc, audit.Id });
            entity.HasIndex(audit => new { audit.RequestId, audit.ResultingVersion }).IsUnique();
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(record => record.Key);
            entity.Property(record => record.Key).HasMaxLength(120);
            entity.Property(record => record.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(record => record.Protocol).HasMaxLength(40).IsRequired();
            entity.Property(record => record.ResponseJson).HasColumnType("jsonb").IsRequired();
            entity.Property(record => record.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<ServiceRequest>().WithOne()
                .HasForeignKey<IdempotencyRecord>(record => record.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(record => record.RequestId).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(message => message.EventId);
            entity.Property(message => message.EventId).ValueGeneratedNever();
            entity.Property(message => message.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(message => message.EventType).HasMaxLength(40).IsRequired();
            entity.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(message => message.LastError).HasMaxLength(500);
            entity.Property(message => message.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(message => message.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(message => message.LastAttemptAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(message => message.SyncedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<ServiceRequest>().WithMany()
                .HasForeignKey(message => message.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(message => new { message.Status, message.NextAttemptAtUtc });
            entity.HasIndex(message => new { message.RequestId, message.RequestVersion }).IsUnique();
        });
    }
}
