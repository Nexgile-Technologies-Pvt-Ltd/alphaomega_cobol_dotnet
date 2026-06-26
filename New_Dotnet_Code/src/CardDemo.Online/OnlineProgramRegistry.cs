using CardDemo.Data;
using CardDemo.Online.Programs;

namespace CardDemo.Online;

/// <summary>
/// Builds the in-process <see cref="ProgramRegistry"/> wiring all 17 base online CICS handlers plus the
/// 5 optional online add-on handlers (<c>CardDemo.Online.Programs</c>) over one shared
/// <see cref="RelationalDb"/>. Each handler is registered under its CICS <c>PROGRAM</c> name (the XCTL/LINK
/// target) and its self-declared <c>TRANSID</c>, per the consolidated dispatcher map in
/// <c>_design/specs/optional/CSD_TRANSACTIONS.md</c>:
/// <code>
/// CC00->COSGN00C  CM00->COMEN01C  CA00->COADM01C
/// CAVW->COACTVWC  CAUP->COACTUPC  CCLI->COCRDLIC  CCDL->COCRDSLC  CCUP->COCRDUPC
/// CT00->COTRN00C  CT01->COTRN01C  CT02->COTRN02C  CB00->COBIL00C  CR00->CORPT00C
/// CU00->COUSR00C  CU01->COUSR01C  CU02->COUSR02C  CU03->COUSR03C
/// CTLI->COTRTLIC  CTTU->COTRTUPC                            (DB2 transaction-type add-on, plan CARDDEMO)
/// CPVS->COPAUS0C  CPVD->COPAUS1C                            (IMS/DB2/MQ pending-auth add-on)
/// </code>
/// Construction with the shared DB means every transaction (and every XCTL hop between them) reads/writes
/// the same seeded VSAM-equivalent tables.
/// </summary>
/// <remarks>
/// <para>Two optional programs are deliberately <b>not</b> registered as dispatchable transactions, per the
/// CSD routing spec §Discrepancies:</para>
/// <list type="bullet">
/// <item><c>COPAUS2C</c> (CRDDEMO2) declares <c>TRANSID(CPVD)</c> on its <c>DEFINE PROGRAM</c> but there is
/// no <c>DEFINE TRANSACTION(CPVD)</c> routing to it — <c>CPVD</c> routes to <c>COPAUS1C</c>. <c>COPAUS2C</c>
/// is reached only by XCTL/LINK from <c>COPAUS1C</c>, not by typing a TRANSID, and it is not an
/// <c>ITransactionHandler</c>. It needs no registry entry.</item>
/// <item><c>COPAUA0C</c> / <c>COACCT01</c> / <c>CODATE01</c> (CRDDEMO2 / CRDDEMOM auth+VSAM-MQ) are MQ
/// request/response <i>servers</i> (no BMS screen), wired into the in-proc <c>MqBroker</c> dispatcher in
/// <c>CardDemo.Mq</c>, not the 3270 online dispatcher. <c>CODATE01</c>/<c>COACCT01</c> carry the utility
/// TRANSIDs <c>CDRD</c>/<c>CDRA</c> but run mapset-less off the trigger monitor.</item>
/// <item>The batch optional programs (<c>COBTUPDT</c> DB2, <c>CBPAUP0C</c> IMS) are JCL-driven and invoked
/// by JobControl — they have no online dispatcher entry.</item>
/// </list>
/// </remarks>
public static class OnlineProgramRegistry
{
    /// <summary>
    /// Registers all 17 base + 5 optional online handlers over <paramref name="db"/> and returns the
    /// populated registry. The factories always hand back a DB-backed instance, so the handler used to run a
    /// turn (and the one-shot probe the registry makes to learn the program/transaction names) has its data
    /// accessor wired.
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
            .Register(() => new Cousr03c(db))   // COUSR03C / CU03 — user delete
            // ---- Optional add-on online handlers (DB2 transaction-type + IMS/DB2/MQ pending-auth) ----
            .Register(() => new Cotrtlic(db))   // COTRTLIC / CTLI — transaction-type list/update (DB2)
            .Register(() => new Cotrtupc(db))   // COTRTUPC / CTTU — transaction-type maintenance (DB2)
            .Register(() => new Copaus0c(db))   // COPAUS0C / CPVS — pending-auth view summary (IMS)
            .Register(() => new Copaus1c(db));  // COPAUS1C / CPVD — pending-auth view detail (IMS, plan AWS01PLN)
    }
}
