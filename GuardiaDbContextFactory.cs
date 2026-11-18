using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GuardiAProjeto;

public class GuardiaDbContextFactory : IDesignTimeDbContextFactory<GuardiaDbContext>
{
    public GuardiaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GuardiaDbContext>();
        
        // Define o SQLite e o arquivo de banco padrão para o ambiente de design/migrações
        optionsBuilder.UseSqlite("Data Source=guardia.db");

        return new GuardiaDbContext(optionsBuilder.Options);
    }
}