using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wordset.Domain.Interfaces;
using Wordset.Infrastructure.Data;
using Wordset.Infrastructure.Repositories;

namespace Wordset.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<WordsetDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IWordsetRepository, WordsetRepository>();
        return services;
    }
}
