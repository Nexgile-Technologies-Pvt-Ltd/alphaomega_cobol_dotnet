using CardDemo.ConsoleApp.Maps;
using CardDemo.Data;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.ConsoleApp;

/// <summary>
/// The single wiring point that turns the 17 base + 5 optional ported online CICS handlers
/// (<c>CardDemo.Online.Programs</c>) into a live <see cref="IProgramRegistry"/> and a matching
/// <see cref="BmsMapCatalog"/> over one shared <see cref="RelationalDb"/>. Each handler is registered under
/// its CICS <c>PROGRAM</c> name (the XCTL/LINK target) and its self-declared <c>TRANSID</c> (per
/// <c>_design/specs/optional/CSD_TRANSACTIONS.md</c>), constructed with the shared DB so every transaction
/// sees the same seeded VSAM-equivalent tables.
/// </summary>
/// <remarks>
/// <para>Every handler in <c>CardDemo.Online.Programs</c> is a near-mechanical port of its COBOL
/// <c>PROCEDURE DIVISION</c>; it builds its own <see cref="BmsMap"/> (from the program's static
/// <c>BuildMap()</c>/<c>BuildBmsMap()</c>) and passes that map object straight to
/// <see cref="IScreenIo.SendMap"/>/<see cref="IScreenIo.ReceiveMap"/>. The <see cref="ConsoleScreenIo"/>
/// renderer therefore renders the handler's own model directly; the <see cref="BmsMapCatalog"/> built here
/// is the by-name fallback the renderer uses when a turn names a map without carrying its model (e.g. a
/// fresh RECEIVE after a SEND TEXT), so it is kept in lock-step with the registry — one map factory per
/// registered program.</para>
/// <para>The registry probes each factory once to learn its <c>ProgramName</c>/<c>TransId</c>; the handlers
/// expose a parameterless ctor for that probe, but the factories below always hand back a DB-backed
/// instance so the resolved handler used to run a turn has its data accessor wired.</para>
/// </remarks>
public static class OnlinePrograms
{
    /// <summary>
    /// Builds the program registry for all 17 base + 5 optional online transactions over the shared
    /// <paramref name="db"/>. Program and transaction routing follow the consolidated CSD dispatcher map.
    /// Delegates to <see cref="OnlineProgramRegistry.Build"/> (the registry lives in
    /// <c>CardDemo.Online</c> so headless tests share it without referencing the console executable).
    /// </summary>
    public static ProgramRegistry BuildRegistry(RelationalDb db) => OnlineProgramRegistry.Build(db);

    /// <summary>
    /// Builds the BMS map catalog covering every map driven by the 17 base + 5 optional handlers, keyed by
    /// DFHMDI map name. Each entry is the handler's own static map builder, so a by-name lookup produces the
    /// same field model the handler would build itself.
    /// </summary>
    public static BmsMapCatalog BuildMapCatalog()
    {
        return new BmsMapCatalog()
            .Register("COSGN0A", SignOnProgram.BuildMap)      // sign-on
            .Register(MainMenuProgram.MapName, MainMenuProgram.BuildBmsMap)
            .Register(AdminMenuProgram.MapName, AdminMenuProgram.BuildBmsMap)
            .Register(AccountViewProgram.MapName, AccountViewProgram.BuildMap)
            .Register(AccountUpdateProgram.MapName, AccountUpdateProgram.BuildBmsMap)
            .Register(CardListProgram.MapName, CardListProgram.BuildMap)
            .Register(CardDetailViewProgram.MapName, CardDetailViewProgram.BuildMap)
            .Register(CardUpdateProgram.MapName, CardUpdateProgram.BuildMap)
            .Register(TransactionListProgram.MapName, TransactionListProgram.BuildMap)
            .Register(TransactionViewProgram.MapName, TransactionViewProgram.BuildMap)
            .Register(TransactionAddProgram.MapName, TransactionAddProgram.BuildMap)
            .Register(BillPaymentProgram.MapName, BillPaymentProgram.BuildMap)
            .Register(TransactionReportProgram.MapName, TransactionReportProgram.BuildMap)
            .Register(UserListProgram.MapName, UserListProgram.BuildMap)
            .Register(UserAddProgram.MapName, UserAddProgram.BuildMap)
            .Register(UserUpdateProgram.MapName, UserUpdateProgram.BuildMap)
            .Register(UserDeleteProgram.MapName, UserDeleteProgram.BuildMap)
            // ---- Optional add-on maps (DB2 transaction-type + IMS/DB2/MQ pending-auth) ----
            .Register(TransactionTypeListProgram.MapName, TransactionTypeListProgram.BuildMap)     // CTRTLIA — tran-type list/update (DB2)
            .Register(TransactionTypeUpdateProgram.MapName, TransactionTypeUpdateProgram.BuildBmsMap)  // CTRTUPA — tran-type maintenance (DB2)
            .Register(PendingAuthSummaryProgram.MapName, PendingAuthSummaryProgram.BuildMap)     // COPAU0A — pending-auth summary (IMS)
            .Register(PendingAuthDetailProgram.MapName, PendingAuthDetailProgram.BuildMap);    // COPAU1A — pending-auth detail (IMS)
    }
}
