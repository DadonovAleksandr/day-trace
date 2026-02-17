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
    public DbSet<Summary> Summaries => Set<Summary>();
    public DbSet<PeriodJob> PeriodJobs => Set<PeriodJob>();
    public DbSet<PeriodRunCounter> PeriodRunCounters => Set<PeriodRunCounter>();
    public DbSet<WeekScheduleHistory> WeekScheduleHistory => Set<WeekScheduleHistory>();
    public DbSet<TimezoneHistory> TimezoneHistory => Set<TimezoneHistory>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<PromptDelivery> PromptDeliveries => Set<PromptDelivery>();

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

        // Summaries
        modelBuilder.Entity<Summary>(entity =>
        {
            entity.ToTable("summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PeriodType).HasColumnName("period_type").HasMaxLength(20);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("generating");
            entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("jsonb");
            entity.Property(e => e.SourceEventIds).HasColumnName("source_event_ids");
            entity.Property(e => e.LastGeneratedAt).HasColumnName("last_generated_at");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.PeriodType, e.PeriodStart, e.PeriodEnd }).IsUnique();
        });

        // PeriodJobs
        modelBuilder.Entity<PeriodJob>(entity =>
        {
            entity.ToTable("period_jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PeriodType).HasColumnName("period_type").HasMaxLength(20);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.RunNumber).HasColumnName("run_number");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            entity.Property(e => e.LeaseId).HasColumnName("lease_id");
            entity.Property(e => e.TargetSummaryVersion).HasColumnName("target_summary_version");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.ReconciledAt).HasColumnName("reconciled_at");
            entity.Property(e => e.RecoverySource).HasColumnName("recovery_source").HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
        });

        // PeriodRunCounters
        modelBuilder.Entity<PeriodRunCounter>(entity =>
        {
            entity.ToTable("period_run_counters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PeriodType).HasColumnName("period_type").HasMaxLength(20);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.LastRunNumber).HasColumnName("last_run_number").HasDefaultValue(1);

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.PeriodType, e.PeriodStart, e.PeriodEnd }).IsUnique();
        });

        // WeekScheduleHistory
        modelBuilder.Entity<WeekScheduleHistory>(entity =>
        {
            entity.ToTable("week_schedule_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WeekEnd).HasColumnName("week_end").HasMaxLength(20);
            entity.Property(e => e.EffectiveFromLocalDate).HasColumnName("effective_from_local_date");
            entity.Property(e => e.TransitionStart).HasColumnName("transition_start");
            entity.Property(e => e.TransitionEnd).HasColumnName("transition_end");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.EffectiveFromLocalDate }).IsUnique();
        });

        // TimezoneHistory
        modelBuilder.Entity<TimezoneHistory>(entity =>
        {
            entity.ToTable("timezone_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(100).IsRequired();
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // DeliveryAttempts
        modelBuilder.Entity<DeliveryAttempt>(entity =>
        {
            entity.ToTable("delivery_attempts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DeliveryType).HasColumnName("delivery_type").HasMaxLength(50);
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.AttemptNumber).HasColumnName("attempt_number").HasDefaultValue(1);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.TelegramMessageId).HasColumnName("telegram_message_id");
            entity.Property(e => e.ScheduledAt).HasColumnName("scheduled_at");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.DeliveryType, e.ScheduledAt });
            entity.HasIndex(e => e.Status)
                  .HasFilter("status IN ('pending', 'failed')")
                  .HasDatabaseName("IX_delivery_attempts_status_partial");
        });

        // PromptDeliveries
        modelBuilder.Entity<PromptDelivery>(entity =>
        {
            entity.ToTable("prompt_deliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.PromptId).HasColumnName("prompt_id").HasMaxLength(200);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PeriodType).HasColumnName("period_type").HasMaxLength(20);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);

            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PromptId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.PeriodType, e.PeriodStart, e.PeriodEnd, e.SentAt }).IsUnique();
        });
    }
}
