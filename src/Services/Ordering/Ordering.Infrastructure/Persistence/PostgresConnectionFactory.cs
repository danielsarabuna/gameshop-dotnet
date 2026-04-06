using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ordering.Infrastructure.Persistence;

public static class PostgresConnectionFactory
{
    public static string GetConnectionString(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Ordering:Postgres").Get<PostgresOptions>() ?? new PostgresOptions();
        var defaultConnection = environment.IsEnvironment("Docker")
            ? "Host=postgres;Port=5432;Username=webshop;Password=webshop;Database=ordering"
            : "Host=localhost;Port=5432;Username=webshop;Password=webshop;Database=ordering";

        return string.IsNullOrWhiteSpace(options.ConnectionString)
            ? defaultConnection
            : options.ConnectionString;
    }
}
