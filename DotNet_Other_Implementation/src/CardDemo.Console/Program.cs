using CardDemo.Application;
using CardDemo.Console;
using CardDemo.Console.Cli;
using CardDemo.Console.Interactive;
using CardDemo.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Anchor configuration (appsettings.json, logging) and bundled fixtures to the
// application directory so commands behave the same regardless of the caller's cwd.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddEnvironmentVariables("CARDDEMO_");

var connectionString = builder.Configuration["Data:ConnectionString"] ?? "Data Source=carddemo.db";
builder.Services.AddCardDemoInfrastructure(connectionString);
builder.Services.AddCardDemoApplication();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InteractiveApp>();

using var host = builder.Build();

using var cts = new CancellationTokenSource();
System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var scope = host.Services.CreateScope();
var router = new CommandRouter(scope.ServiceProvider, builder.Configuration);

try
{
    return await router.RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    System.Console.Error.WriteLine("Cancelled.");
    return ExitCodes.Cancelled;
}
catch (Exception ex)
{
    System.Console.Error.WriteLine($"Fatal: {ex.Message}");
    return ExitCodes.Fatal;
}
