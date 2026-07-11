using System.Globalization;
using System.Net;
using System.Text;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Account statement generation (CBSTM03A / CREASTMT). Produces one combined
/// fixed-width plain-text file and one combined HTML file covering every account
/// that has posted transactions. For each such account the customer and card are
/// resolved through the card cross-reference (card -&gt; xref -&gt; customer), the
/// account's transactions are listed with a running total, and the output is
/// written as a header block, one line per transaction and a total line.
///
/// Two legacy defects are deliberately fixed in this safe target: accounts with no
/// transactions are skipped (legacy emitted an empty statement), and all
/// customer/transaction text is HTML-escaped in the HTML output (legacy performed
/// no escaping).
/// </summary>
public sealed class StatementService(CardDemoDbContext db) : IStatementService
{
    public async Task<StatementRunResult> GenerateAsync(string textPath, string htmlPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(textPath);
        ArgumentException.ThrowIfNullOrEmpty(htmlPath);

        EnsureParentDirectory(textPath);
        EnsureParentDirectory(htmlPath);

        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var xrefs = await db.CardXrefs.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var transactions = await db.Transactions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var customerById = customers
            .GroupBy(c => c.CustomerId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // card number -> xref (primary lookup path).
        var xrefByCard = xrefs
            .GroupBy(x => x.CardNumber, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // account -> first xref (to resolve the customer/card for the account).
        var xrefByAccount = xrefs
            .GroupBy(x => x.AccountId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Group posted transactions by the account behind their card number.
        var txnsByAccount = new Dictionary<string, List<Transaction>>(StringComparer.Ordinal);
        foreach (var t in transactions)
        {
            if (!xrefByCard.TryGetValue(t.CardNumber, out var xref))
                continue;
            if (!txnsByAccount.TryGetValue(xref.AccountId, out var list))
            {
                list = [];
                txnsByAccount[xref.AccountId] = list;
            }
            list.Add(t);
        }

        var text = new StringBuilder();
        var html = new StringBuilder();
        AppendHtmlDocumentStart(html);

        var statementCount = 0;
        var transactionLines = 0;

        foreach (var account in accounts.OrderBy(a => a.AccountId, StringComparer.Ordinal))
        {
            if (!txnsByAccount.TryGetValue(account.AccountId, out var accountTxns) || accountTxns.Count == 0)
                continue; // safe-target: skip accounts with no posted transactions.

            xrefByAccount.TryGetValue(account.AccountId, out var xref);
            Customer? customer = null;
            if (xref is not null)
                customerById.TryGetValue(xref.CustomerId, out customer);

            var ordered = accountTxns
                .OrderBy(t => t.TransactionId, StringComparer.Ordinal)
                .ToList();

            statementCount++;
            transactionLines += ordered.Count;

            AppendTextStatement(text, account, customer, xref, ordered);
            AppendHtmlStatement(html, account, customer, xref, ordered);
        }

        AppendHtmlDocumentEnd(html);

        await File.WriteAllTextAsync(textPath, text.ToString(), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(htmlPath, html.ToString(), ct).ConfigureAwait(false);

        return new StatementRunResult(statementCount, transactionLines, Path.GetFullPath(textPath), Path.GetFullPath(htmlPath));
    }

    // ---- plain text --------------------------------------------------------

    private static void AppendTextStatement(
        StringBuilder sb,
        Account account,
        Customer? customer,
        CardXref? xref,
        IReadOnlyList<Transaction> transactions)
    {
        var name = BuildName(customer);
        var card = xref?.CardNumber ?? string.Empty;

        sb.Append('*', 31).Append("START OF STATEMENT").Append('*', 31).Append('\n');
        sb.Append(name).Append('\n');
        if (customer is not null)
        {
            AppendIfPresent(sb, customer.AddressLine1);
            AppendIfPresent(sb, customer.AddressLine2);
            var line3 = JoinNonEmpty(customer.AddressLine3, customer.StateCode, customer.CountryCode, customer.Zip);
            AppendIfPresent(sb, line3);
        }
        sb.Append(new string('-', 62)).Append('\n');
        sb.Append("Basic Details").Append('\n');
        sb.Append(new string('-', 62)).Append('\n');
        sb.Append("Account ID         : ").Append(account.AccountId).Append('\n');
        sb.Append("Card Number        : ").Append(card).Append('\n');
        sb.Append("Current Balance    : ").Append(FormatMoney(account.CurrentBalance)).Append('\n');
        sb.Append("FICO Score         : ").Append((customer?.FicoCreditScore ?? 0).ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(new string('-', 62)).Append('\n');
        sb.Append("TRANSACTION SUMMARY").Append('\n');
        sb.Append(new string('-', 62)).Append('\n');
        sb.Append($"{"Tran ID",-16} {"Type",-4} {"Cat",-4} {"Tran Details",-49} {"Tran Amount",13}").Append('\n');
        sb.Append(new string('-', 62)).Append('\n');

        decimal total = 0m;
        foreach (var t in transactions)
        {
            total += t.Amount;
            var details = Truncate(t.Description, 49);
            sb.Append($"{t.TransactionId,-16} {t.TypeCode,-4} {t.CategoryCode,-4} {details,-49} {FormatMoney(t.Amount),13}").Append('\n');
        }

        sb.Append(new string('-', 62)).Append('\n');
        sb.Append("Total EXP: ").Append(FormatMoney(total)).Append('\n');
        sb.Append('*', 32).Append("END OF STATEMENT").Append('*', 32).Append('\n');
    }

    private static void AppendIfPresent(StringBuilder sb, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.Append(value).Append('\n');
    }

    // ---- HTML --------------------------------------------------------------

    private static void AppendHtmlDocumentStart(StringBuilder sb)
    {
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>Account Statements</title>\n</head>\n<body>\n");
    }

    private static void AppendHtmlDocumentEnd(StringBuilder sb)
    {
        sb.Append("</body>\n</html>\n");
    }

    private static void AppendHtmlStatement(
        StringBuilder sb,
        Account account,
        Customer? customer,
        CardXref? xref,
        IReadOnlyList<Transaction> transactions)
    {
        var name = BuildName(customer);
        var card = xref?.CardNumber ?? string.Empty;

        sb.Append("<section class=\"statement\">\n");
        sb.Append("<h3>Statement for Account Number: ").Append(Esc(account.AccountId)).Append("</h3>\n");
        sb.Append("<p style=\"font-size:16px\">").Append(Esc(name)).Append("</p>\n");
        if (customer is not null)
        {
            AppendHtmlAddress(sb, customer.AddressLine1);
            AppendHtmlAddress(sb, customer.AddressLine2);
            var line3 = JoinNonEmpty(customer.AddressLine3, customer.StateCode, customer.CountryCode, customer.Zip);
            AppendHtmlAddress(sb, line3);
        }
        sb.Append("<p>Account ID : ").Append(Esc(account.AccountId)).Append("</p>\n");
        sb.Append("<p>Card Number : ").Append(Esc(card)).Append("</p>\n");
        sb.Append("<p>Current Balance : ").Append(Esc(FormatMoney(account.CurrentBalance))).Append("</p>\n");
        sb.Append("<p>FICO : ").Append(Esc((customer?.FicoCreditScore ?? 0).ToString(CultureInfo.InvariantCulture))).Append("</p>\n");

        sb.Append("<table>\n<thead>\n<tr><th>Tran ID</th><th>Type</th><th>Cat</th><th>Tran Details</th><th>Tran Amount</th></tr>\n</thead>\n<tbody>\n");

        decimal total = 0m;
        foreach (var t in transactions)
        {
            total += t.Amount;
            sb.Append("<tr><td>").Append(Esc(t.TransactionId))
                .Append("</td><td>").Append(Esc(t.TypeCode))
                .Append("</td><td>").Append(Esc(t.CategoryCode))
                .Append("</td><td>").Append(Esc(t.Description))
                .Append("</td><td>").Append(Esc(FormatMoney(t.Amount)))
                .Append("</td></tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
        // Safe-target deviation from the legacy HTML (which omitted the total): the
        // HTML statement includes the Total EXP so it matches the text output.
        sb.Append("<p class=\"total\">Total EXP: ").Append(Esc(FormatMoney(total))).Append("</p>\n");
        sb.Append("</section>\n");
    }

    private static void AppendHtmlAddress(StringBuilder sb, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.Append("<p>").Append(Esc(value)).Append("</p>\n");
    }

    // ---- helpers -----------------------------------------------------------

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string BuildName(Customer? customer)
    {
        if (customer is null)
            return string.Empty;
        return JoinNonEmpty(customer.FirstName, customer.MiddleName, customer.LastName);
    }

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(' ', parts
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0));

    private static string FormatMoney(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Truncate(string? value, int width)
    {
        var v = value ?? string.Empty;
        return v.Length > width ? v[..width] : v;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
