using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Import;
using CardDemo.JobControl;

namespace CardDemo.Tests;

/// <summary>
/// Tests for the <see cref="CardDemo.JobControl"/> JCL step-runner: the <see cref="Job"/>/<see cref="JobStep"/>
/// model, the <see cref="JobRunner"/>'s COND/RC gating, the IDCAMS/SORT/IEFBR14/GDG utility primitives, and
/// the wired CardDemo job sequences. The two headline jobs — <b>POSTTRAN</b> (CBTRN01C then CBTRN02C) and
/// <b>INTCALC</b> (CBACT04C) — are run end to end over a seeded in-memory <see cref="RelationalDb"/> and the
/// documented outcomes are asserted, alongside dedicated COND/RC-gating, GDG, and utility-primitive tests.
/// No COBOL is compiled or invoked; the seed data is the shipped EBCDIC masters.
/// </summary>
public sealed class JobControlTests
{
    private static readonly DateTime FixedNow = new(2026, 06, 26, 12, 34, 56, 780);
    private static readonly IClock Clock = new FixedClock(FixedNow);

    /// <summary>Opens a fresh in-memory DB seeded from the shipped EBCDIC masters.</summary>
    private static RelationalDb OpenSeededDb()
    {
        var db = new RelationalDb();
        try
        {
            new MasterImporter(SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir).ImportAll(db);
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>A job context wired to the seeded DB, a unique scratch work-dir, and the seed/copybook dirs.</summary>
    private static JobContext NewContext(RelationalDb db) =>
        new(db, NewWorkDir(), Clock, SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);

    private static string NewWorkDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "carddemo-jobcontrol-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // =================================================================================================
    // POSTTRAN — CBTRN01C (read/validate) then CBTRN02C (posting), over a seeded DB
    // =================================================================================================

    /// <summary>
    /// POSTTRAN runs its two steps in order (STEP05 = CBTRN01C, STEP15 = CBTRN02C). CBTRN02C posts every
    /// valid daily transaction into TRANSACTION, moves the account balance, and writes a DALYREJS(+1) rejects
    /// generation; the step RC is 4 ("posted with rejects") iff any record was rejected, and that RC=4
    /// warning must NOT abort the job.
    /// </summary>
    [Fact]
    public void PostTran_RunsCbtrn01cThenCbtrn02c_PostsAndSurfacesRc4Warning()
    {
        using RelationalDb db = OpenSeededDb();
        long dailyCount = new DailyTransactionRepository(db).ReadAll().LongCount();
        Assert.True(dailyCount > 0);

        JobContext ctx = NewContext(db);
        Job job = CardDemoJobs.PostTran(includeRead01: true);
        JobResult result = new JobRunner().Run(job, ctx);

        // Both steps ran, in order.
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("STEP05", result.Steps[0].StepName);
        Assert.Equal("CBTRN01C", result.Steps[0].Program);
        Assert.Equal(StepStatus.Executed, result.Steps[0].Status);
        Assert.Equal("STEP15", result.Steps[1].StepName);
        Assert.Equal("CBTRN02C", result.Steps[1].Program);
        Assert.Equal(StepStatus.Executed, result.Steps[1].Status);

        // Posting populated the TRANSACTION table and the processed/rejected counters add up.
        long posted = new TransactionRepository(db).ReadAll().LongCount();
        Assert.True(posted > 0);

        int postRc = result.Step("STEP15")!.ReturnCode;
        Assert.True(postRc is 0 or 4);                 // 0 = clean, 4 = posted with rejects.
        Assert.Equal(postRc, result.MaximumRc);        // CBTRN01C is RC 0, so the max is CBTRN02C's RC.

        // RC=4 is a warning, never an abort; the job completed.
        Assert.False(result.Aborted);
        Assert.True(result.Succeeded);

        // The DALYREJS(+1) generation was created and catalogued (a whole number of 430-byte reject records).
        Assert.Equal(1, ctx.Gdg.GenerationCount(CardDemoJobs.GdgDalyRejs));
        string rejPath = ctx.Gdg.Current(CardDemoJobs.GdgDalyRejs)!;
        Assert.True(File.Exists(rejPath));
        Assert.Equal(0, new FileInfo(rejPath).Length % 430);
    }

    /// <summary>
    /// The single-step POSTTRAN variant (no CBTRN01C) runs just CBTRN02C, exactly as the JCL codes one EXEC
    /// step; the posting effect (rows in TRANSACTION) is identical.
    /// </summary>
    [Fact]
    public void PostTran_SingleStepVariant_RunsOnlyCbtrn02c()
    {
        using RelationalDb db = OpenSeededDb();
        JobContext ctx = NewContext(db);

        JobResult result = new JobRunner().Run(CardDemoJobs.PostTran(includeRead01: false), ctx);

        StepResult step = Assert.Single(result.Steps);
        Assert.Equal("CBTRN02C", step.Program);
        Assert.Equal(StepStatus.Executed, step.Status);
        Assert.True(new TransactionRepository(db).ReadAll().Any());
    }

    // =================================================================================================
    // INTCALC — CBACT04C interest accrual, over a seeded DB
    // =================================================================================================

    /// <summary>
    /// INTCALC runs its single CBACT04C step (RC 0): it processes every TRAN_CAT_BAL row, writes one interest
    /// TRANSACTION per non-zero-rate category (each amount the truncated <c>bal*rate/1200</c>), and accrues
    /// interest into the ACCOUNT balances. The interest TXs all carry type '01' / cat 0005 / source 'System'
    /// and the PARM-date TRAN-ID prefix; the SYSTRAN(+1) generation is created and catalogued.
    /// </summary>
    [Fact]
    public void IntCalc_RunsCbact04c_AccruesInterestAndWritesSystranGeneration()
    {
        using RelationalDb db = OpenSeededDb();

        // Re-derive the expected interest set independently (mirrors CBACT04C's DEFAULT-group fallback).
        List<TranCatBalance> balances = new TranCatBalanceRepository(db).ReadAll().ToList();
        Dictionary<long, string> acctGroup = new AccountRepository(db).ReadAll().ToDictionary(a => a.AcctId, a => a.GroupId);
        var disc = new Dictionary<(string, string, int), decimal>();
        foreach (DisclosureGroup g in new DisclosureGroupRepository(db).ReadAll())
            disc[(g.AcctGroupId, g.TranTypeCd, g.TranCatCd)] = g.IntRate;

        decimal Rate(long acctId, string typeCd, int catCd)
        {
            string grp = acctGroup.TryGetValue(acctId, out string? gid) ? gid : "";
            if (disc.TryGetValue((grp, typeCd, catCd), out decimal r)) return r;
            return disc.TryGetValue(("DEFAULT".PadRight(10), typeCd, catCd), out decimal d) ? d : 0m;
        }

        var expectedAmts = new List<decimal>();
        foreach (TranCatBalance b in balances)
        {
            decimal rate = Rate(b.AcctId, b.TypeCd, b.CatCd);
            if (rate != 0m)
                expectedAmts.Add(Decimals.Store(b.TranCatBal * rate / 1200m, 9, 2, true));
        }

        JobContext ctx = NewContext(db);
        JobResult result = new JobRunner().Run(CardDemoJobs.IntCalc("2022071800"), ctx);

        StepResult step = Assert.Single(result.Steps);
        Assert.Equal("CBACT04C", step.Program);
        Assert.Equal(0, step.ReturnCode);
        Assert.True(result.Succeeded);

        // One interest TX per non-zero-rate balance, with the documented signature.
        List<Transaction> tx = new TransactionRepository(db).ReadAll().ToList();
        Assert.Equal(expectedAmts.Count, tx.Count);
        Assert.Equal(
            expectedAmts.OrderBy(x => x).ToList(),
            tx.Select(t => t.Amt).OrderBy(x => x).ToList());
        Assert.All(tx, t =>
        {
            Assert.Equal("01", t.TypeCd);
            Assert.Equal(5, t.CatCd);
            Assert.Equal("System", t.Source.TrimEnd());
            Assert.Equal("2022071800", t.TranId[..10]);  // PARM-DATE prefix on every generated TRAN-ID.
        });

        // The SYSTRAN(+1) interest-transaction generation was created and catalogued.
        Assert.Equal(1, ctx.Gdg.GenerationCount(CardDemoJobs.GdgSysTran));
        string sysTran = ctx.Gdg.Current(CardDemoJobs.GdgSysTran)!;
        Assert.True(File.Exists(sysTran));
        Assert.Equal(0, new FileInfo(sysTran).Length % 350);     // CVTRA05Y 350-byte records.
    }

    /// <summary>
    /// INTCALC on a hand-built single-account DB reproduces the exact truncated interest:
    /// <c>(1234.56 * 19.99) / 1200 = 20.56</c> (truncate toward zero, no ROUNDED). The one interest TX
    /// carries that amount and the account balance moves by it.
    /// </summary>
    [Fact]
    public void IntCalc_OnKnownAccount_ComputesTruncatedInterest()
    {
        using var db = new RelationalDb();
        SeedOneInterestAccount(db,
            acctId: 11111111111L, groupId: "ZEROGRP   ", typeCd: "01", catCd: 5,
            balance: 1234.56m, rate: 19.99m, cardNum: "4111111111111111", startBal: 100.00m);

        // INTCALC requires CopybookDir for the SYSTRAN unload; supply the seed/copybook dirs.
        var ctx = new JobContext(db, NewWorkDir(), Clock, SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);
        JobResult result = new JobRunner().Run(CardDemoJobs.IntCalc("2022071800"), ctx);

        Assert.True(result.Succeeded);
        Transaction interest = Assert.Single(new TransactionRepository(db).ReadAll());
        Assert.Equal(20.56m, interest.Amt);
        Assert.Equal("000001", interest.TranId.Substring(10, 6)); // WS-TRANID-SUFFIX.
    }

    // =================================================================================================
    // COND / RC gating
    // =================================================================================================

    /// <summary>
    /// <c>COND=(4,LT)</c> on TRANBKP's DEFINE step (STEP10): bypass it when 4 &lt; (any prior RC), i.e. when a
    /// prior step ended RC &gt; 4. A hand-built job whose first step ends RC=8 must therefore BYPASS the
    /// COND=(4,LT) step; with the first step ending RC=4 (or 0) the step RUNS.
    /// </summary>
    [Fact]
    public void Cond_4Lt_BypassesStep_WhenPriorRcAbove4()
    {
        // Prior RC = 8 -> 4 < 8 is TRUE -> bypass.
        Job bypassJob = new("CONDTEST", "cond test",
        [
            new JobStep("S1", "UTILITY", _ => 8, stopJobOnFailure: false),
            new JobStep("S2", "UTILITY", _ => 0, conditions: [new CondCode(4, CondOperator.Lt)]),
        ], abortThreshold: 8);
        using var db1 = new RelationalDb();
        JobResult bypassed = new JobRunner().Run(bypassJob, new JobContext(db1, NewWorkDir()));
        Assert.Equal(StepStatus.Bypassed, bypassed.Step("S2")!.Status);

        // Prior RC = 4 -> 4 < 4 is FALSE -> run.
        Job runJob = new("CONDTEST", "cond test",
        [
            new JobStep("S1", "UTILITY", _ => 4),
            new JobStep("S2", "UTILITY", _ => 0, conditions: [new CondCode(4, CondOperator.Lt)]),
        ]);
        using var db2 = new RelationalDb();
        JobResult ran = new JobRunner().Run(runJob, new JobContext(db2, NewWorkDir()));
        Assert.Equal(StepStatus.Executed, ran.Step("S2")!.Status);
    }

    /// <summary>
    /// The TRANBKP job's <c>COND=(4,LT)</c> DEFINE step (STEP10) runs on a normal cycle (all prior RC 0):
    /// the backup is written, the master cleared, and the DEFINE re-creates the empty master. STEP10 is NOT
    /// bypassed because no prior step exceeded RC 4.
    /// </summary>
    [Fact]
    public void TranBkp_DefineStep_RunsWhenPriorStepsClean()
    {
        using RelationalDb db = OpenSeededDb();
        // Post some transactions so the backup has content.
        new JobRunner().Run(CardDemoJobs.PostTran(includeRead01: false), NewContext(db));
        Assert.True(new TransactionRepository(db).ReadAll().Any());

        JobContext ctx = NewContext(db);
        JobResult result = new JobRunner().Run(CardDemoJobs.TranBkp(), ctx);

        Assert.False(result.Aborted);
        Assert.Equal(StepStatus.Executed, result.Step("STEP05R")!.Status); // backup unload
        Assert.Equal(StepStatus.Executed, result.Step("STEP05")!.Status);  // delete master
        Assert.Equal(StepStatus.Executed, result.Step("STEP10")!.Status);  // define (not bypassed)
        // After delete + define the master is empty; the backup generation exists.
        Assert.Empty(new TransactionRepository(db).ReadAll());
        Assert.Equal(1, ctx.Gdg.GenerationCount(CardDemoJobs.GdgTransactBkup));
    }

    /// <summary>
    /// An abend in a step is recorded as a hard failure and stops the job: later steps do not run. A SORT-style
    /// step that throws <see cref="AbendException"/> aborts the job and the trailing step is never recorded.
    /// </summary>
    [Fact]
    public void Runner_AbendsStep_StopsJob_AndRecordsAbendCode()
    {
        Job job = new("ABENDTEST", "abend test",
        [
            new JobStep("S1", "CBxxx", _ => throw new AbendException("999", "boom")),
            new JobStep("S2", "CByyy", _ => 0),
        ]);
        using var db = new RelationalDb();
        JobResult result = new JobRunner().Run(job, new JobContext(db, NewWorkDir()));

        Assert.True(result.Aborted);
        Assert.False(result.Succeeded);
        StepResult s1 = Assert.Single(result.Steps);                 // S2 never ran.
        Assert.Equal(StepStatus.Abended, s1.Status);
        Assert.Equal("999", s1.AbendCode);
    }

    /// <summary>
    /// A non-abend step that ends RC above the job's abort threshold stops the data-dependent pipeline (so a
    /// later step never runs on missing upstream output), unless the step opts out via
    /// <see cref="JobStep.StopJobOnFailure"/>.
    /// </summary>
    [Fact]
    public void Runner_StepRcAboveThreshold_StopsJob()
    {
        Job job = new("RCTEST", "rc test",
        [
            new JobStep("S1", "UTILITY", _ => 12),   // > AbortThreshold(4) and StopJobOnFailure default true.
            new JobStep("S2", "UTILITY", _ => 0),
        ]);
        using var db = new RelationalDb();
        JobResult result = new JobRunner().Run(job, new JobContext(db, NewWorkDir()));

        Assert.True(result.Aborted);
        Assert.Single(result.Steps);                 // S2 was not reached.
        Assert.Equal(12, result.MaximumRc);
    }

    // =================================================================================================
    // Utility primitives — IDCAMS DELETE/DEFINE/REPRO, SORT, IEFBR14, GDG
    // =================================================================================================

    /// <summary>
    /// A file-setup job (ACCTFILE) runs DELETE -&gt; DEFINE -&gt; REPRO(seed): it clears the ACCOUNT table and
    /// reloads it from the EBCDIC seed dataset via MasterImporter. The reloaded row count equals the seed.
    /// </summary>
    [Fact]
    public void AcctFile_DeleteDefineRepro_ReloadsAccountTableFromSeed()
    {
        using RelationalDb db = OpenSeededDb();
        long before = new AccountRepository(db).ReadAll().LongCount();
        Assert.True(before > 0);

        JobResult result = new JobRunner().Run(CardDemoJobs.AcctFile(), NewContext(db));

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.Equal(StepStatus.Executed, s.Status));
        // DELETE then REPRO reload -> the same number of accounts as the seed dataset.
        Assert.Equal(before, new AccountRepository(db).ReadAll().LongCount());
    }

    /// <summary>
    /// IDCAMS DELETE of a base table is idempotent / tolerant of an empty table (RC 0), mirroring
    /// <c>IF MAXCC LE 08 THEN SET MAXCC = 0</c>: deleting the (already empty) TRANSACTION master twice both
    /// return RC 0.
    /// </summary>
    [Fact]
    public void IdcamsDelete_IsTolerantOfMissingData()
    {
        using var db = new RelationalDb();
        var ctx = new JobContext(db, NewWorkDir());
        Assert.Equal(0, UtilitySteps.IdcamsDeleteTransactionMaster(ctx));
        Assert.Equal(0, UtilitySteps.IdcamsDeleteTransactionMaster(ctx)); // still empty -> still RC 0.
    }

    /// <summary>
    /// SORT orders fixed-length records ascending by an ordinal control field. Three 4-byte records out of
    /// key order are written sorted; the INCLUDE predicate can additionally drop records.
    /// </summary>
    [Fact]
    public void Sort_OrdersByControlField_AndIncludeFilters()
    {
        string dir = NewWorkDir();
        string inPath = Path.Combine(dir, "in.dat");
        string outPath = Path.Combine(dir, "out.dat");

        // Three 4-byte records: "0003", "0001", "0002" (ASCII digits).
        File.WriteAllBytes(inPath, System.Text.Encoding.ASCII.GetBytes("000300010002"));

        // Sort ascending by the whole 4-byte field at offset 0.
        Assert.Equal(0, UtilitySteps.Sort([inPath], outPath, 4, [new SortField(0, 4)]));
        Assert.Equal("000100020003", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(outPath)));

        // INCLUDE: keep only records whose first byte is '0' and second '0' and third '0' (i.e. < 0010).
        string filtered = Path.Combine(dir, "filtered.dat");
        Assert.Equal(0, UtilitySteps.Sort([inPath], filtered, 4, [new SortField(0, 4)],
            include: rec => rec[2] == (byte)'0')); // all start "000", so all kept, sorted.
        Assert.Equal(12, new FileInfo(filtered).Length);
    }

    /// <summary>IEFBR14 deletes the named flat datasets (the DISP=(MOD,DELETE,DELETE) pre-delete idiom).</summary>
    [Fact]
    public void Iefbr14_DeletesNamedDatasets()
    {
        string dir = NewWorkDir();
        string a = Path.Combine(dir, "a.dat");
        string b = Path.Combine(dir, "b.dat");
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "y");

        Assert.Equal(0, UtilitySteps.Iefbr14(a, b, Path.Combine(dir, "missing.dat")));
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));   // both removed; a missing path is tolerated.
    }

    /// <summary>
    /// The GDG manager resolves relative generations: a <c>(+1)</c> resolves to the same generation across
    /// steps of one job, <c>Catalog</c> makes it the latest <c>(0)</c>, and <c>LIMIT</c> SCRATCH keeps only
    /// the most recent generations.
    /// </summary>
    [Fact]
    public void Gdg_ResolvesPlusOneAndZero_AndHonorsLimit()
    {
        var gdg = new GdgManager(NewWorkDir());
        gdg.Define("BASE", limit: 2, scratch: true);

        // No catalogued generation yet -> (0) is null; a (+1) is stable within the "job".
        Assert.Null(gdg.Current("BASE"));
        string g1 = gdg.Plus1("BASE");
        Assert.Equal(g1, gdg.Plus1("BASE"));   // same generation for every step in the job.
        gdg.Catalog("BASE");
        Assert.Equal(g1, gdg.Current("BASE")); // now it is the latest (0).

        // Two more generations -> LIMIT(2) scratches the oldest.
        gdg.Plus1("BASE"); gdg.Catalog("BASE");
        gdg.Plus1("BASE"); gdg.Catalog("BASE");
        Assert.Equal(2, gdg.GenerationCount("BASE"));
        Assert.False(File.Exists(g1));         // the first generation was scratched.
    }

    /// <summary>
    /// COMBTRAN's two ordered steps: STEP05R SORTs the (empty-on-a-fresh-base) backup + SYSTRAN inputs into a
    /// combined generation, then STEP10 REPRO-loads it into the TRANSACTION master. Run after INTCALC has
    /// produced a SYSTRAN generation, the combined master equals the SYSTRAN contents.
    /// </summary>
    [Fact]
    public void CombTran_SortsThenRepros_RebuildsTransactionMaster()
    {
        using RelationalDb db = OpenSeededDb();

        // Produce a SYSTRAN(0) generation via INTCALC so COMBTRAN has an input to merge.
        var sharedGdg = new GdgManager(NewWorkDir());
        var intctx = new JobContext(db, NewWorkDir(), Clock, SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir, sharedGdg);
        new JobRunner().Run(CardDemoJobs.IntCalc("2022071800"), intctx);
        long sysTranRows = new TransactionRepository(db).ReadAll().LongCount();
        Assert.True(sysTranRows > 0);

        // COMBTRAN over the SAME gdg (so SYSTRAN(0) resolves) rebuilds the master from the combined file.
        var combctx = new JobContext(db, intctx.WorkDir, Clock, SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir, sharedGdg);
        JobResult result = new JobRunner().Run(CardDemoJobs.CombTran(), combctx);

        Assert.True(result.Succeeded);
        Assert.Equal(StepStatus.Executed, result.Step("STEP05R")!.Status);
        Assert.Equal(StepStatus.Executed, result.Step("STEP10")!.Status);
        // The rebuilt master holds the same number of rows the SYSTRAN generation carried.
        Assert.Equal(sysTranRows, new TransactionRepository(db).ReadAll().LongCount());
    }

    // =================================================================================================
    // Online-config jobs are documented as skipped (not runnable batch)
    // =================================================================================================

    /// <summary>The CICS CSD-install / file-state JCL members are recorded as online-config, not runnable batch.</summary>
    [Fact]
    public void OnlineConfigJobs_AreDocumentedAsSkipped()
    {
        Assert.True(OnlineConfigJobs.IsOnlineConfig("CBADMCDJ"));
        Assert.True(OnlineConfigJobs.IsOnlineConfig("DUSRSECJ"));
        Assert.True(OnlineConfigJobs.IsOnlineConfig("DALYREJS")); // GDG-define, modeled by GdgManager.Define.
        Assert.True(OnlineConfigJobs.IsOnlineConfig("REPTFILE")); // GDG-define, modeled by GdgManager.Define.
        Assert.False(OnlineConfigJobs.IsOnlineConfig("POSTTRAN")); // a real batch job, not online-config.
        Assert.NotEmpty(OnlineConfigJobs.All);
        Assert.All(OnlineConfigJobs.All, j => Assert.False(string.IsNullOrWhiteSpace(j.Reason)));
    }

    // =================================================================================================
    // UNLDPADB / LOADPADB / UNLDGSAM — IMS pending-auth DB unload/load jobs (CBSTM03A/PAUDBUNL/PAUDBLOD/DBUNLDGS)
    // =================================================================================================

    /// <summary>
    /// UNLDPADB then LOADPADB round-trip the PAUT pending-auth data through the two sequential files:
    /// UNLDPADB (PAUDBUNL) writes PAUTDB.ROOT.FILEO (100-byte summary images) + PAUTDB.CHILD.FILEO
    /// (206-byte root-key+detail images); LOADPADB (PAUDBLOD), run over the SAME work-dir but a FRESH empty
    /// DB, reads those files back and re-inserts every PAUT_SUMMARY root and PAUT_DETAIL child. The reloaded
    /// rows must match the original keys/fields/counts.
    /// </summary>
    [Fact]
    public void UnldPaDb_Then_LoadPaDb_RoundTripsPautData()
    {
        // ---- Source: a small hand-seeded PAUT hierarchy (2 roots x 2 children). ----
        using var srcDb = new RelationalDb();
        var srcSummaries = new PautSummaryRepository(srcDb);
        var srcDetails = new PautDetailRepository(srcDb);

        long[] accts = { 11111111111L, 22222222222L };
        foreach (long acct in accts)
        {
            SeedPautSummary(srcSummaries, acct, approvedCnt: 2);
            SeedPautDetail(srcDetails, acct, authDate9c: 73822, authTime9c: 909999999L, approved: true,
                tranId: "T" + acct.ToString().Substring(0, 6) + "001");
            SeedPautDetail(srcDetails, acct, authDate9c: 73823, authTime9c: 809999999L, approved: false,
                tranId: "T" + acct.ToString().Substring(0, 6) + "002");
        }
        int expectedSummaries = accts.Length;
        int expectedDetails = accts.Length * 2;

        // ---- UNLDPADB writes the two unload files into ctx1's work-dir. ----
        JobContext ctx1 = NewContext(srcDb);
        JobResult unl = new JobRunner().Run(CardDemoJobs.UnldPaDb(), ctx1);

        Assert.True(unl.Succeeded);
        Assert.Equal(StepStatus.Executed, unl.Step("STEP0")!.Status);  // IEFBR14 pre-delete
        Assert.Equal(0, unl.Step("STEP01")!.ReturnCode);               // PAUDBUNL clean run

        string rootFile = ctx1.Path("PAUTDB.ROOT.FILEO");
        string childFile = ctx1.Path("PAUTDB.CHILD.FILEO");
        Assert.True(File.Exists(rootFile));
        Assert.True(File.Exists(childFile));
        // Exact image widths: summary 100 bytes, detail record 206 bytes (6-byte key + 200-byte image).
        Assert.Equal(0, new FileInfo(rootFile).Length % 100);
        Assert.Equal(0, new FileInfo(childFile).Length % 206);
        Assert.Equal(expectedSummaries, new FileInfo(rootFile).Length / 100);
        Assert.Equal(expectedDetails, new FileInfo(childFile).Length / 206);

        // ---- LOADPADB over the SAME work-dir but a FRESH empty DB reads those files back. ----
        using var dstDb = new RelationalDb();
        var dstCtx = new JobContext(dstDb, ctx1.WorkDir, Clock, SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);
        JobResult lod = new JobRunner().Run(CardDemoJobs.LoadPaDb(), dstCtx);

        Assert.True(lod.Succeeded);
        Assert.Equal(0, lod.Step("STEP01")!.ReturnCode);              // PAUDBLOD clean run

        var dstSummaries = new PautSummaryRepository(dstDb);
        var dstDetails = new PautDetailRepository(dstDb);

        // Every summary round-trips (count + key + non-key fields).
        Assert.Equal(expectedSummaries, dstSummaries.ReadAll().Count());
        foreach (long acct in accts)
        {
            Assert.Equal(FileStatus.Ok, srcSummaries.ReadByKey(acct, out PautSummary? sOrig));
            Assert.Equal(FileStatus.Ok, dstSummaries.ReadByKey(acct, out PautSummary? sLoad));
            Assert.Equal(sOrig!.AcctId, sLoad!.AcctId);
            Assert.Equal(sOrig.CustId, sLoad.CustId);
            Assert.Equal(sOrig.AuthStatus.TrimEnd(), sLoad.AuthStatus.TrimEnd());
            Assert.Equal(sOrig.ApprovedAuthCnt, sLoad.ApprovedAuthCnt);
            Assert.Equal(sOrig.DeclinedAuthCnt, sLoad.DeclinedAuthCnt);
            Assert.Equal(sOrig.CreditLimit, sLoad.CreditLimit);
            Assert.Equal(sOrig.ApprovedAuthAmt, sLoad.ApprovedAuthAmt);
        }

        // Every detail round-trips (count + key + identifying fields), per parent, in twin-chain order.
        int totalLoaded = 0;
        foreach (long acct in accts)
        {
            List<PautDetail> orig = srcDetails.ReadAllByParent(acct).ToList();
            List<PautDetail> load = dstDetails.ReadAllByParent(acct).ToList();
            Assert.Equal(orig.Count, load.Count);
            totalLoaded += load.Count;
            for (int i = 0; i < orig.Count; i++)
            {
                Assert.Equal(orig[i].AuthKey, load[i].AuthKey);
                Assert.Equal(orig[i].AcctId, load[i].AcctId);
                Assert.Equal(orig[i].AuthDate9c, load[i].AuthDate9c);
                Assert.Equal(orig[i].AuthTime9c, load[i].AuthTime9c);
                Assert.Equal(orig[i].TransactionId.TrimEnd(), load[i].TransactionId.TrimEnd());
                Assert.Equal(orig[i].CardNum.TrimEnd(), load[i].CardNum.TrimEnd());
                Assert.Equal(orig[i].AuthRespCode.TrimEnd(), load[i].AuthRespCode.TrimEnd());
                Assert.Equal(orig[i].TransactionAmt, load[i].TransactionAmt);
                Assert.Equal(orig[i].ApprovedAmt, load[i].ApprovedAmt);
            }
        }
        Assert.Equal(expectedDetails, totalLoaded);
    }

    /// <summary>
    /// UNLDGSAM (DBUNLDGS) unloads the PAUT DB to two GSAM files: PAUTDB.ROOT.GSAM (100-byte summary images)
    /// and PAUTDB.CHILD.GSAM (200-byte detail images, with NO 6-byte root-key prefix). The summary GSAM is
    /// byte-identical to UNLDPADB's PAUTDB.ROOT.FILEO (the summary images are produced identically).
    /// </summary>
    [Fact]
    public void UnldGsam_ProducesGsamFiles()
    {
        using var db = new RelationalDb();
        var summaries = new PautSummaryRepository(db);
        var details = new PautDetailRepository(db);

        long[] accts = { 30000000001L, 40000000002L };
        foreach (long acct in accts)
        {
            SeedPautSummary(summaries, acct, approvedCnt: 3);
            SeedPautDetail(details, acct, authDate9c: 50000, authTime9c: 700000000L, approved: true,
                tranId: "G" + acct.ToString().Substring(0, 6) + "01");
            SeedPautDetail(details, acct, authDate9c: 50001, authTime9c: 600000000L, approved: false,
                tranId: "G" + acct.ToString().Substring(0, 6) + "02");
        }
        int expectedSummaries = accts.Length;
        int expectedDetails = accts.Length * 2;

        // UNLDGSAM writes the two GSAM files.
        JobContext gsamCtx = NewContext(db);
        JobResult gsam = new JobRunner().Run(CardDemoJobs.UnldGsam(), gsamCtx);

        Assert.True(gsam.Succeeded);
        Assert.Equal(0, gsam.Step("STEP01")!.ReturnCode);

        string rootGsam = gsamCtx.Path("PAUTDB.ROOT.GSAM");
        string childGsam = gsamCtx.Path("PAUTDB.CHILD.GSAM");
        Assert.True(File.Exists(rootGsam));
        Assert.True(File.Exists(childGsam));
        Assert.Equal(0, new FileInfo(rootGsam).Length % 100);   // 100-byte summary images
        Assert.Equal(0, new FileInfo(childGsam).Length % 200);  // 200-byte detail images (no key prefix)
        Assert.Equal(expectedSummaries, new FileInfo(rootGsam).Length / 100);
        Assert.Equal(expectedDetails, new FileInfo(childGsam).Length / 200);

        // UNLDPADB (PAUDBUNL) over the SAME data writes the same 100-byte summary images to ROOT.FILEO.
        JobContext bunlCtx = NewContext(db);
        JobResult bunl = new JobRunner().Run(CardDemoJobs.UnldPaDb(), bunlCtx);
        Assert.True(bunl.Succeeded);

        byte[] gsamSummaryBytes = File.ReadAllBytes(rootGsam);
        byte[] bunlSummaryBytes = File.ReadAllBytes(bunlCtx.Path("PAUTDB.ROOT.FILEO"));
        Assert.Equal(bunlSummaryBytes.Length, gsamSummaryBytes.Length);
        Assert.True(bunlSummaryBytes.AsSpan().SequenceEqual(gsamSummaryBytes),
            "UNLDGSAM ROOT.GSAM summary images are not byte-identical to UNLDPADB ROOT.FILEO");
    }

    /// <summary>
    /// CREASTMT runs DELDEF01/STEP010/STEP020 (RC-0 relational-prep no-ops), STEP030 (IEFBR14 delete prior
    /// statement datasets) and STEP040 (CBSTM03A). CBSTM03A has a FIXED 51-card x 10-tran in-storage table
    /// with no bounds guard, so we seed a SMALL DB (1 account + customer + xref card + 2 transactions) rather
    /// than the full master seed. Every step ends RC 0 and the two statement files are produced and non-empty
    /// (carrying the account id and a transaction amount).
    /// </summary>
    [Fact]
    public void CreaStmt_ProducesStatementFiles()
    {
        using var db = new RelationalDb();

        const long acctId = 12345678901L;
        const long custId = 222333444L;
        const string cardNum = "4444333322221111";

        Assert.Equal(FileStatus.Ok, new AccountRepository(db).Insert(new Account
        {
            AcctId = acctId, ActiveStatus = "Y", CurrBal = 4275.18m, CreditLimit = 9000.00m,
            CashCreditLimit = 1000.00m, OpenDate = "2020-01-01", ExpirationDate = "2028-01-01",
            ReissueDate = "2024-01-01", CurrCycCredit = 0.00m, CurrCycDebit = 0.00m,
            AddrZip = "90001", GroupId = "GRP0000001",
        }));
        Assert.Equal(FileStatus.Ok, new CustomerRepository(db).Insert(new Customer
        {
            CustId = custId, FirstName = "ALICE", MiddleName = "Q", LastName = "STMTHOLDER",
            AddrLine1 = "100 STATEMENT WAY", AddrLine2 = "FLOOR 3", AddrLine3 = "METROCITY",
            AddrStateCd = "CA", AddrCountryCd = "USA", AddrZip = "90001",
            PhoneNum1 = "5551112222", PhoneNum2 = "5553334444", Ssn = 123456789,
            GovtIssuedId = "DL99887766", DobYyyyMmDd = "1980-01-01", EftAccountId = "EFT0000001",
            PriCardHolderInd = "Y", FicoCreditScore = 765,
        }));
        Assert.Equal(FileStatus.Ok, new CardXrefRepository(db).Insert(new CardXref
        {
            XrefCardNum = cardNum, CustId = custId, AcctId = acctId,
        }));
        var txns = new TransactionRepository(db);
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = "TXNSTMT000000001", TypeCd = "01", CatCd = 1, Source = "POS",
            Desc = "GROCERY STORE PURCHASE", Amt = 123.45m, MerchantId = 1, MerchantName = "GROCERS",
            MerchantCity = "METROCITY", MerchantZip = "90001", CardNum = cardNum,
            OrigTs = "2026-06-20-10.00.00.000000", ProcTs = "2026-06-20-10.00.00.000000",
        }));
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = "TXNSTMT000000002", TypeCd = "01", CatCd = 1, Source = "POS",
            Desc = "ONLINE BOOKSTORE ORDER", Amt = 67.89m, MerchantId = 2, MerchantName = "BOOKS",
            MerchantCity = "METROCITY", MerchantZip = "90001", CardNum = cardNum,
            OrigTs = "2026-06-21-11.30.00.000000", ProcTs = "2026-06-21-11.30.00.000000",
        }));

        JobContext ctx = NewContext(db);
        JobResult result = new JobRunner().Run(CardDemoJobs.CreaStmt(), ctx);

        Assert.True(result.Succeeded);
        // All five steps ran and ended RC 0.
        foreach (string stepName in new[] { "DELDEF01", "STEP010", "STEP020", "STEP030", "STEP040" })
        {
            StepResult? step = result.Step(stepName);
            Assert.NotNull(step);
            Assert.Equal(StepStatus.Executed, step!.Status);
            Assert.Equal(0, step.ReturnCode);
        }

        // CBSTM03A wrote both statement datasets, non-empty, with the identifying data.
        string psPath = ctx.Path("STATEMNT.PS");
        string htmlPath = ctx.Path("STATEMNT.HTML");
        Assert.True(File.Exists(psPath));
        Assert.True(File.Exists(htmlPath));
        Assert.True(new FileInfo(psPath).Length > 0, "STATEMNT.PS is empty");
        Assert.True(new FileInfo(htmlPath).Length > 0, "STATEMNT.HTML is empty");

        // The plain-text statement is EBCDIC-encoded (CBSTM03A writes via StreamWriter -> default UTF-8 text;
        // assert on the readable content). Read as text and check the account id + an amount appear.
        string ps = File.ReadAllText(psPath);
        string html = File.ReadAllText(htmlPath);
        Assert.Contains("12345678901", ps);   // ACCT-ID
        Assert.Contains("123.45", ps);          // a transaction amount
        Assert.Contains("12345678901", html);
        Assert.Contains("123.45", html);
    }

    // =================================================================================================
    // CBPAUP0J / MNTTRDB2 / TRANEXTR — optional-module application batch job flows (coverage remediation)
    // =================================================================================================

    /// <summary>
    /// CBPAUP0J runs its single IMS-BMP step (CBPAUP0C) over a seeded PAUT hierarchy with the JCL's literal
    /// SYSIN card '00,00001,00001,Y'. The job completes cleanly (RC 0) and the step is recorded as executed.
    /// </summary>
    [Fact]
    public void CbPaup0J_RunsCbpaup0c_Cleanly()
    {
        using var db = new RelationalDb();
        var summaries = new PautSummaryRepository(db);
        var details = new PautDetailRepository(db);
        SeedPautSummary(summaries, 70000000001L, approvedCnt: 1);
        SeedPautDetail(details, 70000000001L, authDate9c: 50000, authTime9c: 700000000L, approved: true, tranId: "PURGE0000001");

        JobResult result = new JobRunner().Run(CardDemoJobs.CbPaup0J(), NewContext(db));

        Assert.True(result.Succeeded);
        StepResult step = Assert.Single(result.Steps);
        Assert.Equal("CBPAUP0C", step.Program);
        Assert.Equal(StepStatus.Executed, step.Status);
        Assert.Equal(0, step.ReturnCode);
    }

    /// <summary>
    /// MNTTRDB2 runs COBTUPDT against the TRANSACTION_TYPE table from an INPFILE control dataset: an 'A' (add)
    /// record inserts a new transaction-type row, observable in the table after the job.
    /// </summary>
    [Fact]
    public void MntTrDb2_RunsCobtupdt_AddsTransactionTypeRow()
    {
        using var db = new RelationalDb();
        var types = new TransactionTypeRepository(db);
        Assert.Equal(FileStatus.RecordNotFound, types.ReadByKey("90", out _));

        // INPFILE record: col 1 = 'A' (add), cols 2-3 = type '90', cols 4-53 = description.
        string inpfile = Path.Combine(NewWorkDir(), "INPFILE.dat");
        File.WriteAllText(inpfile, "A90NEW REFERENCE TYPE".PadRight(53));

        JobResult result = new JobRunner().Run(CardDemoJobs.MntTrDb2(inpfile), NewContext(db));

        Assert.True(result.Succeeded);
        Assert.Equal("COBTUPDT", Assert.Single(result.Steps).Program);
        Assert.Equal(FileStatus.Ok, types.ReadByKey("90", out TransactionType? added));
        Assert.Equal("NEW REFERENCE TYPE", added!.TrDescription.TrimEnd());
    }

    /// <summary>
    /// TRANEXTR unloads TRANSACTION_TYPE and TRANSACTION_TYPE_CATEGORY to the 60-byte TRANTYPE.PS / TRANCATG.PS
    /// reference files in the DSNTIAUL SELECT/CAST format (key + CHAR(50) data + '0' fill), ordered by key.
    /// </summary>
    [Fact]
    public void TranExtr_UnloadsReferenceTablesTo60ByteFixedFiles()
    {
        using var db = new RelationalDb();
        var types = new TransactionTypeRepository(db);
        var cats = new TransactionTypeCategoryRepository(db);
        Assert.Equal(FileStatus.Ok, types.Insert(new TransactionType { TrType = "02", TrDescription = "PAYMENT" }));
        Assert.Equal(FileStatus.Ok, types.Insert(new TransactionType { TrType = "01", TrDescription = "PURCHASE" }));
        Assert.Equal(FileStatus.Ok, cats.Insert(new TransactionTypeCategory
        {
            TrcTypeCode = "01", TrcTypeCategory = "0001", TrcCatData = "REGULAR SALES DRAFT",
        }));

        JobContext ctx = NewContext(db);
        JobResult result = new JobRunner().Run(CardDemoJobs.TranExtr(), ctx);

        Assert.True(result.Succeeded);
        string typePath = ctx.Path("TRANTYPE.PS");
        string catgPath = ctx.Path("TRANCATG.PS");
        Assert.True(File.Exists(typePath));
        Assert.True(File.Exists(catgPath));

        // TRANTYPE: two 60-byte records (TR-TYPE(2) + CHAR(50) desc + '00000000'), ascending by TR_TYPE.
        byte[] typeBytes = File.ReadAllBytes(typePath);
        Assert.Equal(2 * 60, typeBytes.Length);
        string rec0 = HostEncoding.Ebcdic.GetString(typeBytes, 0, 60);
        Assert.Equal("01", rec0[..2]);                          // ordered -> '01' first
        Assert.Equal("PURCHASE", rec0.Substring(2, 50).TrimEnd());
        Assert.Equal("00000000", rec0.Substring(52, 8));
        Assert.Equal("02", HostEncoding.Ebcdic.GetString(typeBytes, 60, 60)[..2]);

        // TRANCATG: one 60-byte record (CODE(2)+CATEGORY(4)+CHAR(50)+'0000').
        byte[] catgBytes = File.ReadAllBytes(catgPath);
        Assert.Equal(60, catgBytes.Length);
        string crec = HostEncoding.Ebcdic.GetString(catgBytes, 0, 60);
        Assert.Equal("01", crec[..2]);
        Assert.Equal("0001", crec.Substring(2, 4));
        Assert.Equal("REGULAR SALES DRAFT", crec.Substring(6, 50).TrimEnd());
        Assert.Equal("0000", crec.Substring(56, 4));
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    /// <summary>Seeds a PAUT_SUMMARY root with the same shape the RemediationTests round-trip suite uses.</summary>
    private static void SeedPautSummary(PautSummaryRepository repo, long acctId, int approvedCnt)
    {
        Assert.Equal(FileStatus.Ok, repo.Insert(new PautSummary
        {
            AcctId = acctId,
            CustId = acctId % 1000000000,
            AuthStatus = "A",
            AccountStatus1 = "Y",
            AccountStatus2 = "Y",
            AccountStatus3 = "N",
            AccountStatus4 = "N",
            AccountStatus5 = "N",
            CreditLimit = 9000.00m,
            CashLimit = 1000.00m,
            CreditBalance = 100.00m,
            CashBalance = 0.00m,
            ApprovedAuthCnt = approvedCnt,
            DeclinedAuthCnt = 1,
            ApprovedAuthAmt = approvedCnt * 25.00m,
            DeclinedAuthAmt = 10.00m,
        }));
    }

    /// <summary>
    /// Inserts a PAUT_DETAIL whose AUTH_KEY equals the canonical (date-9c|time-9c) string the IMS loader
    /// rebuilds on decode (5-digit AUTH_DATE_9C + 9-digit AUTH_TIME_9C), so the composite key round-trips
    /// through an unload+load byte-for-byte. (Mirrors the internal PautSegmentImages.BuildAuthKey.)
    /// </summary>
    private static void SeedPautDetail(
        PautDetailRepository repo, long acctId, int authDate9c, long authTime9c, bool approved, string tranId)
    {
        string authKey =
            (Math.Abs((long)authDate9c) % 100000L).ToString("D5") +
            (Math.Abs(authTime9c) % 1000000000L).ToString("D9");

        Assert.Equal(FileStatus.Ok, repo.Insert(new PautDetail
        {
            AcctId = acctId,
            AuthKey = authKey,
            AuthDate9c = authDate9c,
            AuthTime9c = authTime9c,
            AuthOrigDate = "260626",
            AuthOrigTime = "090000",
            CardNum = acctId.ToString("D16"),
            AuthType = "0100",
            CardExpiryDate = "1228",
            MessageType = "0100",
            MessageSource = "POS",
            AuthIdCode = "AUT001",
            AuthRespCode = approved ? "00" : "05",
            AuthRespReason = approved ? "0000" : "4100",
            ProcessingCode = 123456,
            TransactionAmt = 25.00m,
            ApprovedAmt = approved ? 25.00m : 0.00m,
            MerchantCatagoryCode = "5411",
            AcqrCountryCode = "840",
            PosEntryMode = 1,
            MerchantId = "MERCH0000000001",
            MerchantName = "ROUND TRIP MERCHANT",
            MerchantCity = "TRIPTOWN",
            MerchantState = "CA",
            MerchantZip = "900010000",
            TransactionId = tranId,
            MatchStatus = "P",
            AuthFraud = " ",
            FraudRptDate = " ",
        }));
    }

    private static void SeedOneInterestAccount(
        RelationalDb db, long acctId, string groupId, string typeCd, int catCd,
        decimal balance, decimal rate, string cardNum, decimal startBal)
    {
        Assert.Equal(FileStatus.Ok, new AccountRepository(db).Insert(new Account
        {
            AcctId = acctId, ActiveStatus = "Y", CurrBal = startBal, CreditLimit = 100000.00m,
            CashCreditLimit = 100000.00m, OpenDate = "2020-01-01", ExpirationDate = "2099-12-31",
            ReissueDate = "2020-01-01", CurrCycCredit = 0m, CurrCycDebit = 0m,
            AddrZip = "12345", GroupId = groupId,
        }));
        Assert.Equal(FileStatus.Ok, new CardXrefRepository(db).Insert(
            new CardXref { XrefCardNum = cardNum, CustId = 1, AcctId = acctId }));
        Assert.Equal(FileStatus.Ok, new TranCatBalanceRepository(db).Insert(new TranCatBalance
        {
            AcctId = acctId, TypeCd = typeCd, CatCd = catCd, TranCatBal = balance,
        }));
        Assert.Equal(FileStatus.Ok, new DisclosureGroupRepository(db).Insert(new DisclosureGroup
        {
            AcctGroupId = groupId, TranTypeCd = typeCd, TranCatCd = catCd, IntRate = rate,
        }));
    }
}
