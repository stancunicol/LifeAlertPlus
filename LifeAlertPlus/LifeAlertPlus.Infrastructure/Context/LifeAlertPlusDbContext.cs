using Microsoft.EntityFrameworkCore;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Infrastructure.Context
{
    public class LifeAlertPlusDbContext : DbContext
    {
        public LifeAlertPlusDbContext(DbContextOptions<LifeAlertPlusDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Monitored> Monitoreds { get; set; }
        public DbSet<Measurement> Measurements { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<DailyHistory> DailyHistories { get; set; }
        public DbSet<WeeklyHistory> WeeklyHistories { get; set; }
        public DbSet<UserMonitored> UserMonitoreds { get; set; }
        public DbSet<Role> Roles { get; set; }

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

            modelBuilder.Entity<DailyHistory>()
                .HasIndex(d => d.IdMonitored);

            modelBuilder.Entity<WeeklyHistory>()
                .HasIndex(w => w.IdMonitored);
        }
    }
}
