using CardDemo.Application.Abstractions;
using CardDemo.Infrastructure.Batch;
using CardDemo.Infrastructure.Fixtures;
using CardDemo.Infrastructure.Optional;
using CardDemo.Infrastructure.Persistence;
using CardDemo.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardDemo.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite-backed persistence, the store port, password hashing,
    /// the fixture seeder and the database/batch runners.
    /// </summary>
    public static IServiceCollection AddCardDemoInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CardDemoDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<ICardDemoStore, CardDemoStore>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<FixtureSeeder>();
        services.AddScoped<IDatabaseManager, DatabaseManager>();
        services.AddScoped<IBatchRunner, BatchRunner>();

        // Optional modules (authorization, statements, branch transfer,
        // transaction-type maintenance, inquiry/date services).
        services.AddScoped<ITransactionTypeService, TransactionTypeService>();
        services.AddScoped<IStatementService, StatementService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IInquiryService, InquiryService>();

        return services;
    }
}
