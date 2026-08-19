using Api.Infrastructure;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddAxaMotorClaims(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddHostedService<OutboxWorker>();

        return services;
    }
}
