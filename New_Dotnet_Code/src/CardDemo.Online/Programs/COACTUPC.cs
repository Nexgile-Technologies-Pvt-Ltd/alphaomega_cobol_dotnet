using System.Globalization;
using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COACTUPC</c> — CardDemo Account Update
/// (TRANSID <c>CAUP</c>, BMS map <c>CACTUPA</c> / mapset <c>COACTUP</c>).
/// </summary>
/// <remarks>
/// <para>
/// Near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>. Each COBOL paragraph is
/// one method carrying the original paragraph name and a <c>// source: COACTUPC.cbl:NNN</c> citation; the
/// pseudo-conversational <c>0000-MAIN</c> dispatch on <c>EIBCALEN</c>/<c>EIBAID</c>, the
/// <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage, and every validation message are
/// reproduced verbatim. VSAM ops map to the relational repositories
/// (<see cref="CardXrefRepository"/>/<see cref="AccountRepository"/>/<see cref="CustomerRepository"/>):
/// READ → <c>ReadByKey</c>/<c>ReadByAltKey</c>, READ UPDATE → the same read taking a before-image,
/// REWRITE → <c>Update</c>. Money is exact <see cref="decimal"/> (truncate-toward-zero), never float.
/// </para>
/// <para><b>Program-private COMMAREA.</b> The COBOL packs <c>CARDDEMO-COMMAREA</c> (nav, 160 bytes — the
/// runtime's <see cref="CardDemoCommArea"/>) followed by <c>WS-THIS-PROGCOMMAREA</c> (the <c>ACUP-*</c>
/// state: the change-action flag plus the OLD/NEW before/after images) inside one 2000-byte buffer
/// (0000-MAIN :888-892, COMMON-RETURN :1010-1013). The console runtime only round-trips the nav area, so
/// the <c>ACUP-*</c> trailer is serialized into <see cref="AcupCommArea"/> and carried across turns keyed
/// by the exact nav-area image (<see cref="ProgStateStore"/>) — content-stable across the dispatcher's
/// per-turn COMMAREA clone.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — <c>9700-CHECK-CHANGE-IN-REC</c> aborts on a detected change by <c>GO TO
/// 9600-WRITE-PROCESSING-EXIT</c> (the caller's exit), jumping out of the PERFORM range so the REWRITEs
/// are skipped. Reproduced by returning straight out of <c>WriteProcessing9600</c>.</item>
/// <item>FB-2 — the DOB before-image compare in 9700 reads the 8-char dashless OLD field at
/// <c>(5:2)</c>/<c>(7:2)</c> against the 10-char dashed live record at <c>(6:2)</c>/<c>(9:2)</c>. Exact
/// offsets preserved.</item>
/// <item>FB-3 — reissue-date double write in 9600: <c>MOVE ACCT-REISSUE-DATE TO
/// ACCT-UPDATE-REISSUE-DATE</c> (:3993) is dead, immediately overwritten by the STRING (:3994-4000). Both
/// kept; the STRING wins.</item>
/// <item>FB-4 — the blank-account message (88 <c>WS-PROMPT-FOR-ACCT</c> = "Account number not provided")
/// differs from the non-numeric/zero STRING literal ("Account Number if supplied must be a 11 digit
/// Non-Zero Number"). Both kept.</item>
/// <item>FB-5 — <c>CSSETATY</c> for EFT vs Primary-Holder are cross-labeled: the EFT comment block sets
/// the PRI-CARDHOLDER flag/ACSPFLG field, and vice-versa. Reproduced as authored.</item>
/// <item>FB-6 — the "all phone parts blank → optional" guard tests <c>NUMA</c> twice (third clause uses
/// <c>NUMA</c> where <c>NUMC</c> was clearly intended). Exact (buggy) condition kept.</item>
/// <item>FB-7 — date year edit accepts only century 19 or 20; any 21xx+ date is rejected with
/// ": Century is not valid." Kept.</item>
/// </list>
/// </remarks>
public sealed class Coactupc : ITransactionHandler
{
    // === WS-LITERALS. source: COACTUPC.cbl:532-582 ===
    private const string LIT_THISPGM = "COACTUPC";      // :533-534
    private const string LIT_THISTRANID = "CAUP";       // :535-536
    private const string LIT_THISMAPSET = "COACTUP ";   // :537-538
    private const string LIT_THISMAP = "CACTUPA";       // :539-540
    private const string LIT_MENUPGM = "COMEN01C";      // :557-558
    private const string LIT_MENUTRANID = "CM00";       // :559-560
    private const string LIT_CCLISTMAPSET = "COCRDLI";  // :553-554

    // === COTTL01Y title constants (COPY COTTL01Y). source: 620 ===
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    // === Output edited-numeric PIC. WS-EDIT-CURRENCY-9-2-F PIC +ZZZ,ZZZ,ZZZ.99. source: :371 ===
    private const string CurrencyPic = "+ZZZ,ZZZ,ZZZ.99";

    private readonly RelationalDb _db;

    /// <summary>Factory-friendly constructor. Repositories are created from <c>db.Connection</c> inside
    /// the handler when needed; no DB is opened here. source: spec §2.</summary>
    public Coactupc(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Coactupc() => _db = null!;

    public string ProgramName => LIT_THISPGM;   // PROGRAM-ID. source: :22-23
    public string TransId => LIT_THISTRANID;     // CSD TRANSACTION(CAUP). source: CSD_TRANSACTIONS.md

    // ============================================================================================
    //  Working storage (re-initialized every task per pseudo-conversational semantics)
    // ============================================================================================

    // WS-CICS-PROCESSNG-VARS. source: :39-47
    private string _wsTranId = "";              // WS-TRANID X(4). :44

    // WS-RETURN-MSG X(75) — only the FIRST error wins (every STRING guarded by WS-RETURN-MSG-OFF). :479
    private string _wsReturnMsg = "\0";         // WS-RETURN-FLAG-OFF == LOW-VALUES; here \0 == "off"
    private bool ReturnMsgOff => _wsReturnMsg.Length == 0 || _wsReturnMsg[0] == '\0'; // 88 WS-RETURN-MSG-OFF (SPACES) — see SetReturnMsg

    // WS-INPUT-FLAG. source: :171-174
    private bool _inputError;                   // 88 INPUT-ERROR '1' (INPUT-OK '0')

    // WS-DATACHANGED-FLAG. source: :168-170
    private bool _changeHasOccurred;            // 88 CHANGE-HAS-OCCURRED '1' / NO-CHANGES-FOUND '0'

    // WS-PFK-FLAG. source: :178-180
    private bool _pfkValid;

    // WS-EDIT-ACCT-FLAG / WS-EDIT-CUST-FLAG. source: :183-190
    private AcctFilter _acctFilter = AcctFilter.NotOk; // 88 ISVALID '1' / NOT-OK '0' / BLANK ' '
    private CustFilter _custFilter = CustFilter.NotOk;

    private enum AcctFilter { IsValid, NotOk, Blank }
    private enum CustFilter { IsValid, NotOk, Blank }

    // WS-FILE-READ-FLAGS. source: :384-388
    private bool _foundAcctInMaster;           // 88 FOUND-ACCT-IN-MASTER '1'
    private bool _foundCustInMaster;           // 88 FOUND-CUST-IN-MASTER '1'

    // WS-INFO-MSG X(40) — set via 88-levels in 3250. source: :463-477
    private string _wsInfoMsg = "";

    // WS-EDIT-VARIABLE-NAME X(25). source: :53
    private string _editVarName = "";

    // WS-XREF-RID. source: :376-383 — the keyed read RIDFLD work area.
    private long _cardRidAcctId;               // WS-CARD-RID-ACCT-ID 9(11)
    private long _cardRidCustId;               // WS-CARD-RID-CUST-ID 9(9)

    // === Per-field validity flag groups (WS-NON-KEY-FLAGS + date flags). source: :191-352, CSUTLDWY ===
    // Each flag is {Valid (LOW-VALUES), NotOk ('0'), Blank ('B')}.
    private Flg _fAcctStatus, _fCredLimit, _fCashLimit, _fCurrBal, _fCurrCycCredit, _fCurrCycDebit;
    private Flg _fFico, _fFirstName, _fMiddleName, _fLastName;
    private Flg _fAddr1, _fState, _fZip, _fCity, _fCountry, _fEft, _fPriCardholder;
    // Date triplets: year/month/day flags.
    private Flg _fOpenYear, _fOpenMon, _fOpenDay;
    private Flg _fExpYear, _fExpMon, _fExpDay;
    private Flg _fRisYear, _fRisMon, _fRisDay;
    private Flg _fDobYear, _fDobMon, _fDobDay;
    private Flg _fSsn1, _fSsn2, _fSsn3;
    private Flg _fPh1a, _fPh1b, _fPh1c, _fPh2a, _fPh2b, _fPh2c;

    /// <summary>A per-field validity flag mirroring the 88-levels …-ISVALID/…-NOT-OK/…-BLANK.</summary>
    private enum Flg { Valid, NotOk, Blank }

    // === COMMAREA (nav area) + program-private ACUP state. source: :650-849 ===
    private CardDemoCommArea _ca = new();
    private AcupCommArea _acup = new();

    // The per-turn received symbolic in-map (CACTUPAI) and out-map (CACTUPAO) — one BmsMap instance here.
    private BmsMap _map = null!;

    // ============================================================================================
    //  0000-MAIN. source: COACTUPC.cbl:859-1023
    // ============================================================================================
    public void Handle(CicsContext ctx)
    {
        // EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE) — abends surface as exceptions; no setup needed. :862-864

        // INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA. :866-868 (WS starts clean each task)
        _map = BuildBmsMap();

        // MOVE LIT-THISTRANID TO WS-TRANID. :872
        _wsTranId = LIT_THISTRANID;
        // SET WS-RETURN-MSG-OFF TO TRUE (clear error message). :876
        _wsReturnMsg = "\0";

        // Store passed data. :880-893
        bool freshFromMenu;
        if (ctx.EibCalen == 0
            || (Trim8(ctx.CommArea?.FromProgram) == LIT_THISPGM_MENU() && !(ctx.CommArea?.IsReenter ?? false)))
        {
            // INITIALIZE CARDDEMO-COMMAREA, WS-THIS-PROGCOMMAREA. :883-884
            _ca = ctx.CommArea is null ? new CardDemoCommArea() : ctx.CommArea; // keep inbound nav if present
            if (ctx.EibCalen == 0) _ca = new CardDemoCommArea();
            _acup = new AcupCommArea();
            _ca.SetFirstEntry();                       // SET CDEMO-PGM-ENTER TO TRUE. :885
            _acup.ChangeAction = AcupAction.NotFetched; // SET ACUP-DETAILS-NOT-FETCHED TO TRUE. :886
            freshFromMenu = true;
        }
        else
        {
            // MOVE DFHCOMMAREA(1:LEN OF CARDDEMO-COMMAREA) TO CARDDEMO-COMMAREA. :888-889
            _ca = ctx.CommArea!;
            // MOVE DFHCOMMAREA(LEN+1: LEN OF WS-THIS-PROGCOMMAREA) TO WS-THIS-PROGCOMMAREA. :890-892
            _acup = ProgStateStore.Load(_ca) ?? new AcupCommArea();
            freshFromMenu = false;
        }

        // PERFORM YYYY-STORE-PFKEY. :898-899
        CcardAid aid = CssTrpfy.StorePfKey(ctx.EibAid);

        // Check the AID validity at this point. :905-916
        _pfkValid = false; // SET PFK-INVALID TO TRUE. :905
        if (aid == CcardAid.Enter
            || aid == CcardAid.Pfk03
            || (aid == CcardAid.Pfk05 && _acup.ChangeAction == AcupAction.ChangesOkNotConfirmed)
            || (aid == CcardAid.Pfk12 && _acup.ChangeAction != AcupAction.NotFetched))
        {
            _pfkValid = true; // SET PFK-VALID TO TRUE. :911
        }
        if (!_pfkValid)
            aid = CcardAid.Enter; // SET CCARD-AID-ENTER TO TRUE. :915

        // EVALUATE TRUE. :921-1004
        if (aid == CcardAid.Pfk03)
        {
            // === PF03 EXIT: XCTL to caller or main menu. :927-959 ===
            // (SET CCARD-AID-PFK03 TO TRUE :928 — no-op here)
            if (IsLowOrSpaces(_ca.FromTranId))            // :930-931
                _ca.ToTranId = LIT_MENUTRANID;            // :932
            else
                _ca.ToTranId = _ca.FromTranId;            // :934
            if (IsLowOrSpaces(_ca.FromProgram))           // :937-938
                _ca.ToProgram = LIT_MENUPGM;              // :939
            else
                _ca.ToProgram = _ca.FromProgram;          // :941

            _ca.FromTranId = LIT_THISTRANID;              // :944
            _ca.FromProgram = LIT_THISPGM;                // :945
            _ca.SetUser();                                // SET CDEMO-USRTYP-USER. :947
            _ca.SetFirstEntry();                          // SET CDEMO-PGM-ENTER. :948
            _ca.LastMapSet = LIT_THISMAPSET.TrimEnd();    // :949
            _ca.LastMap = LIT_THISMAP;                    // :950

            // EXEC CICS SYNCPOINT :952-954 — no pending DB UOW on the exit path; nothing to commit.
            // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). :956-959
            ProgStateStore.Forget(_ca); // leaving this program; drop its private trailer
            ctx.Xctl(_ca.ToProgram.Trim(), _ca);
            return;
        }
        else if ((_acup.ChangeAction == AcupAction.NotFetched && _ca.IsFirstEntry)
                 || (Trim8(_ca.FromProgram) == LIT_MENUPGM && !_ca.IsReenter))
        {
            // === FRESH ENTRY: ask for the search key. :964-973 ===
            _acup = new AcupCommArea();                   // INITIALIZE WS-THIS-PROGCOMMAREA. :968
            SendMap3000(ctx);                             // PERFORM 3000-SEND-MAP. :969-970
            _ca.SetReenter();                             // SET CDEMO-PGM-REENTER. :971
            _acup.ChangeAction = AcupAction.NotFetched;   // SET ACUP-DETAILS-NOT-FETCHED. :972
            CommonReturn(ctx);                            // GO TO COMMON-RETURN. :973
            return;
        }
        else if (_acup.ChangeAction == AcupAction.OkayedAndDone
                 || _acup.ChangeAction == AcupAction.OkayedLockError
                 || _acup.ChangeAction == AcupAction.OkayedButFailed)
        {
            // === CHANGES DONE / FAILED: reset search keys. :979-989 ===
            _acup = new AcupCommArea();                   // INITIALIZE WS-THIS-PROGCOMMAREA. :981
            ResetMiscStorage();                           // INITIALIZE WS-MISC-STORAGE. :982
            _ca.AcctId = 0;                               // INITIALIZE CDEMO-ACCT-ID. :983
            _ca.SetFirstEntry();                          // SET CDEMO-PGM-ENTER. :984
            SendMap3000(ctx);                             // PERFORM 3000-SEND-MAP. :985-986
            _ca.SetReenter();                             // SET CDEMO-PGM-REENTER. :987
            _acup.ChangeAction = AcupAction.NotFetched;   // SET ACUP-DETAILS-NOT-FETCHED. :988
            CommonReturn(ctx);                            // GO TO COMMON-RETURN. :989
            return;
        }
        else
        {
            // === WHEN OTHER: process inputs, decide, send. :996-1003 ===
            ProcessInputs1000(ctx);                       // :997-998
            DecideAction2000(ctx);                        // :999-1000
            SendMap3000(ctx);                             // :1001-1002
            CommonReturn(ctx);                            // GO TO COMMON-RETURN. :1003
            return;
        }
    }

    // The COBOL test at :881 is `CDEMO-FROM-PROGRAM = LIT-MENUPGM`. Helper kept tiny + named per source.
    private static string LIT_THISPGM_MENU() => LIT_MENUPGM;

    // === COMMON-RETURN. source: COACTUPC.cbl:1007-1020 ===
    private void CommonReturn(CicsContext ctx)
    {
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. :1008 (CCARD-ERROR-MSG is CC-WORK-AREA, not persisted)
        // Reassemble WS-COMMAREA = CARDDEMO-COMMAREA ++ WS-THIS-PROGCOMMAREA. :1010-1013
        ProgStateStore.Save(_ca, _acup);
        // EXEC CICS RETURN TRANSID(CAUP) COMMAREA(WS-COMMAREA) LENGTH(2000). :1015-1019
        if (ctx.Outcome is null)
            ctx.ReturnTransId(LIT_THISTRANID, _ca);
    }

    // ============================================================================================
    //  1000-PROCESS-INPUTS. source: COACTUPC.cbl:1025-1037
    // ============================================================================================
    private void ProcessInputs1000(CicsContext ctx)
    {
        ReceiveMap1100(ctx);     // PERFORM 1100-RECEIVE-MAP. :1026-1027
        EditMapInputs1200();     // PERFORM 1200-EDIT-MAP-INPUTS. :1028-1029
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. :1030 (work area; no persistence)
        // MOVE LIT-THISPGM/MAPSET/MAP TO CCARD-NEXT-*. :1031-1033 (work area)
    }

    // ============================================================================================
    //  1100-RECEIVE-MAP. source: COACTUPC.cbl:1039-1427
    // ============================================================================================
    private void ReceiveMap1100(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('CACTUPA') MAPSET('COACTUP') INTO(CACTUPAI). :1040-1045
        ctx.ReceiveMap(LIT_THISMAP, LIT_THISMAPSET.TrimEnd(), _map);

        // INITIALIZE ACUP-NEW-DETAILS. :1047
        _acup.New = new AcupDetails();

        // Account id is always processed; '*'/SPACES → LOW-VALUES. CC-ACCT-ID := ACUP-NEW-ACCT-ID-X. :1051-1058
        _acup.New.AcctIdX = MapEmptyToLow(MapIn("ACCTSID"), 11);

        // IF ACUP-DETAILS-NOT-FETCHED GO TO exit (only the account number matters on search screen). :1060-1062
        if (_acup.ChangeAction == AcupAction.NotFetched)
            return;

        // For every editable field: '*'/SPACES → LOW-VALUES, else the value. :1064-1424
        _acup.New.ActiveStatus = MapEmptyToLow(MapIn("ACSTTUS"), 1);                 // :1065-1070

        // Money fields: MOVE raw X; if TEST-NUMVAL-C = 0 then parse to packed numeric, else leave raw. :1073-1140
        _acup.New.CreditLimit = MapMoney(MapIn("ACRDLIM"));                           // :1073-1084
        _acup.New.CashCreditLimit = MapMoney(MapIn("ACSHLIM"));                       // :1087-1098
        _acup.New.CurrBal = MapMoney(MapIn("ACURBAL"));                               // :1101-1112
        _acup.New.CurrCycCredit = MapMoney(MapIn("ACRCYCR"));                         // :1115-1126
        _acup.New.CurrCycDebit = MapMoney(MapIn("ACRCYDB"));                          // :1129-1140

        // Open date Y/M/D. :1144-1163
        _acup.New.OpenYear = MapEmptyToLow(MapIn("OPNYEAR"), 4);
        _acup.New.OpenMon = MapEmptyToLow(MapIn("OPNMON"), 2);
        _acup.New.OpenDay = MapEmptyToLow(MapIn("OPNDAY"), 2);
        // Expiry date Y/M/D. :1167-1186
        _acup.New.ExpYear = MapEmptyToLow(MapIn("EXPYEAR"), 4);
        _acup.New.ExpMon = MapEmptyToLow(MapIn("EXPMON"), 2);
        _acup.New.ExpDay = MapEmptyToLow(MapIn("EXPDAY"), 2);
        // Reissue date Y/M/D. :1190-1209
        _acup.New.RisYear = MapEmptyToLow(MapIn("RISYEAR"), 4);
        _acup.New.RisMon = MapEmptyToLow(MapIn("RISMON"), 2);
        _acup.New.RisDay = MapEmptyToLow(MapIn("RISDAY"), 2);
        // Account group. :1213-1218
        _acup.New.GroupId = MapEmptyToLow(MapIn("AADDGRP"), 10);

        // Customer id (not editable but received). :1224-1229
        _acup.New.CustIdX = MapEmptyToLow(MapIn("ACSTNUM"), 9);
        // SSN parts 1/2/3. :1233-1252
        _acup.New.Ssn1 = MapEmptyToLow(MapIn("ACTSSN1"), 3);
        _acup.New.Ssn2 = MapEmptyToLow(MapIn("ACTSSN2"), 2);
        _acup.New.Ssn3 = MapEmptyToLow(MapIn("ACTSSN3"), 4);
        // Date of birth Y/M/D. :1256-1275
        _acup.New.DobYear = MapEmptyToLow(MapIn("DOBYEAR"), 4);
        _acup.New.DobMon = MapEmptyToLow(MapIn("DOBMON"), 2);
        _acup.New.DobDay = MapEmptyToLow(MapIn("DOBDAY"), 2);
        // FICO. :1279-1284
        _acup.New.FicoX = MapEmptyToLow(MapIn("ACSTFCO"), 3);
        // Names. :1288-1311
        _acup.New.FirstName = MapEmptyToLow(MapIn("ACSFNAM"), 25);
        _acup.New.MiddleName = MapEmptyToLow(MapIn("ACSMNAM"), 25);
        _acup.New.LastName = MapEmptyToLow(MapIn("ACSLNAM"), 25);
        // Address line 1/2, City (ADDR-LINE-3), State, Country, Zip. :1315-1355
        _acup.New.AddrLine1 = MapEmptyToLow(MapIn("ACSADL1"), 50);
        _acup.New.AddrLine2 = MapEmptyToLow(MapIn("ACSADL2"), 50);
        _acup.New.AddrLine3 = MapEmptyToLow(MapIn("ACSCITY"), 50); // city → ADDR-LINE-3
        _acup.New.StateCd = MapEmptyToLow(MapIn("ACSSTTE"), 2);
        _acup.New.CountryCd = MapEmptyToLow(MapIn("ACSCTRY"), 3);
        _acup.New.Zip = MapEmptyToLow(MapIn("ACSZIPC"), 10);
        // Phone 1 A/B/C, Phone 2 A/B/C. :1357-1397
        _acup.New.Ph1a = MapEmptyToLow(MapIn("ACSPH1A"), 3);
        _acup.New.Ph1b = MapEmptyToLow(MapIn("ACSPH1B"), 3);
        _acup.New.Ph1c = MapEmptyToLow(MapIn("ACSPH1C"), 4);
        _acup.New.Ph2a = MapEmptyToLow(MapIn("ACSPH2A"), 3);
        _acup.New.Ph2b = MapEmptyToLow(MapIn("ACSPH2B"), 3);
        _acup.New.Ph2c = MapEmptyToLow(MapIn("ACSPH2C"), 4);
        // Govt id, EFT, Primary holder. :1401-1424
        _acup.New.GovtId = MapEmptyToLow(MapIn("ACSGOVT"), 20);
        _acup.New.EftAccountId = MapEmptyToLow(MapIn("ACSEFTC"), 10);
        _acup.New.PriHolderInd = MapEmptyToLow(MapIn("ACSPFLG"), 1);
    }

    /// <summary>MOVE of a money map field: copy the raw X(12); if FUNCTION TEST-NUMVAL-C = 0 (parseable)
    /// COMPUTE the packed numeric, else leave the raw chars. '*'/SPACES → LOW-VALUES. source: :1073-1084.</summary>
    private static MoneyField MapMoney(string raw)
    {
        if (IsStarOrSpaces(raw)) return new MoneyField { X = "", IsNum = false };
        string x = ClampOrPad(raw, 12); // ACUP-NEW-…-X is X(12)
        if (TestNumValC(x, out decimal n))
            return new MoneyField { X = x, IsNum = true, N = TruncateToV2(n) };
        return new MoneyField { X = x, IsNum = false };
    }

    // ============================================================================================
    //  1200-EDIT-MAP-INPUTS. source: COACTUPC.cbl:1429-1679
    // ============================================================================================
    private void EditMapInputs1200()
    {
        _inputError = false; // SET INPUT-OK TO TRUE. :1431

        if (_acup.ChangeAction == AcupAction.NotFetched)
        {
            // Validate the search keys only. :1433-1449
            EditAccount1210();                                   // :1435-1436
            _acup.Old = new AcupDetails();                       // MOVE LOW-VALUES TO ACUP-OLD-ACCT-DATA. :1438
            if (_acctFilter == AcctFilter.Blank)                 // :1441-1443
                SetReturnMsg("No input received");               // 88 NO-SEARCH-CRITERIA-RECEIVED. :489-490
            return;                                              // GO TO exit. :1446
        }

        // Search keys validated and data fetched. :1452-1457
        _foundAcctInMaster = true; _foundCustInMaster = true;
        _acctFilter = AcctFilter.IsValid; _custFilter = CustFilter.IsValid;

        CompareOldNew1205();                                     // :1460-1461

        // No change, or already confirmed/done → clear flags + exit. :1463-1468
        if (!_changeHasOccurred
            || _acup.ChangeAction == AcupAction.ChangesOkNotConfirmed
            || _acup.ChangeAction == AcupAction.OkayedAndDone)
        {
            ClearNonKeyFlags();                                  // MOVE LOW-VALUES TO WS-NON-KEY-FLAGS. :1466
            return;
        }

        _acup.ChangeAction = AcupAction.ChangesNotOk;           // SET ACUP-CHANGES-NOT-OK. :1470

        // === Full edit battery, in COBOL order. :1472-1662 ===
        _editVarName = "Account Status";                                                   // :1472
        _fAcctStatus = EditYesNo1220(_acup.New.ActiveStatus);                              // :1473-1476

        EditDateCcyymmdd(_acup.New.OpenDate(), "Open Date", out _fOpenYear, out _fOpenMon, out _fOpenDay, dobCheck: false); // :1478-1482

        _editVarName = "Credit Limit";                                                     // :1484
        _fCredLimit = EditSigned9V2(_acup.New.CreditLimitX);                               // :1485-1488

        EditDateCcyymmdd(_acup.New.ExpDate(), "Expiry Date", out _fExpYear, out _fExpMon, out _fExpDay, dobCheck: false); // :1490-1494

        _editVarName = "Cash Credit Limit";                                                // :1496
        _fCashLimit = EditSigned9V2(_acup.New.CashCreditLimitX);                           // :1497-1501

        EditDateCcyymmdd(_acup.New.RisDate(), "Reissue Date", out _fRisYear, out _fRisMon, out _fRisDay, dobCheck: false); // :1503-1507

        _editVarName = "Current Balance";                                                  // :1509
        _fCurrBal = EditSigned9V2(_acup.New.CurrBalX);                                     // :1510-1513

        _editVarName = "Current Cycle Credit Limit";                                       // :1515
        _fCurrCycCredit = EditSigned9V2(_acup.New.CurrCycCreditX);                         // :1516-1520

        _editVarName = "Current Cycle Debit Limit";                                        // :1522
        _fCurrCycDebit = EditSigned9V2(_acup.New.CurrCycDebitX);                           // :1523-1527

        _editVarName = "SSN";                                                              // :1529
        EditUsSsn1265();                                                                   // :1530-1531

        EditDateCcyymmdd(_acup.New.DobDate(), "Date of Birth", out _fDobYear, out _fDobMon, out _fDobDay, dobCheck: true); // :1533-1543

        _editVarName = "FICO Score";                                                       // :1545
        _fFico = EditNumReqd1245(_acup.New.FicoX, 3);                                       // :1546-1552
        if (_fFico == Flg.Valid)                                                           // :1553
            _fFico = EditFicoScore1275(_acup.New.FicoX);                                    // :1554-1555

        _editVarName = "First Name";                                                        // :1560
        _fFirstName = EditAlphaReqd1225(_acup.New.FirstName, 25);                           // :1561-1566
        _editVarName = "Middle Name";                                                       // :1568
        _fMiddleName = EditAlphaOpt1235(_acup.New.MiddleName, 25);                          // :1569-1574
        _editVarName = "Last Name";                                                         // :1576
        _fLastName = EditAlphaReqd1225(_acup.New.LastName, 25);                             // :1577-1582

        _editVarName = "Address Line 1";                                                    // :1584
        _fAddr1 = EditMandatory1215(_acup.New.AddrLine1, 50);                               // :1585-1590

        _editVarName = "State";                                                             // :1592
        _fState = EditAlphaReqd1225(_acup.New.StateCd, 2);                                  // :1593-1598
        if (_fState == Flg.Valid)                                                           // FLG-ALPHA-ISVALID :1599
            _fState = EditUsStateCd1270(_acup.New.StateCd);                                 // :1600-1601

        _editVarName = "Zip";                                                               // :1605
        _fZip = EditNumReqd1245(_acup.New.Zip, 5);                                          // :1606-1611

        _editVarName = "City";                                                              // :1615
        _fCity = EditAlphaReqd1225(_acup.New.AddrLine3, 50);                                // :1616-1621

        _editVarName = "Country";                                                           // :1623
        _fCountry = EditAlphaReqd1225(_acup.New.CountryCd, 3);                              // :1624-1630

        _editVarName = "Phone Number 1";                                                    // :1632
        EditUsPhone1260(_acup.New.PhoneNum1(), out _fPh1a, out _fPh1b, out _fPh1c);         // :1633-1638
        _editVarName = "Phone Number 2";                                                    // :1640
        EditUsPhone1260(_acup.New.PhoneNum2(), out _fPh2a, out _fPh2b, out _fPh2c);         // :1641-1646

        _editVarName = "EFT Account Id";                                                    // :1648
        _fEft = EditNumReqd1245(_acup.New.EftAccountId, 10);                                // :1649-1655

        _editVarName = "Primary Card Holder";                                               // :1657
        _fPriCardholder = EditYesNo1220(_acup.New.PriHolderInd);                            // :1658-1662

        // Cross-field: state + zip combo. :1665-1669
        if (_fState == Flg.Valid && _fZip == Flg.Valid)
            EditUsStateZipCd1280();

        // If no error → SET ACUP-CHANGES-OK-NOT-CONFIRMED. :1671-1675
        if (!_inputError)
            _acup.ChangeAction = AcupAction.ChangesOkNotConfirmed;
    }

    // ============================================================================================
    //  1205-COMPARE-OLD-NEW. source: COACTUPC.cbl:1681-1779
    // ============================================================================================
    private void CompareOldNew1205()
    {
        _changeHasOccurred = false; // SET NO-CHANGES-FOUND (WS-DATACHANGED-FLAG). :1682
        _noChangesDetected = false; // NO-CHANGES-DETECTED is a WS-RETURN-MSG 88, set below.
        AcupDetails n = _acup.New, o = _acup.Old;

        // Account fields. :1684-1705
        bool acctSame =
            n.AcctIdX == o.AcctIdX
            && Up(n.ActiveStatus) == Up(o.ActiveStatus)
            && n.CurrBalX12() == o.CurrBalX12()
            && n.CreditLimitX12() == o.CreditLimitX12()
            && n.CashCreditLimitX12() == o.CashCreditLimitX12()
            && n.OpenDate() == o.OpenDate()
            && n.ExpDate() == o.ExpDate()
            && n.RisDate() == o.RisDate()
            && n.CurrCycCreditX12() == o.CurrCycCreditX12()
            && n.CurrCycDebitX12() == o.CurrCycDebitX12()
            && UpTrim(n.GroupId) == UpTrim(o.GroupId);
        if (!acctSame)
        {
            _changeHasOccurred = true; // SET CHANGE-HAS-OCCURRED, GO TO exit. :1703-1704
            return;
        }

        // Customer fields. :1708-1773
        bool custSame =
            UpTrim(n.CustIdX) == UpTrim(o.CustIdX)
            && UpTrim(n.FirstName) == UpTrim(o.FirstName)
            && UpTrim(n.MiddleName) == UpTrim(o.MiddleName)
            && UpTrim(n.LastName) == UpTrim(o.LastName)
            && UpTrim(n.AddrLine1) == UpTrim(o.AddrLine1)
            && UpTrim(n.AddrLine2) == UpTrim(o.AddrLine2)
            && UpTrim(n.AddrLine3) == UpTrim(o.AddrLine3)
            && UpTrim(n.StateCd) == UpTrim(o.StateCd)
            && UpTrim(n.CountryCd) == UpTrim(o.CountryCd)
            && UpTrim(n.Zip) == UpTrim(o.Zip)
            && n.Ph1a == o.Ph1a && n.Ph1b == o.Ph1b && n.Ph1c == o.Ph1c
            && n.Ph2a == o.Ph2a && n.Ph2b == o.Ph2b && n.Ph2c == o.Ph2c
            && n.SsnX() == o.SsnX()
            && UpTrim(n.GovtId) == UpTrim(o.GovtId)
            && n.DobDate() == o.DobDate()
            && n.EftAccountId == o.EftAccountId
            && UpTrim(n.PriHolderInd) == UpTrim(o.PriHolderInd)
            && n.FicoX == o.FicoX;
        if (custSame)
            _noChangesDetected = true;  // SET NO-CHANGES-DETECTED (WS-DATACHANGED-FLAG stays NO-CHANGES-FOUND). :1769
        else
            _changeHasOccurred = true;  // SET CHANGE-HAS-OCCURRED, GO TO exit. :1771-1772
    }

    // ============================================================================================
    //  1210-EDIT-ACCOUNT. source: COACTUPC.cbl:1783-1822
    // ============================================================================================
    private void EditAccount1210()
    {
        _acctFilter = AcctFilter.NotOk; // SET FLG-ACCTFILTER-NOT-OK. :1784
        string ccAcctId = _acup.New.AcctIdX; // CC-ACCT-ID

        // Not supplied. :1787-1797
        if (IsLowOrSpaces(ccAcctId))
        {
            _inputError = true;                          // :1789
            _acctFilter = AcctFilter.Blank;              // :1790
            if (ReturnMsgOff)
                SetReturnMsg("Account number not provided"); // 88 WS-PROMPT-FOR-ACCT (FB-4). :483-484,1791-1792
            _ca.AcctId = 0;                              // MOVE ZEROES TO CDEMO-ACCT-ID. :1794
            _acup.New.AcctIdX = "";                       // MOVE ZEROES TO ACUP-NEW-ACCT-ID. :1795 (numeric → 0)
            return;                                       // GO TO exit. :1796
        }

        // MOVE CC-ACCT-ID TO ACUP-NEW-ACCT-ID. :1801 (already in New.AcctIdX)
        // Not numeric / zero. :1802-1813
        if (!IsAllDigits(ccAcctId) || ParseLong(ccAcctId) == 0)
        {
            _inputError = true;                          // :1804
            if (ReturnMsgOff)
                SetReturnMsg("Account Number if supplied must be a 11 digit Non-Zero Number"); // :1806-1810
            _ca.AcctId = 0;                              // MOVE ZEROES TO CDEMO-ACCT-ID. :1812
            return;                                       // GO TO exit. :1813
        }
        _ca.AcctId = ParseLong(ccAcctId);                // MOVE CC-ACCT-ID TO CDEMO-ACCT-ID. :1815
        _acctFilter = AcctFilter.IsValid;                 // SET FLG-ACCTFILTER-ISVALID. :1816
    }

    // ============================================================================================
    //  Generic field editors (1215-1250). Each returns the field's validity flag and STRINGs the
    //  exact message into WS-RETURN-MSG only when WS-RETURN-MSG-OFF (first error wins).
    // ============================================================================================

    // 1215-EDIT-MANDATORY. source: :1824-1851
    private Flg EditMandatory1215(string value, int len)
    {
        if (IsBlankField(value, len))
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be supplied."); // :1839-1844
            return Flg.Blank;
        }
        return Flg.Valid;
    }

    // 1220-EDIT-YESNO. source: :1856-1893
    private Flg EditYesNo1220(string value)
    {
        // WS-EDIT-YES-NO is a single char; blank/low/zero → must be supplied. :1861-1874
        char c = FirstChar(value);
        if (c == '\0' || c == ' ' || c == '0') // EQUAL LOW-VALUES OR SPACES OR ZEROS
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be supplied."); // :1867-1872
            return Flg.Blank;
        }
        if (c == 'Y' || c == 'N') // 88 FLG-YES-NO-ISVALID. :1878
            return Flg.Valid;
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be Y or N."); // :1884-1889
        return Flg.NotOk;
    }

    // 1225-EDIT-ALPHA-REQD. source: :1898-1950
    private Flg EditAlphaReqd1225(string value, int len)
    {
        if (IsBlankField(value, len))
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be supplied."); // :1913-1918
            return Flg.Blank;
        }
        // INSPECT … CONVERTING letters→spaces; residue non-empty → not alpha. :1925-1947
        if (AlphaResidueEmpty(Field(value, len)))
            return Flg.Valid;
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " can have alphabets only."); // :1939-1944
        return Flg.NotOk;
    }

    // 1235-EDIT-ALPHA-OPT. source: :2012-2056
    private Flg EditAlphaOpt1235(string value, int len)
    {
        if (IsBlankField(value, len))
            return Flg.Valid; // optional → valid. :2024
        if (AlphaResidueEmpty(Field(value, len)))
            return Flg.Valid;
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " can have alphabets only."); // :2045-2050
        return Flg.NotOk;
    }

    // 1245-EDIT-NUM-REQD. source: :2109-2175
    private Flg EditNumReqd1245(string value, int len)
    {
        if (IsBlankField(value, len))
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be supplied."); // :2124-2129
            return Flg.Blank;
        }
        string fld = Field(value, len);
        if (!IsAllDigits(fld)) // NOT NUMERIC. :2137-2152
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be all numeric."); // :2144-2149
            return Flg.NotOk;
        }
        if (NumVal(fld) == 0m) // FUNCTION NUMVAL = 0. :2156-2168
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must not be zero."); // :2161-2166
            return Flg.NotOk;
        }
        return Flg.Valid;
    }

    // 1250-EDIT-SIGNED-9V2. source: :2180-2221
    private Flg EditSigned9V2(string valueX)
    {
        // WS-EDIT-SIGNED-NUMBER-9V2-X is the raw X(12) from the money field. :2184-2196
        if (IsLowOrSpacesFull(valueX, 12))
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " must be supplied."); // :2189-2194
            return Flg.Blank;
        }
        if (TestNumValC(valueX, out _)) // = 0 → valid. :2201
            return Flg.Valid;
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + " is not valid"); // :2207-2211
        return Flg.NotOk;
    }

    // ============================================================================================
    //  1260-EDIT-US-PHONE-NUM (+ EDIT-AREA-CODE / -PREFIX / -LINENUM). source: :2225-2426
    // ============================================================================================
    private void EditUsPhone1260(string phone15, out Flg fa, out Flg fb, out Flg fc)
    {
        fa = Flg.Valid; fb = Flg.Valid; fc = Flg.Valid;
        // The X(15) is (AAA)BBB-CCCC; parts via redefine: A=(2:3) B=(6:3) C=(10:4). source: :82-100
        string numA = Slice(phone15, 2, 3);
        string numB = Slice(phone15, 6, 3);
        string numC = Slice(phone15, 10, 4);

        // FB-6: optional guard tests NUMA twice (third clause uses NUMA where NUMC was meant). :2234-2240
        bool allBlank =
            (IsSpaces(numA) || IsLow(numA))
            && (IsSpaces(numB) || IsLow(numB))
            && (IsSpaces(numA) || IsLow(numC));
        if (allBlank)
            return; // SET WS-EDIT-US-PHONE-IS-VALID, GO TO EDIT-US-PHONE-EXIT. :2240-2241

        // EDIT-AREA-CODE. :2246-2314
        if (IsSpaces(numA) || IsLow(numA))
        {
            _inputError = true; fa = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Area code must be supplied."); // :2252-2257
            EditUsPhonePrefix(numB, numC, ref fb, ref fc); return;                                // GO TO -PREFIX
        }
        if (!IsAllDigits(numA))
        {
            _inputError = true; fa = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Area code must be A 3 digit number."); // :2270-2275
            EditUsPhonePrefix(numB, numC, ref fb, ref fc); return;
        }
        if (ParseLong(numA) == 0)
        {
            _inputError = true; fa = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Area code cannot be zero"); // :2284-2289
            EditUsPhonePrefix(numB, numC, ref fb, ref fc); return;
        }
        if (!Lookups.ValidGeneralPurposeAreaCode(Trim(numA))) // VALID-GENERAL-PURP-CODE. :2296-2298
        {
            _inputError = true; fa = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Not valid North America general purpose area code"); // :2304-2309
            EditUsPhonePrefix(numB, numC, ref fb, ref fc); return;
        }
        // SET FLG-EDIT-US-PHONEA-ISVALID. :2314 (fa already Valid)
        EditUsPhonePrefix(numB, numC, ref fb, ref fc);
    }

    // EDIT-US-PHONE-PREFIX. source: :2316-2367
    private void EditUsPhonePrefix(string numB, string numC, ref Flg fb, ref Flg fc)
    {
        if (IsSpaces(numB) || IsLow(numB))
        {
            _inputError = true; fb = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Prefix code must be supplied."); // :2323-2328
            EditUsPhoneLinenum(numC, ref fc); return;                                                // GO TO -LINENUM
        }
        if (!IsAllDigits(numB))
        {
            _inputError = true; fb = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Prefix code must be A 3 digit number."); // :2341-2346
            EditUsPhoneLinenum(numC, ref fc); return;
        }
        if (ParseLong(numB) == 0)
        {
            _inputError = true; fb = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Prefix code cannot be zero"); // :2355-2360
            EditUsPhoneLinenum(numC, ref fc); return;
        }
        // SET FLG-EDIT-US-PHONEB-ISVALID. :2367
        EditUsPhoneLinenum(numC, ref fc);
    }

    // EDIT-US-PHONE-LINENUM. source: :2370-2421
    private void EditUsPhoneLinenum(string numC, ref Flg fc)
    {
        if (IsSpaces(numC) || IsLow(numC))
        {
            _inputError = true; fc = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Line number code must be supplied."); // :2376-2381
            return;
        }
        if (!IsAllDigits(numC))
        {
            _inputError = true; fc = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Line number code must be A 4 digit number."); // :2394-2399
            return;
        }
        if (ParseLong(numC) == 0)
        {
            _inputError = true; fc = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": Line number code cannot be zero"); // :2408-2413
            return;
        }
        // SET FLG-EDIT-US-PHONEC-ISVALID. :2421
    }

    // ============================================================================================
    //  1265-EDIT-US-SSN. source: :2431-2490
    // ============================================================================================
    private void EditUsSsn1265()
    {
        // Part 1: 3-digit num required, then INVALID-SSN-PART1 (0, 666, 900-999). :2439-2464
        _editVarName = "SSN: First 3 chars";
        _fSsn1 = EditNumReqd1245(_acup.New.Ssn1, 3);
        if (_fSsn1 == Flg.Valid)
        {
            int p1 = (int)ParseLong(Field(_acup.New.Ssn1, 3));
            if (p1 == 0 || p1 == 666 || (p1 >= 900 && p1 <= 999)) // 88 INVALID-SSN-PART1. :121-123
            {
                _inputError = true; _fSsn1 = Flg.NotOk;
                if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": should not be 000, 666, or between 900 and 999"); // :2455-2460
            }
        }
        // Part 2: 2-digit num required. :2469-2475
        _editVarName = "SSN 4th & 5th chars";
        _fSsn2 = EditNumReqd1245(_acup.New.Ssn2, 2);
        // Part 3: 4-digit num required. :2481-2487
        _editVarName = "SSN Last 4 chars";
        _fSsn3 = EditNumReqd1245(_acup.New.Ssn3, 4);
    }

    // 1270-EDIT-US-STATE-CD. source: :2493-2511
    private Flg EditUsStateCd1270(string stateCd)
    {
        if (Lookups.ValidUsStateCode(Field(stateCd, 2))) // VALID-US-STATE-CODE. :2495
            return Flg.Valid; // unchanged from the alpha-valid caller
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": is not a valid state code"); // :2501-2506
        return Flg.NotOk;
    }

    // 1275-EDIT-FICO-SCORE. source: :2514-2531
    private Flg EditFicoScore1275(string ficoX)
    {
        int fico = (int)ParseLong(Field(ficoX, 3));
        if (fico >= 300 && fico <= 850) // 88 FICO-RANGE-IS-VALID. :848-849
            return Flg.Valid;
        _inputError = true;
        if (ReturnMsgOff) SetReturnMsg(Trim(_editVarName) + ": should be between 300 and 850"); // :2521-2526
        return Flg.NotOk;
    }

    // 1280-EDIT-US-STATE-ZIP-CD. source: :2536-2558
    private void EditUsStateZipCd1280()
    {
        // STRING state ++ first 2 of zip → US-STATE-AND-FIRST-ZIP2 (X(4)). :2537-2540
        string combo = ClampOrPad(Field(_acup.New.StateCd, 2) + Slice(_acup.New.Zip, 1, 2), 4);
        if (Lookups.ValidUsStateZipCombo(combo)) // VALID-US-STATE-ZIP-CD2-COMBO. :2542
            return;
        _inputError = true;
        _fState = Flg.NotOk; _fZip = Flg.NotOk; // SET FLG-STATE-NOT-OK + FLG-ZIPCODE-NOT-OK. :2546-2547
        if (ReturnMsgOff) SetReturnMsg("Invalid zip code for state"); // :2549-2553
    }

    // ============================================================================================
    //  Date edits (COPY CSUTLDPY orchestrated by EDIT-DATE-CCYYMMDD; fall-through paragraphs).
    //  source: CSUTLDPY.cpy:18-331, CSUTLDWY.cpy:4-59
    // ============================================================================================
    /// <summary>
    /// Validates an 8-char CCYYMMDD date (the dashless form WS-EDIT-DATE-CCYYMMDD). Runs the fall-through
    /// chain EDIT-YEAR-CCYY → EDIT-MONTH → EDIT-DAY → EDIT-DAY-MONTH-YEAR → EDIT-DATE-LE, honoring the
    /// GO TO EDIT-DATE-CCYYMMDD-EXIT short-circuits. Sets the three Y/M/D flags. When <paramref name="dobCheck"/>
    /// and the date is fully valid, also runs EDIT-DATE-OF-BIRTH (future check). source: 1478-1543.
    /// </summary>
    private void EditDateCcyymmdd(string ccyymmdd, string name, out Flg fy, out Flg fm, out Flg fd, bool dobCheck)
    {
        _editVarName = name;
        fy = Flg.NotOk; fm = Flg.NotOk; fd = Flg.NotOk;
        bool dateInvalid = true; // SET WS-EDIT-DATE-IS-INVALID. :19

        string cc = Slice(ccyymmdd, 1, 2);   // WS-EDIT-DATE-CC
        string yy = Slice(ccyymmdd, 3, 2);   // WS-EDIT-DATE-YY
        string ccyy = Slice(ccyymmdd, 1, 4); // WS-EDIT-DATE-CCYY
        string mm = Slice(ccyymmdd, 5, 2);   // WS-EDIT-DATE-MM
        string dd = Slice(ccyymmdd, 7, 2);   // WS-EDIT-DATE-DD

        // --- EDIT-YEAR-CCYY. CSUTLDPY:25-87 ---
        fy = Flg.NotOk; // SET FLG-YEAR-NOT-OK. :27
        if (IsLowOrSpacesFull(ccyy, 4))
        {
            _inputError = true; fy = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + " : Year must be supplied."); // :35-39
            // GO TO EDIT-YEAR-CCYY-EXIT → falls through into EDIT-MONTH (and onward). CSUTLDPY:42,88-90
            DateContinueAfterYear(ccyymmdd, name, mm, dd, ccyy, yy, ref fy, ref fm, ref fd, dobCheck, ref dateInvalid);
            return;
        }
        if (!IsAllDigits(ccyy))
        {
            _inputError = true; fy = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + " must be 4 digit number."); // :52-56
            DateContinueAfterYear(ccyymmdd, name, mm, dd, ccyy, yy, ref fy, ref fm, ref fd, dobCheck, ref dateInvalid);
            return;
        }
        int ccN = (int)ParseLong(cc);
        if (ccN == 20 || ccN == 19) // THIS-CENTURY (20) / LAST-CENTURY (19). CSUTLDWY:9-10 (FB-7)
        {
            fy = Flg.Valid; // SET FLG-YEAR-ISVALID. :86
        }
        else
        {
            _inputError = true; fy = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + " : Century is not valid."); // :77-81
            DateContinueAfterYear(ccyymmdd, name, mm, dd, ccyy, yy, ref fy, ref fm, ref fd, dobCheck, ref dateInvalid);
            return;
        }
        DateContinueAfterYear(ccyymmdd, name, mm, dd, ccyy, yy, ref fy, ref fm, ref fd, dobCheck, ref dateInvalid);
    }

    // Year-blank GO TO jumps to EDIT-YEAR-CCYY-EXIT which falls into EDIT-MONTH; non-blank year errors also
    // fall through. This helper runs the remaining fall-through chain from EDIT-MONTH onward.
    private void DateContinueAfterYear(string ccyymmdd, string name, string mm, string dd, string ccyy,
        string yy, ref Flg fy, ref Flg fm, ref Flg fd, bool dobCheck, ref bool dateInvalid)
    {
        // --- EDIT-MONTH. CSUTLDPY:91-147 ---
        fm = Flg.NotOk; // SET FLG-MONTH-NOT-OK. :92
        bool monthOk = false;
        int mmN = 0;
        if (IsLowOrSpacesFull(mm, 2))
        {
            _inputError = true; fm = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + " : Month must be supplied."); // :100-104
            // GO TO EDIT-MONTH-EXIT → falls to EDIT-DAY.
        }
        else if (!(IsAllDigits(mm) && (mmN = (int)ParseLong(mm)) >= 1 && mmN <= 12)) // WS-VALID-MONTH. :111
        {
            _inputError = true; fm = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + ": Month must be a number between 1 and 12."); // :117-121
        }
        else if (!TestNumVal(mm)) // TEST-NUMVAL != 0. :126-141
        {
            _inputError = true; fm = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + ": Month must be a number between 1 and 12."); // :134-138
        }
        else
        {
            mmN = (int)ParseLong(mm); fm = Flg.Valid; monthOk = true; // SET FLG-MONTH-ISVALID. :143
        }

        // --- EDIT-DAY. CSUTLDPY:150-207 ---
        fd = Flg.Valid; // SET FLG-DAY-ISVALID. :152
        int ddN = 0;
        bool dayOk = false;
        if (IsLowOrSpacesFull(dd, 2))
        {
            _inputError = true; fd = Flg.Blank;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + " : Day must be supplied."); // :159-163
            // GO TO EDIT-DAY-EXIT → falls to EDIT-DAY-MONTH-YEAR.
        }
        else if (!TestNumVal(dd)) // TEST-NUMVAL != 0. :170-185
        {
            _inputError = true; fd = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":day must be a number between 1 and 31."); // :178-182
        }
        else
        {
            ddN = (int)ParseLong(dd);
            if (ddN >= 1 && ddN <= 31) // WS-VALID-DAY. :187
            {
                fd = Flg.Valid; dayOk = true; // SET FLG-DAY-ISVALID. :203
            }
            else
            {
                _inputError = true; fd = Flg.NotOk;
                if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":day must be a number between 1 and 31."); // :193-197
            }
        }

        // --- EDIT-DAY-MONTH-YEAR. CSUTLDPY:209-282 ---
        bool[] is31 = { false, true, false, true, false, true, false, true, true, false, true, false, true };
        bool month31 = monthOk && mmN >= 1 && mmN <= 12 && is31[mmN]; // WS-31-DAY-MONTH. :21-23
        bool february = monthOk && mmN == 2;                          // WS-FEBRUARY. :24

        if (!month31 && ddN == 31) // NOT WS-31-DAY-MONTH AND WS-DAY-31. :213-214
        {
            _inputError = true; fd = Flg.NotOk; fm = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":Cannot have 31 days in this month."); // :219-223
            return; // GO TO EDIT-DATE-CCYYMMDD-EXIT. :225
        }
        if (february && ddN == 30) // WS-FEBRUARY AND WS-DAY-30. :228-229
        {
            _inputError = true; fd = Flg.NotOk; fm = Flg.NotOk;
            if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":Cannot have 30 days in this month."); // :234-238
            return; // :240
        }
        if (february && ddN == 29) // WS-FEBRUARY AND WS-DAY-29. :243-244
        {
            int yyN = (int)ParseLong(yy);                 // WS-EDIT-DATE-YY-N
            int divBy = yyN == 0 ? 400 : 4;               // :245-249
            int ccyyN = IsAllDigits(ccyy) ? (int)ParseLong(ccyy) : 0;
            if (ccyyN % divBy == 0)                       // WS-REMAINDER = 0. :256
            {
                // leap year ok — CONTINUE
            }
            else
            {
                _inputError = true; fd = Flg.NotOk; fm = Flg.NotOk; fy = Flg.NotOk;
                if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":Not a leap year.Cannot have 29 days in this month."); // :264-268
                return; // :270
            }
        }

        // IF WS-EDIT-DATE-IS-VALID … ELSE GO TO EXIT. :274-278
        // dateInvalid is still true (set at start), so if any prior edit flagged INPUT-ERROR we stop here.
        if (_inputError && (fy != Flg.Valid || fm != Flg.Valid || fd != Flg.Valid))
            return; // a field is bad; skip the LE check and DOB future check.

        // --- EDIT-DATE-LE: CSUTLDTC LE validation. CSUTLDPY:284-325 ---
        if (!Lookups.LeDateValid(ccyymmdd)) // WS-SEVERITY-N != 0 path. :298
        {
            _inputError = true; fd = Flg.NotOk; fm = Flg.NotOk; fy = Flg.NotOk;
            if (ReturnMsgOff)
                SetReturnMsg(Trim(name) + " validation error Sev code: 0012 Message code: 0002"); // :306-313
            return; // :315
        }
        if (!_inputError) fd = Flg.Valid; // :318-319
        dateInvalid = false; // SET WS-EDIT-DATE-IS-VALID. :326-327

        // --- EDIT-DATE-OF-BIRTH (only for DOB, only if fully valid). CSUTLDPY:341-368; caller :1539-1542 ---
        if (dobCheck && fy == Flg.Valid && fm == Flg.Valid && fd == Flg.Valid && !dateInvalid)
        {
            DateTime now = _now;
            int dobN = IsAllDigits(ccyymmdd) ? (int)ParseLong(ccyymmdd) : 0;
            int todayN = now.Year * 10000 + now.Month * 100 + now.Day;
            if (todayN > dobN) // current > dob → ok (past). :350
            {
                // CONTINUE
            }
            else
            {
                _inputError = true; fd = Flg.NotOk; fm = Flg.NotOk; fy = Flg.NotOk;
                if (ReturnMsgOff) SetReturnMsg(Trim(name) + ":cannot be in the future "); // :361-365
            }
        }
    }

    // ============================================================================================
    //  2000-DECIDE-ACTION. source: COACTUPC.cbl:2562-2645
    // ============================================================================================
    private void DecideAction2000(CicsContext ctx)
    {
        CcardAid aid = CssTrpfy.StorePfKey(ctx.EibAid);
        if (!_pfkValid) aid = CcardAid.Enter; // AID was coerced to ENTER in 0000-MAIN if invalid.

        // WHEN ACUP-DETAILS-NOT-FETCHED / WHEN CCARD-AID-PFK12. :2568-2580
        if (_acup.ChangeAction == AcupAction.NotFetched || aid == CcardAid.Pfk12)
        {
            if (_acctFilter == AcctFilter.IsValid)
            {
                _wsReturnMsg = "\0";              // SET WS-RETURN-MSG-OFF. :2574
                ReadAcct9000();                  // PERFORM 9000-READ-ACCT. :2575-2576
                if (_foundCustInMaster)          // :2577
                    _acup.ChangeAction = AcupAction.ShowDetails; // SET ACUP-SHOW-DETAILS. :2578
            }
        }
        // WHEN ACUP-SHOW-DETAILS. :2585-2591
        else if (_acup.ChangeAction == AcupAction.ShowDetails)
        {
            if (_inputError || _noChangesDetected) // IF INPUT-ERROR OR NO-CHANGES-DETECTED. :2586-2587
            {
                // CONTINUE
            }
            else
                _acup.ChangeAction = AcupAction.ChangesOkNotConfirmed; // :2590
        }
        // WHEN ACUP-CHANGES-NOT-OK → CONTINUE (re-show with errors). :2596-2597
        else if (_acup.ChangeAction == AcupAction.ChangesNotOk)
        {
            // CONTINUE
        }
        // WHEN ACUP-CHANGES-OK-NOT-CONFIRMED AND CCARD-AID-PFK05. :2602-2615
        else if (_acup.ChangeAction == AcupAction.ChangesOkNotConfirmed && aid == CcardAid.Pfk05)
        {
            WriteProcessing9600();               // PERFORM 9600-WRITE-PROCESSING. :2604-2605
            if (_couldNotLockAcct)
                _acup.ChangeAction = AcupAction.OkayedLockError;   // :2608
            else if (_lockedButUpdateFailed)
                _acup.ChangeAction = AcupAction.OkayedButFailed;   // :2610
            else if (_dataWasChanged)
                _acup.ChangeAction = AcupAction.ShowDetails;       // :2612
            else
                _acup.ChangeAction = AcupAction.OkayedAndDone;     // :2614
        }
        // WHEN ACUP-CHANGES-OK-NOT-CONFIRMED (no F5) → CONTINUE. :2620-2621
        else if (_acup.ChangeAction == AcupAction.ChangesOkNotConfirmed)
        {
            // CONTINUE
        }
        // WHEN ACUP-CHANGES-OKAYED-AND-DONE. :2625-2632
        else if (_acup.ChangeAction == AcupAction.OkayedAndDone)
        {
            _acup.ChangeAction = AcupAction.ShowDetails; // SET ACUP-SHOW-DETAILS. :2626
            if (IsLowOrSpaces(_ca.FromTranId))            // :2627-2628
            {
                _ca.AcctId = 0; _ca.CardNum = 0;          // :2629-2630
                _ca.AcctStatus = "\0";                    // MOVE LOW-VALUES TO CDEMO-ACCT-STATUS. :2631
            }
        }
        // WHEN OTHER → ABEND '0001' "UNEXPECTED DATA SCENARIO". :2633-2640
        else
        {
            throw new AbendException("0001"); // ABEND-ROUTINE → ABEND ABCODE. :2633-2640, 4222-4224
        }
    }

    // Tracks NO-CHANGES-DETECTED separately from CHANGE-HAS-OCCURRED (1205 may set either). :169-170,491-492
    private bool _noChangesDetected;

    // ============================================================================================
    //  3000-SEND-MAP and sub-paragraphs. source: COACTUPC.cbl:2649-3605
    // ============================================================================================
    private void SendMap3000(CicsContext ctx)
    {
        ScreenInit3100(ctx);          // :2650-2651
        SetupScreenVars3200();        // :2652-2653
        SetupInfoMsg3250();           // :2654-2655
        SetupScreenAttrs3300();       // :2656-2657
        SetupInfoMsgAttrs3390();      // :2658-2659
        SendScreen3400(ctx);          // :2660-2661
    }

    // 3100-SCREEN-INIT. source: :2668-2692
    private void ScreenInit3100(CicsContext ctx)
    {
        // MOVE LOW-VALUES TO CACTUPAO (blank every output field, clear per-turn overrides). :2669
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
        _now = ctx.Clock.Now;

        SetOut("TITLE01", CCDA_TITLE01);                 // :2673
        SetOut("TITLE02", CCDA_TITLE02);                 // :2674
        SetOut("TRNNAME", LIT_THISTRANID);               // :2675
        SetOut("PGMNAME", LIT_THISPGM);                  // :2676
        SetOut("CURDATE", _now.ToString("MM/dd/yy", CultureInfo.InvariantCulture)); // :2680-2684
        SetOut("CURTIME", _now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)); // :2686-2690
    }

    private DateTime _now;

    // 3200-SETUP-SCREEN-VARS. source: :2698-2726
    private void SetupScreenVars3200()
    {
        if (_ca.IsFirstEntry) return; // CDEMO-PGM-ENTER → leave blank. :2700-2701

        long ccAcctIdN = ParseLong(_acup.New.AcctIdX);
        if (ccAcctIdN == 0 && _acctFilter == AcctFilter.IsValid)
            SetOut("ACCTSID", "");                              // MOVE LOW-VALUES. :2705
        else
            SetOut("ACCTSID", FmtAcctId(_acup.New.AcctIdX));    // MOVE CC-ACCT-ID. :2707

        if (_acup.ChangeAction == AcupAction.NotFetched || ccAcctIdN == 0)
            ShowInitialValues3201();                            // :2711-2714
        else if (_acup.ChangeAction == AcupAction.ShowDetails)
            ShowOriginalValues3202();                           // :2715-2717
        else if (IsChangesMade(_acup.ChangeAction))
            ShowUpdatedValues3203();                            // :2718-2720
        else
            ShowOriginalValues3202();                           // WHEN OTHER. :2721-2723
    }

    // 3201-SHOW-INITIAL-VALUES. source: :2731-2781
    private void ShowInitialValues3201()
    {
        foreach (string f in new[]
        {
            "ACSTTUS","ACRDLIM","ACURBAL","ACSHLIM","ACRCYCR","ACRCYDB",
            "OPNYEAR","OPNMON","OPNDAY","EXPYEAR","EXPMON","EXPDAY","RISYEAR","RISMON","RISDAY",
            "AADDGRP","ACSTNUM","ACTSSN1","ACTSSN2","ACTSSN3","ACSTFCO","DOBYEAR","DOBMON","DOBDAY",
            "ACSFNAM","ACSMNAM","ACSLNAM","ACSADL1","ACSADL2","ACSCITY","ACSSTTE","ACSZIPC","ACSCTRY",
            "ACSPH1A","ACSPH1B","ACSPH1C","ACSPH2A","ACSPH2B","ACSPH2C","ACSGOVT","ACSEFTC","ACSPFLG",
        })
            SetOut(f, ""); // MOVE LOW-VALUES TO each output field. :2732-2780
    }

    // 3202-SHOW-ORIGINAL-VALUES. source: :2787-2864
    private void ShowOriginalValues3202()
    {
        ClearNonKeyFlags();                  // MOVE LOW-VALUES TO WS-NON-KEY-FLAGS. :2789
        SetInfo(Info.PromptForChanges);      // SET PROMPT-FOR-CHANGES. :2791

        AcupDetails o = _acup.Old;
        if (_foundAcctInMaster || _foundCustInMaster) // :2793-2794
        {
            SetOut("ACSTTUS", o.ActiveStatus);                       // :2795
            // MOVE ACUP-OLD-…-N (numeric overlay) TO the edited screen field; the OLD side is loaded from
            // the numeric master so IsNum is set — fall back to the raw X image for a non-numeric overlay.
            SetOut("ACURBAL", o.CurrBal.IsNum ? FmtCurrency(o.CurrBal.N) : o.CurrBalX);             // :2797-2798
            SetOut("ACRDLIM", o.CreditLimit.IsNum ? FmtCurrency(o.CreditLimit.N) : o.CreditLimitX); // :2800-2801
            SetOut("ACSHLIM", o.CashCreditLimit.IsNum ? FmtCurrency(o.CashCreditLimit.N) : o.CashCreditLimitX); // :2803-2805
            SetOut("ACRCYCR", o.CurrCycCredit.IsNum ? FmtCurrency(o.CurrCycCredit.N) : o.CurrCycCreditX); // :2807-2808
            SetOut("ACRCYDB", o.CurrCycDebit.IsNum ? FmtCurrency(o.CurrCycDebit.N) : o.CurrCycDebitX);     // :2810-2811
            SetOut("OPNYEAR", o.OpenYear); SetOut("OPNMON", o.OpenMon); SetOut("OPNDAY", o.OpenDay);    // :2813-2815
            SetOut("EXPYEAR", o.ExpYear); SetOut("EXPMON", o.ExpMon); SetOut("EXPDAY", o.ExpDay);       // :2817-2819
            SetOut("RISYEAR", o.RisYear); SetOut("RISMON", o.RisMon); SetOut("RISDAY", o.RisDay);       // :2821-2823
            SetOut("AADDGRP", o.GroupId);                            // :2824
        }
        if (_foundCustInMaster) // :2827
        {
            SetOut("ACSTNUM", o.CustIdX);                            // :2828
            string ssn = o.SsnX();
            SetOut("ACTSSN1", Slice(ssn, 1, 3));                     // :2829
            SetOut("ACTSSN2", Slice(ssn, 4, 2));                     // :2830
            SetOut("ACTSSN3", Slice(ssn, 6, 4));                     // :2831
            SetOut("ACSTFCO", o.FicoX);                              // :2832
            SetOut("DOBYEAR", o.DobYear); SetOut("DOBMON", o.DobMon); SetOut("DOBDAY", o.DobDay); // :2833-2835
            SetOut("ACSFNAM", o.FirstName); SetOut("ACSMNAM", o.MiddleName); SetOut("ACSLNAM", o.LastName); // :2836-2838
            SetOut("ACSADL1", o.AddrLine1); SetOut("ACSADL2", o.AddrLine2); SetOut("ACSCITY", o.AddrLine3); // :2839-2841
            SetOut("ACSSTTE", o.StateCd); SetOut("ACSZIPC", o.Zip); SetOut("ACSCTRY", o.CountryCd);         // :2842-2845
            string p1 = o.PhoneNum1();
            SetOut("ACSPH1A", Slice(p1, 2, 3)); SetOut("ACSPH1B", Slice(p1, 6, 3)); SetOut("ACSPH1C", Slice(p1, 10, 4)); // :2846-2851
            string p2 = o.PhoneNum2();
            SetOut("ACSPH2A", Slice(p2, 2, 3)); SetOut("ACSPH2B", Slice(p2, 6, 3)); SetOut("ACSPH2C", Slice(p2, 10, 4)); // :2852-2857
            SetOut("ACSGOVT", o.GovtId); SetOut("ACSEFTC", o.EftAccountId); SetOut("ACSPFLG", o.PriHolderInd); // :2858-2863
        }
    }

    // 3203-SHOW-UPDATED-VALUES. source: :2870-2949
    private void ShowUpdatedValues3203()
    {
        AcupDetails n = _acup.New;
        SetOut("ACSTTUS", n.ActiveStatus);                                                       // :2872

        SetOut("ACRDLIM", n.CreditLimit.IsNum ? FmtCurrency(n.CreditLimit.N) : n.CreditLimitX);  // :2874-2879
        SetOut("ACSHLIM", n.CashCreditLimit.IsNum ? FmtCurrency(n.CashCreditLimit.N) : n.CashCreditLimitX); // :2881-2888
        SetOut("ACURBAL", n.CurrBal.IsNum ? FmtCurrency(n.CurrBal.N) : n.CurrBalX);              // :2890-2895
        SetOut("ACRCYCR", n.CurrCycCredit.IsNum ? FmtCurrency(n.CurrCycCredit.N) : n.CurrCycCreditX); // :2897-2902
        SetOut("ACRCYDB", n.CurrCycDebit.IsNum ? FmtCurrency(n.CurrCycDebit.N) : n.CurrCycDebitX);     // :2904-2909

        SetOut("OPNYEAR", n.OpenYear); SetOut("OPNMON", n.OpenMon); SetOut("OPNDAY", n.OpenDay); // :2911-2913
        SetOut("EXPYEAR", n.ExpYear); SetOut("EXPMON", n.ExpMon); SetOut("EXPDAY", n.ExpDay);    // :2915-2917
        SetOut("RISYEAR", n.RisYear); SetOut("RISMON", n.RisMon); SetOut("RISDAY", n.RisDay);    // :2918-2920
        SetOut("AADDGRP", n.GroupId);                                                            // :2921
        SetOut("ACSTNUM", n.CustIdX);                                                            // :2922
        SetOut("ACTSSN1", n.Ssn1); SetOut("ACTSSN2", n.Ssn2); SetOut("ACTSSN3", n.Ssn3);        // :2923-2925
        SetOut("ACSTFCO", n.FicoX);                                                              // :2926
        SetOut("DOBYEAR", n.DobYear); SetOut("DOBMON", n.DobMon); SetOut("DOBDAY", n.DobDay);    // :2927-2929
        SetOut("ACSFNAM", n.FirstName); SetOut("ACSMNAM", n.MiddleName); SetOut("ACSLNAM", n.LastName); // :2930-2932
        SetOut("ACSADL1", n.AddrLine1); SetOut("ACSADL2", n.AddrLine2); SetOut("ACSCITY", n.AddrLine3); // :2933-2935
        SetOut("ACSSTTE", n.StateCd); SetOut("ACSZIPC", n.Zip); SetOut("ACSCTRY", n.CountryCd);  // :2936-2938
        SetOut("ACSPH1A", n.Ph1a); SetOut("ACSPH1B", n.Ph1b); SetOut("ACSPH1C", n.Ph1c);        // :2939-2941
        SetOut("ACSPH2A", n.Ph2a); SetOut("ACSPH2B", n.Ph2b); SetOut("ACSPH2C", n.Ph2c);        // :2942-2944
        SetOut("ACSGOVT", n.GovtId); SetOut("ACSEFTC", n.EftAccountId); SetOut("ACSPFLG", n.PriHolderInd); // :2945-2947
    }

    // 3250-SETUP-INFOMSG. source: :2955-2982
    private void SetupInfoMsg3250()
    {
        if (_ca.IsFirstEntry) SetInfo(Info.PromptForSearchKeys);                       // :2958-2959
        else switch (_acup.ChangeAction)
        {
            case AcupAction.NotFetched: SetInfo(Info.PromptForSearchKeys); break;      // :2960-2961
            case AcupAction.ShowDetails: SetInfo(Info.PromptForChanges); break;        // :2962-2963
            case AcupAction.ChangesNotOk: SetInfo(Info.PromptForChanges); break;       // :2964-2965
            case AcupAction.ChangesOkNotConfirmed: SetInfo(Info.PromptForConfirmation); break; // :2966-2967
            case AcupAction.OkayedAndDone: SetInfo(Info.ConfirmUpdateSuccess); break;  // :2968-2969
            case AcupAction.OkayedLockError: SetInfo(Info.InformFailure); break;       // :2971-2972
            case AcupAction.OkayedButFailed: SetInfo(Info.InformFailure); break;       // :2973-2974
            default:
                if (string.IsNullOrEmpty(_wsInfoMsg)) SetInfo(Info.PromptForSearchKeys); // WS-NO-INFO-MESSAGE. :2975-2976
                break;
        }
        SetOut("INFOMSG", _wsInfoMsg);                                                 // :2979
        SetOut("ERRMSG", ReturnMsgText());                                             // :2981
    }

    // 3300-SETUP-SCREEN-ATTRS. source: :2986-3436
    private void SetupScreenAttrs3300()
    {
        ProtectAllAttrs3310(); // PERFORM 3310-PROTECT-ALL-ATTRS. :2989-2990

        // Unprotect based on context. :2993-3006
        switch (_acup.ChangeAction)
        {
            case AcupAction.NotFetched:
                SetAttr("ACCTSID", Unprot());      // make Account Id editable (DFHBMFSE). :2996
                break;
            case AcupAction.ShowDetails:
            case AcupAction.ChangesNotOk:
                UnprotectFewAttrs3320();           // :2999-3000
                break;
            case AcupAction.ChangesOkNotConfirmed:
            case AcupAction.OkayedAndDone:
                // CONTINUE (leave protected). :3001-3003
                break;
            default:
                SetAttr("ACCTSID", Unprot());      // WHEN OTHER. :3005
                break;
        }

        // Position cursor at the first errored field in screen order. :3009-3167
        PositionCursor3300();

        // SETUP COLOR. :3170-3192
        if (_ca.LastMapSet.TrimEnd() == LIT_CCLISTMAPSET)                   // :3171
            SetColor("ACCTSID", BmsColor.Default);                         // DFHDFCOL. :3172
        if (_acctFilter == AcctFilter.NotOk)                               // :3176
            SetColor("ACCTSID", BmsColor.Red);                            // DFHRED. :3177
        if (_acctFilter == AcctFilter.Blank && _ca.IsReenter)             // :3180-3181
        {
            SetOut("ACCTSID", "*");                                        // :3182
            SetColor("ACCTSID", BmsColor.Red);                           // :3183
        }
        if (_acup.ChangeAction == AcupAction.NotFetched
            || _acctFilter == AcctFilter.Blank
            || _acctFilter == AcctFilter.NotOk)
            return; // GO TO 3300-SETUP-SCREEN-ATTRS-EXIT. :3186-3192

        // COPY CSSETATY per field: red + '*' when NOT-OK/BLANK and re-enter. :3208-3435
        Csetaty(_fAcctStatus, "ACSTTUS"); // :3208-3211
        Csetaty(_fOpenYear, "OPNYEAR"); Csetaty(_fOpenMon, "OPNMON"); Csetaty(_fOpenDay, "OPNDAY"); // :3214-3229
        Csetaty(_fCredLimit, "ACRDLIM"); // :3231-3235
        Csetaty(_fExpYear, "EXPYEAR"); Csetaty(_fExpMon, "EXPMON"); Csetaty(_fExpDay, "EXPDAY"); // :3237-3253
        Csetaty(_fCashLimit, "ACSHLIM"); // :3255-3259
        Csetaty(_fRisYear, "RISYEAR"); Csetaty(_fRisMon, "RISMON"); Csetaty(_fRisDay, "RISDAY"); // :3261-3277
        Csetaty(_fCurrBal, "ACURBAL"); // :3279-3283
        Csetaty(_fCurrCycCredit, "ACRCYCR"); Csetaty(_fCurrCycDebit, "ACRCYDB"); // :3285-3295
        Csetaty(_fSsn1, "ACTSSN1"); Csetaty(_fSsn2, "ACTSSN2"); Csetaty(_fSsn3, "ACTSSN3"); // :3297-3313
        Csetaty(_fDobYear, "DOBYEAR"); Csetaty(_fDobMon, "DOBMON"); Csetaty(_fDobDay, "DOBDAY"); // :3315-3331
        Csetaty(_fFico, "ACSTFCO"); // :3333-3337
        Csetaty(_fFirstName, "ACSFNAM"); Csetaty(_fMiddleName, "ACSMNAM"); Csetaty(_fLastName, "ACSLNAM"); // :3339-3355
        Csetaty(_fAddr1, "ACSADL1"); // :3357-3361
        Csetaty(_fState, "ACSSTTE"); // :3363-3367
        // Address Line 2 / Zip / City / Country. :3369-3391
        Csetaty(Flg.Valid, "ACSADL2"); // ADDRESS-LINE-2 has no edit flag; always valid.
        Csetaty(_fZip, "ACSZIPC");
        Csetaty(_fCity, "ACSCITY");
        Csetaty(_fCountry, "ACSCTRY");
        Csetaty(_fPh1a, "ACSPH1A"); Csetaty(_fPh1b, "ACSPH1B"); Csetaty(_fPh1c, "ACSPH1C"); // :3393-3408
        Csetaty(_fPh2a, "ACSPH2A"); Csetaty(_fPh2b, "ACSPH2B"); Csetaty(_fPh2c, "ACSPH2C"); // :3410-3425
        // FB-5: cross-labeled EFT vs Primary-Holder. EFT comment sets PRI-CARDHOLDER/ACSPFLG, and vice-versa.
        Csetaty(_fPriCardholder, "ACSPFLG"); // :3426-3430 (labeled "EFT Account Id")
        Csetaty(_fEft, "ACSEFTC");           // :3432-3435 (labeled "Primary Card Holder")
    }

    // The big cursor-positioning EVALUATE. source: :3009-3167
    private void PositionCursor3300()
    {
        // FOUND-ACCOUNT-DATA / NO-CHANGES-DETECTED → cursor on ACSTTUS. :3010-3012
        if (_foundAccountData || _noChangesDetected) { PutCursor("ACSTTUS"); return; }
        if (_acctFilter == AcctFilter.NotOk || _acctFilter == AcctFilter.Blank) { PutCursor("ACCTSID"); return; } // :3013-3015
        if (IsBad(_fAcctStatus)) { PutCursor("ACSTTUS"); return; }     // :3017-3019
        if (IsBad(_fOpenYear)) { PutCursor("OPNYEAR"); return; }       // :3021-3023
        if (IsBad(_fOpenMon)) { PutCursor("OPNMON"); return; }         // :3025-3027
        if (IsBad(_fOpenDay)) { PutCursor("OPNDAY"); return; }         // :3029-3031
        if (IsBad(_fCredLimit)) { PutCursor("ACRDLIM"); return; }      // :3033-3035
        if (IsBad(_fExpYear)) { PutCursor("EXPYEAR"); return; }        // :3037-3039
        if (IsBad(_fExpMon)) { PutCursor("EXPMON"); return; }          // :3041-3043
        if (IsBad(_fExpDay)) { PutCursor("EXPDAY"); return; }          // :3045-3047
        if (IsBad(_fCashLimit)) { PutCursor("ACSHLIM"); return; }      // :3049-3051
        if (IsBad(_fRisYear)) { PutCursor("RISYEAR"); return; }        // :3053-3055
        if (IsBad(_fRisMon)) { PutCursor("RISMON"); return; }          // :3057-3059
        if (IsBad(_fRisDay)) { PutCursor("RISDAY"); return; }          // :3061-3063
        if (IsBad(_fCurrBal)) { PutCursor("ACURBAL"); return; }        // :3066-3068
        if (IsBad(_fCurrCycCredit)) { PutCursor("ACRCYCR"); return; }  // :3070-3072
        if (IsBad(_fCurrCycDebit)) { PutCursor("ACRCYDB"); return; }   // :3074-3076
        if (IsBad(_fSsn1)) { PutCursor("ACTSSN1"); return; }           // :3078-3080
        if (IsBad(_fSsn2)) { PutCursor("ACTSSN2"); return; }           // :3082-3084
        if (IsBad(_fSsn3)) { PutCursor("ACTSSN3"); return; }           // :3086-3088
        if (IsBad(_fDobYear)) { PutCursor("DOBYEAR"); return; }        // :3090-3092
        if (IsBad(_fDobMon)) { PutCursor("DOBMON"); return; }          // :3094-3096
        if (IsBad(_fDobDay)) { PutCursor("DOBDAY"); return; }          // :3098-3100
        if (IsBad(_fFico)) { PutCursor("ACSTFCO"); return; }           // :3102-3104
        if (IsBad(_fFirstName)) { PutCursor("ACSFNAM"); return; }      // :3106-3108
        if (_fMiddleName == Flg.NotOk) { PutCursor("ACSMNAM"); return; } // middle: NOT-OK only. :3110-3111
        if (IsBad(_fLastName)) { PutCursor("ACSLNAM"); return; }       // :3113-3115
        if (IsBad(_fAddr1)) { PutCursor("ACSADL1"); return; }          // :3117-3119
        if (IsBad(_fState)) { PutCursor("ACSSTTE"); return; }          // :3121-3123
        if (IsBad(_fZip)) { PutCursor("ACSZIPC"); return; }            // :3126-3128
        if (IsBad(_fCity)) { PutCursor("ACSCITY"); return; }           // :3130-3132
        if (IsBad(_fCountry)) { PutCursor("ACSCTRY"); return; }        // :3134-3136
        if (IsBad(_fPh1a)) { PutCursor("ACSPH1A"); return; }           // :3138-3140
        if (IsBad(_fPh1b)) { PutCursor("ACSPH1B"); return; }           // :3141-3143
        if (IsBad(_fPh1c)) { PutCursor("ACSPH1C"); return; }           // :3144-3146
        if (IsBad(_fPh2a)) { PutCursor("ACSPH2A"); return; }           // :3148-3150
        if (IsBad(_fPh2b)) { PutCursor("ACSPH2B"); return; }           // :3151-3153
        if (IsBad(_fPh2c)) { PutCursor("ACSPH2C"); return; }           // :3154-3156
        if (IsBad(_fEft)) { PutCursor("ACSEFTC"); return; }            // :3158-3160
        if (IsBad(_fPriCardholder)) { PutCursor("ACSPFLG"); return; }  // :3162-3164
        PutCursor("ACCTSID");                                           // WHEN OTHER. :3165-3166
    }

    // FOUND-ACCOUNT-DATA is the 88 of WS-INFO-MSG; set by 3202. Track it so 3300's first WHEN works. :466-467
    private bool _foundAccountData;

    // 3310-PROTECT-ALL-ATTRS. source: :3441-3495
    private void ProtectAllAttrs3310()
    {
        foreach (string f in AllInputFields.Concat(new[] { "INFOMSG" }))
            SetAttr(f, Prot()); // MOVE DFHBMPRF (protect). :3442-3494
    }

    // 3320-UNPROTECT-FEW-ATTRS. source: :3500-3560
    private void UnprotectFewAttrs3320()
    {
        foreach (string f in new[]
        {
            "ACSTTUS","ACRDLIM","ACSHLIM","ACURBAL","ACRCYCR","ACRCYDB",
            "OPNYEAR","OPNMON","OPNDAY","EXPYEAR","EXPMON","EXPDAY","RISYEAR","RISMON","RISDAY",
            "DOBYEAR","DOBMON","DOBDAY","AADDGRP",
        })
            SetAttr(f, Unprot()); // MOVE DFHBMFSE. :3502-3529
        SetAttr("ACSTNUM", Prot()); // MOVE DFHBMPRF TO ACSTNUMA. :3531
        foreach (string f in new[]
        {
            "ACTSSN1","ACTSSN2","ACTSSN3","ACSTFCO","ACSFNAM","ACSMNAM","ACSLNAM",
            "ACSADL1","ACSADL2","ACSCITY","ACSSTTE","ACSZIPC",
        })
            SetAttr(f, Unprot()); // MOVE DFHBMFSE. :3532-3545
        SetAttr("ACSCTRY", Prot()); // country protected (USA-specific edits). :3547
        foreach (string f in new[] { "ACSPH1A","ACSPH1B","ACSPH1C","ACSPH2A","ACSPH2B","ACSPH2C","ACSGOVT","ACSEFTC","ACSPFLG" })
            SetAttr(f, Unprot()); // :3549-3559
        SetAttr("INFOMSG", Prot()); // :3560
    }

    // 3390-SETUP-INFOMSG-ATTRS. source: :3566-3583
    private void SetupInfoMsgAttrs3390()
    {
        // IF WS-NO-INFO-MESSAGE → DFHBMDAR (dark) ELSE DFHBMASB (bright). :3567-3571
        SetAttr("INFOMSG", string.IsNullOrEmpty(Trim(_wsInfoMsg))
            ? BmsAttribute.AutoSkip | BmsAttribute.Dark      // DFHBMDAR
            : BmsAttribute.AutoSkip | BmsAttribute.Bright);  // DFHBMASB

        // F12 shown when changes made and not done. :3573-3576
        if (IsChangesMade(_acup.ChangeAction) && _acup.ChangeAction != AcupAction.OkayedAndDone)
            F("FKEY12").AttributeOverride = BmsAttribute.AutoSkip | BmsAttribute.Bright; // DFHBMASB
        // F05 + F12 shown when prompting for confirmation. :3578-3581
        if (_acup.ChangeAction == AcupAction.ChangesOkNotConfirmed) // PROMPT-FOR-CONFIRMATION
        {
            F("FKEY05").AttributeOverride = BmsAttribute.AutoSkip | BmsAttribute.Bright;
            F("FKEY12").AttributeOverride = BmsAttribute.AutoSkip | BmsAttribute.Bright;
        }
    }

    // 3400-SEND-SCREEN. source: :3589-3602
    private void SendScreen3400(CicsContext ctx)
    {
        // EXEC CICS SEND MAP('CACTUPA') MAPSET('COACTUP') FROM(CACTUPAO) CURSOR ERASE FREEKB. :3594-3601
        ctx.SendMap(LIT_THISMAP, LIT_THISMAPSET.TrimEnd(), _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
    }

    // ============================================================================================
    //  9000-series file access. source: COACTUPC.cbl:3608-4195
    // ============================================================================================

    // Lock/update outcome flags (drive the 2000 EVALUATE). source: :517-524
    private bool _couldNotLockAcct;
    private bool _lockedButUpdateFailed;
    private bool _dataWasChanged;

    // 9000-READ-ACCT. source: :3608-3644
    private void ReadAcct9000()
    {
        _acup.Old = new AcupDetails();      // INITIALIZE ACUP-OLD-DETAILS. :3610
        SetInfo(Info.None);                  // SET WS-NO-INFO-MESSAGE. :3612
        long ccAcctId = ParseLong(_acup.New.AcctIdX);
        _acup.Old.AcctIdX = _acup.New.AcctIdX; // MOVE CC-ACCT-ID TO ACUP-OLD-ACCT-ID. :3614
        _cardRidAcctId = ccAcctId;             // … WS-CARD-RID-ACCT-ID. :3615

        GetCardXrefByAcct9200();               // :3617-3618
        if (_acctFilter == AcctFilter.NotOk) return; // :3620-3622

        GetAcctDataByAcct9300();               // :3624-3625
        if (!_foundAcctInMaster) return;       // DID-NOT-FIND-ACCT-IN-ACCTDAT. :3627-3629

        _cardRidCustId = _ca.CustId;           // MOVE CDEMO-CUST-ID TO WS-CARD-RID-CUST-ID. :3631
        GetCustDataByCust9400();               // :3633-3634
        if (!_foundCustInMaster) return;       // DID-NOT-FIND-CUST-IN-CUSTDAT. :3636-3638

        StoreFetchedData9500();                // :3642-3643
    }

    // 9200-GETCARDXREF-BYACCT. source: :3650-3697
    private void GetCardXrefByAcct9200()
    {
        var repo = new CardXrefRepository(_db.Connection);
        string status = repo.ReadByAltKey(_cardRidAcctId, out CardXref? xref); // READ CXACAIX by acct alt key. :3654-3662
        if (status == FileStatus.Ok && xref is not null) // DFHRESP(NORMAL). :3665
        {
            _ca.CustId = xref.CustId;          // MOVE XREF-CUST-ID TO CDEMO-CUST-ID. :3666
            _ca.CardNum = ParseLong(xref.XrefCardNum); // MOVE XREF-CARD-NUM TO CDEMO-CARD-NUM. :3667
        }
        else if (status == FileStatus.RecordNotFound) // DFHRESP(NOTFND). :3668
        {
            _inputError = true; _acctFilter = AcctFilter.NotOk; // :3669-3670
            if (ReturnMsgOff)
                SetReturnMsg($"Account:{Fmt11(_cardRidAcctId)} not found in Cross ref file.  Resp:{RespText()} Reas:{ReasText()}"); // :3674-3684
        }
        else // WHEN OTHER. :3686
        {
            _inputError = true; _acctFilter = AcctFilter.NotOk;
            if (ReturnMsgOff) SetReturnMsg(FileErrorMessage("READ", "CXACAIX")); // :3689-3693
        }
    }

    // 9300-GETACCTDATA-BYACCT. source: :3701-3748
    private void GetAcctDataByAcct9300()
    {
        var repo = new AccountRepository(_db.Connection);
        string status = repo.ReadByKey(_cardRidAcctId, out Account? acct); // READ ACCTDAT. :3703-3711
        if (status == FileStatus.Ok && acct is not null) // NORMAL. :3714
        {
            _foundAcctInMaster = true; _fetchedAccount = acct; // SET FOUND-ACCT-IN-MASTER. :3715
        }
        else if (status == FileStatus.RecordNotFound) // NOTFND. :3716
        {
            _inputError = true; _acctFilter = AcctFilter.NotOk; // :3717-3718
            if (ReturnMsgOff)
                SetReturnMsg($"Account:{Fmt11(_cardRidAcctId)} not found in Acct Master file.Resp:{RespText()} Reas:{ReasText()}"); // :3723-3733
        }
        else // OTHER. :3736
        {
            _inputError = true; _acctFilter = AcctFilter.NotOk;
            if (ReturnMsgOff) SetReturnMsg(FileErrorMessage("READ", "ACCTDAT")); // :3739-3743
        }
    }

    // 9400-GETCUSTDATA-BYCUST. source: :3752-3797
    private void GetCustDataByCust9400()
    {
        var repo = new CustomerRepository(_db.Connection);
        string status = repo.ReadByKey(_cardRidCustId, out Customer? cust); // READ CUSTDAT. :3753-3761
        if (status == FileStatus.Ok && cust is not null) // NORMAL. :3764
        {
            _foundCustInMaster = true; _fetchedCustomer = cust; // SET FOUND-CUST-IN-MASTER. :3765
        }
        else if (status == FileStatus.RecordNotFound) // NOTFND. :3766
        {
            _inputError = true; _custFilter = CustFilter.NotOk; // :3767-3768
            if (ReturnMsgOff)
                SetReturnMsg($"CustId:{Fmt9(_cardRidCustId)} not found in customer master.Resp: {RespText()} REAS:{ReasText()}"); // :3773-3783
        }
        else // OTHER. :3785
        {
            _inputError = true; _custFilter = CustFilter.NotOk;
            if (ReturnMsgOff) SetReturnMsg(FileErrorMessage("READ", "CUSTDAT")); // :3788-3792
        }
    }

    private Account? _fetchedAccount;
    private Customer? _fetchedCustomer;

    // 9500-STORE-FETCHED-DATA. source: :3801-3884
    private void StoreFetchedData9500()
    {
        Account a = _fetchedAccount!;
        Customer c = _fetchedCustomer!;

        // Store nav context. :3805-3811
        _ca.AcctId = a.AcctId; _ca.CustId = c.CustId;
        _ca.CustFName = c.FirstName; _ca.CustMName = c.MiddleName; _ca.CustLName = c.LastName;
        _ca.AcctStatus = a.ActiveStatus;
        // XREF-CARD-NUM already moved in 9200; CDEMO-CARD-NUM stays as set there.

        _acup.Old = new AcupDetails(); // INITIALIZE ACUP-OLD-DETAILS. :3813

        AcupDetails o = _acup.Old;
        o.AcctIdX = Fmt11(a.AcctId);                 // :3817
        o.ActiveStatus = a.ActiveStatus;             // :3819
        o.SetMoney(o.CurrBal, Money(a.CurrBal));            // MOVE ACCT-CURR-BAL TO ACUP-OLD-CURR-BAL-N. :3821
        o.SetMoney(o.CreditLimit, Money(a.CreditLimit));    // :3823
        o.SetMoney(o.CashCreditLimit, Money(a.CashCreditLimit)); // :3825
        o.SetMoney(o.CurrCycCredit, Money(a.CurrCycCredit));     // :3827
        o.SetMoney(o.CurrCycDebit, Money(a.CurrCycDebit));       // :3829
        // Dates sliced from X(10) CCYY-MM-DD. :3832-3845
        o.OpenYear = Slice(a.OpenDate, 1, 4); o.OpenMon = Slice(a.OpenDate, 6, 2); o.OpenDay = Slice(a.OpenDate, 9, 2);
        o.ExpYear = Slice(a.ExpirationDate, 1, 4); o.ExpMon = Slice(a.ExpirationDate, 6, 2); o.ExpDay = Slice(a.ExpirationDate, 9, 2);
        o.RisYear = Slice(a.ReissueDate, 1, 4); o.RisMon = Slice(a.ReissueDate, 6, 2); o.RisDay = Slice(a.ReissueDate, 9, 2);
        o.GroupId = a.GroupId;                       // :3847

        o.CustIdX = Fmt9(c.CustId);                  // :3852
        o.SetSsn(c.Ssn);                             // :3854
        // DOB sliced; OLD DOB is the 8-char dashless field (year+mon+day parts). :3857-3859
        o.DobYear = Slice(c.DobYyyyMmDd, 1, 4); o.DobMon = Slice(c.DobYyyyMmDd, 6, 2); o.DobDay = Slice(c.DobYyyyMmDd, 9, 2);
        o.FicoX = Fmt3(c.FicoCreditScore);           // :3861
        o.FirstName = c.FirstName; o.MiddleName = c.MiddleName; o.LastName = c.LastName; // :3863-3867
        o.AddrLine1 = c.AddrLine1; o.AddrLine2 = c.AddrLine2; o.AddrLine3 = c.AddrLine3; // :3869-3871
        o.StateCd = c.AddrStateCd; o.CountryCd = c.AddrCountryCd; o.Zip = c.AddrZip;     // :3872-3875
        o.SetPhone1(c.PhoneNum1); o.SetPhone2(c.PhoneNum2);          // :3876-3877
        o.GovtId = c.GovtIssuedId;                   // :3879
        o.EftAccountId = c.EftAccountId;             // :3881
        o.PriHolderInd = c.PriCardHolderInd;         // :3883
    }

    // 9600-WRITE-PROCESSING. source: :3888-4104
    private void WriteProcessing9600()
    {
        _cardRidAcctId = ParseLong(_acup.New.AcctIdX); // MOVE CC-ACCT-ID TO WS-CARD-RID-ACCT-ID. :3892

        // (1) READ ACCTDAT UPDATE. :3894-3915
        var acctRepo = new AccountRepository(_db.Connection);
        string aStatus = acctRepo.ReadByKey(_cardRidAcctId, out Account? acct);
        if (aStatus != FileStatus.Ok || acct is null) // not NORMAL → could-not-lock. :3907-3915
        {
            _inputError = true;
            if (ReturnMsgOff) { _couldNotLockAcct = true; SetReturnMsg("Could not lock account record for update"); } // :3912
            return; // GO TO 9600-WRITE-PROCESSING-EXIT. :3914
        }
        _fetchedAccount = acct;

        // (2) READ CUSTDAT UPDATE. :3917-3942
        _cardRidCustId = _ca.CustId; // MOVE CDEMO-CUST-ID TO WS-CARD-RID-CUST-ID. :3919
        var custRepo = new CustomerRepository(_db.Connection);
        string cStatus = custRepo.ReadByKey(_cardRidCustId, out Customer? cust);
        if (cStatus != FileStatus.Ok || cust is null) // not NORMAL → could-not-lock cust. :3934-3942
        {
            _inputError = true;
            if (ReturnMsgOff) SetReturnMsg("Could not lock customer record for update"); // :3939 (COULD-NOT-LOCK-CUST)
            return; // :3941
        }
        _fetchedCustomer = cust;

        // (3) Optimistic-lock check. :3947-3952
        CheckChangeInRec9700(acct, cust);
        if (_dataWasChanged)
            return; // GO TO 9600-WRITE-PROCESSING-EXIT. :3950-3951 (and FB-1: 9700 already short-circuits here)

        // (4) Build ACCT-UPDATE-RECORD from NEW fields. :3956-4002
        AcupDetails n = _acup.New;
        Account upd = new()
        {
            AcctId = ParseLong(n.AcctIdX),                               // :3960
            ActiveStatus = Field(n.ActiveStatus, 1),                     // :3962
            CurrBal = n.CurrBal.IsNum ? n.CurrBal.N : 0m,                // :3964
            CreditLimit = n.CreditLimit.IsNum ? n.CreditLimit.N : 0m,    // :3966
            CashCreditLimit = n.CashCreditLimit.IsNum ? n.CashCreditLimit.N : 0m, // :3968-3969
            CurrCycCredit = n.CurrCycCredit.IsNum ? n.CurrCycCredit.N : 0m,       // :3971-3972
            CurrCycDebit = n.CurrCycDebit.IsNum ? n.CurrCycDebit.N : 0m,          // :3974
            OpenDate = ClampOrPad($"{n.OpenYear}-{n.OpenMon}-{n.OpenDay}", 10),  // STRING. :3976-3982
            ExpirationDate = ClampOrPad($"{n.ExpYear}-{n.ExpMon}-{n.ExpDay}", 10), // :3984-3990
            // FB-3: MOVE ACCT-REISSUE-DATE (dead) then STRING overwrites. :3993-4000
            ReissueDate = ClampOrPad($"{n.RisYear}-{n.RisMon}-{n.RisDay}", 10),
            GroupId = Field(n.GroupId, 10),                              // :4002
            AddrZip = acct.AddrZip,                                      // ACCT-UPDATE has no zip; preserve existing
        };

        // (5) Build CUST-UPDATE-RECORD. :4007-4059
        Customer cupd = new()
        {
            CustId = ParseLong(n.CustIdX),                              // :4009
            FirstName = Field(n.FirstName, 25),                         // :4010-4011
            MiddleName = Field(n.MiddleName, 25),                       // :4012-4013
            LastName = Field(n.LastName, 25),                           // :4014
            AddrLine1 = Field(n.AddrLine1, 50),                         // :4015-4016
            AddrLine2 = Field(n.AddrLine2, 50),                         // :4017-4018
            AddrLine3 = Field(n.AddrLine3, 50),                         // :4019-4020
            AddrStateCd = Field(n.StateCd, 2),                          // :4021-4022
            AddrCountryCd = Field(n.CountryCd, 3),                      // :4023-4024
            AddrZip = Field(n.Zip, 10),                                 // :4025
            PhoneNum1 = ClampOrPad($"({n.Ph1a}){n.Ph1b}-{n.Ph1c}", 15), // STRING. :4027-4033
            PhoneNum2 = ClampOrPad($"({n.Ph2a}){n.Ph2b}-{n.Ph2c}", 15), // :4035-4041
            Ssn = ParseLong(n.SsnX()),                                  // :4044
            GovtIssuedId = Field(n.GovtId, 20),                        // :4045-4046
            DobYyyyMmDd = ClampOrPad($"{n.DobYear}-{n.DobMon}-{n.DobDay}", 10), // STRING. :4047-4052
            EftAccountId = Field(n.EftAccountId, 10),                   // :4054-4055
            PriCardHolderInd = Field(n.PriHolderInd, 1),               // :4056-4057
            FicoCreditScore = (int)ParseLong(Field(n.FicoX, 3)),       // :4058-4059
        };

        // (6) REWRITE ACCTDAT. :4065-4081
        string aw = acctRepo.Update(upd);
        if (aw != FileStatus.Ok)
        {
            _lockedButUpdateFailed = true; // SET LOCKED-BUT-UPDATE-FAILED. :4079
            return; // :4080
        }

        // (7) REWRITE CUSTDAT. :4085-4103
        string cw = custRepo.Update(cupd);
        if (cw != FileStatus.Ok)
        {
            _lockedButUpdateFailed = true;       // :4098
            // EXEC CICS SYNCPOINT ROLLBACK — undo the account REWRITE. :4099-4101
            acctRepo.Update(acct); // restore the before-image of the account row
            return; // :4102
        }
        // Both REWRITEs succeeded.
    }

    // 9700-CHECK-CHANGE-IN-REC. source: :4109-4191
    private void CheckChangeInRec9700(Account acct, Customer cust)
    {
        AcupDetails o = _acup.Old;

        // Account fields. :4115-4140 (money compared against the S9(10)V99 redefine ACUP-OLD-…-N)
        bool acctSame =
            acct.ActiveStatus == o.ActiveStatus
            && acct.CurrBal == o.CurrBal.N
            && acct.CreditLimit == o.CreditLimit.N
            && acct.CashCreditLimit == o.CashCreditLimit.N
            && acct.CurrCycCredit == o.CurrCycCredit.N
            && acct.CurrCycDebit == o.CurrCycDebit.N
            && Slice(acct.OpenDate, 1, 4) == o.OpenYear
            && Slice(acct.OpenDate, 6, 2) == o.OpenMon
            && Slice(acct.OpenDate, 9, 2) == o.OpenDay
            && Slice(acct.ExpirationDate, 1, 4) == o.ExpYear
            && Slice(acct.ExpirationDate, 6, 2) == o.ExpMon
            && Slice(acct.ExpirationDate, 9, 2) == o.ExpDay
            && Slice(acct.ReissueDate, 1, 4) == o.RisYear
            && Slice(acct.ReissueDate, 6, 2) == o.RisMon
            && Slice(acct.ReissueDate, 9, 2) == o.RisDay
            && Low(Field(acct.GroupId, 10)) == Low(Field(o.GroupId, 10)); // FUNCTION LOWER-CASE. :4139-4140
        if (!acctSame)
        {
            _dataWasChanged = true; // SET DATA-WAS-CHANGED-BEFORE-UPDATE. :4143
            return; // FB-1: GO TO 9600-WRITE-PROCESSING-EXIT (caller's exit). :4144
        }

        // Customer fields. :4152-4186
        // FB-2: DOB OLD is 8-char dashless field read at (5:2)/(7:2) vs live 10-char at (6:2)/(9:2).
        string oldDob8 = Field(o.DobYear + o.DobMon + o.DobDay, 8); // ACUP-OLD-CUST-DOB-YYYY-MM-DD X(08)
        bool custSame =
            Up(Field(cust.FirstName, 25)) == Up(Field(o.FirstName, 25))
            && Up(Field(cust.MiddleName, 25)) == Up(Field(o.MiddleName, 25))
            && Up(Field(cust.LastName, 25)) == Up(Field(o.LastName, 25))
            && Up(Field(cust.AddrLine1, 50)) == Up(Field(o.AddrLine1, 50))
            && Up(Field(cust.AddrLine2, 50)) == Up(Field(o.AddrLine2, 50))
            && Up(Field(cust.AddrLine3, 50)) == Up(Field(o.AddrLine3, 50))
            && Up(Field(cust.AddrStateCd, 2)) == Up(Field(o.StateCd, 2))
            && Up(Field(cust.AddrCountryCd, 3)) == Up(Field(o.CountryCd, 3))
            && Field(cust.AddrZip, 10) == Field(o.Zip, 10)
            && Field(cust.PhoneNum1, 15) == o.PhoneNum1()
            && Field(cust.PhoneNum2, 15) == o.PhoneNum2()
            && cust.Ssn == ParseLong(o.SsnX())
            && Up(Field(cust.GovtIssuedId, 20)) == Up(Field(o.GovtId, 20))
            && Slice(cust.DobYyyyMmDd, 1, 4) == Slice(oldDob8, 1, 4)   // (1:4) == (1:4). :4174-4175
            && Slice(cust.DobYyyyMmDd, 6, 2) == Slice(oldDob8, 5, 2)   // (6:2) == OLD (5:2). FB-2. :4176-4177
            && Slice(cust.DobYyyyMmDd, 9, 2) == Slice(oldDob8, 7, 2)   // (9:2) == OLD (7:2). FB-2. :4178-4179
            && Field(cust.EftAccountId, 10) == Field(o.EftAccountId, 10)
            && Field(cust.PriCardHolderInd, 1) == Field(o.PriHolderInd, 1)
            && cust.FicoCreditScore == (int)ParseLong(Field(o.FicoX, 3));
        if (!custSame)
        {
            _dataWasChanged = true; // :4189
            return; // FB-1. :4190
        }
    }

    // ============================================================================================
    //  Symbolic-map I/O + COBOL idiom helpers
    // ============================================================================================

    private ScreenField F(string name) => _map.Field(name);
    private string MapIn(string name) => F(name).Value; // …I (raw, as received)
    private void SetOut(string name, string? value) => F(name).SetValue(LowToEmpty(value)); // …O (MOVE; LOW-VALUES→blank)
    private void SetAttr(string name, BmsAttribute attr) => F(name).AttributeOverride = attr;
    private void SetColor(string name, BmsColor color) => F(name).ColorOverride = color;
    private void PutCursor(string name) => F(name).CursorLength = -1;

    private static BmsAttribute Prot() => BmsAttribute.AutoSkip | BmsAttribute.Normal;     // DFHBMPRF
    private static BmsAttribute Unprot() => BmsAttribute.Unprotected | BmsAttribute.Normal | BmsAttribute.Fset; // DFHBMFSE

    // CSSETATY: when the field is NOT-OK/BLANK and CDEMO-PGM-REENTER → red + '*' placeholder. source: CSSETATY.cpy:17-27
    private void Csetaty(Flg flg, string field)
    {
        if ((flg == Flg.NotOk || flg == Flg.Blank) && _ca.IsReenter)
        {
            SetColor(field, BmsColor.Red);     // MOVE DFHRED.
            if (flg == Flg.Blank)
                SetOut(field, "*");             // MOVE '*' on blank.
        }
    }

    private static bool IsBad(Flg f) => f == Flg.NotOk || f == Flg.Blank;
    private static bool IsChangesMade(AcupAction a) =>
        a is AcupAction.ChangesNotOk or AcupAction.ChangesOkNotConfirmed
          or AcupAction.OkayedAndDone or AcupAction.OkayedLockError or AcupAction.OkayedButFailed; // 88 ACUP-CHANGES-MADE. :660-662

    private void ClearNonKeyFlags()
    {
        Flg v = Flg.Valid;
        _fAcctStatus = _fCredLimit = _fCashLimit = _fCurrBal = _fCurrCycCredit = _fCurrCycDebit = v;
        _fFico = _fFirstName = _fMiddleName = _fLastName = v;
        _fAddr1 = _fState = _fZip = _fCity = _fCountry = _fEft = _fPriCardholder = v;
        _fOpenYear = _fOpenMon = _fOpenDay = _fExpYear = _fExpMon = _fExpDay = v;
        _fRisYear = _fRisMon = _fRisDay = _fDobYear = _fDobMon = _fDobDay = v;
        _fSsn1 = _fSsn2 = _fSsn3 = _fPh1a = _fPh1b = _fPh1c = _fPh2a = _fPh2b = _fPh2c = v;
    }

    private void ResetMiscStorage()
    {
        _acctFilter = AcctFilter.NotOk; _custFilter = CustFilter.NotOk;
        _foundAcctInMaster = false; _foundCustInMaster = false;
        _wsInfoMsg = ""; _foundAccountData = false; _noChangesDetected = false;
        _wsReturnMsg = "\0";
    }

    // === Info-message setter (the WS-INFO-MSG 88-levels). source: :463-477 ===
    private enum Info { None, FoundAccountData, PromptForSearchKeys, PromptForChanges, PromptForConfirmation, ConfirmUpdateSuccess, InformFailure }
    private void SetInfo(Info info)
    {
        _foundAccountData = info == Info.FoundAccountData;
        _wsInfoMsg = info switch
        {
            Info.FoundAccountData => "Details of selected account shown above",
            Info.PromptForSearchKeys => "Enter or update id of account to update",
            Info.PromptForChanges => "Update account details presented above.",
            Info.PromptForConfirmation => "Changes validated.Press F5 to save",
            Info.ConfirmUpdateSuccess => "Changes committed to database",
            Info.InformFailure => "Changes unsuccessful. Please try again",
            _ => "",
        };
    }

    // === WS-RETURN-MSG handling (first error wins). source: :876, 479-480 ===
    private void SetReturnMsg(string msg) => _wsReturnMsg = msg; // turns WS-RETURN-MSG-OFF false (non-spaces/low)
    private string ReturnMsgText() => ReturnMsgOff ? "" : _wsReturnMsg;

    // === RESP/RESP2 text rendering for the file messages (pinned). source: §9.3 ===
    private static string RespText() => "0000000013"; // ERROR-RESP X(10) — NOTFND
    private static string ReasText() => "0000000000"; // ERROR-RESP2 X(10)
    private static string FileErrorMessage(string op, string file) =>
        $"File Error: {ClampOrPad(op, 8)} on {ClampOrPad(file, 9)} returned RESP {RespText()} ,RESP2 {ReasText()}     "; // :389-408

    // === Currency formatting via the +ZZZ,ZZZ,ZZZ.99 edited PIC. source: :371, CobolEditedNumeric ===
    private static string FmtCurrency(decimal v) => CobolEditedNumeric.Format(v, CurrencyPic);

    // === Numeric formatting helpers (zoned DISPLAY) ===
    private static string Fmt11(long v) => Math.Abs(v).ToString("D11", CultureInfo.InvariantCulture);
    private static string Fmt9(long v) => Math.Abs(v).ToString("D9", CultureInfo.InvariantCulture);
    private static string Fmt3(int v) => Math.Abs(v % 1000).ToString("D3", CultureInfo.InvariantCulture);
    private static string FmtAcctId(string acctIdX) => ClampOrPad(acctIdX, 11);

    private static decimal Money(decimal v) => TruncateToV2(v);
    private static decimal TruncateToV2(decimal v) =>
        decimal.Truncate(v * 100m) / 100m; // S9(10)V99, truncate toward zero (no rounding)

    // === COBOL string/char idioms ===
    private static string Trim(string s) => s.TrimEnd(' ').TrimEnd('\0'); // FUNCTION TRIM (trailing)
    private static string Trim8(string? s) => (s ?? "").TrimEnd();
    private static string Up(string s) => UpperAscii(s);
    private static string Low(string s) => LowerAscii(s);
    private static string UpTrim(string s) => UpperAscii(Trim(s));

    private static string UpperAscii(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v ?? "";
        var b = new char[v.Length];
        for (int i = 0; i < v.Length; i++) { char c = v[i]; b[i] = c >= 'a' && c <= 'z' ? (char)(c - 32) : c; }
        return new string(b);
    }
    private static string LowerAscii(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v ?? "";
        var b = new char[v.Length];
        for (int i = 0; i < v.Length; i++) { char c = v[i]; b[i] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c; }
        return new string(b);
    }

    /// <summary>1-based reference modification s(start:len), space-safe (LOW-VALUES read back as space).</summary>
    private static string Slice(string? s, int start1, int len)
    {
        s ??= "";
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            int idx = start1 - 1 + i;
            sb.Append(idx >= 0 && idx < s.Length ? s[idx] : ' ');
        }
        return sb.ToString();
    }

    /// <summary>The first char of a single-char field; '\0' (LOW-VALUES) if empty.</summary>
    private static char FirstChar(string? s) => string.IsNullOrEmpty(s) ? '\0' : s[0];

    /// <summary>Renders a value into a fixed PIC X(len) cell (clamp/space-pad), for residue/equality tests.</summary>
    private static string Field(string? value, int len) => ClampOrPad(value ?? "", len);

    private static string ClampOrPad(string s, int width)
    {
        s ??= "";
        if (s.Length > width) return s[..width];
        return s.PadRight(width, ' ');
    }

    // '*' / SPACES test (the RECEIVE convention). source: :1051-1052
    private static bool IsStarOrSpaces(string? v) =>
        v is null || v == "*" || v.Length == 0 || v.All(c => c == ' ');

    /// <summary>Maps a received field: '*'/SPACES → "" (LOW-VALUES sentinel), else the value (clamped to len).</summary>
    private static string MapEmptyToLow(string? v, int len) => IsStarOrSpaces(v) ? "" : ClampOrPad(v!, len);

    private static string LowToEmpty(string? v) => v ?? "";

    // LOW-VALUES / SPACES tests on variable values.
    private static bool IsLow(string? v) => string.IsNullOrEmpty(v) || v.All(c => c == '\0');
    private static bool IsSpaces(string? v) => !string.IsNullOrEmpty(v) && v.All(c => c == ' ');
    private static bool IsLowOrSpaces(string? v) => string.IsNullOrEmpty(v) || v.All(c => c == ' ' || c == '\0');

    /// <summary>True when a fixed X(len) field is all LOW-VALUES (empty/\0) or all SPACES, or TRIM length 0.</summary>
    private static bool IsBlankField(string? v, int len)
    {
        string f = Field(v, len);
        return IsLow(v) || f.All(c => c == ' ') || Trim(f).Length == 0;
    }
    private static bool IsLowOrSpacesFull(string? v, int len)
    {
        string f = Field(v, len);
        return IsLow(v) || f.All(c => c == ' ');
    }

    private static bool IsAllDigits(string? v)
    {
        string t = (v ?? "").Trim();
        if (t.Length == 0) return false;
        foreach (char c in t) if (c < '0' || c > '9') return false;
        return true;
    }

    private static long ParseLong(string? v)
    {
        string t = new string((v ?? "").Where(char.IsDigit).ToArray());
        return t.Length == 0 ? 0 : long.Parse(t, CultureInfo.InvariantCulture);
    }

    /// <summary>FUNCTION NUMVAL: parse a (possibly signed/spaced) plain numeric; non-numeric → 0.</summary>
    private static decimal NumVal(string? v)
    {
        string t = (v ?? "").Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n) ? n : 0m;
    }

    /// <summary>FUNCTION TEST-NUMVAL = 0 (the string is a valid plain number).</summary>
    private static bool TestNumVal(string? v)
    {
        string t = (v ?? "").Trim();
        if (t.Length == 0) return false;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// FUNCTION TEST-NUMVAL-C(x) = 0 (parseable currency) + FUNCTION NUMVAL-C(x). Accepts an optional
    /// leading/trailing sign, thousands commas, a leading currency symbol, and one decimal point.
    /// source: :1078-1080, 2201.
    /// </summary>
    private static bool TestNumValC(string? value, out decimal result)
    {
        result = 0m;
        string t = (value ?? "").Trim();
        if (t.Length == 0) return false;
        bool neg = false;
        if (t.StartsWith("-") || t.EndsWith("-") || t.StartsWith("(") && t.EndsWith(")")) neg = true;
        var sb = new StringBuilder();
        bool dot = false;
        foreach (char c in t)
        {
            if (c is >= '0' and <= '9') sb.Append(c);
            else if (c == '.' && !dot) { dot = true; sb.Append('.'); }
            else if (c is ',' or '+' or '-' or '$' or '(' or ')' or ' ') { /* skip */ }
            else return false; // any other char → not parseable
        }
        if (sb.Length == 0 || (sb.Length == 1 && sb[0] == '.')) return false;
        if (!decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n))
            return false;
        result = neg ? -n : n;
        return true;
    }

    private static bool AlphaResidueEmpty(string fld)
    {
        // INSPECT CONVERTING letters → spaces, then TRIM length 0 means only letters/spaces present.
        foreach (char c in fld)
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == ' ' || c == '\0'))
                return false;
        return true;
    }

    // ============================================================================================
    //  Program-private COMMAREA model (WS-THIS-PROGCOMMAREA). source: :652-849
    // ============================================================================================

    /// <summary>ACUP-CHANGE-ACTION states (the X(1) flag with its 88-levels). source: :654-668.</summary>
    private enum AcupAction
    {
        NotFetched,             // LOW-VALUES / SPACES (88 ACUP-DETAILS-NOT-FETCHED)
        ShowDetails,            // 'S'
        ChangesNotOk,           // 'E'
        ChangesOkNotConfirmed,  // 'N'
        OkayedAndDone,          // 'C'
        OkayedLockError,        // 'L'
        OkayedButFailed,        // 'F'
    }

    /// <summary>The packed money map field: raw X(12) plus the parsed S9(10)V99 (when valid). source: :763-795.</summary>
    private sealed class MoneyField
    {
        public string X = "";      // …-X (raw chars; "" == LOW-VALUES)
        public bool IsNum;          // whether N was computed (TEST-NUMVAL-C = 0)
        public decimal N;           // …-N (packed numeric)
        public string X12() => ClampOrPad(IsNum ? FmtSigned12(N) : X, 12); // canonical X(12) image for equality
        private static string FmtSigned12(decimal v) => ClampOrPad(((long)decimal.Truncate(v * 100m)).ToString(CultureInfo.InvariantCulture), 12);
    }

    /// <summary>ACUP-OLD-DETAILS / ACUP-NEW-DETAILS image (account + customer before/after). source: :669-849.</summary>
    private sealed class AcupDetails
    {
        // Account
        public string AcctIdX = "";    // X(11)
        public string ActiveStatus = "";
        public MoneyField CurrBal = new(), CreditLimit = new(), CashCreditLimit = new(),
                          CurrCycCredit = new(), CurrCycDebit = new();
        public string OpenYear = "", OpenMon = "", OpenDay = "";
        public string ExpYear = "", ExpMon = "", ExpDay = "";
        public string RisYear = "", RisMon = "", RisDay = "";
        public string GroupId = "";
        // Customer
        public string CustIdX = "";
        public string FirstName = "", MiddleName = "", LastName = "";
        public string AddrLine1 = "", AddrLine2 = "", AddrLine3 = "";
        public string StateCd = "", CountryCd = "", Zip = "";
        public string Ph1a = "", Ph1b = "", Ph1c = "", Ph2a = "", Ph2b = "", Ph2c = "";
        public string Ssn1 = "", Ssn2 = "", Ssn3 = "";
        public string GovtId = "";
        public string DobYear = "", DobMon = "", DobDay = "";
        public string EftAccountId = "";
        public string PriHolderInd = "";
        public string FicoX = "";

        /// <summary>MOVE ACCT-… TO ACUP-OLD-…-N: store a decimal into a MoneyField's numeric overlay (9500).</summary>
        public void SetMoney(MoneyField m, decimal v) { m.N = v; m.IsNum = true; }

        // --- Composite/derived accessors mirroring the COBOL group fields. ---
        public string OpenDate() => Field(OpenYear, 4) + Field(OpenMon, 2) + Field(OpenDay, 2); // ACUP-…-OPEN-DATE X(8)
        public string ExpDate() => Field(ExpYear, 4) + Field(ExpMon, 2) + Field(ExpDay, 2);
        public string RisDate() => Field(RisYear, 4) + Field(RisMon, 2) + Field(RisDay, 2);
        public string DobDate() => Field(DobYear, 4) + Field(DobMon, 2) + Field(DobDay, 2);
        public string SsnX() => Field(Ssn1, 3) + Field(Ssn2, 2) + Field(Ssn3, 4); // ACUP-…-SSN-X 9(9)/X(9)
        public string PhoneNum1() => "(" + Field(Ph1a, 3) + ")" + Field(Ph1b, 3) + "-" + Field(Ph1c, 4); // X(15)
        public string PhoneNum2() => "(" + Field(Ph2a, 3) + ")" + Field(Ph2b, 3) + "-" + Field(Ph2c, 4);

        // Money X(12) overlays (for the 1205 byte-equality comparisons).
        public string CurrBalX12() => CurrBal.X12();
        public string CreditLimitX12() => CreditLimit.X12();
        public string CashCreditLimitX12() => CashCreditLimit.X12();
        public string CurrCycCreditX12() => CurrCycCredit.X12();
        public string CurrCycDebitX12() => CurrCycDebit.X12();

        // NEW-side raw X accessors (used by 3203 echo + 1485 signed edit).
        public string CreditLimitX { get => CreditLimit.X; set => CreditLimit.X = value; }
        public string CashCreditLimitX { get => CashCreditLimit.X; set => CashCreditLimit.X = value; }
        public string CurrBalX { get => CurrBal.X; set => CurrBal.X = value; }
        public string CurrCycCreditX { get => CurrCycCredit.X; set => CurrCycCredit.X = value; }
        public string CurrCycDebitX { get => CurrCycDebit.X; set => CurrCycDebit.X = value; }

        public void SetSsn(long ssn) { string s = Fmt9(ssn); Ssn1 = s[..3]; Ssn2 = s.Substring(3, 2); Ssn3 = s.Substring(5, 4); }
        public void SetPhone1(string p15) { Ph1a = Slice(p15, 2, 3); Ph1b = Slice(p15, 6, 3); Ph1c = Slice(p15, 10, 4); }
        public void SetPhone2(string p15) { Ph2a = Slice(p15, 2, 3); Ph2b = Slice(p15, 6, 3); Ph2c = Slice(p15, 10, 4); }
    }

    /// <summary>The serializable program-private trailer (ACUP-* state). source: :652-849.</summary>
    private sealed class AcupCommArea
    {
        public AcupAction ChangeAction = AcupAction.NotFetched;
        public AcupDetails Old = new();
        public AcupDetails New = new();
    }

    // Expose the OLD-side decimal money for 9500 setters via the AcupDetails properties used above.
    // (CurrBal etc. are MoneyField; 9500 sets .N + .IsNum via Money().)
    // Helper to bridge the 9500 MOVE ACCT-… TO ACUP-OLD-…-N pattern.
    // (Implemented inline in StoreFetchedData9500 by setting MoneyField.N/IsNum.)

    /// <summary>
    /// Cross-turn store for the program-private trailer. The console runtime round-trips only the nav area
    /// (<see cref="CardDemoCommArea"/>); the <c>ACUP-*</c> bytes are keyed by the nav-area image so they
    /// survive the dispatcher's per-turn COMMAREA clone (the image is content-stable). source: spec §6.
    /// </summary>
    private static class ProgStateStore
    {
        private static readonly Dictionary<string, AcupCommArea> _store = new(StringComparer.Ordinal);

        public static void Save(CardDemoCommArea ca, AcupCommArea acup) => _store[Key(ca)] = acup;
        public static AcupCommArea? Load(CardDemoCommArea ca) => _store.TryGetValue(Key(ca), out var s) ? s : null;
        public static void Forget(CardDemoCommArea ca) => _store.Remove(Key(ca));

        private static string Key(CardDemoCommArea ca) => ca.ToImage();
    }

    // === Lookup tables (COPY CSLKPCDY). source: CSLKPCDY.cpy ===
    private static class Lookups
    {
        public static bool ValidGeneralPurposeAreaCode(string code) => GeneralPurpose.Contains(code);
        public static bool ValidUsStateCode(string st) => StateCodes.Contains(Trim(st));
        public static bool ValidUsStateZipCombo(string combo) => StateZip.Contains(combo);

        /// <summary>CSUTLDTC LE date validator stand-in: a real Gregorian date check on CCYYMMDD.</summary>
        public static bool LeDateValid(string ccyymmdd)
        {
            string t = (ccyymmdd ?? "").Trim();
            return DateTime.TryParseExact(t, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
        }

        // US state codes (incl. DC + territories). source: CSLKPCDY.cpy:1013-1069
        private static readonly HashSet<string> StateCodes = new(StringComparer.Ordinal)
        {
            "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA",
            "ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK",
            "OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC","AS","GU","MP","PR","VI",
        };

        // VALID-GENERAL-PURP-CODE area codes. source: CSLKPCDY.cpy:521-930
        private static readonly HashSet<string> GeneralPurpose = new(StringComparer.Ordinal)
        {
            "201","202","203","204","205","206","207","208","209","210","212","213","214","215","216","217",
            "218","219","220","223","224","225","226","228","229","231","234","236","239","240","242","246",
            "248","249","250","251","252","253","254","256","260","262","264","267","268","269","270","272",
            "276","279","281","284","289","301","302","303","304","305","306","307","308","309","310","312",
            "313","314","315","316","317","318","319","320","321","323","325","326","330","331","332","334",
            "336","337","339","340","341","343","345","346","347","351","352","360","361","364","365","367",
            "368","380","385","386","401","402","403","404","405","406","407","408","409","410","412","413",
            "414","415","416","417","418","419","423","424","425","430","431","432","434","435","437","438",
            "440","441","442","443","445","447","448","450","458","463","464","469","470","473","474","475",
            "478","479","480","484","501","502","503","504","505","506","507","508","509","510","512","513",
            "514","515","516","517","518","519","520","530","531","534","539","540","541","548","551","559",
            "561","562","563","564","567","570","571","572","573","574","575","579","580","581","582","585",
            "586","587","601","602","603","604","605","606","607","608","609","610","612","613","614","615",
            "616","617","618","619","620","623","626","628","629","630","631","636","639","640","641","646",
            "647","649","650","651","656","657","658","659","660","661","662","664","667","669","670","671",
            "672","678","680","681","682","683","684","689","701","702","703","704","705","706","707","708",
            "709","712","713","714","715","716","717","718","719","720","721","724","725","726","727","731",
            "732","734","737","740","742","743","747","753","754","757","758","760","762","763","765","767",
            "769","770","771","772","773","774","775","778","779","780","781","782","784","785","786","787",
            "801","802","803","804","805","806","807","808","809","810","812","813","814","815","816","817",
            "818","819","820","825","826","828","829","830","831","832","838","839","840","843","845","847",
            "848","849","850","854","856","857","858","859","860","862","863","864","865","867","868","869",
            "870","872","873","876","878","901","902","903","904","905","906","907","908","909","910","912",
            "913","914","915","916","917","918","919","920","925","928","929","930","931","934","936","937",
            "938","939","940","941","943","945","947","948","949","951","952","954","956","959","970","971",
            "972","973","978","979","980","983","984","985","986","989",
        };

        // VALID-US-STATE-ZIP-CD2-COMBO. source: CSLKPCDY.cpy:1073-1313
        private static readonly HashSet<string> StateZip = new(StringComparer.Ordinal)
        {
            "AA34","AE90","AE91","AE92","AE93","AE94","AE95","AE96","AE97","AE98","AK99","AL35","AL36","AP96",
            "AR71","AR72","AS96","AZ85","AZ86","CA90","CA91","CA92","CA93","CA94","CA95","CA96","CO80","CO81",
            "CT60","CT61","CT62","CT63","CT64","CT65","CT66","CT67","CT68","CT69","DC20","DC56","DC88","DE19",
            "FL32","FL33","FL34","FM96","GA30","GA31","GA39","GU96","HI96","IA50","IA51","IA52","ID83","IL60",
            "IL61","IL62","IN46","IN47","KS66","KS67","KY40","KY41","KY42","LA70","LA71","MA10","MA11","MA12",
            "MA13","MA14","MA15","MA16","MA17","MA18","MA19","MA20","MA21","MA22","MA23","MA24","MA25","MA26",
            "MA27","MA55","MD20","MD21","ME39","ME40","ME41","ME42","ME43","ME44","ME45","ME46","ME47","ME48",
            "ME49","MH96","MI48","MI49","MN55","MN56","MO63","MO64","MO65","MO72","MP96","MS38","MS39","MT59",
            "NC27","NC28","ND58","NE68","NE69","NH30","NH31","NH32","NH33","NH34","NH35","NH36","NH37","NH38",
            "NJ70","NJ71","NJ72","NJ73","NJ74","NJ75","NJ76","NJ77","NJ78","NJ79","NJ80","NJ81","NJ82","NJ83",
            "NJ84","NJ85","NJ86","NJ87","NJ88","NJ89","NM87","NM88","NV88","NV89","NY50","NY54","NY63","NY10",
            "NY11","NY12","NY13","NY14","OH43","OH44","OH45","OK73","OK74","OR97","PA15","PA16","PA17","PA18",
            "PA19","PR60","PR61","PR62","PR63","PR64","PR65","PR66","PR67","PR68","PR69","PR70","PR71","PR72",
            "PR73","PR74","PR75","PR76","PR77","PR78","PR79","PR90","PR91","PR92","PR93","PR94","PR95","PR96",
            "PR97","PR98","PW96","RI28","RI29","SC29","SD57","TN37","TN38","TX73","TX75","TX76","TX77","TX78",
            "TX79","TX88","UT84","VA20","VA22","VA23","VA24","VI80","VI82","VI83","VI84","VI85","VT50","VT51",
            "VT52","VT53","VT54","VT56","VT57","VT58","VT59","WA98","WA99","WI53","WI54","WV24","WV25","WV26",
            "WY82","WY83",
        };
    }

    // ============================================================================================
    //  BMS map builder — CACTUPA in mapset COACTUP (24x80). source: COACTUP.bms / SCREEN_COACTUP.md
    // ============================================================================================

    /// <summary>The DFHMDI map name. source: SCREEN_COACTUP.md.</summary>
    public const string MapName = LIT_THISMAP;

    /// <summary>The DFHMSD mapset name. source: SCREEN_COACTUP.md.</summary>
    public const string MapsetName = "COACTUP";

    // All named input fields, in BMS order — used by 3310-PROTECT-ALL-ATTRS.
    private static readonly string[] AllInputFields =
    {
        "ACCTSID","ACSTTUS","ACRDLIM","ACSHLIM","ACURBAL","ACRCYCR","ACRCYDB",
        "OPNYEAR","OPNMON","OPNDAY","EXPYEAR","EXPMON","EXPDAY","RISYEAR","RISMON","RISDAY",
        "AADDGRP","ACSTNUM","ACTSSN1","ACTSSN2","ACTSSN3","ACSTFCO","DOBYEAR","DOBMON","DOBDAY",
        "ACSFNAM","ACSMNAM","ACSLNAM","ACSADL1","ACSADL2","ACSCITY","ACSSTTE","ACSZIPC","ACSCTRY",
        "ACSPH1A","ACSPH1B","ACSPH1C","ACSPH2A","ACSPH2B","ACSPH2C","ACSGOVT","ACSEFTC","ACSPFLG",
    };

    /// <summary>
    /// Constructs the <c>CACTUPA</c> screen map from the BMS definition: every <c>DFHMDF</c> as a
    /// <see cref="ScreenField"/> with its exact Row/Col/Length/attribute/colour/highlight/initial value, in
    /// source order; the IC cursor on <c>ACCTSID</c>; the protected literals; and the named in/out fields.
    /// source: COACTUP.bms / SCREEN_COACTUP.md.
    /// </summary>
    public static BmsMap BuildBmsMap()
    {
        var fields = new List<ScreenField>
        {
            // --- shared 3-line header (rows 1-2) ---
            Lit(1, 1, 5, Askip(), BmsColor.Blue, "Tran:"),                                  // (1,1)
            Out("TRNNAME", 1, 7, 4, Askip(BmsAttribute.Fset), BmsColor.Blue),               // (1,7)
            Out("TITLE01", 1, 21, 40, Askip(), BmsColor.Yellow),                            // (1,21)
            Lit(1, 65, 5, Askip(), BmsColor.Blue, "Date:"),                                 // (1,65)
            Out("CURDATE", 1, 71, 8, Askip(), BmsColor.Blue, "mm/dd/yy"),                   // (1,71)
            Lit(2, 1, 5, Askip(), BmsColor.Blue, "Prog:"),                                  // (2,1)
            Out("PGMNAME", 2, 7, 8, Askip(), BmsColor.Blue),                                // (2,7)
            Out("TITLE02", 2, 21, 40, Askip(), BmsColor.Yellow),                            // (2,21)
            Lit(2, 65, 5, Askip(), BmsColor.Blue, "Time:"),                                 // (2,65)
            Out("CURTIME", 2, 71, 8, Askip(), BmsColor.Blue, "hh:mm:ss"),                   // (2,71)

            // --- headings ---
            Lit(4, 33, 14, DefAttr(), BmsColor.Neutral, "Update Account"),                  // (4,33)
            Lit(5, 19, 16, Askip(), BmsColor.Turquoise, "Account Number :"),                // (5,19)

            // --- account section ---
            InF("ACCTSID", 5, 38, 11, BmsAttribute.Unprotected | BmsAttribute.Ic, BmsColor.Default), // (5,38) IC
            Stop(5, 50),
            Lit(5, 57, 12, DefAttr(), BmsColor.Turquoise, "Active Y/N: "),                  // (5,57)
            In("ACSTTUS", 5, 70, 1, BmsColor.Default),                                      // (5,70)
            Stop(5, 72),

            Lit(6, 8, 8, DefAttr(), BmsColor.Turquoise, "Opened :"),                        // (6,8)
            InR("OPNYEAR", 6, 17, 4, BmsAttribute.Fset),                                    // (6,17) FSET RIGHT
            Lit(6, 22, 1, DefAttr(), BmsColor.Default, "-"),
            InR("OPNMON", 6, 24, 2, 0),                                                      // (6,24) RIGHT
            Lit(6, 27, 1, DefAttr(), BmsColor.Default, "-"),
            InR("OPNDAY", 6, 29, 2, 0),                                                      // (6,29) RIGHT
            Stop(6, 32),
            Lit(6, 39, 21, Askip(), BmsColor.Turquoise, "Credit Limit        :"),           // (6,39)
            InF("ACRDLIM", 6, 61, 15, BmsAttribute.Unprotected | BmsAttribute.Fset, BmsColor.Default), // (6,61) FSET
            Stop(6, 77),

            Lit(7, 8, 8, DefAttr(), BmsColor.Turquoise, "Expiry :"),                        // (7,8)
            InR("EXPYEAR", 7, 17, 4, 0),
            Lit(7, 22, 1, DefAttr(), BmsColor.Default, "-"),
            InR("EXPMON", 7, 24, 2, 0),
            Lit(7, 27, 1, DefAttr(), BmsColor.Default, "-"),
            InR("EXPDAY", 7, 29, 2, 0),
            Stop(7, 32),
            Lit(7, 39, 21, Askip(), BmsColor.Turquoise, "Cash credit Limit   :"),           // (7,39)
            InF("ACSHLIM", 7, 61, 15, BmsAttribute.Unprotected | BmsAttribute.Fset, BmsColor.Default),
            Stop(7, 77),

            Lit(8, 8, 8, DefAttr(), BmsColor.Turquoise, "Reissue:"),                        // (8,8)
            InR("RISYEAR", 8, 17, 4, 0),
            Lit(8, 22, 1, DefAttr(), BmsColor.Default, "-"),
            InR("RISMON", 8, 24, 2, 0),
            Lit(8, 27, 1, DefAttr(), BmsColor.Default, "-"),
            InR("RISDAY", 8, 29, 2, 0),
            Stop(8, 32),
            Lit(8, 39, 21, Askip(), BmsColor.Turquoise, "Current Balance     :"),           // (8,39)
            InF("ACURBAL", 8, 61, 15, BmsAttribute.Unprotected | BmsAttribute.Fset, BmsColor.Default),
            Stop(8, 77),

            Lit(9, 39, 21, Askip(), BmsColor.Turquoise, "Current Cycle Credit:"),           // (9,39)
            InF("ACRCYCR", 9, 61, 15, BmsAttribute.Unprotected | BmsAttribute.Fset, BmsColor.Default),
            Stop(9, 77),

            Lit(10, 8, 14, DefAttr(), BmsColor.Turquoise, "Account Group:"),                // (10,8)
            In("AADDGRP", 10, 23, 10, BmsColor.Default),                                    // (10,23)
            Stop(10, 34),
            Lit(10, 39, 21, Askip(), BmsColor.Turquoise, "Current Cycle Debit :"),          // (10,39)
            InF("ACRCYDB", 10, 61, 15, BmsAttribute.Unprotected | BmsAttribute.Fset, BmsColor.Default),
            Stop(10, 77),

            // --- customer section ---
            Lit(11, 32, 16, DefAttr(), BmsColor.Neutral, "Customer Details"),               // (11,32)
            Lit(12, 8, 14, DefAttr(), BmsColor.Turquoise, "Customer id  :"),                // (12,8)
            In("ACSTNUM", 12, 23, 9, BmsColor.Default),                                     // (12,23)
            Stop(12, 33),
            Lit(12, 49, 4, DefAttr(), BmsColor.Turquoise, "SSN:"),                          // (12,49)
            InV("ACTSSN1", 12, 55, 3, BmsColor.Default, "999"),                             // (12,55) INITIAL 999
            Lit(12, 59, 1, DefAttr(), BmsColor.Default, "-"),
            InV("ACTSSN2", 12, 61, 2, BmsColor.Default, "99"),                              // (12,61) INITIAL 99
            Lit(12, 64, 1, DefAttr(), BmsColor.Default, "-"),
            InV("ACTSSN3", 12, 66, 4, BmsColor.Default, "9999"),                            // (12,66) INITIAL 9999
            Stop(12, 71),

            Lit(13, 8, 14, DefAttr(), BmsColor.Turquoise, "Date of birth:"),                // (13,8)
            InR("DOBYEAR", 13, 23, 4, 0),                                                    // (13,23) RIGHT
            Lit(13, 28, 1, DefAttr(), BmsColor.Default, "-"),
            InR("DOBMON", 13, 30, 2, 0),
            Lit(13, 33, 1, DefAttr(), BmsColor.Default, "-"),
            InR("DOBDAY", 13, 35, 2, 0),
            Stop(13, 38),
            Lit(13, 49, 11, DefAttr(), BmsColor.Turquoise, "FICO Score:"),                  // (13,49)
            In("ACSTFCO", 13, 62, 3, BmsColor.Default),                                     // (13,62)
            Stop(13, 66),

            Lit(14, 1, 10, DefAttr(), BmsColor.Turquoise, "First Name"),                    // (14,1)
            Lit(14, 28, 13, DefAttr(), BmsColor.Turquoise, "Middle Name: "),               // (14,28)
            Lit(14, 55, 12, DefAttr(), BmsColor.Turquoise, "Last Name : "),                // (14,55)
            In("ACSFNAM", 15, 1, 25, BmsColor.Default),                                     // (15,1)
            Stop(15, 27),
            In("ACSMNAM", 15, 28, 25, BmsColor.Default),                                    // (15,28)
            Stop(15, 54),
            In("ACSLNAM", 15, 55, 25, BmsColor.Default),                                    // (15,55)

            Lit(16, 1, 8, DefAttr(), BmsColor.Turquoise, "Address:"),                       // (16,1)
            In("ACSADL1", 16, 10, 50, BmsColor.Default),                                    // (16,10)
            Stop(16, 61),
            Lit(16, 63, 6, DefAttr(), BmsColor.Turquoise, "State "),                        // (16,63)
            In("ACSSTTE", 16, 73, 2, BmsColor.Default),                                     // (16,73)
            Stop(16, 76),
            In("ACSADL2", 17, 10, 50, BmsColor.Default),                                    // (17,10)
            Stop(17, 61),
            Lit(17, 63, 3, DefAttr(), BmsColor.Turquoise, "Zip"),                           // (17,63)
            In("ACSZIPC", 17, 73, 5, BmsColor.Default),                                     // (17,73)
            Stop(17, 79),
            Lit(18, 1, 5, DefAttr(), BmsColor.Turquoise, "City "),                          // (18,1)
            In("ACSCITY", 18, 10, 50, BmsColor.Default),                                    // (18,10)
            Stop(18, 61),
            Lit(18, 63, 7, DefAttr(), BmsColor.Turquoise, "Country"),                       // (18,63)
            In("ACSCTRY", 18, 73, 3, BmsColor.Default),                                     // (18,73)
            Stop(18, 77),

            Lit(19, 1, 8, DefAttr(), BmsColor.Turquoise, "Phone 1:"),                       // (19,1)
            InR("ACSPH1A", 19, 10, 3, 0),                                                    // (19,10) RIGHT
            InR("ACSPH1B", 19, 14, 3, 0),                                                    // (19,14)
            InR("ACSPH1C", 19, 18, 4, 0),                                                    // (19,18)
            Stop(19, 23),
            Lit(19, 24, 30, DefAttr(), BmsColor.Turquoise, "Government Issued Id Ref    : "), // (19,24)
            In("ACSGOVT", 19, 58, 20, BmsColor.Default),                                    // (19,58)
            Stop(19, 79),

            Lit(20, 1, 8, DefAttr(), BmsColor.Turquoise, "Phone 2:"),                       // (20,1)
            InR("ACSPH2A", 20, 10, 3, 0),                                                    // (20,10)
            InR("ACSPH2B", 20, 14, 3, 0),                                                    // (20,14)
            InR("ACSPH2C", 20, 18, 4, 0),                                                    // (20,18)
            Stop(20, 23),
            Lit(20, 24, 16, DefAttr(), BmsColor.Turquoise, "EFT Account Id: "),             // (20,24)
            In("ACSEFTC", 20, 41, 10, BmsColor.Default),                                    // (20,41)
            Stop(20, 52),
            Lit(20, 53, 24, DefAttr(), BmsColor.Turquoise, "Primary Card Holder Y/N:"),     // (20,53)
            In("ACSPFLG", 20, 78, 1, BmsColor.Default),                                     // (20,78)
            Stop(20, 80),

            // --- message + function-key lines ---
            Out("INFOMSG", 22, 23, 45, BmsAttribute.AutoSkip, BmsColor.Neutral),            // (22,23) HILIGHT=OFF
            Stop(22, 69),
            Out("ERRMSG", 23, 1, 78, Askip(BmsAttribute.Bright | BmsAttribute.Fset), BmsColor.Red), // (23,1) BRT RED
            Out("FKEYS", 24, 1, 21, Askip(), BmsColor.Yellow, "ENTER=Process F3=Exit"),     // (24,1)
            Out("FKEY05", 24, 23, 7, BmsAttribute.AutoSkip | BmsAttribute.Dark, BmsColor.Yellow, "F5=Save"),  // (24,23) DRK
            Out("FKEY12", 24, 31, 10, BmsAttribute.AutoSkip | BmsAttribute.Dark, BmsColor.Yellow, "F12=Cancel"), // (24,31) DRK
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // === BMS field builders ===
    private static BmsAttribute Askip(BmsAttribute extra = BmsAttribute.None) =>
        BmsAttribute.AutoSkip | BmsAttribute.Normal | extra;          // ASKIP,NORM
    private static BmsAttribute DefAttr() => BmsAttribute.AutoSkip | BmsAttribute.Normal; // default (no ATTRB) label

    private static ScreenField Lit(int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = BmsHilight.Off, Value = initial };

    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial = "") =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = BmsHilight.Off, Value = initial };

    /// <summary>Unprotected input field (UNPROT, HILIGHT=UNDERLINE).</summary>
    private static ScreenField In(string name, int row, int col, int len, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = color, Hilight = BmsHilight.Underline };

    /// <summary>Unprotected input field with extra attribute bits (IC / FSET).</summary>
    private static ScreenField InF(string name, int row, int col, int len, BmsAttribute extra, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len,
                Attribute = extra | BmsAttribute.Normal, Color = color, Hilight = BmsHilight.Underline };

    /// <summary>Right-justified unprotected input field (date/phone components), with optional extra bits.</summary>
    private static ScreenField InR(string name, int row, int col, int len, BmsAttribute extra) =>
        new() { Name = name, Row = row, Col = col, Length = len,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal | extra, Color = BmsColor.Default,
                Hilight = BmsHilight.Underline, RightJustify = true };

    /// <summary>Unprotected input field with an INITIAL value (the SSN pre-fills).</summary>
    private static ScreenField InV(string name, int row, int col, int len, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = color, Hilight = BmsHilight.Underline, Value = initial };

    /// <summary>Zero-length stopper field (attribute cell only).</summary>
    private static ScreenField Stop(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = BmsAttribute.AutoSkip | BmsAttribute.Normal, Color = BmsColor.Default };
}
