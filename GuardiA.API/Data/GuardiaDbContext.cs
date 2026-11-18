using Microsoft.EntityFrameworkCore;
using GuardiA.API.Models;

namespace GuardiA.API.Data;

public class GuardiaDbContext : DbContext
{
    public GuardiaDbContext(DbContextOptions<GuardiaDbContext> options) : base(options) { }

    public DbSet<UserMachineAccess> UserMachineAccesses => Set<UserMachineAccess>();
}