using CardDemo.Data;
using CardDemo.Online.Programs;

namespace CardDemo.Online;

/// <summary>
/// Builds the in-process <see cref="ProgramRegistry"/> wiring all 17 base online CICS handlers
/// (<c>CardDemo.Online.Programs</c>) over one shared <see cref="RelationalDb"/>. Each handler is registered
/// under its CICS <c>PROGRAM</c> name (the XCTL/LINK target) and its self-declared <c>TRANSID</c>, per the
/// consolidated dispatcher map in <c>_design/specs/optional/CSD_TRANSACTIONS.md</c>:
/// <code>
/// CC00->COSGN00C  CM00->COMEN01C  CA00->COADM01C
/// CAVW->COACTVWC  CAUP->COACTUPC  CCLI->COCRDLIC  CCDL->COCRDSLC  CCUP->COCRDUPC
/// CT00->COTRN00C  CT01->COTRN01C  CT02->COTRN02C  CB00->COBIL00C  CR00->CORPT00C
/// CU00->COUSR00C  CU01->COUSR01C  CU02->COUSR02C  CU03->COUSR03C
/// </code>
/// Construction with the shared DB means every transaction (and every XCTL hop between them) reads/writes
/// the same seeded VSAM-equivalent tables.
/// </summary>
public static class OnlineProgramRegistry
{
    /// <summary>
    /// Registers all 17 base online handlers over <paramref name="db"/> and returns the populated registry.
    /// The factories always hand back a DB-backed instance, so the handler used to run a turn (and the
    /// one-shot probe the registry makes to learn the program/transaction names) has its data accessor wired.
    /// </summary>
    public static ProgramRegistry Build(RelationalDb db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new ProgramRegistry()
            // Entry + menus.
            .Register(() => new Cosgn00c(db))   // COSGN00C / CC00 — sign-on (entry point)
            .Register(() => new Comen01c(db))   // COMEN01C / CM00 — main menu
            .Register(() => new Coadm01c(db))   // COADM01C / CA00 — admin menu
            // Account / card.
            .Register(() => new Coactvwc(db))   // COACTVWC / CAVW — account view
            .Register(() => new Coactupc(db))   // COACTUPC / CAUP — account update
            .Register(() => new Cocrdlic(db))   // COCRDLIC / CCLI — credit card list
            .Register(() => new Cocrdslc(db))   // COCRDSLC / CCDL — credit card view (detail)
            .Register(() => new Cocrdupc(db))   // COCRDUPC / CCUP — credit card update
            // Transactions / bill / reports.
            .Register(() => new Cotrn00c(db))   // COTRN00C / CT00 — transaction list
            .Register(() => new Cotrn01c(db))   // COTRN01C / CT01 — transaction view
            .Register(() => new Cotrn02c(db))   // COTRN02C / CT02 — transaction add
            .Register(() => new Cobil00c(db))   // COBIL00C / CB00 — bill payment
            .Register(() => new Corpt00c(db))   // CORPT00C / CR00 — transaction reports
            // User security (admin).
            .Register(() => new Cousr00c(db))   // COUSR00C / CU00 — user list
            .Register(() => new Cousr01c(db))   // COUSR01C / CU01 — user add
            .Register(() => new Cousr02c(db))   // COUSR02C / CU02 — user update
            .Register(() => new Cousr03c(db));  // COUSR03C / CU03 — user delete
    }
}
