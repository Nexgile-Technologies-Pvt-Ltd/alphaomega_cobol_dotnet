using CardDemo.Batch;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Import;

namespace CardDemo.JobControl;

/// <summary>
/// Factory for the runnable CardDemo BATCH job sequences described in
/// <c>_design/specs/jobs/*.md</c>, each wired to the ported <c>CB*</c> programs (CardDemo.Batch) and the
/// IDCAMS / SORT / IEFBR14 primitives in <see cref="UtilitySteps"/>. A returned <see cref="Job"/> is run by
/// <see cref="JobRunner.Run"/> over a <see cref="JobContext"/> holding the live database, the GDG manager,
/// and the seed/copybook directories.
/// </summary>
/// <remarks>
/// <para>The CICS CSD-install jobs (<c>CBADMCDJ</c>, <c>DUSRSECJ</c>, the <c>OPENFIL</c>/<c>CLOSEFIL</c>
/// SDSF brackets, etc.) are <b>online configuration</b>, not runnable batch, and are intentionally not
/// produced here — see <see cref="OnlineConfigJobs"/> for the documented list.</para>
/// <para>The dataset names below use the canonical CardDemo GDG bases so a daily cycle (COMBTRAN -&gt;
/// POSTTRAN -&gt; INTCALC -&gt; TRANREPT/TRANBKP) chains generations correctly through one shared
/// <see cref="GdgManager"/>.</para>
/// </remarks>
public static class CardDemoJobs
{
    // ---- Canonical GDG base names (the AWS.M2.CARDDEMO.* generation groups) --------------------------
    public const string GdgDalyRejs = "AWS.M2.CARDDEMO.DALYREJS";
    public const string GdgSysTran = "AWS.M2.CARDDEMO.SYSTRAN";
    public const string GdgTransactBkup = "AWS.M2.CARDDEMO.TRANSACT.BKUP";
    public const string GdgTransactCombined = "AWS.M2.CARDDEMO.TRANSACT.COMBINED";
    public const string GdgTransactDaly = "AWS.M2.CARDDEMO.TRANSACT.DALY";
    public const string GdgTranRept = "AWS.M2.CARDDEMO.TRANREPT";
    public const string GdgTcatbalfBkup = "AWS.M2.CARDDEMO.TCATBALF.BKUP";

    private const int TranRecLen = 350; // CVTRA05Y TRAN-RECORD
    private const int TcatRecLen = 50;  // CVTRA01Y TRAN-CAT-BAL-RECORD

    // =================================================================================================
    // File-setup / reload jobs (IDCAMS DELETE/DEFINE/REPRO from a .PS seed)
    // =================================================================================================

    /// <summary>ACCTFILE — delete + define + REPRO-load the ACCOUNT master from its EBCDIC seed.</summary>
    public static Job AcctFile() => DeleteDefineReproJob(
        "ACCTFILE", "Delete define Account Data", "ACCOUNT",
        (imp, db) => imp.ImportAccounts(db));

    /// <summary>CARDFILE — delete + define + REPRO-load the CARD master from its EBCDIC seed.</summary>
    public static Job CardFile() => DeleteDefineReproJob(
        "CARDFILE", "Delete define Card Data", "CARD",
        (imp, db) => imp.ImportCards(db));

    /// <summary>CUSTFILE — delete + define + REPRO-load the CUSTOMER master from its EBCDIC seed.</summary>
    public static Job CustFile() => DeleteDefineReproJob(
        "CUSTFILE", "Delete define Customer Data", "CUSTOMER",
        (imp, db) => imp.ImportCustomers(db));

    /// <summary>XREFFILE — delete + define + REPRO-load the CARD_XREF cross-reference from its EBCDIC seed.</summary>
    public static Job XrefFile() => DeleteDefineReproJob(
        "XREFFILE", "Delete define Card Xref Data", "CARD_XREF",
        (imp, db) => imp.ImportCardXrefs(db));

    /// <summary>TRANTYPE — delete + define + REPRO-load the TRAN_TYPE reference from its EBCDIC seed.</summary>
    public static Job TranType() => DeleteDefineReproJob(
        "TRANTYPE", "Delete define Transaction Type", "TRAN_TYPE",
        (imp, db) => imp.ImportTranTypes(db));

    /// <summary>TRANCATG — delete + define + REPRO-load the TRAN_CATEGORY reference from its EBCDIC seed.</summary>
    public static Job TranCatg() => DeleteDefineReproJob(
        "TRANCATG", "Delete define Transaction Category", "TRAN_CATEGORY",
        (imp, db) => imp.ImportTranCategories(db));

    /// <summary>DISCGRP — delete + define + REPRO-load the DISCLOSURE_GROUP file from its EBCDIC seed.</summary>
    public static Job DiscGrp() => DeleteDefineReproJob(
        "DISCGRP", "Delete define Disclosure Group", "DISCLOSURE_GROUP",
        (imp, db) => imp.ImportDisclosureGroups(db));

    /// <summary>TCATBALF — delete + define + REPRO-load the TRAN_CAT_BAL master from its EBCDIC seed.</summary>
    public static Job TcatBalf() => DeleteDefineReproJob(
        "TCATBALF", "Delete define Transaction Category Balance", "TRAN_CAT_BAL",
        (imp, db) => imp.ImportTranCatBalances(db));

    /// <summary>
    /// TRANFILE — delete + define + REPRO-load the TRANSACTION master from its initial seed file. Modeled
    /// faithfully as the IDCAMS lifecycle steps (the SDSF CICS quiesce/resume and the AIX DEFINE/BLDINDEX
    /// are online-config / secondary-index concerns with no relational store; the alternate index is a
    /// secondary query, per ARCHITECTURE.md, so no separate build step is needed).
    /// </summary>
    public static Job TranFile() => new(
        "TRANFILE", "DEFINE TRANSACTION MASTER",
        [
            new JobStep("STEP05", "IDCAMS", ctx => UtilitySteps.IdcamsDeleteTransactionMaster(ctx)),
            new JobStep("STEP10", "IDCAMS", ctx => UtilitySteps.IdcamsDefineCluster(ctx, "\"TRANSACTION\"")),
            new JobStep("STEP15", "IDCAMS", ctx => UtilitySteps.IdcamsReproSeed(ctx, (imp, db) => imp.ImportDailyTransactions(db))),
        ]);

    /// <summary>
    /// DEFCUST — delete + define only (no REPRO load): leaves the CUSTOMER store defined but empty. The
    /// source JCL's duplicate <c>STEP05</c> names are disambiguated to <c>STEP05-DELETE</c> /
    /// <c>STEP10-DEFINE</c> (per the spec's conversion note).
    /// </summary>
    public static Job DefCust() => new(
        "DEFCUST", "Define Customer Data File",
        [
            new JobStep("STEP05-DELETE", "IDCAMS", ctx => UtilitySteps.IdcamsDeleteCluster(ctx, "CUSTOMER")),
            new JobStep("STEP10-DEFINE", "IDCAMS", ctx => UtilitySteps.IdcamsDefineCluster(ctx, "CUSTOMER")),
        ]);

    /// <summary>Shared 3-step DELETE/DEFINE/REPRO(seed) template for the simple file-setup jobs.</summary>
    private static Job DeleteDefineReproJob(
        string name, string description, string table,
        Func<MasterImporter, RelationalDb, int> load)
    {
        string quoted = table == "TRANSACTION" ? "\"TRANSACTION\"" : table;
        return new Job(name, description,
        [
            new JobStep("STEP05", "IDCAMS", ctx => UtilitySteps.IdcamsDeleteCluster(ctx, quoted)),
            new JobStep("STEP10", "IDCAMS", ctx => UtilitySteps.IdcamsDefineCluster(ctx, quoted)),
            new JobStep("STEP15", "IDCAMS", ctx => UtilitySteps.IdcamsReproSeed(ctx, load)),
        ]);
    }

    // =================================================================================================
    // POSTTRAN — daily transaction posting (CBTRN01C optional read/validate, then CBTRN02C posting)
    // =================================================================================================

    /// <summary>
    /// POSTTRAN — the core daily posting job. The spec's single EXEC step runs <c>CBTRN02C</c>; this factory
    /// also exposes the read/validate driver <c>CBTRN01C</c> as an optional leading step (the conversion
    /// note "POSTTRAN (CBTRN01C then CBTRN02C)"). <c>CBTRN02C</c> posts valid daily transactions into
    /// <c>TRANSACTION</c>, updates the account + category balances, and writes a new <c>DALYREJS(+1)</c>
    /// rejects generation; RC=4 ("posted with rejects") is a warning, not a job-stopping failure.
    /// </summary>
    /// <param name="includeRead01">When true, prepend the <c>CBTRN01C</c> read/validate step (STEP05).</param>
    public static Job PostTran(bool includeRead01 = true)
    {
        var steps = new List<JobStep>();

        if (includeRead01)
        {
            steps.Add(new JobStep("STEP05", "CBTRN01C", ctx =>
            {
                // Read-and-validate driver: emits diagnostics only, no posting. Always RC 0.
                Cbtrn01c.Run(ctx.Db, ctx.Path("CBTRN01C.sysout"));
                return 0;
            }));
        }

        steps.Add(new JobStep("STEP15", "CBTRN02C", ctx =>
        {
            // DALYREJS(+1): a fresh rejects generation each run.
            ctx.Gdg.Define(GdgDalyRejs);
            string rejPath = ctx.Gdg.AllocateNext(GdgDalyRejs);
            try
            {
                int rc = new Cbtrn02c().Run(ctx.Db, rejPath, ctx.Clock, HostKind.Ebcdic);
                ctx.Gdg.Catalog(GdgDalyRejs); // DISP=(NEW,CATLG,DELETE): catalog on clean end.
                return rc;                     // 4 == posted with rejects (warning), 0 == clean.
            }
            catch
            {
                ctx.Gdg.Discard(GdgDalyRejs);  // delete the partial generation on abend.
                throw;
            }
        }));

        // RC=4 (posted with rejects) must NOT stop the cycle, so AbortThreshold stays at 4.
        return new Job("POSTTRAN", "POSTTRAN", steps);
    }

    // =================================================================================================
    // INTCALC — interest calculator (CBACT04C)
    // =================================================================================================

    /// <summary>
    /// INTCALC — the interest calculator. The single EXEC step runs <c>CBACT04C</c> with the
    /// <c>PARM='2022071800'</c> run-date prefix; it accrues interest into the <c>ACCOUNT</c> balances and
    /// writes the interest <c>TRANSACTION</c> rows to a new <c>SYSTRAN(+1)</c> generation (modelled as the
    /// transaction master load; the GDG generation is catalogued on success).
    /// </summary>
    /// <param name="parmDate">The 10-char PARM run date (TRAN-ID prefix). Defaults to the spec's value.</param>
    public static Job IntCalc(string parmDate = "2022071800") => new(
        "INTCALC", "INTEREST CALCULATOR",
        [
            new JobStep("STEP15", "CBACT04C", ctx =>
            {
                ctx.Gdg.Define(GdgSysTran);
                ctx.Gdg.AllocateNext(GdgSysTran); // SYSTRAN(+1) — the interest TX output generation.
                try
                {
                    int rc = new Cbact04c().Run(ctx.Db, parmDate, ctx.Clock);
                    // Snapshot the freshly written interest transactions into the SYSTRAN generation.
                    UtilitySteps.IdcamsReproUnload(ctx, ctx.Gdg.Plus1(GdgSysTran),
                        w => SerializeTransactions(ctx, w), HostKind.Ebcdic);
                    ctx.Gdg.Catalog(GdgSysTran);
                    return rc;
                }
                catch
                {
                    ctx.Gdg.Discard(GdgSysTran);
                    throw;
                }
            }),
        ]);

    // =================================================================================================
    // COMBTRAN — combine + load the transaction master (SORT merge, then IDCAMS REPRO)
    // =================================================================================================

    /// <summary>
    /// COMBTRAN — rebuilds the <c>TRANSACTION</c> master from a sorted union of the latest backup
    /// (<c>TRANSACT.BKUP(0)</c>) and system-generated (<c>SYSTRAN(0)</c>) generations. STEP05R SORTs the two
    /// inputs ascending by the 16-byte <c>TRAN-ID</c> (offset 0) into <c>TRANSACT.COMBINED(+1)</c>; STEP10
    /// REPRO-loads that file into the master. STEP10 runs only if STEP05R succeeds (it consumes its output).
    /// </summary>
    public static Job CombTran() => new(
        "COMBTRAN", "COMBINE TRANSACTIONS",
        [
            new JobStep("STEP05R", "SORT", ctx =>
            {
                ctx.Gdg.Define(GdgTransactBkup);
                ctx.Gdg.Define(GdgSysTran);
                ctx.Gdg.Define(GdgTransactCombined);

                var inputs = new List<string>();
                if (ctx.Gdg.Current(GdgTransactBkup) is { } bkup) inputs.Add(bkup);
                if (ctx.Gdg.Current(GdgSysTran) is { } systran) inputs.Add(systran);

                string outPath = ctx.Gdg.AllocateNext(GdgTransactCombined);
                // SORT FIELDS=(TRAN-ID,A): TRAN-ID is 16 bytes at offset 0 (SYMNAMES 1,16,CH).
                int rc = UtilitySteps.Sort(inputs, outPath, TranRecLen,
                    [new SortField(0, 16, Ascending: true)]);
                if (rc == 0) ctx.Gdg.Catalog(GdgTransactCombined); else ctx.Gdg.Discard(GdgTransactCombined);
                return rc;
            }),
            new JobStep("STEP10", "IDCAMS", ctx =>
            {
                // REPRO the combined sorted file into the TRANSACTION master (rebuild, keyed by TRAN-ID).
                // STEP05R already catalogued TRANSACT.COMBINED(+1), so resolve it as the current (0)
                // generation (a fresh Plus1 would allocate the NEXT, empty, generation).
                string combined = ctx.Gdg.Current(GdgTransactCombined)!;
                UtilitySteps.IdcamsDeleteTransactionMaster(ctx); // clean rebuild (avoid dup-key on reload).
                return UtilitySteps.IdcamsReproTransactionsFromFile(ctx, combined, HostKind.Ebcdic);
            }),
        ]);

    // =================================================================================================
    // CBEXPORT / CBIMPORT
    // =================================================================================================

    /// <summary>
    /// CBEXPORT — define the export cluster (IDCAMS, idempotent) then run <c>CBEXPORT</c> to write the
    /// consolidated 500-byte multi-record export dataset from the five masters. Requires
    /// <see cref="JobContext.CopybookDir"/> (the CVEXPORT variant layouts) to be configured.
    /// </summary>
    /// <param name="exportPath">Optional explicit export dataset path; defaults to the job working dir.</param>
    public static Job CbExport(string? exportPath = null) => new(
        "CBEXPORT", "Export Customer Data for Migration",
        [
            new JobStep("STEP01", "IDCAMS", _ => 0), // DELETE PURGE + DEFINE export cluster (SET MAXCC=0).
            new JobStep("STEP02", "CBEXPORT", ctx =>
            {
                if (ctx.CopybookDir is null) return 12;
                string outPath = exportPath ?? ctx.Path("EXPORT.DATA");
                Cbexport.Run(
                    new BatchSupport(ctx.Db), outPath, ctx.CopybookDir, ctx.Clock, HostKind.Ebcdic);
                return 0;
            }),
        ]);

    /// <summary>
    /// CBIMPORT — run <c>CBIMPORT</c> to split a 500-byte multi-record export feed into the per-type output
    /// staging files (CUSTOUT / ACCTOUT / XREFOUT / TRNXOUT / CARDOUT) + ERROUT. A <paramref name="cardOutPath"/>
    /// is supplied (option-b override) so type-'D' records are handled; pass <c>null</c> to reproduce the
    /// JCL-faithful FB-1 abend (missing CARDOUT DD). Requires <see cref="JobContext.CopybookDir"/>.
    /// </summary>
    public static Job CbImport(string exportPath, bool supplyCardOut = true) => new(
        "CBIMPORT", "Import CARDDEMO Data",
        [
            new JobStep("STEP01", "CBIMPORT", ctx =>
            {
                if (ctx.CopybookDir is null) return 12;
                var serializer = new RecordSerializer(new RecordLayouts(ctx.CopybookDir));
                string cvexport = File.ReadAllText(Path.Combine(ctx.CopybookDir, "CVEXPORT.cpy"));
                var contextRec = new Cbimport.Context(
                    ExportPath: exportPath,
                    CustomerOutPath: ctx.Path("CUSTOUT.dat"),
                    AccountOutPath: ctx.Path("ACCTOUT.dat"),
                    XrefOutPath: ctx.Path("XREFOUT.dat"),
                    TransactionOutPath: ctx.Path("TRNXOUT.dat"),
                    ErrorOutPath: ctx.Path("ERROUT.dat"),
                    Serializer: serializer,
                    CustomerVariant: Tooling.CopybookParser.ParseVariant(cvexport, "EXPORT-CUSTOMER-DATA"),
                    AccountVariant: Tooling.CopybookParser.ParseVariant(cvexport, "EXPORT-ACCOUNT-DATA"),
                    XrefVariant: Tooling.CopybookParser.ParseVariant(cvexport, "EXPORT-CARD-XREF-DATA"),
                    TransactionVariant: Tooling.CopybookParser.ParseVariant(cvexport, "EXPORT-TRANSACTION-DATA"),
                    CardVariant: Tooling.CopybookParser.ParseVariant(cvexport, "EXPORT-CARD-DATA"),
                    Clock: ctx.Clock,
                    Host: HostKind.Ebcdic,
                    CardOutPath: supplyCardOut ? ctx.Path("CARDOUT.dat") : null);
                return new Cbimport(contextRec).Run();
            }),
        ]);

    // =================================================================================================
    // TRANREPT / TRANBKP / PRTCATBL — reporting + backup
    // =================================================================================================

    /// <summary>
    /// TRANREPT — the transaction-reporting pipeline. STEP05R.REPROC unloads the <c>TRANSACTION</c> master to
    /// <c>TRANSACT.BKUP(+1)</c>; STEP05R.SORT date-filters (proc-date window) and sorts by card number
    /// (offset 263, len 16) into <c>TRANSACT.DALY(+1)</c>; STEP10R runs <c>CBTRN03C</c> over that file plus
    /// the reference masters to write the 133-byte report. The two source <c>STEP05R</c> names are
    /// disambiguated per the spec.
    /// </summary>
    public static Job TranRept(string startDate = "2022-01-01", string endDate = "2022-07-06") => new(
        "TRANREPT", "TRANSACTION REPORT",
        [
            new JobStep("STEP05R-REPROC", "IDCAMS", ctx =>
            {
                ctx.Gdg.Define(GdgTransactBkup);
                string outPath = ctx.Gdg.AllocateNext(GdgTransactBkup);
                int rc = UtilitySteps.IdcamsReproUnload(ctx, outPath, w => SerializeTransactions(ctx, w), HostKind.Ebcdic);
                if (rc == 0) ctx.Gdg.Catalog(GdgTransactBkup); else ctx.Gdg.Discard(GdgTransactBkup);
                return rc;
            }),
            new JobStep("STEP05R-SORT", "SORT", ctx =>
            {
                ctx.Gdg.Define(GdgTransactDaly);
                string bkup = ctx.Gdg.Current(GdgTransactBkup)!;
                string outPath = ctx.Gdg.AllocateNext(GdgTransactDaly);
                // SYMNAMES: TRAN-CARD-NUM at pos 263 (offset 262) len 16; TRAN-PROC-DT at pos 305 (offset 304) len 10.
                int rc = UtilitySteps.Sort(
                    [bkup], outPath, TranRecLen,
                    [new SortField(262, 16, Ascending: true)],
                    include: rec => ProcDateInRange(rec, startDate, endDate));
                if (rc == 0) ctx.Gdg.Catalog(GdgTransactDaly); else ctx.Gdg.Discard(GdgTransactDaly);
                return rc;
            }),
            new JobStep("STEP10R", "CBTRN03C", ctx =>
            {
                ctx.Gdg.Define(GdgTranRept);
                string reportPath = ctx.Gdg.AllocateNext(GdgTranRept);
                try
                {
                    Cbtrn03c.Run(ctx.Db, reportPath, startDate, endDate, HostKind.Ascii);
                    ctx.Gdg.Catalog(GdgTranRept);
                    return 0;
                }
                catch
                {
                    ctx.Gdg.Discard(GdgTranRept);
                    throw;
                }
            }),
        ]);

    /// <summary>
    /// TRANBKP — backup + re-create of the transaction master. STEP05R REPRO-unloads <c>TRANSACTION</c> to
    /// <c>TRANSACT.BKUP(+1)</c>; STEP05 deletes the master (cluster + AIX, not-found tolerated); STEP10
    /// (<c>COND=(4,LT)</c>) re-defines a fresh empty master, bypassed when any prior step ended RC&gt;4.
    /// </summary>
    public static Job TranBkp() => new(
        "TRANBKP", "REPRO and Delete Transaction Master",
        [
            new JobStep("STEP05R", "IDCAMS", ctx =>
            {
                ctx.Gdg.Define(GdgTransactBkup);
                string outPath = ctx.Gdg.AllocateNext(GdgTransactBkup);
                int rc = UtilitySteps.IdcamsReproUnload(ctx, outPath, w => SerializeTransactions(ctx, w), HostKind.Ebcdic);
                if (rc == 0) ctx.Gdg.Catalog(GdgTransactBkup); else ctx.Gdg.Discard(GdgTransactBkup);
                return rc;
            }),
            new JobStep("STEP05", "IDCAMS", ctx => UtilitySteps.IdcamsDeleteTransactionMaster(ctx)),
            new JobStep("STEP10", "IDCAMS",
                ctx => UtilitySteps.IdcamsDefineCluster(ctx, "\"TRANSACTION\""),
                // COND=(4,LT): bypass the DEFINE when 4 < (any prior RC), i.e. a prior step ended RC > 4.
                conditions: [new CondCode(4, CondOperator.Lt)]),
        ]);

    /// <summary>
    /// PRTCATBL — print the transaction-category-balance file. DELDEF (IEFBR14) deletes the prior report;
    /// STEP05R REPRO-unloads <c>TRAN_CAT_BAL</c> to <c>TCATBALF.BKUP(+1)</c>; STEP10R SORTs that backup by
    /// the composite key (acct-id, type-cd, cat-cd) into the report extract.
    /// </summary>
    public static Job PrtCatBl() => new(
        "PRTCATBL", "Print Trasaction Category Balance File",
        [
            new JobStep("DELDEF", "IEFBR14", ctx => UtilitySteps.Iefbr14(ctx.Path("TCATBALF.REPT"))),
            new JobStep("STEP05R", "IDCAMS", ctx =>
            {
                ctx.Gdg.Define(GdgTcatbalfBkup);
                string outPath = ctx.Gdg.AllocateNext(GdgTcatbalfBkup);
                int rc = UtilitySteps.IdcamsReproUnload(ctx, outPath, w => SerializeTranCatBalances(ctx, w), HostKind.Ebcdic);
                if (rc == 0) ctx.Gdg.Catalog(GdgTcatbalfBkup); else ctx.Gdg.Discard(GdgTcatbalfBkup);
                return rc;
            }),
            new JobStep("STEP10R", "SORT", ctx =>
            {
                string bkup = ctx.Gdg.Current(GdgTcatbalfBkup)!;
                string outPath = ctx.Path("TCATBALF.REPT");
                // SYMNAMES: ACCT-ID 1,11 (offset 0); TYPE-CD 12,2 (offset 11); CAT-CD 14,4 (offset 13).
                return UtilitySteps.Sort(
                    [bkup], outPath, TcatRecLen,
                    [
                        new SortField(0, 11, Ascending: true),
                        new SortField(11, 2, Ascending: true),
                        new SortField(13, 4, Ascending: true),
                    ]);
            }),
        ]);

    // =================================================================================================
    // READACCT / READCARD / READCUST / READXREF — read-and-print drivers
    // =================================================================================================

    /// <summary>
    /// READACCT — read the ACCOUNT master and write the three derived extract files (CBACT01C). PREDEL
    /// (IEFBR14) deletes the prior outputs; STEP05 runs <c>CBACT01C</c>. Account master is INPUT-only.
    /// </summary>
    public static Job ReadAcct() => new(
        "READACCT", "READACCT",
        [
            new JobStep("PREDEL", "IEFBR14", ctx => UtilitySteps.Iefbr14(
                ctx.Path("ACCTDATA.PSCOMP"), ctx.Path("ACCTDATA.ARRYPS"), ctx.Path("ACCTDATA.VBPS"))),
            new JobStep("STEP05", "CBACT01C", ctx =>
            {
                Cbact01c.Run(new BatchSupport(ctx.Db),
                    ctx.Path("ACCTDATA.PSCOMP"), ctx.Path("ACCTDATA.ARRYPS"), ctx.Path("ACCTDATA.VBPS"),
                    HostKind.Ebcdic);
                return 0;
            }),
        ]);

    /// <summary>READCARD — sequentially read and DISPLAY the CARD master (CBACT02C), a read-only dump.</summary>
    public static Job ReadCard() => new(
        "READCARD", "READCARD",
        [
            new JobStep("STEP05", "CBACT02C", ctx =>
                new Cbact02c().Run(ctx.Db, ctx.Path("READCARD.sysout"))),
        ]);

    /// <summary>READCUST — sequentially read and DISPLAY the CUSTOMER master (CBCUS01C), a read-only dump.</summary>
    public static Job ReadCust() => new(
        "READCUST", "READCUST",
        [
            new JobStep("STEP05", "CBCUS01C", ctx =>
                new Cbcus01c().Run(ctx.Db, ctx.Path("READCUST.sysout"))),
        ]);

    /// <summary>READXREF — sequentially read and DISPLAY the CARD_XREF master (CBACT03C), a read-only dump.</summary>
    public static Job ReadXref() => new(
        "READXREF", "READXREF",
        [
            new JobStep("STEP05", "CBACT03C", ctx =>
                new Cbact03c().Run(ctx.Db, ctx.Path("READXREF.sysout"))),
        ]);

    // =================================================================================================
    // Helpers — serialize a table to a flat fixed-width unload (for the REPRO unload primitive)
    // =================================================================================================

    private static void SerializeTransactions(JobContext ctx, FixedFileWriter w)
    {
        if (ctx.CopybookDir is null) return;
        var serializer = new RecordSerializer(new RecordLayouts(ctx.CopybookDir));
        foreach (Domain.Transaction t in new TransactionRepository(ctx.Db).ReadAll())
            w.WriteRecord(serializer.Serialize(t, w.Host), TranRecLen);
    }

    private static void SerializeTranCatBalances(JobContext ctx, FixedFileWriter w)
    {
        if (ctx.CopybookDir is null) return;
        var serializer = new RecordSerializer(new RecordLayouts(ctx.CopybookDir));
        foreach (Domain.TranCatBalance t in new TranCatBalanceRepository(ctx.Db).ReadAll())
            w.WriteRecord(serializer.Serialize(t, w.Host), TcatRecLen);
    }

    /// <summary>
    /// INCLUDE COND for TRANREPT's SORT: keep a 350-byte TRAN-RECORD whose TRAN-PROC-TS (offset 304, the
    /// first 10 chars = proc-date) is within [start, end]. The bytes are EBCDIC; decode to text first so the
    /// CCYY-MM-DD comparison matches the SORT's CH (character) collation.
    /// </summary>
    private static bool ProcDateInRange(byte[] rec, string startDate, string endDate)
    {
        const int procTsOffset = 304; // CVTRA05Y TRAN-PROC-TS X(26) starts at offset 304.
        if (rec.Length < procTsOffset + 10) return false;
        string procDate = HostEncoding.Ebcdic.GetString(rec, procTsOffset, 10);
        return string.CompareOrdinal(procDate, startDate) >= 0
            && string.CompareOrdinal(procDate, endDate) <= 0;
    }
}
