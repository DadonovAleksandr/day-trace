using DayTrace.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Data;

public class DayTraceDbContext : DbContext
{
    public DayTraceDbContext(DbContextOptions<DayTraceDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UsersSettings => Set<UserSettings>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.TelegramUserId).HasColumnName("telegram_user_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.HasIndex(e => e.TelegramUserId).IsUnique();
        });

        // UserSettings
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("users_settings");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(100).HasDefaultValue("UTC");
            entity.Property(e => e.ReminderTime).HasColumnName("reminder_time");
            entity.Property(e => e.ReminderEnabled).HasColumnName("reminder_enabled").HasDefaultValue(true);
            entity.Property(e => e.WeekEnd).HasColumnName("week_end").HasMaxLength(20).HasDefaultValue("Sunday");

            entity.HasOne(e => e.User)
                  .WithOne(u => u.Settings)
                  .HasForeignKey<UserSettings>(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Events
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
            entity.Property(e => e.LocalDate).HasColumnName("local_date").IsRequired();
            entity.Property(e => e.Importance).HasColumnName("importance").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.LocalDate });

            entity.ToTable(t => t.HasCheckConstraint("CK_events_importance", "importance >= 1 AND importance <= 5"));
        });
    }
}
