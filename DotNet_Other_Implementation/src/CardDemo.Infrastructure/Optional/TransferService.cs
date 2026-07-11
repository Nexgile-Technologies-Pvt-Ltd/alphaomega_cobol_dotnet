using System.Text;
using CardDemo.Application.Abstractions;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Branch export/import over the fixed 500-byte record format
/// (CBEXPORT / CBIMPORT). Exports every master as a stream of
/// <see cref="BranchExportCodec.RecordLength"/>-byte records in the legacy type
/// order (C, A, X, T, D) and imports them back by dispatching on the type byte
/// and UPSERTing by primary key. Malformed or short records are written to a
/// separate error file and skipped. The whole import runs inside one transaction.
/// </summary>
public sealed class TransferService(CardDemoDbContext db) : ITransferService
{
    private const int RecLen = BranchExportCodec.RecordLength;

    public async Task<TransferResult> ExportAsync(string outputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        EnsureParentDirectory(outputPath);

        var customers = await db.Customers.AsNoTracking().OrderBy(c => c.CustomerId).ToListAsync(ct).ConfigureAwait(false);
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.AccountId).ToListAsync(ct).ConfigureAwait(false);
        var xrefs = await db.CardXrefs.AsNoTracking().OrderBy(x => x.CardNumber).ToListAsync(ct).ConfigureAwait(false);
        var transactions = await db.Transactions.AsNoTracking().OrderBy(t => t.TransactionId).ToListAsync(ct).ConfigureAwait(false);
        var cards = await db.Cards.AsNoTracking().OrderBy(d => d.CardNumber).ToListAsync(ct).ConfigureAwait(false);

        var byType = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = customers.Count,
            ["A"] = accounts.Count,
            ["X"] = xrefs.Count,
            ["T"] = transactions.Count,
            ["D"] = cards.Count,
        };

        var sb = new StringBuilder((customers.Count + accounts.Count + xrefs.Count + transactions.Count + cards.Count) * RecLen);
        foreach (var c in customers) sb.Append(BranchExportCodec.EncodeCustomer(c));
        foreach (var a in accounts) sb.Append(BranchExportCodec.EncodeAccount(a));
        foreach (var x in xrefs) sb.Append(BranchExportCodec.EncodeXref(x));
        foreach (var t in transactions) sb.Append(BranchExportCodec.EncodeTransaction(t));
        foreach (var d in cards) sb.Append(BranchExportCodec.EncodeCard(d));

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct).ConfigureAwait(false);

        var total = byType.Values.Sum();
        return new TransferResult(total, byType, Path.GetFullPath(outputPath));
    }

    public async Task<TransferResult> ImportAsync(string inputPath, string errorPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentException.ThrowIfNullOrEmpty(errorPath);
        EnsureParentDirectory(errorPath);

        var raw = await File.ReadAllTextAsync(inputPath, ct).ConfigureAwait(false);

        var byType = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = 0, ["A"] = 0, ["X"] = 0, ["T"] = 0, ["D"] = 0,
        };
        var errors = new StringBuilder();
        var imported = 0;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var offset = 0;
            var sequence = 0;
            while (offset < raw.Length)
            {
                sequence++;
                var remaining = raw.Length - offset;
                if (remaining < RecLen)
                {
                    // Short/truncated trailing record: route to the error file.
                    AppendError(errors, sequence, '?', "Short record (truncated)", raw.Substring(offset, remaining));
                    break;
                }

                var record = raw.Substring(offset, RecLen);
                offset += RecLen;

                var type = record[0];
                try
                {
                    switch (type)
                    {
                        case BranchExportCodec.CustomerType:
                            await UpsertAsync(db.Customers, BranchExportCodec.DecodeCustomer(record), e => e.CustomerId, ct).ConfigureAwait(false);
                            byType["C"]++;
                            break;
                        case BranchExportCodec.AccountType:
                            await UpsertAsync(db.Accounts, BranchExportCodec.DecodeAccount(record), e => e.AccountId, ct).ConfigureAwait(false);
                            byType["A"]++;
                            break;
                        case BranchExportCodec.XrefType:
                            await UpsertAsync(db.CardXrefs, BranchExportCodec.DecodeXref(record), e => e.CardNumber, ct).ConfigureAwait(false);
                            byType["X"]++;
                            break;
                        case BranchExportCodec.TransactionType:
                            await UpsertAsync(db.Transactions, BranchExportCodec.DecodeTransaction(record), e => e.TransactionId, ct).ConfigureAwait(false);
                            byType["T"]++;
                            break;
                        case BranchExportCodec.CardType:
                            await UpsertAsync(db.Cards, BranchExportCodec.DecodeCard(record), e => e.CardNumber, ct).ConfigureAwait(false);
                            byType["D"]++;
                            break;
                        default:
                            AppendError(errors, sequence, type, "Unknown record type encountered", record);
                            continue;
                    }

                    imported++;
                }
                catch (FormatException ex)
                {
                    AppendError(errors, sequence, type, ex.Message, record);
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await File.WriteAllTextAsync(errorPath, errors.ToString(), ct).ConfigureAwait(false);

        return new TransferResult(imported, byType, Path.GetFullPath(inputPath));
    }

    /// <summary>
    /// Insert or replace <paramref name="incoming"/> by primary key. If a row with
    /// the same key is already tracked/stored, its scalar values are overwritten;
    /// otherwise the row is added.
    /// </summary>
    private async Task UpsertAsync<TEntity>(
        DbSet<TEntity> set,
        TEntity incoming,
        Func<TEntity, string> keySelector,
        CancellationToken ct)
        where TEntity : class
    {
        var key = keySelector(incoming);
        var existing = await set.FindAsync([key], ct).ConfigureAwait(false);
        if (existing is null)
        {
            await set.AddAsync(incoming, ct).ConfigureAwait(false);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(incoming);
        }
    }

    private static void AppendError(StringBuilder errors, int sequence, char type, string message, string record)
    {
        // Pipe-delimited error record (WS-ERROR-RECORD parity): a header describing
        // the failure, followed by the raw offending bytes, one line per record.
        errors.Append(sequence.ToString("D7", System.Globalization.CultureInfo.InvariantCulture))
            .Append('|').Append(type)
            .Append('|').Append(message)
            .Append('|').Append(record)
            .Append('\n');
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
