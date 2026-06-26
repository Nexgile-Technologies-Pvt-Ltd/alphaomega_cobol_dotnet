using System.Text;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Db2;
using CardDemo.Domain;
using CardDemo.Ims;
using CardDemo.Mq;
using CardDemo.Mq.Programs;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

/// <summary>
/// End-to-end tests for the 10 optional CardDemo add-on programs (DB2 transaction-type maintenance,
/// IMS pending-authorization, VSAM+MQ utilities) over a freshly-seeded in-memory <see cref="RelationalDb"/>.
/// At least one test exercises each optional module:
/// <list type="bullet">
/// <item><b>DB2 / TRANSACTION_TYPE.</b> <see cref="Cobtupdt"/> (batch INSERT/UPDATE/DELETE round-trip),
/// <see cref="Cotrtlic"/> (online list paging) and <see cref="Cotrtupc"/> (online add) over the same
/// <c>TRANSACTION_TYPE</c> table.</item>
/// <item><b>IMS / PAUT_SUMMARY + PAUT_DETAIL.</b> <see cref="Cbpaup0c"/> (batch expiry-purge GU/GNP/DLET
/// flow) and <see cref="Copaus0c"/>/<see cref="Copaus1c"/> (online pending-auth view) over the relational
/// re-host of the IMS hierarchy.</item>
/// <item><b>MQ request/response.</b> <see cref="Coacct01"/> (account inquiry) and <see cref="Codate01"/>
/// (date utility) driven through the in-proc <see cref="MqBroker"/> shim, plus <see cref="Copaua0c"/> (the
/// auth-decision server that writes the IMS PAUT tables) request→reply round-trip.</item>
/// </list>
/// The optional online handlers are also asserted to be wired into <see cref="OnlineProgramRegistry"/> under
/// their CSD TRANSIDs (CTLI/CTTU/CPVS/CPVD).
/// </summary>
public sealed class OptionalTests
{
    /// <summary>A clock pinned so date-driven output (CODATE01, CBPAUP0C, COPAU* headers) is deterministic.</summary>
    private static readonly IClock FixedClk = new FixedClock(new DateTime(2026, 6, 26, 9, 0, 0));

    /// <summary>An empty (schema-only) in-memory relational DB — the optional tables exist but hold no rows.</summary>
    private static RelationalDb EmptyDb() => new();

    // =================================================================================================
    //  Wiring: the optional online handlers are registered under their CSD TRANSIDs.
    // =================================================================================================

    [Fact]
    public void Registry_wires_the_4_optional_online_programs_and_transactions()
    {
        using var db = EmptyDb();
        ProgramRegistry reg = OnlineProgramRegistry.Build(db);

        // PROGRAM (XCTL/LINK target) registration.
        foreach (string p in new[] { "COTRTLIC", "COTRTUPC", "COPAUS0C", "COPAUS1C" })
            Assert.True(reg.HasProgram(p), $"optional program {p} not registered");

        // CSD TRANSID -> PROGRAM routing for the add-on transactions.
        Assert.Equal("COTRTLIC", reg.ProgramForTransId("CTLI")); // DB2 tran-type list/update
        Assert.Equal("COTRTUPC", reg.ProgramForTransId("CTTU")); // DB2 tran-type maintenance
        Assert.Equal("COPAUS0C", reg.ProgramForTransId("CPVS")); // IMS pending-auth summary
        Assert.Equal("COPAUS1C", reg.ProgramForTransId("CPVD")); // IMS pending-auth detail

        // COPAUS2C is XCTL-only (no DEFINE TRANSACTION CPVD -> COPAUS2C); it must NOT be a dispatch target.
        Assert.False(reg.HasProgram("COPAUS2C"));
    }

    // =================================================================================================
    //  DB2 module #1 — COBTUPDT: batch transaction-type list/update round-trip over TRANSACTION_TYPE.
    // =================================================================================================

    [Fact]
    public void Cobtupdt_inserts_updates_and_deletes_over_TRANSACTION_TYPE()
    {
        using var db = EmptyDb();
        var repo = new TransactionTypeRepository(db.Connection);

        // INPFILE records are 53-byte RECFM=F: action(1) + type(2) + desc(50).
        IReadOnlyList<string> sysout = Cobtupdt.Run(db, new[]
        {
            InpRec('A', "91", "PURCHASE TYPE NINETY-ONE"),  // add
            InpRec('A', "92", "PAYMENT TYPE NINETY-TWO"),   // add
            InpRec('U', "91", "PURCHASE (UPDATED)"),        // update 91's description
            InpRec('D', "92", ""),                          // delete 92
            InpRec('*', "00", "this is a comment line"),    // ignored
        });

        // SYSOUT confirms each branch ran.
        Assert.Contains("RECORD INSERTED SUCCESSFULLY", sysout);
        Assert.Contains("RECORD UPDATED SUCCESSFULLY", sysout);
        Assert.Contains("RECORD DELETED SUCCESSFULLY", sysout);
        Assert.Contains("IGNORING COMMENTED LINE", sysout);

        // 91 exists with the UPDATED description (right-padded to 50); 92 was deleted.
        Assert.Equal(FileStatus.Ok, repo.ReadByKey("91", out TransactionType? t91));
        Assert.Equal("PURCHASE (UPDATED)", t91!.TrDescription.TrimEnd());
        Assert.Equal(FileStatus.RecordNotFound, repo.ReadByKey("92", out _));
    }

    // =================================================================================================
    //  DB2 module #2 — COTRTLIC: online transaction-type list (Db2 cursor paging, 7 rows/page).
    // =================================================================================================

    [Fact]
    public void Cotrtlic_lists_transaction_types_first_page()
    {
        using var db = EmptyDb();
        SeedTransactionTypes(db, count: 10); // TR_TYPE = "01".."10"

        var screen = new ScriptedScreen();
        var dispatcher = new Dispatcher(OnlineProgramRegistry.Build(db), screen, FixedClk);

        // Cold start (EIBCALEN=0): COTRTLIC primes the forward cursor and paints the first 7-row page.
        CicsOutcome outcome = dispatcher.RunTurn("CTLI", AidKey.Enter, commArea: null);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CTLI", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("CTRTLIA", screen.LastSentMap!.Name);
        // First page shows TR_TYPE rows starting at "01" (7 of the 10 rows).
        Assert.Equal("01", screen.LastSentMap.Field("TRTTYP1").Value.TrimEnd());
        Assert.Equal("07", screen.LastSentMap.Field("TRTTYP7").Value.TrimEnd());
        // The first row's description is painted.
        Assert.Contains("TYPE 01", screen.LastSentMap.Field("TRTYPD1").Value);
    }

    // =================================================================================================
    //  DB2 module #3 — COTRTUPC: online transaction-type maintenance (add a new type, multi-turn).
    // =================================================================================================

    [Fact]
    public void Cotrtupc_creates_a_new_transaction_type_over_a_multi_turn_flow()
    {
        using var db = EmptyDb();
        var repo = new TransactionTypeRepository(db.Connection);
        Assert.Equal(FileStatus.RecordNotFound, repo.ReadByKey("77", out _));

        var registry = OnlineProgramRegistry.Build(db);

        // The keyed-input script: every turn re-presents the new type code + description (the operator keeps
        // them on screen across the confirm turns).
        Action<BmsMap> keyNew = map =>
        {
            map.Field("TRTYPCD").SetValue("77");                  // new type code (2 digits)
            map.Field("TRTYDSC").SetValue("NEW MAINTENANCE TYPE");// its description
        };

        // COTRTUPC's conversational create is a 4-turn flow (per 0000-MAIN / 2000-DECIDE-ACTION):
        //   1) ENTER new code      -> 9000-READ-TRANTYPE misses -> TTUP-DETAILS-NOT-FOUND (prompts F5)
        //   2) F05 confirm-create  -> TTUP-CREATE-NEW-RECORD
        //   3) ENTER (edit inputs) -> change detected + desc valid -> TTUP-CHANGES-OK-NOT-CONFIRMED
        //   4) F05 save            -> 9600-WRITE-PROCESSING -> UPDATE +100 -> 9700-INSERT-RECORD
        var ca = new CardDemoCommArea { FromTranId = "CTTU", FromProgram = "COTRTUPC" };
        ca.SetReenter();

        CicsOutcome turn = RunCotrtupcTurn(registry, AidKey.Enter, keyNew, ca);   // turn 1 -> DetailsNotFound
        turn = RunCotrtupcTurn(registry, AidKey.Pf5, keyNew, turn.CommArea!);     // turn 2 -> CreateNewRecord
        turn = RunCotrtupcTurn(registry, AidKey.Enter, keyNew, turn.CommArea!);   // turn 3 -> ChangesOkNotConfirmed
        turn = RunCotrtupcTurn(registry, AidKey.Pf5, keyNew, turn.CommArea!);     // turn 4 -> INSERT

        Assert.Equal(CicsOutcomeKind.ReturnTransId, turn.Kind);
        Assert.Equal("CTTU", turn.TransId);

        // The conversational add reaches the INSERT: the new type now exists in TRANSACTION_TYPE.
        Assert.Equal(FileStatus.Ok, repo.ReadByKey("77", out TransactionType? added));
        Assert.Equal("NEW MAINTENANCE TYPE", added!.TrDescription.TrimEnd());
    }

    /// <summary>Runs one COTRTUPC (CTTU) turn with the given AID and keyed-input script over the carried COMMAREA.</summary>
    private static CicsOutcome RunCotrtupcTurn(
        ProgramRegistry registry, AidKey aid, Action<BmsMap> onReceive, CardDemoCommArea commArea)
    {
        var screen = new ScriptedScreen { NextAid = aid, OnReceive = onReceive };
        return new Dispatcher(registry, screen, FixedClk).RunTurn("CTTU", aid, commArea);
    }

    // =================================================================================================
    //  IMS module #1 — CBPAUP0C: batch pending-auth expiry purge (GU summary / GNP detail / DLET).
    // =================================================================================================

    [Fact]
    public void Cbpaup0c_purges_expired_auth_details_and_empty_summaries()
    {
        using var db = EmptyDb();
        var summaries = new PautSummaryRepository(db.Connection);
        var details = new PautDetailRepository(db.Connection);

        // Today (fixed clock 2026-06-26) -> Julian YYDDD = 26 * 1000 + 177 = 26177.
        const int todayYyddd = 26177;
        const int expiryDays = 5;

        // ---- Account 1: one OLD approved auth (qualifies for delete) + a fresh one (survives). ----
        // Seed approvedCnt=2 so that after the single expired-approved detail is purged (count 2 -> 1) the
        // summary's remaining APPROVED count stays > 0 and the summary is NOT deleted (the bug-#2 check only
        // tests PA-APPROVED-AUTH-CNT). That keeps the fresh detail (seq 2) reachable rather than cascade-gone.
        InsertSummary(summaries, acctId: 111, approvedCnt: 2);
        InsertDetail(details, acctId: 111, seq: 1, yyddd: 26000, approved: true);  // WS-DAY-DIFF=177 >= 5 -> delete
        InsertDetail(details, acctId: 111, seq: 2, yyddd: todayYyddd, approved: false); // diff=0 < 5 -> survives

        // ---- Account 2: a fresh DECLINED auth, but the summary's APPROVED count is already 0. ----
        // Faithful bug #2: the empty-summary test only checks PA-APPROVED-AUTH-CNT (twice), never the
        // DECLINED count, so a summary whose approved-count is <= 0 is DELETED even though it still owns a
        // (declined) detail — and the FK cascade then removes that surviving detail too.
        InsertSummary(summaries, acctId: 222, approvedCnt: 0);
        InsertDetail(details, acctId: 222, seq: 1, yyddd: todayYyddd, approved: false); // fresh, but cascade-purged

        // SYSIN positional card: P-EXPIRY-DAYS(2), P-CHKP-FREQ(5), P-CHKP-DIS-FREQ(5), P-DEBUG-FLAG(1).
        string sysin = $"{expiryDays:D2},00001,00001,N";
        Cbpaup0cResult result = Cbpaup0c.Run(summaries, details, sysin, FixedClk);

        Assert.Equal(0, result.ReturnCode); // clean run, no abend
        Assert.Contains("STARTING PROGRAM CBPAUP0C::", result.Sysout);

        // Account 1: the OLD approved detail (seq 1) expired and was deleted; the fresh declined one
        // (seq 2) survives, and because the remaining APPROVED count is still > 0 the summary is kept.
        Assert.Equal(FileStatus.RecordNotFound, details.ReadByKey(111, AuthKey(1), out _));
        Assert.Equal(FileStatus.Ok, details.ReadByKey(111, AuthKey(2), out _));
        Assert.Equal(FileStatus.Ok, summaries.ReadByKey(111, out _));

        // Account 2: nothing expired, yet the approved-count-0 summary is purged (bug #2) and its fresh
        // declined detail is cascade-deleted with it.
        Assert.Equal(FileStatus.RecordNotFound, summaries.ReadByKey(222, out _));
        Assert.Equal(FileStatus.RecordNotFound, details.ReadByKey(222, AuthKey(1), out _));

        // The totals report shows the reads/deletes (2 summaries read, 1 summary + 1 detail deleted).
        Assert.Contains("# SUMMARY REC DELETED : 00000001", result.Sysout);
        Assert.Contains("# DETAILS REC DELETED : 00000001", result.Sysout);
    }

    // =================================================================================================
    //  IMS module #2 — COPAUS0C: online pending-auth view summary (resolve acct, page details).
    // =================================================================================================

    [Fact]
    public void Copaus0c_views_pending_auths_for_an_account()
    {
        using var db = EmptyDb();
        SeedAccountChain(db, acctId: 55501234567, custId: 9001, cardNum: "5550123456789012");
        var summaries = new PautSummaryRepository(db.Connection);
        var details = new PautDetailRepository(db.Connection);
        InsertSummary(summaries, acctId: 55501234567, approvedCnt: 2);
        InsertDetail(details, acctId: 55501234567, seq: 1, yyddd: 26177, approved: true,
            tranId: "TXN0000000001");
        InsertDetail(details, acctId: 55501234567, seq: 2, yyddd: 26176, approved: false,
            tranId: "TXN0000000002");

        var screen = new ScriptedScreen
        {
            NextAid = AidKey.Enter,
            OnReceive = map => map.Field("ACCTID").SetValue("55501234567"),
        };
        var dispatcher = new Dispatcher(OnlineProgramRegistry.Build(db), screen, FixedClk);

        // A reenter turn so MAIN-PARA RECEIVEs the keyed account id, gathers details, and pages forward.
        var ca = new CardDemoCommArea { FromTranId = "CPVS", FromProgram = "COPAUS0C" };
        ca.SetReenter();
        CicsOutcome outcome = dispatcher.RunTurn("CPVS", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CPVS", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("COPAU0A", screen.LastSentMap!.Name);
        // The first auth detail (newest = smallest AUTH_KEY) is painted into the row-1 transaction id field.
        Assert.Equal("TXN0000000001", screen.LastSentMap.Field("TRNID01").Value.TrimEnd());
        // Header echoes the account id.
        Assert.Equal("55501234567", screen.LastSentMap.Field("ACCTID").Value.TrimEnd());
    }

    // =================================================================================================
    //  IMS module #3 — COPAUS1C: online pending-auth detail (cold-start returns to the summary screen).
    // =================================================================================================

    [Fact]
    public void Copaus1c_cold_start_xctls_back_to_summary_program()
    {
        using var db = EmptyDb();
        var screen = new ScriptedScreen();
        var dispatcher = new Dispatcher(OnlineProgramRegistry.Build(db), screen, FixedClk);

        // Cold start (EIBCALEN=0): COPAUS1C immediately XCTLs to COPAUS0C (its summary parent), which then
        // RETURNs with its own TRANSID(CPVS). The dispatcher follows the XCTL chain in-turn.
        CicsOutcome outcome = dispatcher.RunTurn("CPVD", AidKey.None, commArea: null);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CPVS", outcome.TransId); // landed on COPAUS0C after the XCTL-back
    }

    // =================================================================================================
    //  MQ module #1 — COACCT01: account-inquiry request/response round-trip via the in-proc shim.
    // =================================================================================================

    [Fact]
    public void Coacct01_replies_with_the_account_snapshot_for_a_known_account()
    {
        using var db = EmptyDb();
        InsertAccount(db, acctId: 12345678901, status: "Y", currBal: 250.00m, creditLimit: 5000.00m);

        var broker = new MqBroker();
        broker.RegisterServer(new Coacct01(db));

        // Request payload (REQUEST-MSG-COPY): WS-FUNC 'INQA' + WS-KEY 9(11) account id.
        string request = "INQA" + "12345678901";
        // COACCT01 (faithful bug FB-5) ignores ReplyToQueue and PUTs to its literal CARD.DEMO.REPLY.ACCT, so
        // we listen on that queue.
        MqMessage? reply = broker.Request(
            MqQueues.RequestAcct, request, MqQueues.ReplyAcct, MqConstants.MqciNone);

        Assert.NotNull(reply);
        // The reply is the labeled WS-ACCT-RESPONSE snapshot (NORMAL read path), echoing the account id.
        Assert.Contains("ACCOUNT ID : 12345678901", reply!.Body);
        Assert.Contains("ACCOUNT STATUS : Y", reply.Body);
    }

    [Fact]
    public void Coacct01_replies_invalid_for_an_unknown_account()
    {
        using var db = EmptyDb();
        var broker = new MqBroker();
        broker.RegisterServer(new Coacct01(db));

        // A NOTFND read -> INVALID REQUEST PARAMETERS text reply (no abend).
        MqMessage? reply = broker.Request(
            MqQueues.RequestAcct, "INQA" + "99999999999", MqQueues.ReplyAcct, MqConstants.MqciNone);

        Assert.NotNull(reply);
        Assert.Contains("INVALID REQUEST PARAMETERS", reply!.Body);
    }

    // =================================================================================================
    //  MQ module #2 — CODATE01: date-utility request/response round-trip via the in-proc shim.
    // =================================================================================================

    [Fact]
    public void Codate01_replies_with_the_system_date_and_time()
    {
        var broker = new MqBroker();
        broker.RegisterServer(new Codate01(FixedClk));

        // CODATE01 ignores the request body (FB-1) and replies with the formatted system date/time.
        MqMessage? reply = broker.Request(
            MqQueues.RequestDate, "INQD", MqQueues.ReplyDate, MqConstants.MqciNone);

        Assert.NotNull(reply);
        // Fixed clock 2026-06-26 -> MM-DD-YYYY = 06-26-2026; the reply butts the date against the time label.
        Assert.Contains("SYSTEM DATE : 06-26-2026", reply!.Body);
        Assert.Contains("SYSTEM TIME : 09:00:00", reply.Body);
    }

    // =================================================================================================
    //  MQ module #3 — COPAUA0C: auth-decision server request->reply (also writes the IMS PAUT tables).
    // =================================================================================================

    [Fact]
    public void Copaua0c_approves_an_in_limit_auth_and_writes_the_pending_auth_tables()
    {
        using var db = EmptyDb();
        // A card whose account has plenty of available credit, so a small auth is approved.
        SeedAccountChain(db, acctId: 70012345678, custId: 7001, cardNum: "7001234567890123",
            creditLimit: 9000.00m, currBal: 100.00m);

        var broker = new MqBroker();
        broker.RegisterServer(new Copaua0c(db, clock: FixedClk));

        // The request is a comma-delimited card-auth record (18 fields); field 3 is the card number and
        // field 9 is the transaction amount. A $50.00 auth is well under the available limit -> APPROVE.
        string request = string.Join(',',
            "260626",                 // 1  auth date YYMMDD
            "090000",                 // 2  auth time HHMMSS
            "7001234567890123",       // 3  card num
            "0100",                   // 4  auth type
            "1228",                   // 5  card expiry
            "0100",                   // 6  message type
            "POS",                    // 7  message source
            "000000",                 // 8  processing code
            "0000000050.00",          // 9  transaction amount (-> 50.00)
            "5411",                   // 10 merchant category
            "840",                    // 11 acquirer country
            "01",                     // 12 pos entry mode
            "MERCH000000001",         // 13 merchant id
            "TEST MERCHANT",          // 14 merchant name
            "ANYTOWN",                // 15 merchant city
            "CA",                     // 16 merchant state
            "900010000",              // 17 merchant zip
            "AUTHTXN00000001");       // 18 transaction id

        byte[] correlId = Encoding.ASCII.GetBytes("CORREL-COPAUA0C-0001".PadRight(24).Substring(0, 24));
        MqMessage? reply = broker.Request(
            MqQueues.PauthRequest, request, MqQueues.PauthReply, correlId);

        Assert.NotNull(reply);
        // Reply layout: CARD-NUM(16) ',' TRANSACTION-ID(15) ',' AUTH-ID-CODE(6) ',' AUTH-RESP-CODE(2) ...
        // Auth resp code "00" = approved. Offset = 16 + 1 + 15 + 1 + 6 + 1 = 40.
        string respCode = reply!.Body.Substring(40, 2);
        Assert.Equal("00", respCode);

        // Side effect: COPAUA0C wrote the IMS pending-auth summary + detail for the account.
        var summaries = new PautSummaryRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, summaries.ReadByKey(70012345678, out PautSummary? smry));
        Assert.True(smry!.ApprovedAuthCnt >= 1);
        var details = new PautDetailRepository(db.Connection);
        Assert.True(details.ReadAllByParent(70012345678).Any());
    }

    // =================================================================================================
    //  Helpers — INPFILE records, seed data, AUTH_KEY synthesis.
    // =================================================================================================

    /// <summary>Builds a 53-byte COBTUPDT INPFILE record: action(1) + type(2) + desc(50), space-padded.</summary>
    private static string InpRec(char action, string type, string desc)
        => action + type.PadRight(2).Substring(0, 2) + desc.PadRight(50).Substring(0, 50);

    /// <summary>Seeds TRANSACTION_TYPE with rows "01".."NN" each with a "TYPE NN" description.</summary>
    private static void SeedTransactionTypes(RelationalDb db, int count)
    {
        var repo = new TransactionTypeRepository(db.Connection);
        for (int i = 1; i <= count; i++)
        {
            string code = i.ToString("D2");
            Assert.Equal(FileStatus.Ok, repo.Insert(new TransactionType
            {
                TrType = code,
                TrDescription = $"TRANSACTION TYPE {code}",
            }));
        }
    }

    /// <summary>An 8-char AUTH_KEY for a parent-scoped child sequence (ascending = newest-first).</summary>
    private static string AuthKey(int seq) => seq.ToString("D8");

    private static void InsertSummary(PautSummaryRepository repo, long acctId, int approvedCnt)
    {
        Assert.Equal(FileStatus.Ok, repo.Insert(new PautSummary
        {
            AcctId = acctId,
            CustId = acctId % 1000000000,
            AuthStatus = "A",
            CreditLimit = 9000.00m,
            CashLimit = 1000.00m,
            CreditBalance = 100.00m,
            CashBalance = 0.00m,
            ApprovedAuthCnt = approvedCnt,
            DeclinedAuthCnt = 0,
            ApprovedAuthAmt = approvedCnt * 25.00m,
            DeclinedAuthAmt = 0.00m,
        }));
    }

    /// <summary>
    /// Inserts a PAUT_DETAIL child. AUTH_DATE_9C is the 9s-complement of the real Julian date
    /// (99999 − yyddd) so CBPAUP0C decodes WS-AUTH-DATE = yyddd; the 8-char AUTH_KEY orders the twin chain.
    /// </summary>
    private static void InsertDetail(
        PautDetailRepository repo, long acctId, int seq, int yyddd, bool approved, string? tranId = null)
    {
        Assert.Equal(FileStatus.Ok, repo.Insert(new PautDetail
        {
            AcctId = acctId,
            AuthKey = AuthKey(seq),
            AuthDate9c = 99999 - yyddd,
            AuthTime9c = 999999999L - 90000000L,
            AuthOrigDate = "260626",
            AuthOrigTime = "090000",
            CardNum = acctId.ToString("D16"),
            AuthType = "0100",
            AuthRespCode = approved ? "00" : "05",
            AuthRespReason = approved ? "0000" : "4100",
            ApprovedAmt = approved ? 25.00m : 0.00m,
            TransactionAmt = 25.00m,
            TransactionId = tranId ?? $"TXN{seq:D12}",
            MatchStatus = "P",
            AuthFraud = " ",
        }));
    }

    /// <summary>Inserts a minimal ACCOUNT row.</summary>
    private static void InsertAccount(
        RelationalDb db, long acctId, string status, decimal currBal, decimal creditLimit)
    {
        var repo = new AccountRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, repo.Insert(new Account
        {
            AcctId = acctId,
            ActiveStatus = status,
            CurrBal = currBal,
            CreditLimit = creditLimit,
            CashCreditLimit = 1000.00m,
            OpenDate = "2020-01-01",
            ExpirationDate = "2028-01-01",
            ReissueDate = "2024-01-01",
            CurrCycCredit = 0.00m,
            CurrCycDebit = 0.00m,
            AddrZip = "90001",
            GroupId = "GRP0000001",
        }));
    }

    /// <summary>
    /// Seeds the CARD_XREF + ACCOUNT + CUSTOMER chain COPAUS0C/COPAUA0C walk to resolve an account from a
    /// card / account id (xref by acct alt key -> account master -> customer master).
    /// </summary>
    private static void SeedAccountChain(
        RelationalDb db, long acctId, long custId, string cardNum,
        decimal creditLimit = 5000.00m, decimal currBal = 100.00m)
    {
        InsertAccount(db, acctId, status: "Y", currBal: currBal, creditLimit: creditLimit);

        var xref = new CardXrefRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, xref.Insert(new CardXref
        {
            XrefCardNum = cardNum,
            CustId = custId,
            AcctId = acctId,
        }));

        var cust = new CustomerRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, cust.Insert(new Customer
        {
            CustId = custId,
            FirstName = "TEST",
            MiddleName = "Q",
            LastName = "CARDHOLDER",
            AddrLine1 = "123 MAIN ST",
            AddrLine2 = "SUITE 100",
            AddrLine3 = "",
            AddrStateCd = "CA",
            AddrCountryCd = "USA",
            AddrZip = "90001",
            PhoneNum1 = "5551234567",
            PhoneNum2 = "5559876543",
            Ssn = 123456789,
            GovtIssuedId = "DL12345678",
            DobYyyyMmDd = "1980-01-01",
            EftAccountId = "EFT0000001",
            PriCardHolderInd = "Y",
            FicoCreditScore = 750,
        }));
    }

    // ---- test doubles -------------------------------------------------------------------------------

    /// <summary>
    /// A scripted <see cref="IScreenIo"/>: on RECEIVE it runs <see cref="OnReceive"/> to inject keyed input
    /// field values and returns <see cref="NextAid"/>; on SEND it records the last symbolic map so the test
    /// can assert the painted field values. (Mirrors the double in <c>OnlineTests</c>.)
    /// </summary>
    private sealed class ScriptedScreen : IScreenIo
    {
        public Action<BmsMap>? OnReceive { get; set; }
        public AidKey NextAid { get; set; } = AidKey.Enter;
        public BmsMap? LastSentMap { get; private set; }
        public SendMapOptions LastSentOptions { get; private set; }
        public string? LastSentText { get; private set; }

        public void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options)
        {
            LastSentMap = symbolicMap as BmsMap;
            LastSentOptions = options;
        }

        public AidKey ReceiveMap(string map, string mapset, object symbolicMap)
        {
            if (symbolicMap is BmsMap m) OnReceive?.Invoke(m);
            return NextAid;
        }

        public void SendText(string text, bool erase = true, bool freeKb = true) => LastSentText = text;
    }
}
