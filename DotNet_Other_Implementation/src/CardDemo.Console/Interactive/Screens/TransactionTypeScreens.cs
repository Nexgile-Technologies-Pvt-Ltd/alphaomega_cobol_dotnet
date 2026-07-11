using CardDemo.Application.Abstractions;
using CardDemo.Application.Dtos;
using CardDemo.Domain.Entities;

namespace CardDemo.Console.Interactive.Screens;

/// <summary>
/// Transaction-type and category maintenance screens (COTRTLIC list / COTRTUPC update).
/// Lists types and categories (paged) and lets the operator add/update/delete each.
/// </summary>
internal sealed class TransactionTypeScreens(ConsoleIo io, ITransactionTypeService svc)
{
    private const int PageSize = 10;
    private readonly ConsoleIo _io = io;
    private readonly ITransactionTypeService _svc = svc;

    /// <summary>Top-level menu for the transaction-type maintenance module.</summary>
    public async Task MenuAsync(CancellationToken ct)
    {
        while (true)
        {
            _io.Header("Transaction-type Maintenance (COTRTLIC/COTRTUPC)");
            _io.Line();
            _io.Line("  Types");
            _io.Line("    1) List transaction types");
            _io.Line("    2) View a transaction type");
            _io.Line("    3) Add / update a transaction type");
            _io.Line("    4) Delete a transaction type");
            _io.Line("  Categories");
            _io.Line("    5) List transaction categories");
            _io.Line("    6) Add / update a transaction category");
            _io.Line("    7) Delete a transaction category");
            _io.Rule('-');
            _io.Line("    0) Back");
            _io.Line();

            var choice = _io.MenuChoice();
            switch (choice)
            {
                case null:
                case "0":
                case "X":
                    return;
                case "1":
                    await ListTypesAsync(ct);
                    break;
                case "2":
                    await ViewTypeAsync(ct);
                    break;
                case "3":
                    await UpsertTypeAsync(ct);
                    break;
                case "4":
                    await DeleteTypeAsync(ct);
                    break;
                case "5":
                    await ListCategoriesAsync(ct);
                    break;
                case "6":
                    await UpsertCategoryAsync(ct);
                    break;
                case "7":
                    await DeleteCategoryAsync(ct);
                    break;
                default:
                    _io.ShowError("Unknown option. Please choose a number from the menu.");
                    _io.PressEnter();
                    break;
            }
        }
    }

    private async Task ListTypesAsync(CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            var result = await _svc.ListTypesAsync(page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.Header("Transaction Types (COTRTLIC)");
                _io.ShowError(result.Message.Length == 0 ? "No transaction types available." : result.Message);
                _io.PressEnter();
                return;
            }

            var data = result.Value;
            _io.Header("Transaction Types (COTRTLIC)");
            _io.Line($"Page {data.Page}");
            _io.Rule('-');
            _io.Line($"{"Type",-6} Description");
            _io.Rule('-');
            if (data.Items.Count == 0)
            {
                _io.Line("(no transaction types)");
            }
            foreach (var t in data.Items)
            {
                _io.Line($"{t.TypeCode.Trim(),-6} {t.Description.TrimEnd()}");
            }
            _io.Rule('-');
            _io.Line(Nav(data.HasPrevious, data.HasNext));

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && data.HasNext) { page++; continue; }
            if (choice == "P" && data.HasPrevious) { page--; continue; }
        }
    }

    private async Task ViewTypeAsync(CancellationToken ct)
    {
        _io.Header("View Transaction Type (COTRTUPC)");
        _io.Line("Enter the 2-char type code, or 0 to go back.");
        _io.Line();

        var typeCode = _io.PromptRequired("Type code: ");
        if (typeCode is null || typeCode == ConsoleIo.BackMarker) { return; }

        var result = await _svc.GetTypeAsync(typeCode, ct);
        if (!result.Success || result.Value is null)
        {
            _io.ShowError(result.Message.Length == 0 ? "Transaction type not found." : result.Message);
            _io.PressEnter();
            return;
        }

        var t = result.Value;
        _io.Line();
        _io.Rule('-');
        _io.Line($"Type code   : {t.TypeCode.Trim()}");
        _io.Line($"Description : {t.Description.TrimEnd()}");
        _io.Rule('-');
        _io.PressEnter();
    }

    private async Task UpsertTypeAsync(CancellationToken ct)
    {
        _io.Header("Add / Update Transaction Type (COTRTUPC)");
        _io.Line("Enter the type details. Type 0 at any prompt to cancel.");
        _io.Line();

        var typeCode = _io.PromptRequired("Type code (2 chars): ");
        if (typeCode is null || typeCode == ConsoleIo.BackMarker) { return; }

        var description = _io.PromptRequired("Description: ");
        if (description is null || description == ConsoleIo.BackMarker) { return; }

        var result = await _svc.UpsertTypeAsync(typeCode, description, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private async Task DeleteTypeAsync(CancellationToken ct)
    {
        _io.Header("Delete Transaction Type (COTRTUPC)");
        _io.Line("Enter the type code to delete, or 0 to go back.");
        _io.Line();

        var typeCode = _io.PromptRequired("Type code: ");
        if (typeCode is null || typeCode == ConsoleIo.BackMarker) { return; }

        var confirm = _io.Prompt($"Confirm deletion of type {typeCode}? (Y/N): ");
        if (confirm is null || confirm == ConsoleIo.BackMarker) { return; }
        if (!string.Equals(confirm, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _io.ShowInfo("Deletion cancelled.");
            _io.PressEnter();
            return;
        }

        var result = await _svc.DeleteTypeAsync(typeCode, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private async Task ListCategoriesAsync(CancellationToken ct)
    {
        _io.Header("Transaction Categories (COTRTLIC)");
        _io.Line("Enter a type code to filter, or leave blank for all. Type 0 to go back.");
        _io.Line();

        var filter = _io.Prompt("Filter by type code (blank = all): ");
        if (filter is null || filter == ConsoleIo.BackMarker) { return; }
        var typeCode = filter.Length == 0 ? null : filter;

        var page = 1;
        while (true)
        {
            var result = await _svc.ListCategoriesAsync(typeCode, page, PageSize, ct);
            if (!result.Success || result.Value is null)
            {
                _io.Header("Transaction Categories (COTRTLIC)");
                _io.ShowError(result.Message.Length == 0 ? "No transaction categories available." : result.Message);
                _io.PressEnter();
                return;
            }

            var data = result.Value;
            _io.Header("Transaction Categories (COTRTLIC)");
            _io.Line(typeCode is null ? $"Page {data.Page}" : $"Type {typeCode}    Page {data.Page}");
            _io.Rule('-');
            _io.Line($"{"Type",-6} {"Cat",-6} Description");
            _io.Rule('-');
            if (data.Items.Count == 0)
            {
                _io.Line("(no transaction categories)");
            }
            foreach (var c in data.Items)
            {
                _io.Line($"{c.TypeCode.Trim(),-6} {c.CategoryCode.Trim(),-6} {c.Description.TrimEnd()}");
            }
            _io.Rule('-');
            _io.Line(Nav(data.HasPrevious, data.HasNext));

            var choice = _io.MenuChoice();
            if (choice is null || choice == "0" || choice == "X")
            {
                return;
            }
            if (choice == "N" && data.HasNext) { page++; continue; }
            if (choice == "P" && data.HasPrevious) { page--; continue; }
        }
    }

    private async Task UpsertCategoryAsync(CancellationToken ct)
    {
        _io.Header("Add / Update Transaction Category (COTRTUPC)");
        _io.Line("Enter the category details. Type 0 at any prompt to cancel.");
        _io.Line();

        var typeCode = _io.PromptRequired("Type code (2 chars): ");
        if (typeCode is null || typeCode == ConsoleIo.BackMarker) { return; }

        var categoryCode = _io.PromptRequired("Category code (4 digits): ");
        if (categoryCode is null || categoryCode == ConsoleIo.BackMarker) { return; }

        var description = _io.PromptRequired("Description: ");
        if (description is null || description == ConsoleIo.BackMarker) { return; }

        var result = await _svc.UpsertCategoryAsync(typeCode, categoryCode, description, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private async Task DeleteCategoryAsync(CancellationToken ct)
    {
        _io.Header("Delete Transaction Category (COTRTUPC)");
        _io.Line("Enter the type and category codes to delete, or 0 to go back.");
        _io.Line();

        var typeCode = _io.PromptRequired("Type code: ");
        if (typeCode is null || typeCode == ConsoleIo.BackMarker) { return; }

        var categoryCode = _io.PromptRequired("Category code: ");
        if (categoryCode is null || categoryCode == ConsoleIo.BackMarker) { return; }

        var confirm = _io.Prompt($"Confirm deletion of category {typeCode}/{categoryCode}? (Y/N): ");
        if (confirm is null || confirm == ConsoleIo.BackMarker) { return; }
        if (!string.Equals(confirm, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _io.ShowInfo("Deletion cancelled.");
            _io.PressEnter();
            return;
        }

        var result = await _svc.DeleteCategoryAsync(typeCode, categoryCode, ct);
        _io.ShowResult(result);
        _io.PressEnter();
    }

    private static string Nav(bool hasPrevious, bool hasNext)
    {
        var options = new List<string>();
        if (hasPrevious) { options.Add("P=Prev"); }
        if (hasNext) { options.Add("N=Next"); }
        options.Add("0=Back");
        return string.Join("    ", options);
    }
}
