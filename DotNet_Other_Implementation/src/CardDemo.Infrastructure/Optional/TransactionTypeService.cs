using System.Globalization;
using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Common;
using CardDemo.Domain.Entities;
using CardDemo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Infrastructure.Optional;

/// <summary>
/// Transaction-type / category maintenance (COTRTLIC list, COTRTUPC single-maintain).
/// Operates on the relational <see cref="TransactionType"/> and <see cref="TransactionCategory"/>
/// tables reproducing the legacy validation and the FK-restrict-on-delete behaviour.
///
/// Key edits mirror COTRTUPC 1210-EDIT-TRANTYPE / 1245-EDIT-NUM-REQD: the 2-char type is
/// required, must be numeric and non-zero, and is re-stored zero-padded to 2 digits (so an
/// input of "5" persists as "05"). Category codes mirror the 4-digit zero-padded key. The
/// description edit mirrors 1230-EDIT-ALPHANUM-REQD: required and letters/digits/spaces only.
/// Deleting a type that still has category children fails (Db2 SQLCODE -532 equivalent).
/// </summary>
public sealed class TransactionTypeService(CardDemoDbContext db) : ITransactionTypeService
{
    public async Task<OperationResult<PagedResult<TransactionType>>> ListTypesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (pageSize <= 0)
            return OperationResult<PagedResult<TransactionType>>.Fail("Page size must be positive.");
        if (page < 0)
            page = 0;

        // Peek-ahead: fetch pageSize+1 ordered by code to compute HasNext (COTRTLIC 8000-READ-FORWARD).
        var rows = await db.TransactionTypes
            .AsNoTracking()
            .OrderBy(t => t.TypeCode)
            .Skip(page * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasNext = rows.Count > pageSize;
        if (hasNext)
            rows.RemoveAt(rows.Count - 1);

        var paged = new PagedResult<TransactionType>(rows, page, pageSize, hasNext, page > 0);
        return OperationResult<PagedResult<TransactionType>>.Ok(paged);
    }

    public async Task<OperationResult<TransactionType>> GetTypeAsync(string typeCode, CancellationToken ct = default)
    {
        var key = NormalizeExistingType(typeCode);
        var found = await db.TransactionTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TypeCode == key, ct)
            .ConfigureAwait(false);

        return found is null
            ? OperationResult<TransactionType>.Fail("Transaction type not found.")
            : OperationResult<TransactionType>.Ok(found);
    }

    public async Task<OperationResult> UpsertTypeAsync(string typeCode, string description, CancellationToken ct = default)
    {
        var keyResult = EditTypeCode(typeCode);
        if (!keyResult.Success)
            return OperationResult.Fail(keyResult.Message);

        var descResult = EditDescription(description);
        if (!descResult.Success)
            return OperationResult.Fail(descResult.Message);

        var key = keyResult.Value!;
        var value = description.Trim();

        var existing = await db.TransactionTypes
            .FirstOrDefaultAsync(t => t.TypeCode == key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.TransactionTypes.Add(new TransactionType { TypeCode = key, Description = value });
        }
        else
        {
            existing.Description = value;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok(existing is null ? "Transaction type added." : "Transaction type updated.");
    }

    public async Task<OperationResult> DeleteTypeAsync(string typeCode, CancellationToken ct = default)
    {
        var key = NormalizeExistingType(typeCode);

        var existing = await db.TransactionTypes
            .FirstOrDefaultAsync(t => t.TypeCode == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
            return OperationResult.Fail("Transaction type not found.");

        // FK RESTRICT (TRNTYCAT ON DELETE RESTRICT / SQLCODE -532): block while children exist.
        var hasChildren = await db.TransactionCategories
            .AnyAsync(c => c.TypeCode == key, ct)
            .ConfigureAwait(false);
        if (hasChildren)
            return OperationResult.Fail("Please delete associated child records first.");

        db.TransactionTypes.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Transaction type deleted.");
    }

    public async Task<OperationResult<PagedResult<TransactionCategory>>> ListCategoriesAsync(string? typeCode, int page, int pageSize, CancellationToken ct = default)
    {
        if (pageSize <= 0)
            return OperationResult<PagedResult<TransactionCategory>>.Fail("Page size must be positive.");
        if (page < 0)
            page = 0;

        var query = db.TransactionCategories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(typeCode))
        {
            var key = NormalizeExistingType(typeCode);
            query = query.Where(c => c.TypeCode == key);
        }

        var rows = await query
            .OrderBy(c => c.TypeCode)
            .ThenBy(c => c.CategoryCode)
            .Skip(page * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasNext = rows.Count > pageSize;
        if (hasNext)
            rows.RemoveAt(rows.Count - 1);

        var paged = new PagedResult<TransactionCategory>(rows, page, pageSize, hasNext, page > 0);
        return OperationResult<PagedResult<TransactionCategory>>.Ok(paged);
    }

    public async Task<OperationResult> UpsertCategoryAsync(string typeCode, string categoryCode, string description, CancellationToken ct = default)
    {
        var keyResult = EditTypeCode(typeCode);
        if (!keyResult.Success)
            return OperationResult.Fail(keyResult.Message);

        var catResult = EditCategoryCode(categoryCode);
        if (!catResult.Success)
            return OperationResult.Fail(catResult.Message);

        var descResult = EditDescription(description);
        if (!descResult.Success)
            return OperationResult.Fail(descResult.Message);

        var typeKey = keyResult.Value!;
        var catKey = catResult.Value!;
        var value = description.Trim();

        // FK to the parent type must exist before a category can be added/updated.
        var parentExists = await db.TransactionTypes
            .AnyAsync(t => t.TypeCode == typeKey, ct)
            .ConfigureAwait(false);
        if (!parentExists)
            return OperationResult.Fail("Transaction type not found.");

        var existing = await db.TransactionCategories
            .FirstOrDefaultAsync(c => c.TypeCode == typeKey && c.CategoryCode == catKey, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.TransactionCategories.Add(new TransactionCategory
            {
                TypeCode = typeKey,
                CategoryCode = catKey,
                Description = value,
            });
        }
        else
        {
            existing.Description = value;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok(existing is null ? "Transaction category added." : "Transaction category updated.");
    }

    public async Task<OperationResult> DeleteCategoryAsync(string typeCode, string categoryCode, CancellationToken ct = default)
    {
        var typeKey = NormalizeExistingType(typeCode);
        var catKey = NormalizeExistingCategory(categoryCode);

        var existing = await db.TransactionCategories
            .FirstOrDefaultAsync(c => c.TypeCode == typeKey && c.CategoryCode == catKey, ct)
            .ConfigureAwait(false);
        if (existing is null)
            return OperationResult.Fail("Transaction category not found.");

        db.TransactionCategories.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Transaction category deleted.");
    }

    // ----- file-driven batch apply / export (COBTUPDT / TRANEXTR) -----

    /// <summary>
    /// Apply a COBTUPDT maintenance file. Each fixed record is 53 bytes:
    /// INPUT-TYPE PIC X(1) (action A/U/D/*), INPUT-TR-NUMBER PIC X(2), INPUT-TR-DESC PIC X(50).
    /// The file is read as lines (each line = one record, trailing padding trimmed):
    ///   'A'/'U' -> INSERT/UPDATE the transaction type (UpsertTypeAsync-equivalent);
    ///   'D'     -> DELETE the transaction type (DeleteTypeAsync-equivalent);
    ///   '*'/blank action -> commented / skipped (not counted as applied or failed).
    /// COBTUPDT abends on the first bad record; the batch driver instead continues past
    /// per-record errors (invalid action, failed edit, delete-with-children, missing row)
    /// and reports the applied/failed counts.
    /// </summary>
    public async Task<BatchApplyResult> ApplyBatchAsync(string inputPath, CancellationToken ct = default)
    {
        var applied = 0;
        var failed = 0;

        var lines = await File.ReadAllLinesAsync(inputPath, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            // A wholly blank line carries no action (INPUT-REC-TYPE = space) -> skip like '*'.
            if (line.Length == 0)
                continue;

            // Fixed layout: action(1) + type(2) + description(50). Slice defensively so a
            // short (untrimmed-but-clipped) line still yields whatever fields are present.
            var action = line[0];
            var typeCode = line.Length > 1 ? line.Substring(1, Math.Min(2, line.Length - 1)) : string.Empty;
            var description = line.Length > 3 ? line.Substring(3) : string.Empty;

            // COBTUPDT truncates the description field to its 50-byte PIC X(50) width.
            if (description.Length > 50)
                description = description[..50];

            switch (action)
            {
                case 'A':
                case 'U':
                {
                    var result = await UpsertTypeAsync(typeCode, description, ct).ConfigureAwait(false);
                    if (result.Success)
                        applied++;
                    else
                        failed++;
                    break;
                }
                case 'D':
                {
                    var result = await DeleteTypeAsync(typeCode, ct).ConfigureAwait(false);
                    if (result.Success)
                        applied++;
                    else
                        failed++;
                    break;
                }
                case '*':
                case ' ':
                    // 1003-TREAT-RECORD WHEN '*': commented line -> ignore (not applied/failed).
                    break;
                default:
                    // 1003-TREAT-RECORD WHEN OTHER: 'ERROR: TYPE NOT VALID' -> count as a failure.
                    failed++;
                    break;
            }
        }

        return new BatchApplyResult(applied, failed);
    }

    /// <summary>
    /// Export every transaction type and category row to a text file, one record per line,
    /// each tagged with its kind (TRANEXTR-style extract). Parent directories are created.
    /// Returns the total number of records written.
    /// </summary>
    public async Task<int> ExportReferencesAsync(string outputPath, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var types = await db.TransactionTypes
            .AsNoTracking()
            .OrderBy(t => t.TypeCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var categories = await db.TransactionCategories
            .AsNoTracking()
            .OrderBy(c => c.TypeCode)
            .ThenBy(c => c.CategoryCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lines = new List<string>(types.Count + categories.Count);
        foreach (var t in types)
            lines.Add($"TYPE|{t.TypeCode}|{t.Description}");
        foreach (var c in categories)
            lines.Add($"CATEGORY|{c.TypeCode}|{c.CategoryCode}|{c.Description}");

        await File.WriteAllLinesAsync(outputPath, lines, ct).ConfigureAwait(false);
        return lines.Count;
    }

    // ----- validation helpers (COTRTUPC 1245-EDIT-NUM-REQD / 1230-EDIT-ALPHANUM-REQD) -----

    /// <summary>
    /// 1245-EDIT-NUM-REQD on the 2-char type: required, numeric, non-zero; then zero-pad to
    /// 2 digits (INSPECT REPLACING SPACES BY ZEROS). "5" -> "05". First failure wins.
    /// </summary>
    private static OperationResult<string> EditTypeCode(string? typeCode)
    {
        var trimmed = (typeCode ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return OperationResult<string>.Fail("Transaction type must be supplied.");
        if (trimmed.Length > 2 || !IsAllDigits(trimmed))
            return OperationResult<string>.Fail("Transaction type must be numeric.");

        var value = int.Parse(trimmed, CultureInfo.InvariantCulture);
        if (value == 0)
            return OperationResult<string>.Fail("Transaction type must not be zero.");

        return OperationResult<string>.Ok(value.ToString("D2", CultureInfo.InvariantCulture));
    }

    /// <summary>Category code: required, numeric, up to 4 digits, zero-padded to 4 (TRC_TYPE_CATEGORY).</summary>
    private static OperationResult<string> EditCategoryCode(string? categoryCode)
    {
        var trimmed = (categoryCode ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return OperationResult<string>.Fail("Transaction category must be supplied.");
        if (trimmed.Length > 4 || !IsAllDigits(trimmed))
            return OperationResult<string>.Fail("Transaction category must be numeric.");

        var value = int.Parse(trimmed, CultureInfo.InvariantCulture);
        return OperationResult<string>.Ok(value.ToString("D4", CultureInfo.InvariantCulture));
    }

    /// <summary>1230-EDIT-ALPHANUM-REQD on the description: required, letters/digits/spaces only, max 50.</summary>
    private static OperationResult EditDescription(string? description)
    {
        var value = (description ?? string.Empty).Trim();
        if (value.Length == 0)
            return OperationResult.Fail("Description must be supplied.");
        if (value.Length > 50)
            return OperationResult.Fail("Description must be 50 characters or fewer.");

        foreach (var ch in value)
        {
            if (!(char.IsLetter(ch) || char.IsDigit(ch) || ch == ' '))
                return OperationResult.Fail("Description can have numbers or alphabets only.");
        }

        return OperationResult.Ok();
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var ch in value)
        {
            if (ch is < '0' or > '9')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normalise a lookup key the same way the edit does (zero-pad numeric to 2) without
    /// failing on non-numeric input — an unknown key simply misses and reports not-found.
    /// </summary>
    private static string NormalizeExistingType(string? typeCode)
    {
        var trimmed = (typeCode ?? string.Empty).Trim();
        if (trimmed.Length is > 0 and <= 2 && IsAllDigits(trimmed))
            return int.Parse(trimmed, CultureInfo.InvariantCulture).ToString("D2", CultureInfo.InvariantCulture);
        return trimmed;
    }

    private static string NormalizeExistingCategory(string? categoryCode)
    {
        var trimmed = (categoryCode ?? string.Empty).Trim();
        if (trimmed.Length is > 0 and <= 4 && IsAllDigits(trimmed))
            return int.Parse(trimmed, CultureInfo.InvariantCulture).ToString("D4", CultureInfo.InvariantCulture);
        return trimmed;
    }
}
