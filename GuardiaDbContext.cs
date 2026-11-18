using Microsoft.EntityFrameworkCore;
using GuardiAProjeto.Models;

namespace GuardiAProjeto
{
    public class GuardiaDbContext : DbContext
    {
        public GuardiaDbContext(DbContextOptions<GuardiaDbContext> options) : base(options)
        {
        }

        public DbSet<MonitoredTarget> MonitoredTargets => Set<MonitoredTarget>();
        public DbSet<VaultItem> VaultItems => Set<VaultItem>();
        public DbSet<HealthLog> HealthLogs => Set<HealthLog>();
        
        // Nova tabela para gerenciar credenciais e máquinas autorizadas
        public DbSet<UserMachineAccess> UserMachineAccesses => Set<UserMachineAccess>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=guardia.db");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<MonitoredTarget>()
                .HasIndex(t => t.Url);

            modelBuilder.Entity<HealthLog>()
                .HasIndex(h => h.CheckedAt);

            // Criando índices para otimizar as buscas por credencial e ID de máquina
            modelBuilder.Entity<UserMachineAccess>()
                .HasIndex(u => u.CredentialNumber)
                .IsUnique();

            modelBuilder.Entity<UserMachineAccess>()
                .HasIndex(u => u.AuthorizedMachineId);
        }
    }
}