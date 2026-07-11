using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Console.Interactive;
using CardDemo.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CardDemo.Console.Cli;

/// <summary>
/// Parses the product command surface (09-DotNet-Target-Architecture.md#console-command-surface)
/// and dispatches to exactly one mode. Returns the documented process exit code.
/// </summary>
public sealed class CommandRouter(IServiceProvider services, IConfiguration configuration)
{
    private readonly IServiceProvider _services = services;
    private readonly IConfiguration _configuration = configuration;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var positionals = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var options = ParseOptions(args);

        if (options.ContainsKey("help") || positionals is ["help"])
        {
            PrintUsage();
            return ExitCodes.Ok;
        }

        // Default (no command) launches the interactive terminal.
        var group = positionals.Length > 0 ? positionals[0].ToLowerInvariant() : "interactive";
        var verb = positionals.Length > 1 ? positionals[1].ToLowerInvariant() : string.Empty;

        try
        {
            return group switch
            {
                "interactive" => await RunInteractiveAsync(ct),
                "database" => await RunDatabaseAsync(verb, options, ct),
                "batch" => await RunBatchAsync(verb, options, ct),
                "transfer" => await RunTransferAsync(verb, options, ct),
                "authorization" => await RunAuthorizationAsync(verb, options, ct),
                "worker" => await RunWorkerAsync(verb, options, ct),
                "inquiry" => await RunInquiryAsync(verb, options, ct),
                "reference-data" => await RunReferenceDataAsync(verb, options, ct),
                _ => Unknown(group),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task<int> RunInteractiveAsync(CancellationToken ct)
    {
        if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected)
        {
            System.Console.Error.WriteLine("interactive mode requires an interactive terminal (a TTY).");
            return ExitCodes.UsageError;
        }

        var app = _services.GetRequiredService<InteractiveApp>();
        return await app.RunAsync(ct);
    }

    private async Task<int> RunDatabaseAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var db = _services.GetRequiredService<IDatabaseManager>();
        switch (verb)
        {
            case "initialize":
            {
                var fixtureRoot = ResolveFixtureRoot(options);
                var report = await db.InitializeAsync(fixtureRoot, reseed: true, ct);
                System.Console.WriteLine($"Database initialized at {report.DatabasePath}");
                PrintCounts(report.Counts);
                return ExitCodes.Ok;
            }
            case "migrate":
                await db.MigrateAsync(ct);
                System.Console.WriteLine("Schema is up to date.");
                return ExitCodes.Ok;
            case "verify":
            {
                var report = await db.VerifyAsync(ct);
                PrintCounts(report.Counts);
                if (report.Ok)
                {
                    System.Console.WriteLine("Verification passed.");
                    return ExitCodes.Ok;
                }
                System.Console.Error.WriteLine("Verification found issues:");
                foreach (var issue in report.Issues)
                    System.Console.Error.WriteLine("  - " + issue);
                return ExitCodes.BusinessRejects;
            }
            default:
                return Unknown($"database {verb}");
        }
    }

    private async Task<int> RunBatchAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var batch = _services.GetRequiredService<IBatchRunner>();
        switch (verb)
        {
            case "refresh-masters":
            {
                var report = await batch.RefreshMastersAsync(ResolveFixtureRoot(options), ct);
                System.Console.WriteLine("Masters refreshed.");
                PrintCounts(report.Counts);
                return ExitCodes.Ok;
            }
            case "post-transactions":
            {
                var input = options.GetValueOrDefault("input", Path.Combine(ResolveFixtureRoot(options), "dailytran.txt"));
                var rejects = options.GetValueOrDefault("rejects", ResolveRejectsPath());
                var report = await batch.PostTransactionsAsync(input, rejects, ct);
                System.Console.WriteLine($"Posting complete: {report.Accepted} accepted, {report.Rejected} rejected.");
                foreach (var (reason, count) in report.RejectsByReason.OrderBy(k => k.Key))
                    System.Console.WriteLine($"  reason {reason}: {count}");
                return report.HasRejects ? ExitCodes.BusinessRejects : ExitCodes.Ok;
            }
            case "calculate-interest":
            {
                var cycleId = options.GetValueOrDefault("cycle-id", _configuration["Batch:DefaultCycleId"] ?? "2022071800");
                var report = await batch.CalculateInterestAsync(cycleId, ct);
                System.Console.WriteLine($"Interest complete: {report.InterestTransactions} transactions, total {report.TotalInterest:0.00}, {report.AccountsUpdated} accounts updated.");
                return ExitCodes.Ok;
            }
            case "generate-report":
            {
                var from = options.GetValueOrDefault("from", string.Empty);
                var to = options.GetValueOrDefault("to", string.Empty);
                if (from.Length == 0 || to.Length == 0)
                {
                    System.Console.Error.WriteLine("generate-report requires --from <yyyy-MM-dd> and --to <yyyy-MM-dd>.");
                    return ExitCodes.UsageError;
                }
                var output = options.GetValueOrDefault("output", Path.Combine(ResolveReportsDirectory(), $"tranreport_{from}_{to}.txt"));
                var result = await batch.GenerateReportAsync(from, to, output, ct);
                System.Console.WriteLine($"Report written to {result.OutputPath}: {result.TransactionsIncluded} transactions, total {result.TotalAmount:0.00}.");
                return ExitCodes.Ok;
            }
            case "run-pending-reports":
            {
                var count = await batch.RunPendingReportsAsync(ResolveReportsDirectory(), ct);
                System.Console.WriteLine($"Processed {count} pending report request(s).");
                return ExitCodes.Ok;
            }
            case "rebuild-transaction-index":
                // Indexes are maintained automatically by the relational store; the
                // legacy VSAM index-rebuild step is a no-op here.
                System.Console.WriteLine("Transaction indexes are maintained automatically by the relational store; nothing to rebuild.");
                return ExitCodes.Ok;
            case "generate-statements":
            {
                var text = options.GetValueOrDefault("text", Path.Combine(ResolveReportsDirectory(), "statements.txt"));
                var html = options.GetValueOrDefault("html", Path.Combine(ResolveReportsDirectory(), "statements.html"));
                var statements = _services.GetRequiredService<IStatementService>();
                var result = await statements.GenerateAsync(text, html, ct);
                System.Console.WriteLine($"Statements: {result.Statements} account(s), {result.TransactionLines} transaction lines.");
                System.Console.WriteLine($"  text: {result.TextPath}");
                System.Console.WriteLine($"  html: {result.HtmlPath}");
                return ExitCodes.Ok;
            }
            case "combine-transactions":
            {
                var result = await batch.CombineTransactionsAsync(ct);
                System.Console.WriteLine($"Combined transaction master: {result.TotalTransactions} transactions ({result.PostedTransactions} posted + {result.InterestTransactions} interest).");
                return ExitCodes.Ok;
            }
            case "full-cycle":
            {
                var profile = options.GetValueOrDefault("profile", "Safe");
                if (!profile.Equals("Safe", StringComparison.OrdinalIgnoreCase) &&
                    !profile.Equals("StrictLegacy", StringComparison.OrdinalIgnoreCase))
                {
                    System.Console.Error.WriteLine("--profile must be 'Safe' or 'StrictLegacy'.");
                    return ExitCodes.UsageError;
                }
                System.Console.WriteLine($"Run profile: {profile}.");
                var fixtureRoot = ResolveFixtureRoot(options);
                await batch.RefreshMastersAsync(fixtureRoot, ct);
                var post = await batch.PostTransactionsAsync(Path.Combine(fixtureRoot, "dailytran.txt"), ResolveRejectsPath(), ct);
                var cycleId = options.GetValueOrDefault("cycle-id", _configuration["Batch:DefaultCycleId"] ?? "2022071800");
                var interest = await batch.CalculateInterestAsync(cycleId, ct);
                System.Console.WriteLine($"Full cycle complete: posted {post.Accepted}/{post.Accepted + post.Rejected}, interest {interest.InterestTransactions} txns.");
                return post.HasRejects ? ExitCodes.BusinessRejects : ExitCodes.Ok;
            }
            default:
                return Unknown($"batch {verb}");
        }
    }

    private async Task<int> RunTransferAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var transfer = _services.GetRequiredService<ITransferService>();
        switch (verb)
        {
            case "export-branch":
            {
                var output = options.GetValueOrDefault("output", Path.Combine(ResolveReportsDirectory(), "branch-export.dat"));
                var result = await transfer.ExportAsync(output, ct);
                System.Console.WriteLine($"Exported {result.Records} record(s) to {result.Path}.");
                foreach (var (type, count) in result.RecordsByType.OrderBy(k => k.Key))
                    System.Console.WriteLine($"  type {type}: {count}");
                return ExitCodes.Ok;
            }
            case "import-branch":
            {
                var input = options.GetValueOrDefault("input", string.Empty);
                if (input.Length == 0)
                {
                    System.Console.Error.WriteLine("import-branch requires --input <path>.");
                    return ExitCodes.UsageError;
                }
                var errorOutput = options.GetValueOrDefault("error-output", Path.Combine(ResolveReportsDirectory(), "branch-import-errors.dat"));
                var result = await transfer.ImportAsync(input, errorOutput, ct);
                System.Console.WriteLine($"Imported {result.Records} record(s) from {result.Path}.");
                foreach (var (type, count) in result.RecordsByType.OrderBy(k => k.Key))
                    System.Console.WriteLine($"  type {type}: {count}");
                return ExitCodes.Ok;
            }
            default:
                return Unknown($"transfer {verb}");
        }
    }

    private async Task<int> RunAuthorizationAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var auth = _services.GetRequiredService<IAuthorizationService>();
        switch (verb)
        {
            case "process":
            {
                var result = await auth.ProcessPendingAsync(ParseInt(options, "max", 1000), ct);
                System.Console.WriteLine($"Authorization: {result.Processed} processed ({result.Approved} approved, {result.Declined} declined).");
                return ExitCodes.Ok;
            }
            case "purge-expired":
            {
                var purged = await auth.PurgeExpiredAsync(ParseInt(options, "days", 30), ct);
                System.Console.WriteLine($"Purged {purged} expired authorization detail(s).");
                return ExitCodes.Ok;
            }
            case "unload":
            {
                var output = options.GetValueOrDefault("output", Path.Combine(ResolveReportsDirectory(), "pending-auth-unload.dat"));
                var count = await auth.UnloadAsync(output, ct);
                System.Console.WriteLine($"Unloaded {count} pending-authorization record(s) to {output}.");
                return ExitCodes.Ok;
            }
            case "load":
            {
                var input = options.GetValueOrDefault("input", string.Empty);
                if (input.Length == 0)
                {
                    System.Console.Error.WriteLine("authorization load requires --input <path>.");
                    return ExitCodes.UsageError;
                }
                var count = await auth.LoadAsync(input, ct);
                System.Console.WriteLine($"Loaded {count} pending-authorization record(s) from {input}.");
                return ExitCodes.Ok;
            }
            case "submit":
            {
                var card = options.GetValueOrDefault("card", string.Empty);
                if (card.Length == 0)
                {
                    System.Console.Error.WriteLine("authorization submit requires --card <card-number> and --amount <value>.");
                    return ExitCodes.UsageError;
                }
                var request = new AuthorizationRequest
                {
                    CardNumber = card,
                    TransactionAmount = ParseDecimal(options, "amount", 0m),
                    AuthType = options.GetValueOrDefault("type", "0100"),
                    MerchantName = options.GetValueOrDefault("merchant", "TEST MERCHANT"),
                };
                var result = await auth.SubmitAsync(request, ct);
                if (!result.Success)
                {
                    System.Console.Error.WriteLine(result.Message);
                    return ExitCodes.Unavailable;
                }
                System.Console.WriteLine("Authorization request queued.");
                return ExitCodes.Ok;
            }
            default:
                return Unknown($"authorization {verb}");
        }
    }

    private async Task<int> RunWorkerAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var max = ParseInt(options, "max", 1000);
        switch (verb)
        {
            case "account-inquiry":
            {
                var inquiry = _services.GetRequiredService<IInquiryService>();
                var result = await inquiry.ProcessPendingAsync("INQA", max, ct);
                System.Console.WriteLine($"Account-inquiry worker: {result.Processed} request(s) answered.");
                return ExitCodes.Ok;
            }
            case "system-date":
            {
                var inquiry = _services.GetRequiredService<IInquiryService>();
                var result = await inquiry.ProcessPendingAsync("DATE", max, ct);
                System.Console.WriteLine($"System-date worker: {result.Processed} request(s) answered.");
                return ExitCodes.Ok;
            }
            case "authorization":
            {
                var auth = _services.GetRequiredService<IAuthorizationService>();
                var result = await auth.ProcessPendingAsync(max, ct);
                System.Console.WriteLine($"Authorization worker: {result.Processed} processed ({result.Approved} approved, {result.Declined} declined).");
                return ExitCodes.Ok;
            }
            default:
                return Unknown($"worker {verb}");
        }
    }

    private async Task<int> RunInquiryAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var inquiry = _services.GetRequiredService<IInquiryService>();
        switch (verb)
        {
            case "account":
            {
                var account = options.GetValueOrDefault("account", string.Empty);
                if (account.Length == 0)
                {
                    System.Console.Error.WriteLine("inquiry account requires --account <11-digit>.");
                    return ExitCodes.UsageError;
                }
                var result = await inquiry.SubmitAccountInquiryAsync(account, ct);
                System.Console.WriteLine(result.Success ? "Account inquiry queued." : result.Message);
                return result.Success ? ExitCodes.Ok : ExitCodes.UsageError;
            }
            case "date":
            {
                await inquiry.SubmitDateInquiryAsync(ct);
                System.Console.WriteLine("System-date inquiry queued.");
                return ExitCodes.Ok;
            }
            case "replies":
            {
                var replies = await inquiry.RecentRepliesAsync(ParseInt(options, "take", 10), ct);
                foreach (var reply in replies)
                    System.Console.WriteLine($"[{reply.Service}] {reply.Payload}");
                return ExitCodes.Ok;
            }
            default:
                return Unknown($"inquiry {verb}");
        }
    }

    private async Task<int> RunReferenceDataAsync(string verb, IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var svc = _services.GetRequiredService<ITransactionTypeService>();
        switch (verb)
        {
            case "apply-transaction-types":
            {
                var input = options.GetValueOrDefault("input", string.Empty);
                if (input.Length == 0)
                {
                    System.Console.Error.WriteLine("reference-data apply-transaction-types requires --input <53-byte-file>.");
                    return ExitCodes.UsageError;
                }
                var result = await svc.ApplyBatchAsync(input, ct);
                System.Console.WriteLine($"Applied {result.Applied} transaction-type record(s), {result.Failed} failed.");
                return result.Failed > 0 ? ExitCodes.BusinessRejects : ExitCodes.Ok;
            }
            case "export-transaction-references":
            {
                var output = options.GetValueOrDefault("output", Path.Combine(ResolveReportsDirectory(), "transaction-references.dat"));
                var count = await svc.ExportReferencesAsync(output, ct);
                System.Console.WriteLine($"Exported {count} transaction reference record(s) to {output}.");
                return ExitCodes.Ok;
            }
            default:
                return Unknown($"reference-data {verb}");
        }
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> options, string key, int fallback) =>
        options.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static decimal ParseDecimal(IReadOnlyDictionary<string, string> options, string key, decimal fallback) =>
        options.TryGetValue(key, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private string ResolveFixtureRoot(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("fixture-root", out var opt) && !string.IsNullOrWhiteSpace(opt))
            return opt;
        var configured = _configuration["Files:FixtureRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "ASCII");
    }

    private string ResolveReportsDirectory()
    {
        var dir = _configuration["Files:ReportsDirectory"];
        return string.IsNullOrWhiteSpace(dir) ? "reports" : dir;
    }

    private string ResolveRejectsPath()
    {
        var path = _configuration["Files:RejectsPath"];
        return string.IsNullOrWhiteSpace(path) ? Path.Combine("rejects", "dalyrejs.txt") : path;
    }

    private static int Unknown(string command)
    {
        System.Console.Error.WriteLine($"Unknown command: {command}");
        System.Console.Error.WriteLine("Run 'carddemo --help' for usage.");
        return ExitCodes.UsageError;
    }

    private static void PrintCounts(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var (name, count) in counts.OrderBy(k => k.Key))
            System.Console.WriteLine($"  {name,-28} {count,8}");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            var eq = key.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                options[key[..eq]] = key[(eq + 1)..];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[i + 1];
                i++;
            }
            else
            {
                options[key] = "true";
            }
        }
        return options;
    }

    private static void PrintUsage()
    {
        System.Console.WriteLine(
            """
            carddemo — AWS CardDemo modernized on .NET 10 (SQLite).

            Usage:
              carddemo interactive
              carddemo database initialize [--fixture-root <path>]
              carddemo database migrate
              carddemo database verify
              carddemo batch refresh-masters [--fixture-root <path>]
              carddemo batch post-transactions [--input <path>] [--rejects <path>]
              carddemo batch calculate-interest [--cycle-id <10-char>]
              carddemo batch generate-report --from <yyyy-MM-dd> --to <yyyy-MM-dd> [--output <path>]
              carddemo batch generate-statements [--text <path>] [--html <path>]
              carddemo batch combine-transactions
              carddemo batch run-pending-reports
              carddemo batch full-cycle [--fixture-root <path>] [--cycle-id <10-char>] [--profile Safe|StrictLegacy]

              carddemo transfer export-branch [--output <path>]
              carddemo transfer import-branch --input <path> [--error-output <path>]

              carddemo authorization submit --card <n> --amount <v> [--type <4>]
              carddemo authorization process [--max <n>]
              carddemo authorization purge-expired [--days <n>]
              carddemo authorization unload [--output <path>]
              carddemo authorization load --input <path>

              carddemo inquiry account --account <11-digit>
              carddemo inquiry date
              carddemo inquiry replies [--take <n>]
              carddemo worker account-inquiry [--max <n>]
              carddemo worker system-date [--max <n>]
              carddemo worker authorization [--max <n>]

              carddemo reference-data apply-transaction-types --input <53-byte-file>
              carddemo reference-data export-transaction-references [--output <path>]

            With no arguments, the interactive terminal starts.
            """);
    }
}
