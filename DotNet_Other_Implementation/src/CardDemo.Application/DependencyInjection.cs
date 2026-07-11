using CardDemo.Application.Abstractions;
using CardDemo.Application.Services;
using CardDemo.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CardDemo.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the online application services and pure domain engines.</summary>
    public static IServiceCollection AddCardDemoApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICardService, CardService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IBillPayService, BillPayService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IReportRequestService, ReportRequestService>();

        // Pure domain engines (stateless, safe to share).
        services.AddSingleton<PostingEngine>();
        services.AddSingleton<InterestEngine>();

        return services;
    }
}
