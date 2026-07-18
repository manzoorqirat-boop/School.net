using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using QMSoft.Api.Infrastructure.Crypto;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Data;

/// <summary>
/// Lets `dotnet ef migrations add / database update` construct the context
/// WITHOUT booting Program.cs — which (by design) refuses to start without
/// JWT_SECRET / ENCRYPTION_KEY and would make every design-time command fail.
///
/// The tenant is unfiltered (migrations see no rows anyway) and the crypto
/// service runs key-less (ValueConverters are never invoked at design time —
/// scaffolding only reads the model shape).
///
/// Connection string: DATABASE_URL env var if set (for `database update`
/// against Railway), else a localhost placeholder — `migrations add` never
/// connects at all.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("DATABASE_URL")
                   ?? "Host=localhost;Username=postgres;Database=school_design";

        // Same URI→keyword tolerance as Program.cs, minimal version.
        if (conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(conn);
            var ui = uri.UserInfo.Split(':', 2);
            conn = $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};" +
                   $"Username={Uri.UnescapeDataString(ui[0])};" +
                   (ui.Length > 1 ? $"Password={Uri.UnescapeDataString(ui[1])};" : "") +
                   $"Database={uri.AbsolutePath.TrimStart('/')}";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            // MUST match Program.cs — the migration scaffolder reads THIS
            // context's model; without it, `migrations add` emits PascalCase
            // columns and every raw-SQL constraint breaks again.
            .UseSnakeCaseNamingConvention()
            .Options;

        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var env = new DesignTimeEnvironment();
        var crypto = new CryptoService(cfg, env, NullLogger<CryptoService>.Instance);

        return new AppDbContext(options, FixedTenantContext.Unfiltered(), crypto);
    }

    private sealed class DesignTimeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "QMSoft.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
