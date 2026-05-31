using LifeAlertPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Context
{
    public class LifeAlertPlusDbContext : DbContext
    {
        public LifeAlertPlusDbContext(DbContextOptions<LifeAlertPlusDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = default!;
        public DbSet<Monitored> Monitoreds { get; set; } = default!;
        public DbSet<Measurement> Measurements { get; set; } = default!;
        public DbSet<Notification> Notifications { get; set; } = default!;
        public DbSet<DailyHistory> DailyHistories { get; set; } = default!;
        public DbSet<WeeklyHistory> WeeklyHistories { get; set; } = default!;
        public DbSet<UserMonitored> UserMonitoreds { get; set; } = default!;
        public DbSet<Role> Roles { get; set; } = default!;
        public DbSet<Invitation> Invitations { get; set; } = default!;
        public DbSet<ActivityProfile> ActivityProfiles { get; set; } = default!;
        public DbSet<MonitoredCondition> MonitoredConditions { get; set; } = default!;
        public DbSet<WifiNetwork> WifiNetworks { get; set; } = default!;
        public DbSet<AuditLog> AuditLogs { get; set; } = default!;
        public DbSet<SystemError> SystemErrors { get; set; } = default!;
        public DbSet<DoctorNote> DoctorNotes { get; set; } = default!;
        public DbSet<PushSubscription> PushSubscriptions { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Monitored>().HasKey(m => m.Id);
            modelBuilder.Entity<Measurement>().HasKey(m => m.Id);
            modelBuilder.Entity<Notification>().HasKey(n => n.Id);
            modelBuilder.Entity<DailyHistory>().HasKey(d => d.Id);
            modelBuilder.Entity<WeeklyHistory>().HasKey(w => w.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);

            modelBuilder.Entity<UserMonitored>().HasKey(um => new { um.IdUser, um.IdMonitored });
            modelBuilder.Entity<UserMonitored>()
                .HasOne(um => um.User)
                .WithMany()
                .HasForeignKey(um => um.IdUser)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserMonitored>()
                .HasOne(um => um.Monitored)
                .WithMany()
                .HasForeignKey(um => um.IdMonitored)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Measurement>()
                .HasOne(m => m.Monitored)
                .WithMany()
                .HasForeignKey(m => m.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Measurement>()
                .HasIndex(m => m.IdMonitored);

            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.IdMonitored);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.Monitored)
                .WithMany(m => m.Notifications)
                .HasForeignKey(n => n.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.IdUser)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.IdUser);

            modelBuilder.Entity<DailyHistory>()
                .HasIndex(d => d.IdMonitored);

            modelBuilder.Entity<WeeklyHistory>()
                .HasIndex(w => w.IdMonitored);

            modelBuilder.Entity<Invitation>().HasKey(i => i.Id);
            modelBuilder.Entity<Invitation>().HasIndex(i => i.Token).IsUnique();
            modelBuilder.Entity<Invitation>().HasIndex(i => new { i.DoctorEmail, i.PatientId });

            modelBuilder.Entity<ActivityProfile>().HasKey(ap => new { ap.IdMonitored, ap.HourOfDay });
            modelBuilder.Entity<ActivityProfile>()
                .HasOne(ap => ap.Monitored)
                .WithMany()
                .HasForeignKey(ap => ap.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ActivityProfile>()
                .HasIndex(ap => ap.IdMonitored);

            modelBuilder.Entity<MonitoredCondition>().HasKey(c => c.Id);
            modelBuilder.Entity<MonitoredCondition>()
                .HasOne(c => c.Monitored)
                .WithMany()
                .HasForeignKey(c => c.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<MonitoredCondition>()
                .HasIndex(c => c.IdMonitored);
            modelBuilder.Entity<MonitoredCondition>()
                .HasIndex(c => new { c.IdMonitored, c.ConditionKey })
                .IsUnique();

            modelBuilder.Entity<WifiNetwork>().HasKey(w => w.Id);
            modelBuilder.Entity<WifiNetwork>()
                .HasOne(w => w.Monitored)
                .WithMany()
                .HasForeignKey(w => w.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WifiNetwork>()
                .HasIndex(w => w.IdMonitored);

            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.Timestamp);

            modelBuilder.Entity<SystemError>().HasKey(e => e.Id);
            modelBuilder.Entity<SystemError>()
                .HasIndex(e => e.Timestamp);

            modelBuilder.Entity<DoctorNote>().HasKey(n => n.Id);
            modelBuilder.Entity<DoctorNote>()
                .HasOne(n => n.Monitored)
                .WithMany()
                .HasForeignKey(n => n.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DoctorNote>()
                .HasIndex(n => n.IdMonitored);

            modelBuilder.Entity<PushSubscription>().HasKey(p => p.Id);
            modelBuilder.Entity<PushSubscription>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PushSubscription>()
                .HasIndex(p => p.UserId);
            modelBuilder.Entity<PushSubscription>()
                .HasIndex(p => p.Endpoint)
                .IsUnique();
        }
    }
}
