using LifeAlertPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeAlertPlus.Infrastructure.Context
{
    // DbContext-ul principal EF Core — mapează toate entitățile Domain pe tabelele din PostgreSQL
    // și configurează relațiile (FK), indecșii și comportamentul de ștergere (cascade/restrict)
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

            // Chei primare explicite (majoritatea entităților folosesc Guid ca PK)
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique(); // Email unic — folosit la autentificare
            modelBuilder.Entity<User>().HasIndex(u => u.PhoneNumber);     // Index pentru căutare rapidă după telefon
            modelBuilder.Entity<Monitored>().HasKey(m => m.Id);
            modelBuilder.Entity<Measurement>().HasKey(m => m.Id);
            modelBuilder.Entity<Notification>().HasKey(n => n.Id);
            modelBuilder.Entity<DailyHistory>().HasKey(d => d.Id);
            modelBuilder.Entity<WeeklyHistory>().HasKey(w => w.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);

            // UserMonitored: tabelă de legătură many-to-many (cheie compusă User+Monitored)
            // Restrict (nu Cascade) — ștergerea unui User/Monitored nu trebuie să șteargă automat legătura;
            // logica de business (UserRepository.DeleteUserAsync) gestionează explicit ordinea ștergerilor
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

            // Measurement: Cascade — ștergerea unui pacient șterge automat toate măsurătorile lui
            modelBuilder.Entity<Measurement>()
                .HasOne(m => m.Monitored)
                .WithMany()
                .HasForeignKey(m => m.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Measurement>()
                .HasIndex(m => m.IdMonitored); // Index pentru filtrarea măsurătorilor per pacient

            modelBuilder.Entity<Measurement>()
                .HasIndex(m => m.CreatedAt); // Index pentru sortare/filtrare cronologică (paginare, retenție)

            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.IdMonitored);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.Monitored)
                .WithMany(m => m.Notifications)
                .HasForeignKey(n => n.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade); // Ștergerea pacientului șterge și notificările lui

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.IdUser)
                .IsRequired(false) // Notificarea poate exista fără un User asociat (ex: notificări de sistem)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.IdUser);

            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.IdUser, n.IsRead }); // Index compus — listarea notificărilor necitite per utilizator

            modelBuilder.Entity<DailyHistory>()
                .HasIndex(d => d.IdMonitored);

            modelBuilder.Entity<WeeklyHistory>()
                .HasIndex(w => w.IdMonitored);

            // Invitation: token unic (hash SHA-256) + index compus pentru căutarea invitațiilor existente per medic/pacient
            modelBuilder.Entity<Invitation>().HasKey(i => i.Id);
            modelBuilder.Entity<Invitation>().HasIndex(i => i.Token).IsUnique();
            modelBuilder.Entity<Invitation>().HasIndex(i => new { i.DoctorEmail, i.PatientId });

            // ActivityProfile: cheie compusă (Pacient, OraZilei) — o singură înregistrare per oră per pacient
            modelBuilder.Entity<ActivityProfile>().HasKey(ap => new { ap.IdMonitored, ap.HourOfDay });
            modelBuilder.Entity<ActivityProfile>()
                .HasOne(ap => ap.Monitored)
                .WithMany()
                .HasForeignKey(ap => ap.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ActivityProfile>()
                .HasIndex(ap => ap.IdMonitored);

            // MonitoredCondition: index unic (Pacient, ConditionKey) — previne afecțiuni duplicate pentru același pacient
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

            // WifiNetwork: rețelele salvate pentru ESP32 — NOTĂ: parola e stocată ca text simplu (fără criptare)
            modelBuilder.Entity<WifiNetwork>().HasKey(w => w.Id);
            modelBuilder.Entity<WifiNetwork>()
                .HasOne(w => w.Monitored)
                .WithMany()
                .HasForeignKey(w => w.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WifiNetwork>()
                .HasIndex(w => w.IdMonitored);

            // AuditLog: index pe Timestamp pentru interogări cronologice (Admin — istoric acțiuni)
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.Timestamp);

            // SystemError: index pe Timestamp pentru interogări cronologice (Admin — depanare erori)
            modelBuilder.Entity<SystemError>().HasKey(e => e.Id);
            modelBuilder.Entity<SystemError>()
                .HasIndex(e => e.Timestamp);

            // DoctorNote: notițele lăsate de medic pentru un pacient, șterse în cascadă cu pacientul
            modelBuilder.Entity<DoctorNote>().HasKey(n => n.Id);
            modelBuilder.Entity<DoctorNote>()
                .HasOne(n => n.Monitored)
                .WithMany()
                .HasForeignKey(n => n.IdMonitored)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DoctorNote>()
                .HasIndex(n => n.IdMonitored);

            // PushSubscription: abonamentele Web Push (VAPID) ale browserelor utilizatorilor
            // Endpoint unic — un browser nu poate avea două abonamente identice
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
