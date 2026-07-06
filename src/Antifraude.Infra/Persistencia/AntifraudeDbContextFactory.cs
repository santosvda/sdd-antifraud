using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Fábrica de design-time para o EF CLI (<c>dotnet ef migrations</c>) — o Infra é uma
/// classlib sem host próprio. Usa uma <see cref="ServerVersion"/> fixa para não precisar
/// de um banco vivo ao gerar migrations.
/// </summary>
public sealed class AntifraudeDbContextFactory : IDesignTimeDbContextFactory<AntifraudeDbContext>
{
    public AntifraudeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
            ?? "server=localhost;port=3306;database=antifraude;user=root;password=root";

        var options = new DbContextOptionsBuilder<AntifraudeDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;

        return new AntifraudeDbContext(options);
    }
}
