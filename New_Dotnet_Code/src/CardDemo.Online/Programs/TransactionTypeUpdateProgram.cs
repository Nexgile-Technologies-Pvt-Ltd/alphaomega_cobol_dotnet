using System.Globalization;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the optional CICS/DB2 online program <c>COTRTUPC</c> — "Maintain Transaction
/// Type" (TRANSID <c>CTTU</c>, BMS map <c>CTRTUPA</c> / mapset <c>COTRTUP</c>). It is the update
/// counterpart of the list program COTRTLIC and maintains (view / add / change / delete) rows of the
/// re-hosted DB2 <c>CARDDEMO.TRANSACTION_TYPE</c> table (the 2-char transaction-type code + 50-char
/// description) via embedded <c>EXEC SQL</c> SELECT / UPDATE / INSERT / DELETE, here issued through the
/// relational <see cref="TransactionTypeRepository"/>. source: COTRTUPC.cbl:2-4,201-224.
/// </summary>
/// <remarks>
/// <para>Each COBOL paragraph is one method, named after the paragraph and carrying a
/// <c>// source: COTRTUPC.cbl:NNN</c> citation; statement order and the master <c>EVALUATE TRUE</c>
/// dispatch are preserved exactly. The program is state-driven through a single
/// <c>TTUP-CHANGE-ACTION</c> flag carried in its private commarea
/// (<see cref="TtupProgCommArea"/>), which the console runtime round-trips alongside the shared
/// <see cref="CardDemoCommArea"/> by keying a per-program trailer store on the nav-area image
/// (<see cref="ProgStateStore"/>) — the same idiom COACTUPC uses. source: spec §3.1, §8.</para>
///
/// <para><b>SQLCODE → repository mapping (relational port).</b>
/// SELECT row-found → SQLCODE 0; SELECT no row → SQLCODE +100. UPDATE affecting a row → 0; UPDATE
/// affecting 0 rows → +100 (drives the faithful update-then-insert fallback). INSERT ok → 0; INSERT
/// duplicate/other DB error → negative. DELETE ok → 0; DELETE no row → here treated as the "other"
/// negative path. The DB2-only <c>-911</c> (lock/timeout) and <c>-532</c> (RI child rows) codes cannot
/// arise from SQLite, so those branches are characterization-only; the two-stage flag→state EVALUATE in
/// 9600 is preserved verbatim so a simulated -911 would still reach the lock-error state. source: spec §2,§8.</para>
///
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — VARCHAR description length is always 50 on write: 9600 puts <c>TRIM(desc)</c> into the
/// host TEXT but computes <c>LEN = LENGTH(TTUP-NEW-TTYP-TYPE-DESC)</c> over a fixed PIC X(50), so the
/// stored value is the trimmed text right-padded with spaces to 50, length 50. (Read in 9500 instead
/// trims by the fetched LEN — an asymmetry.) source: COTRTUPC.cbl:1539-1542,335.</item>
/// <item>FB-2 — <c>SQLERRM</c> is concatenated twice in the DELETE -532 message. source: :1645-1646.</item>
/// <item>FB-3 — <c>3201-SHOW-INITIAL-VALUES</c> MOVEs LOW-VALUES to <c>TRTYPCDO</c> twice in one MOVE.
/// source: :1177-1178.</item>
/// <item>FB-4 — <c>FUNCTION CURRENT-DATE</c> moved to <c>WS-CURDATE-DATA</c> twice in 3100. source: :1113,1120.</item>
/// <item>FB-5 — dead/never-set message 88s and <c>TTUP-REVIEW-NEW-RECORD</c> ('V') are kept declared
/// only. source: :145,169,173,183,195,306.</item>
/// <item>FB-7 — the two-EVALUATE structure of 9600 (message 88s first, then state mapping) is kept
/// order-dependent. source: :1555-1589.</item>
/// </list>
/// </remarks>
public sealed class TransactionTypeUpdateProgram : ITransactionHandler
{
    // === WS-LITERALS. source: COTRTUPC.cbl:200-224 ===
    private const string LIT_THISPGM = "COTRTUPC";      // :201-202
    private const string LIT_THISTRANID = "CTTU";       // :203-204
    private const string LIT_THISMAPSET = "COTRTUP";    // :205-206 (X(8) 'COTRTUP ')
    private const string LIT_THISMAP = "CTRTUPA";       // :207-208
    private const string LIT_ADMINPGM = "COADM01C";     // :209-210
    private const string LIT_ADMINTRANID = "CA00";      // :211-212
    private const string LIT_LISTTPGM = "COTRTLIC";     // :217-218

    // === Constants from copybooks ===
    /// <summary>CCDA-TITLE01 PIC X(40). source: COTTL01Y.cpy.</summary>
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    /// <summary>CCDA-TITLE02 PIC X(40). source: COTTL01Y.cpy.</summary>
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    // === WS-MISC-STORAGE (WORKING-STORAGE, reinit each task). source: :35-196 ===

    // WS-EDIT-VARIABLE-NAME / WS-EDIT-ALPHANUM-ONLY etc are local to the edit methods.

    // WS-DATACHANGED-FLAG X(1): 88 NO-CHANGES-FOUND='0' / CHANGE-HAS-OCCURRED='1'. source: :78-80
    private bool _changeHasOccurred;

    // WS-INPUT-FLAG X(1): 88 INPUT-OK='0' / INPUT-ERROR='1' / INPUT-PENDING=LOW-VALUES. source: :81-84
    private bool _inputError;

    // WS-PFK-FLAG X(1): 88 PFK-VALID='0' / PFK-INVALID='1'. source: :88-90
    private bool _pfkValid;

    // WS-EDIT-TTYP-FLAG X(1): 88 FLG-TRANFILTER-ISVALID=LOW / -NOT-OK='0' / -BLANK='B'. source: :94-97
    // INITIALIZE WS-MISC-STORAGE sets it to SPACES (no VALUE clause) → Unset (none of the 88s true), so
    // FLG-TRANFILTER-ISVALID is FALSE until an explicit SET — load-bearing for the F12-cancel path. :353,94-95
    private Flg _ttypFlag = Flg.Unset;

    // WS-EDIT-DESC-FLAGS X(1): 88 FLG-DESCRIPTION-* (in WS-NON-KEY-FLAGS). source: :99-103
    private Flg _descFlag = Flg.Unset;

    // WS-TRANTYPE-MASTER-READ-FLAG X(1): 88 FOUND-TRANTYPE-IN-TABLE='1'. source: :125-127
    private bool _foundTranTypeInTable;

    // WS-DISP-SQLCODE PIC ----9 — edited SQLCODE for messages. source: :68
    private int _wsSqlcode;

    // WS-INFO-MSG X(40) — the chosen info prompt (set by 3250). source: :142-165
    private string _wsInfoMsg = "";
    // 88 WS-NO-INFO-MESSAGE = SPACES / LOW-VALUES. source: :143-144
    private bool WsNoInfoMessage => string.IsNullOrEmpty(Trim(_wsInfoMsg));

    // WS-RETURN-MSG X(75) — error/return message (first non-blank wins). source: :167-196
    // Initialized LOW-VALUES via SET WS-RETURN-MSG-OFF; we model "off" as the empty/null sentinel.
    private string _wsReturnMsg = "\0";
    // 88 WS-RETURN-MSG-OFF VALUE SPACES — true when message is still cleared. source: :168
    private bool ReturnMsgOff => IsLowOrSpaces(_wsReturnMsg);

    // === Repository over CARDDEMO.TRANSACTION_TYPE (DCLTRTYP). source: :286 ===
    private readonly TransactionTypeRepository _trantype;
    private readonly RelationalDb _db;

    // === CARDDEMO-COMMAREA (nav area) + program-private TTUP state. source: :291-336 ===
    private CardDemoCommArea _ca = new();
    private TtupProgCommArea _ttup = new();

    // The per-turn received/sent symbolic map (CTRTUPAI / CTRTUPAO) — one BmsMap instance.
    private BmsMap _map = null!;

    // === DCLGEN host variables (DCL-TR-TYPE / DCL-TR-DESCRIPTION). source: :286 / DCLTRTYP.dcl ===
    private string _dclTrType = "";          // DCL-TR-TYPE CHAR(2)
    private string _dclTrDescriptionText = ""; // DCL-TR-DESCRIPTION-TEXT X(50)
    private int _dclTrDescriptionLen;          // DCL-TR-DESCRIPTION-LEN S9(4) COMP

    /// <summary>Constructs the handler over the shared relational DB.</summary>
    public TransactionTypeUpdateProgram(RelationalDb db)
    {
        _db = db;
        _trantype = new TransactionTypeRepository(db.Connection);
    }

    public string ProgramName => LIT_THISPGM;
    public string TransId => LIT_THISTRANID;

    // ============================================================================================
    //  0000-MAIN. source: COTRTUPC.cbl:345-557
    // ============================================================================================
    public void Handle(CicsContext ctx)
    {
        // EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE) — abends surface as exceptions; no setup. :348-350
        _map = BuildBmsMap();

        // INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA (WS starts clean each task). :352-354
        // MOVE LIT-THISTRANID TO WS-TRANID. :358 (WS-TRANID not otherwise read)
        // SET WS-RETURN-MSG-OFF TO TRUE. :362
        _wsReturnMsg = "\0";

        // Store passed data if any. :366-381
        // IF EIBCALEN = 0 OR (FROM = COADM01C and NOT REENTER) OR (FROM = COTRTLIC and NOT REENTER).
        bool reenter = ctx.CommArea?.IsReenter ?? false;
        string fromPgm = Trim8(ctx.CommArea?.FromProgram);
        if (ctx.EibCalen == 0
            || (fromPgm == LIT_ADMINPGM && !reenter)
            || (fromPgm == LIT_LISTTPGM && !reenter))
        {
            // INITIALIZE CARDDEMO-COMMAREA, WS-THIS-PROGCOMMAREA. :371-372
            _ca = (ctx.EibCalen == 0 || ctx.CommArea is null) ? new CardDemoCommArea() : ctx.CommArea;
            if (ctx.EibCalen == 0) _ca = new CardDemoCommArea();
            _ttup = new TtupProgCommArea();
            _ca.SetFirstEntry();                       // SET CDEMO-PGM-ENTER. :373
            _ttup.Action = TtupAction.NotFetched;      // SET TTUP-DETAILS-NOT-FETCHED. :374
        }
        else
        {
            // MOVE DFHCOMMAREA(1:LEN OF CARDDEMO-COMMAREA) TO CARDDEMO-COMMAREA. :376-377
            _ca = ctx.CommArea!;
            // MOVE DFHCOMMAREA(LEN+1: LEN OF WS-THIS-PROGCOMMAREA) TO WS-THIS-PROGCOMMAREA. :378-380
            _ttup = ProgStateStore.Load(_ca) ?? new TtupProgCommArea();
        }

        // PERFORM YYYY-STORE-PFKEY (map EIBAID -> CCARD-AID-*). :386-387
        CcardAid aid = CssTrpfy.StorePfKey(ctx.EibAid);

        // SET PFK-INVALID TO TRUE. :398
        _pfkValid = false;
        // PERFORM 0001-CHECK-PFKEYS. :400-401
        aid = CheckPfkeys0001(aid);
        // The AID (CCARD-AID-*) is carried for paragraphs that test it (1150/1200/2000). :407-409, etc.
        _lastAid = aid;

        // Simulate initial entry if the following flags are set. :405-419
        if ((aid == CcardAid.Pfk12
                && (_ttup.Action == TtupAction.ShowDetails
                    || _ttup.Action == TtupAction.CreateNewRecord
                    || _ttup.Action == TtupAction.DetailsNotFound))
            || _ttup.Action == TtupAction.ChangesOkayedAndDone
            || IsChangesFailed(_ttup.Action)
            || (_ttup.Action == TtupAction.ChangesBackedOut && OldDetailsEmpty())
            || _ttup.Action == TtupAction.DeleteDone
            || _ttup.Action == TtupAction.DeleteFailed)
        {
            _ca.SetFirstEntry();                       // SET CDEMO-PGM-ENTER. :417
            _ttup.Action = TtupAction.NotFetched;      // SET TTUP-DETAILS-NOT-FETCHED. :418
        }

        // === Main dispatch EVALUATE TRUE (first matching WHEN wins). :423-556 ===

        // WHEN CCARD-AID-PFK03 — exit. :429-460
        if (aid == CcardAid.Pfk03)
        {
            if (IsLowOrSpaces(_ca.FromTranId))             // :431-432
                _ca.ToTranId = LIT_ADMINTRANID;            // :433
            else
                _ca.ToTranId = _ca.FromTranId;             // :435

            if (IsLowOrSpaces(_ca.FromProgram))            // :438-439
                _ca.ToProgram = LIT_ADMINPGM;              // :440
            else
                _ca.ToProgram = _ca.FromProgram;           // :442

            _ca.FromTranId = LIT_THISTRANID;               // :445
            _ca.FromProgram = LIT_THISPGM;                 // :446
            _ca.SetAdmin();                                // SET CDEMO-USRTYP-ADMIN. :448
            _ca.SetFirstEntry();                           // SET CDEMO-PGM-ENTER. :449
            _ca.LastMapSet = LIT_THISMAPSET;               // :450
            _ca.LastMap = LIT_THISMAP;                     // :451

            // EXEC CICS SYNCPOINT. :453-455 — commit any pending UOW (none on this exit path).
            Syncpoint();
            // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). :457-460
            ProgStateStore.Forget(_ca); // leaving this program; drop its private trailer
            ctx.Xctl(Trim8(_ca.ToProgram), _ca);
            return;
        }

        // (continued below — the dispatch falls through into HandleDispatch for the remaining WHENs)
        HandleDispatch(ctx, aid);
    }

    // Split out so the PF03 exit path above can early-return cleanly; this carries the remaining
    // WHEN branches of the 0000-MAIN EVALUATE. source: :465-556
    private void HandleDispatch(CicsContext ctx, CcardAid aid)
    {
        // WHEN (NOT REENTER and FROM = COADM01C) / (NOT REENTER and FROM = COTRTLIC) /
        //      (CDEMO-PGM-ENTER and TTUP-DETAILS-NOT-FETCHED). :465-478
        string fromPgm = Trim8(_ca.FromProgram);
        if ((!_ca.IsReenter && fromPgm == LIT_ADMINPGM)
            || (!_ca.IsReenter && fromPgm == LIT_LISTTPGM)
            || (_ca.IsFirstEntry && _ttup.Action == TtupAction.NotFetched))
        {
            // INITIALIZE WS-THIS-PROGCOMMAREA, WS-MISC-STORAGE, CDEMO-ACCT-ID. :471-473
            _ttup = new TtupProgCommArea();
            ResetMiscStorage();
            _ca.AcctId = 0;
            SendMap3000(ctx);                              // PERFORM 3000-SEND-MAP. :474-475
            _ca.SetReenter();                              // SET CDEMO-PGM-REENTER. :476
            _ttup.Action = TtupAction.NotFetched;          // SET TTUP-DETAILS-NOT-FETCHED. :477
            CommonReturn(ctx);                             // GO TO COMMON-RETURN. :478
            return;
        }

        // WHEN F04 and CONFIRM-DELETE — execute the delete. :482-489
        if (aid == CcardAid.Pfk04 && _ttup.Action == TtupAction.ConfirmDelete)
        {
            _ttup.Action = TtupAction.StartDelete;         // SET TTUP-START-DELETE. :484
            DeleteProcessing9800();                        // PERFORM 9800-DELETE-PROCESSING. :485-486
            SendMap3000(ctx);                              // PERFORM 3000-SEND-MAP. :487-488
            CommonReturn(ctx);                             // GO TO COMMON-RETURN. :489
            return;
        }

        // WHEN F04 and SHOW-DETAILS — ask for delete confirmation. :493-498
        if (aid == CcardAid.Pfk04 && _ttup.Action == TtupAction.ShowDetails)
        {
            _ttup.Action = TtupAction.ConfirmDelete;       // SET TTUP-CONFIRM-DELETE. :495
            SendMap3000(ctx);
            CommonReturn(ctx);
            return;
        }

        // WHEN F05 and DETAILS-NOT-FOUND — confirm new-record creation. :503-508
        if (aid == CcardAid.Pfk05 && _ttup.Action == TtupAction.DetailsNotFound)
        {
            _ttup.Action = TtupAction.CreateNewRecord;     // SET TTUP-CREATE-NEW-RECORD. :505
            SendMap3000(ctx);
            CommonReturn(ctx);
            return;
        }

        // WHEN F05 and CHANGES-OK-NOT-CONFIRMED — save the changes. :514-520
        if (aid == CcardAid.Pfk05 && _ttup.Action == TtupAction.ChangesOkNotConfirmed)
        {
            WriteProcessing9600();                         // PERFORM 9600-WRITE-PROCESSING. :516-517
            SendMap3000(ctx);
            CommonReturn(ctx);
            return;
        }

        // WHEN F12 and (CHANGES-OK-NOT-CONFIRMED / CONFIRM-DELETE / SHOW-DETAILS) — cancel. :524-533
        if (aid == CcardAid.Pfk12
            && (_ttup.Action == TtupAction.ChangesOkNotConfirmed
                || _ttup.Action == TtupAction.ConfirmDelete
                || _ttup.Action == TtupAction.ShowDetails))
        {
            _foundTranTypeInTable = true;                  // SET FOUND-TRANTYPE-IN-TABLE. :528
            DecideAction2000(ctx);                         // PERFORM 2000-DECIDE-ACTION. :529-530
            SendMap3000(ctx);
            CommonReturn(ctx);
            return;
        }

        // WHEN WS-INVALID-KEY-PRESSED (message flag set by 0001-CHECK-PFKEYS). :539-542
        if (_invalidKeyPressed)
        {
            SendMap3000(ctx);
            CommonReturn(ctx);
            return;
        }

        // WHEN OTHER. :548-555
        ProcessInputs1000(ctx);                            // PERFORM 1000-PROCESS-INPUTS. :549-550
        DecideAction2000(ctx);                             // PERFORM 2000-DECIDE-ACTION. :551-552
        SendMap3000(ctx);                                  // PERFORM 3000-SEND-MAP. :553-554
        CommonReturn(ctx);                                 // GO TO COMMON-RETURN. :555
    }

    // === COMMON-RETURN. source: COTRTUPC.cbl:559-572 ===
    private void CommonReturn(CicsContext ctx)
    {
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. :560 (CCARD-ERROR-MSG is CC-WORK-AREA, not persisted)
        // Reassemble WS-COMMAREA = CARDDEMO-COMMAREA ++ WS-THIS-PROGCOMMAREA. :562-565
        ProgStateStore.Save(_ca, _ttup);
        // EXEC CICS RETURN TRANSID(CTTU) COMMAREA(WS-COMMAREA). :567-571
        if (ctx.Outcome is null)
            ctx.ReturnTransId(LIT_THISTRANID, _ca);
    }

    // ============================================================================================
    //  0001-CHECK-PFKEYS. source: COTRTUPC.cbl:577-623
    // ============================================================================================
    // Returns the (possibly unchanged) AID. Sets _pfkValid and, on an invalid key, the
    // WS-INVALID-KEY-PRESSED message flag (only when the message is still off).
    private bool _invalidKeyPressed;

    private CcardAid CheckPfkeys0001(CcardAid aid)
    {
        // Should mirror 3391-PFKEY-ATTRS. :579-580
        bool valid =
            aid == CcardAid.Pfk03                                                            // :582
            || (aid == CcardAid.Enter && _ttup.Action != TtupAction.ConfirmDelete)          // :583
            || (aid == CcardAid.Pfk04 && (_ttup.Action == TtupAction.ShowDetails
                                          || _ttup.Action == TtupAction.ConfirmDelete))      // :584-586
            || (aid == CcardAid.Pfk05 && (_ttup.Action == TtupAction.ChangesOkNotConfirmed
                                          || _ttup.Action == TtupAction.DetailsNotFound
                                          || IsDeleteInProgress(_ttup.Action)))             // :588-593
            || (aid == CcardAid.Pfk12 && (_ttup.Action == TtupAction.ChangesOkNotConfirmed
                                          || _ttup.Action == TtupAction.ShowDetails
                                          || _ttup.Action == TtupAction.DetailsNotFound
                                          || _ttup.Action == TtupAction.ConfirmDelete
                                          || _ttup.Action == TtupAction.CreateNewRecord));  // :594-601

        if (valid)
        {
            _pfkValid = true;   // SET PFK-VALID. :602
        }
        else
        {
            _pfkValid = false;  // SET PFK-INVALID. :604
            if (ReturnMsgOff)   // IF WS-RETURN-MSG-OFF. :605
                SetInvalidKeyPressed(); // SET WS-INVALID-KEY-PRESSED. :606
        }

        // The commented-out remap (SET CCARD-AID-ENTER) is dead in the source; AID returned unchanged. :611-616
        return aid;
    }

    // ============================================================================================
    //  1000-PROCESS-INPUTS. source: COTRTUPC.cbl:625-640
    // ============================================================================================
    private void ProcessInputs1000(CicsContext ctx)
    {
        ReceiveMap1100(ctx);    // PERFORM 1100-RECEIVE-MAP. :626-627
        StoreMapInNew1150();    // PERFORM 1150-STORE-MAP-IN-NEW. :628-629
        EditMapInputs1200();    // PERFORM 1200-EDIT-MAP-INPUTS. :630-631
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG; MOVE LIT-THISPGM/MAPSET/MAP TO CCARD-NEXT-*. :632-635
        // (CC-WORK-AREA only; not persisted.)
    }

    // ============================================================================================
    //  1100-RECEIVE-MAP. source: COTRTUPC.cbl:641-650
    // ============================================================================================
    private void ReceiveMap1100(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('CTRTUPA') MAPSET('COTRTUP') INTO(CTRTUPAI) RESP/RESP2. :642-647
        ctx.ReceiveMap(LIT_THISMAP, LIT_THISMAPSET, _map);
    }

    // ============================================================================================
    //  1150-STORE-MAP-IN-NEW. source: COTRTUPC.cbl:652-688
    // ============================================================================================
    private void StoreMapInNew1150()
    {
        string trtypcdi = MapIn("TRTYPCD");
        string trtydsci = MapIn("TRTYDSC");

        // Guard: IF DETAILS-NOT-FOUND AND NOT F05 AND TRIM(TRTYPCDI) = TTUP-NEW-TTYP-TYPE -> keep prior. :654-661
        if (_ttup.Action == TtupAction.DetailsNotFound
            && _lastAid != CcardAid.Pfk05
            && Trim(trtypcdi) == Trim(_ttup.NewType))
            return;

        // INITIALIZE TTUP-NEW-DETAILS. :663
        _ttup.NewType = "";
        _ttup.NewTypeDesc = "";

        // Transaction Type: IF TRTYPCDI='*' OR SPACES -> LOW-VALUES else TRIM. :667-673
        if (trtypcdi == "*" || IsLowOrSpaces(trtypcdi))
            _ttup.NewType = "";                 // MOVE LOW-VALUES. :669
        else
            _ttup.NewType = Trim(trtypcdi);     // MOVE TRIM(TRTYPCDI). :671-672

        // Transaction Desc: IF TRTYDSCI='*' OR SPACES -> LOW-VALUES else TRIM. :678-684
        if (trtydsci == "*" || IsLowOrSpaces(trtydsci))
            _ttup.NewTypeDesc = "";             // MOVE LOW-VALUES. :680
        else
            _ttup.NewTypeDesc = Trim(trtydsci); // MOVE TRIM(TRTYDSCI). :682-683
    }

    // ============================================================================================
    //  1200-EDIT-MAP-INPUTS. source: COTRTUPC.cbl:689-781
    // ============================================================================================
    private void EditMapInputs1200()
    {
        // SET INPUT-OK. :690
        _inputError = false;

        // Re-prompt-same-not-found shortcut. :698-710
        if (_ttup.Action == TtupAction.DetailsNotFound
            && Trim(MapIn("TRTYPCD")) == Trim(_ttup.NewType))
        {
            if (_lastAid == CcardAid.Pfk05)
            {
                // CONTINUE. :701-702
            }
            else
            {
                _ttup.Action = TtupAction.NotFetched; // SET TTUP-DETAILS-NOT-FETCHED. :704
            }
            _ttypFlag = Flg.Valid;                    // SET FLG-TRANFILTER-ISVALID. :706
            return;                                   // GO TO 1200-EDIT-MAP-INPUTS-EXIT. :707
        }

        // IF CREATE-NEW-RECORD OR CHANGES-OK-NOT-CONFIRMED -> skip key edit. :712-714
        if (_ttup.Action == TtupAction.CreateNewRecord
            || _ttup.Action == TtupAction.ChangesOkNotConfirmed)
        {
            // CONTINUE. :713-714
        }
        else
        {
            EditTrantype1210(); // PERFORM 1210-EDIT-TRANTYPE. :716-717

            // IF FLG-TRANFILTER-BLANK. :720-726
            if (_ttypFlag == Flg.Blank)
            {
                if (ReturnMsgOff) SetNoSearchCriteriaReceived(); // :721-723
                _ttup.Action = TtupAction.NotFetched;            // SET TTUP-DETAILS-NOT-FETCHED. :724
                return;                                          // GO TO ...-EXIT. :725
            }

            // IF FLG-TRANFILTER-NOT-OK. :728-732
            // COBOL SETs TTUP-INVALID-SEARCH-KEYS ('K') then TTUP-DETAILS-NOT-FETCHED (LOW-VALUES); both
            // target the SAME one-byte TTUP-CHANGE-ACTION, so the LAST set wins -> NOT-FETCHED. :729-730
            if (_ttypFlag == Flg.NotOk)
            {
                _ttup.Action = TtupAction.InvalidSearchKeys;     // SET TTUP-INVALID-SEARCH-KEYS. :729
                _ttup.Action = TtupAction.NotFetched;            // SET TTUP-DETAILS-NOT-FETCHED (overwrites). :730
                return;                                          // GO TO ...-EXIT. :731
            }

            // IF TTUP-DETAILS-NOT-FETCHED. :734-736
            if (_ttup.Action == TtupAction.NotFetched)
                return; // GO TO ...-EXIT. :735
        }

        // SET FLG-TRANFILTER-ISVALID. :741
        _ttypFlag = Flg.Valid;

        // PERFORM 1205-COMPARE-OLD-NEW. :743-744
        CompareOldNew1205();

        // IF NO-CHANGES-FOUND OR CHANGES-OK-NOT-CONFIRMED OR CHANGES-OKAYED-AND-DONE. :746-751
        if (!_changeHasOccurred
            || _ttup.Action == TtupAction.ChangesOkNotConfirmed
            || _ttup.Action == TtupAction.ChangesOkayedAndDone)
        {
            // MOVE LOW-VALUES TO WS-NON-KEY-FLAGS. :749
            _descFlag = Flg.Valid;
            return; // GO TO ...-EXIT. :750
        }

        // SET TTUP-CHANGES-NOT-OK. :753
        _ttup.Action = TtupAction.ChangesNotOk;

        // Edit Description. :758-764
        string editName = "Transaction Desc";          // :758
        Flg result = EditAlphanumReqd1230(editName, _ttup.NewTypeDesc, 50); // :759-762
        _descFlag = result;                             // MOVE result -> WS-EDIT-DESC-FLAGS. :763-764

        // Cross-field edits: none. :766-768

        // Set green light for confirmation if no errors. :772-776
        if (_inputError)
        {
            // CONTINUE. :773
        }
        else
        {
            _ttup.Action = TtupAction.ChangesOkNotConfirmed; // SET TTUP-CHANGES-OK-NOT-CONFIRMED. :775
        }
    }

    // ============================================================================================
    //  1205-COMPARE-OLD-NEW. source: COTRTUPC.cbl:783-816
    // ============================================================================================
    private void CompareOldNew1205()
    {
        // SET NO-CHANGES-FOUND. :784
        _changeHasOccurred = false;

        // IF UPPER(NEW-TYPE)=UPPER(OLD-TYPE) AND UPPER(TRIM(NEW-DESC))=UPPER(TRIM(OLD-DESC))
        //    AND LENGTH(TRIM(NEW-DESC))=LENGTH(TRIM(OLD-DESC)). :786-797
        bool same =
            Up(Fixed(_ttup.NewType, 2)) == Up(Fixed(_ttup.OldType, 2))
            && Up(Trim(_ttup.NewTypeDesc)) == Up(Trim(_ttup.OldTypeDesc))
            && Trim(_ttup.NewTypeDesc).Length == Trim(_ttup.OldTypeDesc).Length;

        if (same)
        {
            // IF WS-RETURN-MSG-OFF SET NO-CHANGES-DETECTED ELSE CONTINUE. :799-803
            if (ReturnMsgOff) SetNoChangesDetected();
        }
        else
        {
            // IF WS-RETURN-MSG-OFF SET CHANGE-HAS-OCCURRED ELSE CONTINUE. :805-809
            if (ReturnMsgOff) _changeHasOccurred = true;
            // GO TO 1205-COMPARE-OLD-NEW-EXIT. :810
        }
    }

    // ============================================================================================
    //  1210-EDIT-TRANTYPE. source: COTRTUPC.cbl:820-847
    // ============================================================================================
    private void EditTrantype1210()
    {
        // SET FLG-TRANFILTER-NOT-OK. :821
        _ttypFlag = Flg.NotOk;

        // Edit Tran Type code: name='Tran Type code', value=NEW-TYPE, len=2; PERFORM 1245-EDIT-NUM-REQD. :826-830
        Flg result = EditNumReqd1245("Tran Type code", _ttup.NewType, 2);
        _ttypFlag = result; // MOVE result -> WS-EDIT-TTYP-FLAG. :831-832

        // IF FLG-TRANFILTER-ISVALID -> normalize to 2-digit zero-padded numeric. :834-842
        if (_ttypFlag == Flg.Valid)
        {
            // COMPUTE WS-EDIT-NUMERIC-2 = NUMVAL(NEW-TYPE) (PIC 9(2): keep low 2 digits). :835-837
            int n = (int)(Numval(_ttup.NewType) % 100);
            // MOVE WS-EDIT-NUMERIC-2 TO WS-EDIT-ALPHANUMERIC-2 (X(2)); INSPECT SPACES->ZEROS;
            // MOVE back TO NEW-TYPE — left-zero-pads a 1-digit code (e.g. "5"->"05"). :838-841
            _ttup.NewType = n.ToString("D2", CultureInfo.InvariantCulture);
        }
    }

    // ============================================================================================
    //  1230-EDIT-ALPHANUM-REQD. source: COTRTUPC.cbl:849-905
    //  Reusable "required alphanumeric" edit on value(1:len). Returns the FLG-ALPHNANUM-* result.
    // ============================================================================================
    private Flg EditAlphanumReqd1230(string varName, string value, int len)
    {
        // SET FLG-ALPHNANUM-NOT-OK. :851
        // Slice value(1:len) as a fixed COBOL field (space-padded to len).
        string slice = Fixed(value, len);

        // Blank: LOW-VALUES or SPACES or LENGTH(TRIM)=0. :854-873
        if (IsLowOrSpaces(slice) || Trim(slice).Length == 0)
        {
            _inputError = true;                                   // SET INPUT-ERROR. :861
            if (ReturnMsgOff)                                     // :863
                SetReturnMsg(Trim(varName) + " must be supplied."); // :864-868
            return Flg.Blank;                                     // SET FLG-ALPHNANUM-BLANK. :862
        }

        // Charset: only letters/digits/space allowed (INSPECT CONVERTING then TRIM-length test). :876-899
        if (!IsAlphaNumericOrSpaceOnly(slice))
        {
            _inputError = true;                                   // SET INPUT-ERROR. :888
            if (ReturnMsgOff)                                     // :890
                SetReturnMsg(Trim(varName) + " can have numbers or alphabets only."); // :891-895
            return Flg.NotOk;                                     // SET FLG-ALPHNANUM-NOT-OK. :889
        }

        // SET FLG-ALPHNANUM-ISVALID. :901
        return Flg.Valid;
    }

    // ============================================================================================
    //  1245-EDIT-NUM-REQD. source: COTRTUPC.cbl:907-976
    //  Reusable "required numeric" edit on value(1:len). Returns the FLG-ALPHNANUM-* result.
    // ============================================================================================
    private Flg EditNumReqd1245(string varName, string value, int len)
    {
        // SET FLG-ALPHNANUM-NOT-OK. :909
        string slice = Fixed(value, len);

        // Blank. :912-930
        if (IsLowOrSpaces(slice) || Trim(slice).Length == 0)
        {
            _inputError = true;                                   // SET INPUT-ERROR. :919
            if (ReturnMsgOff)                                     // :921
                SetReturnMsg(Trim(varName) + " must be supplied."); // :922-926
            return Flg.Blank;                                     // SET FLG-ALPHNANUM-BLANK. :920
        }

        // Numeric: TEST-NUMVAL(slice)=0 means valid number. :934-949
        if (!IsValidNumber(slice))
        {
            _inputError = true;                                   // SET INPUT-ERROR. :938
            if (ReturnMsgOff)                                     // :940
                SetReturnMsg(Trim(varName) + " must be numeric."); // :941-945
            return Flg.NotOk;                                     // SET FLG-ALPHNANUM-NOT-OK. :939
        }

        // Non-zero: NUMVAL(slice)=0 -> error. :954-969
        if (Numval(slice) == 0)
        {
            _inputError = true;                                   // SET INPUT-ERROR. :956
            if (ReturnMsgOff)                                     // :958
                SetReturnMsg(Trim(varName) + " must not be zero."); // :959-963
            return Flg.NotOk;                                     // SET FLG-ALPHNANUM-NOT-OK. :957
        }

        // SET FLG-ALPHNANUM-ISVALID. :972
        return Flg.Valid;
    }

    // ============================================================================================
    //  2000-DECIDE-ACTION. source: COTRTUPC.cbl:978-1085 — EVALUATE TRUE (first match wins)
    // ============================================================================================
    private void DecideAction2000(CicsContext ctx)
    {
        // WHEN TTUP-DETAILS-NOT-FETCHED / WHEN CCARD-AID-PFK12 (shared body). :984-1010
        if (_ttup.Action == TtupAction.NotFetched || _lastAid == CcardAid.Pfk12)
        {
            if (_ttypFlag == Flg.Valid) // IF FLG-TRANFILTER-ISVALID. :989
            {
                _wsReturnMsg = "\0";                       // SET WS-RETURN-MSG-OFF. :990
                ReadTrantype9000();                        // PERFORM 9000-READ-TRANTYPE. :991-992
                if (_foundTranTypeInTable)                 // IF FOUND-TRANTYPE-IN-TABLE. :993
                    _ttup.Action = TtupAction.ShowDetails; // SET TTUP-SHOW-DETAILS. :994
                else
                    _ttup.Action = TtupAction.DetailsNotFound; // SET TTUP-DETAILS-NOT-FOUND. :996
            }
            else // ELSE nested EVALUATE. :998-1008
            {
                if (_ttup.Action == TtupAction.ConfirmDelete)
                {
                    SetDeleteWasCancelled();               // SET WS-DELETE-WAS-CANCELLED. :1001
                    _ttup.Action = TtupAction.NotFetched;  // SET TTUP-DETAILS-NOT-FETCHED. :1002
                }
                else if (_ttup.Action == TtupAction.ChangesOkNotConfirmed)
                {
                    SetUpdateWasCancelled();               // SET WS-UPDATE-WAS-CANCELLED. :1004
                    _ttup.Action = TtupAction.ChangesBackedOut; // SET TTUP-CHANGES-BACKED-OUT. :1005
                }
                else // WHEN OTHER. :1006
                {
                    _ttup.Action = TtupAction.NotFetched;  // SET TTUP-DETAILS-NOT-FETCHED. :1007
                }
            }
            return;
        }

        // WHEN CONFIRM-DELETE and F12 -> stay in confirm. :1016-1018
        if (_ttup.Action == TtupAction.ConfirmDelete && _lastAid == CcardAid.Pfk12)
        {
            _ttup.Action = TtupAction.ConfirmDelete; // SET TTUP-CONFIRM-DELETE. :1018
            return;
        }

        // WHEN SHOW-DETAILS. :1023-1030
        if (_ttup.Action == TtupAction.ShowDetails)
        {
            // IF INPUT-ERROR OR NO-CHANGES-DETECTED OR WS-INVALID-KEY CONTINUE ELSE confirm. :1024-1029
            if (_inputError || _noChangesDetected || _wsInvalidKey)
            {
                // CONTINUE. :1027
            }
            else
            {
                _ttup.Action = TtupAction.ChangesOkNotConfirmed; // :1029
            }
            return;
        }

        // WHEN CHANGES-NOT-OK -> CONTINUE. :1035-1036
        if (_ttup.Action == TtupAction.ChangesNotOk)
            return;

        // WHEN CHANGES-BACKED-OUT -> back to CHANGES-NOT-OK. :1041-1042
        if (_ttup.Action == TtupAction.ChangesBackedOut)
        {
            _ttup.Action = TtupAction.ChangesNotOk;
            return;
        }

        // WHEN INVALID-SEARCH-KEYS -> CONTINUE. :1046-1047
        if (_ttup.Action == TtupAction.InvalidSearchKeys)
            return;

        // WHEN F05 and DETAILS-NOT-FOUND -> create new record. :1053-1055
        if (_lastAid == CcardAid.Pfk05 && _ttup.Action == TtupAction.DetailsNotFound)
        {
            _ttup.Action = TtupAction.CreateNewRecord;
            return;
        }

        // WHEN CHANGES-OK-NOT-CONFIRMED -> CONTINUE. :1060-1061
        if (_ttup.Action == TtupAction.ChangesOkNotConfirmed)
            return;

        // WHEN CHANGES-OKAYED-AND-DONE -> show details, reset acct/card if from-tranid empty. :1065-1072
        if (_ttup.Action == TtupAction.ChangesOkayedAndDone)
        {
            _ttup.Action = TtupAction.ShowDetails;         // SET TTUP-SHOW-DETAILS. :1066
            if (IsLowOrSpaces(_ca.FromTranId))             // :1067-1068
            {
                _ca.AcctId = 0;                            // MOVE ZEROES TO CDEMO-ACCT-ID. :1069
                _ca.CardNum = 0;                           //              CDEMO-CARD-NUM. :1070
                _ca.AcctStatus = "\0";                     // MOVE LOW-VALUES TO CDEMO-ACCT-STATUS. :1071
            }
            return;
        }

        // WHEN OTHER -> abend (UNEXPECTED DATA SCENARIO). :1073-1080
        AbendRoutine("UNEXPECTED DATA SCENARIO", "0001");
    }

    // ============================================================================================
    //  3000-SEND-MAP. source: COTRTUPC.cbl:1089-1108
    // ============================================================================================
    private void SendMap3000(CicsContext ctx)
    {
        ScreenInit3100(ctx);        // PERFORM 3100-SCREEN-INIT. :1090-1091
        SetupScreenVars3200();      // PERFORM 3200-SETUP-SCREEN-VARS. :1092-1093
        SetupInfoMsg3250();         // PERFORM 3250-SETUP-INFOMSG. :1094-1095
        SetupScreenAttrs3300();     // PERFORM 3300-SETUP-SCREEN-ATTRS. :1096-1097
        SetupInfoMsgAttrs3390();    // PERFORM 3390-SETUP-INFOMSG-ATTRS. :1098-1099
        SetupPfkeyAttrs3391();      // PERFORM 3391-SETUP-PFKEY-ATTRS. :1100-1101
        SendScreen3400(ctx);        // PERFORM 3400-SEND-SCREEN. :1102-1103
    }

    // ============================================================================================
    //  3100-SCREEN-INIT. source: COTRTUPC.cbl:1110-1138
    // ============================================================================================
    private void ScreenInit3100(CicsContext ctx)
    {
        // MOVE LOW-VALUES TO CTRTUPAO (fresh symbolic out-map). :1111
        // (BuildBmsMap already gives a fresh map each turn.)

        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA (twice — FB-4). :1113,1120
        DateTime now = ctx.Clock.Now;

        SetOut("TITLE01", CCDA_TITLE01);     // :1115
        SetOut("TITLE02", CCDA_TITLE02);     // :1116
        SetOut("TRNNAME", LIT_THISTRANID);   // :1117
        SetOut("PGMNAME", LIT_THISPGM);      // :1118

        // Build mm/dd/yy and hh:mm:ss from the current date/time. :1122-1132
        SetOut("CURDATE", now.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
        SetOut("CURTIME", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    // ============================================================================================
    //  3200-SETUP-SCREEN-VARS. source: COTRTUPC.cbl:1140-1174
    // ============================================================================================
    private void SetupScreenVars3200()
    {
        // IF CDEMO-PGM-ENTER CONTINUE (leave fields empty). :1142-1143
        if (_ca.IsFirstEntry)
            return;

        // EVALUATE TRUE. :1145-1169
        switch (_ttup.Action)
        {
            case TtupAction.NotFetched: // :1146
                ShowInitialValues3201();
                break;

            // SHOW-DETAILS / CONFIRM-DELETE / DELETE-FAILED / DELETE-DONE / CHANGES-BACKED-OUT. :1149-1156
            case TtupAction.ShowDetails:
            case TtupAction.ConfirmDelete:
            case TtupAction.DeleteFailed:
            case TtupAction.DeleteDone:
            case TtupAction.ChangesBackedOut:
                _ttup.NewType = "";          // INITIALIZE TTUP-NEW-DETAILS. :1154
                _ttup.NewTypeDesc = "";
                ShowOriginalValues3202();    // :1155-1156
                break;

            // CHANGES-MADE / CHANGES-NOT-OK / DETAILS-NOT-FOUND / INVALID-SEARCH-KEYS / CREATE-NEW-RECORD /
            // CHANGES-OKAYED-AND-DONE. :1157-1164
            case TtupAction.ChangesNotOk:
            case TtupAction.ChangesOkNotConfirmed:
            case TtupAction.ChangesOkayedLockError:
            case TtupAction.ChangesOkayedButFailed:
            case TtupAction.ChangesOkayedAndDone:
            case TtupAction.DetailsNotFound:
            case TtupAction.InvalidSearchKeys:
            case TtupAction.CreateNewRecord:
                ShowUpdatedValues3203();
                break;

            default: // WHEN OTHER. :1165-1168
                _ttup.NewType = "";          // INITIALIZE TTUP-NEW-DETAILS. :1166
                _ttup.NewTypeDesc = "";
                ShowOriginalValues3202();    // :1167-1168
                break;
        }
    }

    // 3201-SHOW-INITIAL-VALUES. source: :1176-1183
    private void ShowInitialValues3201()
    {
        // MOVE LOW-VALUES TO TRTYPCDO OF CTRTUPAO, TRTYPCDO OF CTRTUPAO (listed twice — FB-3). :1177-1178
        SetOut("TRTYPCD", "");
        SetOut("TRTYPCD", "");
    }

    // 3202-SHOW-ORIGINAL-VALUES. source: :1185-1196
    private void ShowOriginalValues3202()
    {
        // MOVE LOW-VALUES TO WS-NON-KEY-FLAGS. :1187
        _descFlag = Flg.Valid;
        // MOVE TTUP-OLD-TTYP-TYPE -> TRTYPCDO; TTUP-OLD-TTYP-TYPE-DESC -> TRTYDSCO. :1189-1190
        SetOut("TRTYPCD", _ttup.OldType);
        SetOut("TRTYDSC", _ttup.OldTypeDesc);
    }

    // 3203-SHOW-UPDATED-VALUES. source: :1197-1205
    private void ShowUpdatedValues3203()
    {
        // MOVE TTUP-NEW-TTYP-TYPE -> TRTYPCDO; TTUP-NEW-TTYP-TYPE-DESC -> TRTYDSCO. :1199-1200
        SetOut("TRTYPCD", _ttup.NewType);
        SetOut("TRTYDSC", _ttup.NewTypeDesc);
    }

    // ============================================================================================
    //  3250-SETUP-INFOMSG. source: COTRTUPC.cbl:1210-1268
    // ============================================================================================
    private void SetupInfoMsg3250()
    {
        // EVALUATE TRUE choosing the info prompt (first match wins). :1213-1247
        if (_ca.IsFirstEntry)                                            // WHEN CDEMO-PGM-ENTER. :1214
            _wsInfoMsg = PROMPT_FOR_SEARCH_KEYS;
        else if (_ttup.Action == TtupAction.NotFetched
                 || _ttup.Action == TtupAction.InvalidSearchKeys)       // :1216-1218
            _wsInfoMsg = PROMPT_FOR_SEARCH_KEYS;
        else if (_ttup.Action == TtupAction.DetailsNotFound)            // :1219-1220
            _wsInfoMsg = PROMPT_CREATE_NEW_RECORD;
        else if (_ttup.Action == TtupAction.ShowDetails
                 || (_ttup.Action == TtupAction.ChangesBackedOut && OldTypeEmpty())) // :1221-1225
            _wsInfoMsg = PROMPT_FOR_SEARCH_KEYS;
        else if (_ttup.Action == TtupAction.ChangesBackedOut
                 || _ttup.Action == TtupAction.ChangesNotOk)            // :1226-1228
            _wsInfoMsg = PROMPT_FOR_CHANGES;
        else if (_ttup.Action == TtupAction.ConfirmDelete)             // :1229-1230
            _wsInfoMsg = PROMPT_DELETE_CONFIRM;
        else if (_ttup.Action == TtupAction.DeleteFailed)             // :1231-1232
            _wsInfoMsg = INFORM_FAILURE;
        else if (_ttup.Action == TtupAction.DeleteDone)              // :1233-1234
            _wsInfoMsg = CONFIRM_DELETE_SUCCESS;
        else if (_ttup.Action == TtupAction.CreateNewRecord)        // :1235-1236
            _wsInfoMsg = PROMPT_FOR_NEWDATA;
        else if (_ttup.Action == TtupAction.ChangesOkNotConfirmed)  // :1237-1238
            _wsInfoMsg = PROMPT_FOR_CONFIRMATION;
        else if (_ttup.Action == TtupAction.ChangesOkayedAndDone)   // :1239-1240
            _wsInfoMsg = CONFIRM_UPDATE_SUCCESS;
        else if (_ttup.Action == TtupAction.ChangesOkayedLockError) // :1241-1242
            _wsInfoMsg = INFORM_FAILURE;
        else if (_ttup.Action == TtupAction.ChangesOkayedButFailed) // :1243-1244
            _wsInfoMsg = INFORM_FAILURE;
        else if (WsNoInfoMessage)                                    // :1245-1246
            _wsInfoMsg = PROMPT_FOR_SEARCH_KEYS;

        // Center-justify the text into WS-STRING-OUT (X(40)). :1249-1262
        string centered = CenterInto40(_wsInfoMsg);
        SetOut("INFOMSG", centered);

        // MOVE WS-RETURN-MSG TO ERRMSGO. :1264
        SetOut("ERRMSG", ReturnMsgText());
    }

    // ============================================================================================
    //  3300-SETUP-SCREEN-ATTRS. source: COTRTUPC.cbl:1269-1366
    // ============================================================================================
    private void SetupScreenAttrs3300()
    {
        // PERFORM 3310-PROTECT-ALL-ATTRS. :1272
        ProtectAllAttrs3310();

        // Unprotect EVALUATE. :1276-1298
        if (_ttup.Action == TtupAction.NotFetched
            || _ttup.Action == TtupAction.InvalidSearchKeys
            || _ttup.Action == TtupAction.DetailsNotFound
            || (_ttup.Action == TtupAction.ChangesBackedOut && OldTypeEmpty()))
        {
            SetAttr("TRTYPCD", Unprot()); // MOVE DFHBMFSE TO TRTYPCDA (search key editable). :1284
        }
        else if (_ttup.Action == TtupAction.ShowDetails
                 || _ttup.Action == TtupAction.ChangesNotOk
                 || _ttup.Action == TtupAction.CreateNewRecord
                 || _ttup.Action == TtupAction.ChangesBackedOut)
        {
            UnprotectFewAttrs3320(); // description editable. :1289-1290
        }
        else if (_ttup.Action == TtupAction.ChangesOkNotConfirmed
                 || _ttup.Action == TtupAction.ChangesOkayedAndDone
                 || IsDeleteInProgress(_ttup.Action))
        {
            // CONTINUE — keep all protected. :1294-1295
        }
        else
        {
            SetAttr("TRTYPCD", Unprot()); // WHEN OTHER. :1297
        }

        // Cursor EVALUATE (MOVE -1 TO ...L). :1303-1325
        if (_ttup.Action == TtupAction.NotFetched
            || _ttup.Action == TtupAction.DetailsNotFound
            || _ttup.Action == TtupAction.InvalidSearchKeys
            || _ttypFlag == Flg.NotOk
            || _ttypFlag == Flg.Blank
            || _ttup.Action == TtupAction.ChangesOkayedAndDone
            || (_ttup.Action == TtupAction.ChangesBackedOut && OldTypeEmpty()))
        {
            PutCursor("TRTYPCD"); // MOVE -1 TO TRTYPCDL. :1313
        }
        else if (_ttup.Action == TtupAction.CreateNewRecord
                 || _noChangesDetected
                 || _descFlag == Flg.NotOk
                 || _descFlag == Flg.Blank
                 || IsChangesMade(_ttup.Action)
                 || _ttup.Action == TtupAction.ChangesBackedOut
                 || _ttup.Action == TtupAction.ShowDetails)
        {
            PutCursor("TRTYDSC"); // MOVE -1 TO TRTYDSCL. :1322
        }
        else
        {
            PutCursor("TRTYPCD"); // WHEN OTHER. :1324
        }

        // Setup color: TRTYPCDC = RED if FILTER-NOT-OK or DELETE-FAILED. :1331-1334
        if (_ttypFlag == Flg.NotOk || _ttup.Action == TtupAction.DeleteFailed)
            SetColor("TRTYPCD", BmsColor.Red); // MOVE DFHRED. :1333

        // IF FILTER-BLANK AND CDEMO-PGM-REENTER -> TRTYPCDO='*' + RED. :1336-1340
        if (_ttypFlag == Flg.Blank && _ca.IsReenter)
        {
            SetOut("TRTYPCD", "*");            // :1338
            SetColor("TRTYPCD", BmsColor.Red); // :1339
        }

        // Early exit for the key-entry states. :1342-1350
        if (_ttup.Action == TtupAction.NotFetched
            || _ttup.Action == TtupAction.DetailsNotFound
            || _ttup.Action == TtupAction.InvalidSearchKeys
            || _ttypFlag == Flg.Blank
            || _ttypFlag == Flg.NotOk)
            return; // GO TO 3300-SETUP-SCREEN-ATTRS-EXIT. :1347

        // COPY CSSETATY for the Description field: red + '*' on error when CDEMO-PGM-REENTER. :1358-1361
        Csetaty(_descFlag, "TRTYDSC");
    }

    // 3310-PROTECT-ALL-ATTRS. source: :1368-1375
    private void ProtectAllAttrs3310()
    {
        // MOVE DFHBMPRF TO TRTYPCDA, TRTYDSCA, INFOMSGA. :1369-1371
        SetAttr("TRTYPCD", Prot());
        SetAttr("TRTYDSC", Prot());
        SetAttr("INFOMSG", Prot());
    }

    // 3320-UNPROTECT-FEW-ATTRS. source: :1377-1384
    private void UnprotectFewAttrs3320()
    {
        // MOVE DFHBMFSE TO TRTYDSCA (description editable); MOVE DFHBMPRF TO INFOMSGA. :1379-1380
        SetAttr("TRTYDSC", Unprot());
        SetAttr("INFOMSG", Prot());
    }

    // 3390-SETUP-INFOMSG-ATTRS. source: :1386-1395
    private void SetupInfoMsgAttrs3390()
    {
        // IF WS-NO-INFO-MESSAGE -> DFHBMDAR (dark) ELSE DFHBMASB (bright). :1387-1391
        SetAttr("INFOMSG", WsNoInfoMessage
            ? BmsAttribute.AutoSkip | BmsAttribute.Dark      // DFHBMDAR
            : BmsAttribute.AutoSkip | BmsAttribute.Bright);  // DFHBMASB
    }

    // 3391-SETUP-PFKEY-ATTRS. source: :1397-1426 (mirrors 0001-CHECK-PFKEYS)
    private void SetupPfkeyAttrs3391()
    {
        BmsAttribute asb = BmsAttribute.AutoSkip | BmsAttribute.Bright; // DFHBMASB
        BmsAttribute dar = BmsAttribute.AutoSkip | BmsAttribute.Dark;   // DFHBMDAR

        // Enter/FKEYS: CONFIRM-DELETE -> DFHBMDAR else DFHBMASB. :1400-1404
        SetAttr("FKEYS", _ttup.Action == TtupAction.ConfirmDelete ? dar : asb);

        // F04: SHOW-DETAILS or CONFIRM-DELETE -> DFHBMASB. :1406-1409
        if (_ttup.Action == TtupAction.ShowDetails || _ttup.Action == TtupAction.ConfirmDelete)
            SetAttr("FKEY04", asb);

        // F05: CHANGES-OK-NOT-CONFIRMED or DETAILS-NOT-FOUND -> DFHBMASB. :1411-1414
        if (_ttup.Action == TtupAction.ChangesOkNotConfirmed || _ttup.Action == TtupAction.DetailsNotFound)
            SetAttr("FKEY05", asb);

        // F12: CHANGES-OK-NOT-CONFIRMED or SHOW-DETAILS or DETAILS-NOT-FOUND or CONFIRM-DELETE or
        //      CREATE-NEW-RECORD -> DFHBMASB. :1416-1422
        if (_ttup.Action == TtupAction.ChangesOkNotConfirmed
            || _ttup.Action == TtupAction.ShowDetails
            || _ttup.Action == TtupAction.DetailsNotFound
            || _ttup.Action == TtupAction.ConfirmDelete
            || _ttup.Action == TtupAction.CreateNewRecord)
            SetAttr("FKEY12", asb);
    }

    // 3400-SEND-SCREEN. source: :1428-1444
    private void SendScreen3400(CicsContext ctx)
    {
        // MOVE LIT-THISMAPSET/MAP TO CCARD-NEXT-MAPSET/MAP. :1430-1431 (work area)
        // EXEC CICS SEND MAP('CTRTUPA') MAPSET('COTRTUP') FROM(CTRTUPAO) CURSOR ERASE FREEKB. :1433-1440
        ctx.SendMap(LIT_THISMAP, LIT_THISMAPSET, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
    }

    // ============================================================================================
    //  9000-READ-TRANTYPE. source: COTRTUPC.cbl:1447-1468
    // ============================================================================================
    private void ReadTrantype9000()
    {
        // INITIALIZE TTUP-OLD-DETAILS. :1449
        _ttup.OldType = "";
        _ttup.OldTypeDesc = "";

        // SET WS-NO-INFO-MESSAGE TO TRUE (clear info). :1451
        _wsInfoMsg = "";

        // PERFORM 9100-GET-TRANSACTION-TYPE. :1453-1454
        GetTransactionType9100();

        // IF FLG-TRANFILTER-NOT-OK GO TO EXIT. :1456-1458
        if (_ttypFlag == Flg.NotOk)
            return;

        // PERFORM 9500-STORE-FETCHED-DATA. :1461-1462
        StoreFetchedData9500();
    }

    // ============================================================================================
    //  9100-GET-TRANSACTION-TYPE. source: COTRTUPC.cbl:1469-1514
    // ============================================================================================
    private void GetTransactionType9100()
    {
        // MOVE TTUP-NEW-TTYP-TYPE TO DCL-TR-TYPE. :1473
        _dclTrType = Fixed(_ttup.NewType, 2);

        // EXEC SQL SELECT TR_TYPE, TR_DESCRIPTION INTO :DCL-* FROM TRANSACTION_TYPE WHERE TR_TYPE=:DCL-TR-TYPE. :1475-1482
        string status = _trantype.ReadByKey(_dclTrType, out TransactionType? row);

        // EVALUATE SQLCODE (0 found / +100 not found / <0 error). :1486-1509
        if (status == FileStatus.Ok && row is not null) // SQLCODE = ZERO. :1487-1488
        {
            _wsSqlcode = 0;
            // Unpack the VARCHAR back into the host variable (TEXT + LEN). The DB2 VARCHAR's LEN is the
            // stored byte count — modeled here as the stored string length (clamped to 50), NOT a re-trim,
            // so a faithful FB-1 write (trimmed text padded to 50, LEN=50) round-trips to LEN=50 and an
            // externally-seeded trimmed row round-trips to its own stored length. source: DCLTRTYP.dcl:40-46.
            _dclTrType = Fixed(row.TrType, 2);
            string text = row.TrDescription ?? "";
            _dclTrDescriptionText = Fixed(text, 50);
            _dclTrDescriptionLen = Math.Min(text.Length, 50);
            _foundTranTypeInTable = true;               // SET FOUND-TRANTYPE-IN-TABLE. :1488
        }
        else if (status == FileStatus.RecordNotFound)   // SQLCODE = +100. :1489-1494
        {
            _wsSqlcode = 100;
            _inputError = true;                          // SET INPUT-ERROR. :1490
            _ttypFlag = Flg.NotOk;                       // SET FLG-TRANFILTER-NOT-OK. :1491
            if (ReturnMsgOff)                            // :1492
                SetRecordNotFound();                     // SET WS-RECORD-NOT-FOUND. :1493
        }
        else // SQLCODE < 0 (DB error). :1495-1508
        {
            _wsSqlcode = -1; // characterization: actual DB2 SQLCODE unavailable from SQLite
            _inputError = true;                          // SET INPUT-ERROR. :1496
            _ttypFlag = Flg.NotOk;                       // SET FLG-TRANFILTER-NOT-OK. :1497
            if (ReturnMsgOff)                            // :1498
                SetReturnMsg("Error accessing:"          // :1500-1507
                    + " TRANSACTION_TYPE table. SQLCODE:"
                    + EditSqlcode(_wsSqlcode) + ":" + Sqlerrm());
        }
    }

    // ============================================================================================
    //  9500-STORE-FETCHED-DATA. source: COTRTUPC.cbl:1517-1530
    // ============================================================================================
    private void StoreFetchedData9500()
    {
        // INITIALIZE TTUP-OLD-DETAILS. :1519
        _ttup.OldType = "";
        _ttup.OldTypeDesc = "";

        // MOVE DCL-TR-TYPE TO TTUP-OLD-TTYP-TYPE. :1523
        _ttup.OldType = Trim(_dclTrType);
        // MOVE DCL-TR-DESCRIPTION-TEXT(1:DCL-TR-DESCRIPTION-LEN) TO TTUP-OLD-TTYP-TYPE-DESC (VARCHAR by LEN). :1524-1525
        _ttup.OldTypeDesc = _dclTrDescriptionLen > 0
            ? _dclTrDescriptionText.Substring(0, Math.Min(_dclTrDescriptionLen, _dclTrDescriptionText.Length))
            : "";
    }

    // ============================================================================================
    //  9600-WRITE-PROCESSING. source: COTRTUPC.cbl:1531-1595
    // ============================================================================================
    private void WriteProcessing9600()
    {
        // MOVE TTUP-NEW-TTYP-TYPE TO DCL-TR-TYPE. :1538
        _dclTrType = Fixed(_ttup.NewType, 2);
        // MOVE TRIM(TTUP-NEW-TTYP-TYPE-DESC) TO DCL-TR-DESCRIPTION-TEXT. :1539-1540
        _dclTrDescriptionText = Fixed(Trim(_ttup.NewTypeDesc), 50);
        // COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(TTUP-NEW-TTYP-TYPE-DESC) — ALWAYS 50 (FB-1). :1541-1542
        _dclTrDescriptionLen = 50;

        // The VARCHAR stored = trimmed text right-padded with spaces to 50, length 50 (FB-1).
        var rowToWrite = new TransactionType
        {
            TrType = _dclTrType,
            TrDescription = _dclTrDescriptionText, // already padded to 50 (length 50)
        };

        // EXEC SQL UPDATE ... SET TR_DESCRIPTION=:DCL-... WHERE TR_TYPE=:DCL-TR-TYPE. :1544-1548
        string updStatus = _trantype.Update(rowToWrite);

        // First EVALUATE: SQLCODE -> message 88s. :1555-1578
        bool couldNotLock = false;  // 88 COULD-NOT-LOCK-REC-FOR-UPDATE
        bool tableUpdateFailed = false; // 88 TABLE-UPDATE-FAILED
        if (updStatus == FileStatus.Ok) // SQLCODE = ZERO. :1556-1557
        {
            _wsSqlcode = 0;
            Syncpoint(); // EXEC CICS SYNCPOINT. :1557
        }
        else if (updStatus == FileStatus.RecordNotFound) // SQLCODE = +100 -> insert. :1558-1560
        {
            _wsSqlcode = 100;
            InsertRecord9700(rowToWrite); // PERFORM 9700-INSERT-RECORD. :1559-1560
        }
        // The -911 lock branch is DB2-only; cannot arise from SQLite (characterization-only). :1561-1566
        // else if (SQLCODE == -911) { _inputError = true; if (ReturnMsgOff) SetCouldNotLock(); couldNotLock = true; }
        else // SQLCODE < 0 (other DB error). :1567-1577
        {
            _wsSqlcode = -1;
            tableUpdateFailed = true;       // SET TABLE-UPDATE-FAILED. :1568
            SetReturnMsg("Error updating:"  // :1569-1577
                + " TRANSACTION_TYPE Table. SQLCODE:"
                + EditSqlcode(_wsSqlcode) + ":" + Sqlerrm());
        }

        // Second EVALUATE: message 88s -> TTUP-* states (order-dependent — FB-7). :1580-1589
        if (couldNotLock)
            _ttup.Action = TtupAction.ChangesOkayedLockError;       // :1582
        else if (tableUpdateFailed)
            _ttup.Action = TtupAction.ChangesOkayedButFailed;       // :1584
        else if (_dataWasChangedBeforeUpdate) // DATA-WAS-CHANGED-BEFORE-UPDATE never set (FB-5). :1585-1586
            _ttup.Action = TtupAction.ShowDetails;
        else
            _ttup.Action = TtupAction.ChangesOkayedAndDone;         // WHEN OTHER. :1588
    }

    // ============================================================================================
    //  9700-INSERT-RECORD. source: COTRTUPC.cbl:1596-1623
    // ============================================================================================
    private void InsertRecord9700(TransactionType rowToWrite)
    {
        // EXEC SQL INSERT INTO TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION) VALUES (:DCL-TR-TYPE, :DCL-...). :1597-1602
        string insStatus = _trantype.Insert(rowToWrite);

        // EVALUATE SQLCODE (0 -> SYNCPOINT / other -> update-failed). :1604-1619
        if (insStatus == FileStatus.Ok) // SQLCODE = ZERO. :1605-1606
        {
            _wsSqlcode = 0;
            Syncpoint(); // EXEC CICS SYNCPOINT. :1606
        }
        else // WHEN OTHER. :1607-1618
        {
            _wsSqlcode = -1;
            SetTableUpdateFailed();                 // SET TABLE-UPDATE-FAILED. :1608
            SetReturnMsg("Error inserting record into:" // :1609-1617
                + " TRANSACTION_TYPE Table. SQLCODE:"
                + EditSqlcode(_wsSqlcode) + ":" + Sqlerrm());
        }
    }

    // ============================================================================================
    //  9800-DELETE-PROCESSING. source: COTRTUPC.cbl:1624-1666
    // ============================================================================================
    private void DeleteProcessing9800()
    {
        // MOVE TTUP-OLD-TTYP-TYPE TO DCL-TR-TYPE. :1625
        _dclTrType = Fixed(_ttup.OldType, 2);

        // EXEC SQL DELETE FROM TRANSACTION_TYPE WHERE TR_TYPE=:DCL-TR-TYPE. :1627-1630
        string delStatus = _trantype.Delete(_dclTrType);

        // EVALUATE SQLCODE (0 -> delete done + SYNCPOINT / -532 -> RI child / other -> failed). :1634-1662
        if (delStatus == FileStatus.Ok) // SQLCODE = ZERO. :1635-1637
        {
            _wsSqlcode = 0;
            _ttup.Action = TtupAction.DeleteDone; // SET TTUP-DELETE-DONE. :1636
            Syncpoint(); // EXEC CICS SYNCPOINT. :1637
        }
        // The -532 RI-child branch is DB2-only; SQLite has no FK from TRANSACTION_TYPE here, so this is
        // characterization-only. Reproduced for completeness (SQLERRM concatenated twice — FB-2). :1638-1649
        // else if (SQLCODE == -532) {
        //     SetRecordDeleteFailed();
        //     SetReturnMsg("Please delete associated child records first:" + "SQLCODE :"
        //         + EditSqlcode(_wsSqlcode) + ":" + Sqlerrm() + Sqlerrm()); // FB-2: SQLERRM twice
        // }
        else // WHEN OTHER (incl. no-row delete). :1650-1661
        {
            _wsSqlcode = -1;
            SetRecordDeleteFailed();                // SET RECORD-DELETE-FAILED. :1651
            _ttup.Action = TtupAction.DeleteFailed; // SET TTUP-DELETE-FAILED. :1652
            SetReturnMsg("Delete failed with message:" // :1653-1660
                + "SQLCODE :" + EditSqlcode(_wsSqlcode) + ":" + Sqlerrm());
        }
    }

    // === ABEND-ROUTINE. source: :1675-1701 ===
    private void AbendRoutine(string msg, string code)
    {
        // SEND ABEND-DATA, HANDLE ABEND CANCEL, EXEC CICS ABEND ABCODE('9999'). Surface as an exception.
        throw new InvalidOperationException(
            $"COTRTUPC ABEND {code}: {(string.IsNullOrEmpty(msg) ? "UNEXPECTED ABEND OCCURRED." : msg)}");
    }

    // ============================================================================================
    //  WS-INFO-MSG 88-levels (the centered info prompts). source: :143-165
    // ============================================================================================
    private const string PROMPT_FOR_SEARCH_KEYS = "Enter transaction type to be maintained"; // :147-148
    private const string PROMPT_CREATE_NEW_RECORD = "Press F05 to add. F12 to cancel";        // :149-150
    private const string PROMPT_DELETE_CONFIRM = "Delete this record ? Press F4 to confirm";  // :151-152
    private const string CONFIRM_DELETE_SUCCESS = "Delete successful.";                       // :153-154
    private const string PROMPT_FOR_CHANGES = "Update transaction type details shown.";       // :155-156
    private const string PROMPT_FOR_NEWDATA = "Enter new transaction type details.";          // :157-158
    private const string PROMPT_FOR_CONFIRMATION = "Changes validated.Press F5 to save";      // :160-161
    private const string CONFIRM_UPDATE_SUCCESS = "Changes committed to database";            // :162-163
    private const string INFORM_FAILURE = "Changes unsuccessful";                             // :164-165

    // ============================================================================================
    //  WS-RETURN-MSG 88-levels (error/return messages). source: :167-196
    // ============================================================================================
    // Tracked via _wsInvalidKey / _noChangesDetected for the conditions 2000 tests; the literal text is
    // assigned to _wsReturnMsg here so ERRMSGO renders it. First non-blank wins (the IF WS-RETURN-MSG-OFF guards).
    private bool _wsInvalidKey;          // 88 WS-INVALID-KEY ('Invalid Key pressed. ') — never set (FB-5). :171-172
    private bool _noChangesDetected;     // 88 NO-CHANGES-DETECTED. :179-180
    private bool _dataWasChangedBeforeUpdate; // 88 DATA-WAS-CHANGED-BEFORE-UPDATE — never set (FB-5). :183-184

    private void SetRecordNotFound() => SetReturnMsg("No record found for this key in database"); // :175-176
    private void SetNoSearchCriteriaReceived() => SetReturnMsg("No input received");               // :177-178
    private void SetNoChangesDetected()
    {
        _noChangesDetected = true;
        SetReturnMsg("No change detected with respect to values fetched."); // :179-180
    }
    private void SetUpdateWasCancelled() => SetReturnMsg("Update was cancelled");  // :185-186
    private void SetTableUpdateFailed() => SetReturnMsg("Update of record failed"); // :187-188
    private void SetRecordDeleteFailed() => SetReturnMsg("Delete of record failed"); // :189-190
    private void SetDeleteWasCancelled() => SetReturnMsg("Delete was cancelled");  // :191-192
    private void SetInvalidKeyPressed()
    {
        _invalidKeyPressed = true;
        SetReturnMsg("Invalid key pressed"); // WS-INVALID-KEY-PRESSED. :193-194
    }

    // === WS-RETURN-MSG handling (first non-blank wins). source: :362, :605, etc. ===
    private void SetReturnMsg(string msg) => _wsReturnMsg = msg; // turns WS-RETURN-MSG-OFF false
    private string ReturnMsgText() => ReturnMsgOff ? "" : _wsReturnMsg;

    // === Info-message setter helpers ===
    private void ResetMiscStorage()
    {
        // INITIALIZE WS-MISC-STORAGE → SPACES for the no-VALUE PIC X flags (Unset, not ISVALID). :353
        _ttypFlag = Flg.Unset;
        _descFlag = Flg.Unset;
        _foundTranTypeInTable = false;
        _wsInfoMsg = "";
        _wsReturnMsg = "\0";
        _wsInvalidKey = false;
        _invalidKeyPressed = false;
        _noChangesDetected = false;
        _changeHasOccurred = false;
        _inputError = false;
        _wsSqlcode = 0;
    }

    // === The AID this turn (used by paragraphs that test CCARD-AID-*). ===
    private CcardAid _lastAid;

    // ============================================================================================
    //  Symbolic-map I/O + COBOL idiom helpers
    // ============================================================================================
    private ScreenField F(string name) => _map.Field(name);
    private string MapIn(string name) => F(name).Value;               // …I (raw, as received)
    private void SetOut(string name, string? value) => F(name).SetValue(LowToEmpty(value)); // …O
    private void SetAttr(string name, BmsAttribute attr) => F(name).AttributeOverride = attr;
    private void SetColor(string name, BmsColor color) => F(name).ColorOverride = color;
    private void PutCursor(string name) => F(name).CursorLength = -1;

    private static BmsAttribute Prot() => BmsAttribute.AutoSkip | BmsAttribute.Normal;                 // DFHBMPRF
    private static BmsAttribute Unprot() => BmsAttribute.Unprotected | BmsAttribute.Normal | BmsAttribute.Fset; // DFHBMFSE

    // CSSETATY: when the field is NOT-OK/BLANK and CDEMO-PGM-REENTER -> red + '*' placeholder. source: CSSETATY.cpy:17-27
    private void Csetaty(Flg flg, string field)
    {
        if ((flg == Flg.NotOk || flg == Flg.Blank) && _ca.IsReenter)
        {
            SetColor(field, BmsColor.Red); // MOVE DFHRED.
            if (flg == Flg.Blank)
                SetOut(field, "*");         // MOVE '*' on blank.
        }
    }

    private static string LowToEmpty(string? v) => v ?? "";

    // === State predicates over TTUP-CHANGE-ACTION ===
    private static bool IsDeleteInProgress(TtupAction a) =>
        a is TtupAction.ConfirmDelete or TtupAction.StartDelete
          or TtupAction.DeleteDone or TtupAction.DeleteFailed; // 88 TTUP-DELETE-IN-PROGRESS '9','8','7','6'. :307-309
    private static bool IsChangesFailed(TtupAction a) =>
        a is TtupAction.ChangesOkayedLockError or TtupAction.ChangesOkayedButFailed; // 88 TTUP-CHANGES-FAILED 'L','F'. :322
    private static bool IsChangesMade(TtupAction a) =>
        a is TtupAction.ChangesNotOk or TtupAction.ChangesOkNotConfirmed
          or TtupAction.ChangesOkayedLockError or TtupAction.ChangesOkayedButFailed; // 88 TTUP-CHANGES-MADE 'E','N','L','F'. :315-317

    private bool OldDetailsEmpty()
    {
        // TTUP-OLD-DETAILS EQUAL LOW-VALUES OR EQUAL SPACES (the whole 52-byte group). :413-414
        return IsLowOrSpaces(_ttup.OldType) && IsLowOrSpaces(_ttup.OldTypeDesc);
    }

    private bool OldTypeEmpty()
    {
        // TTUP-OLD-TTYP-TYPE = LOW-VALUES OR SPACES. :1223-1224
        return IsLowOrSpaces(_ttup.OldType);
    }

    // === COBOL string/char idioms ===
    private static string Trim(string? s) => (s ?? "").TrimEnd(' ').TrimEnd('\0').TrimStart(' '); // FUNCTION TRIM
    private static string Trim8(string? s) => (s ?? "").TrimEnd();
    private static string Up(string? s) => UpperAscii(s);

    private static string UpperAscii(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v ?? "";
        var b = new char[v.Length];
        for (int i = 0; i < v.Length; i++) { char c = v[i]; b[i] = c >= 'a' && c <= 'z' ? (char)(c - 32) : c; }
        return new string(b);
    }

    /// <summary>True when an X(n) field is all LOW-VALUES (modeled as NUL) or all spaces / empty.</summary>
    private static bool IsLowOrSpaces(string? v)
    {
        if (string.IsNullOrEmpty(v)) return true;
        foreach (char c in v)
            if (c != ' ' && c != '\0') return false;
        return true;
    }

    /// <summary>Right-pads/truncates a value to a fixed COBOL X(width) field with spaces.</summary>
    private static string Fixed(string? v, int width)
    {
        v ??= "";
        return v.Length >= width ? v.Substring(0, width) : v.PadRight(width, ' ');
    }

    /// <summary>
    /// INSPECT CONVERTING charset scrub (1230): only letters/digits/space allowed. Returns true when the
    /// slice contains no character outside <c>[A-Za-z0-9 ]</c> (after the scrub TRIM-length is 0). source: :876-899.
    /// </summary>
    private static bool IsAlphaNumericOrSpaceOnly(string slice)
    {
        foreach (char c in slice)
        {
            if (c == ' ' || c == '\0') continue;
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>FUNCTION TEST-NUMVAL = 0 (valid number): a strict numeric parse of the trimmed slice. source: :934-935.</summary>
    private static bool IsValidNumber(string slice)
    {
        string t = Trim(slice);
        if (t.Length == 0) return false;
        return long.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out _)
            || decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>FUNCTION NUMVAL of the trimmed slice (integer magnitude for this 2-digit field). source: :836,954.</summary>
    private static long Numval(string slice)
    {
        string t = Trim(slice);
        if (t.Length == 0) return 0;
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d))
            return (long)decimal.Truncate(Math.Abs(d));
        // Fallback: keep digits only.
        long v = 0;
        foreach (char c in t) if (c >= '0' && c <= '9') v = v * 10 + (c - '0');
        return v;
    }

    /// <summary>
    /// Center-justify the trimmed info message into a 40-char field (WS-STRING-OUT), placing it at
    /// <c>(40 - len)/2 + 1</c> (1-based, integer divide). source: :1249-1262.
    /// </summary>
    private static string CenterInto40(string msg)
    {
        string trimmed = Trim(msg);
        int len = trimmed.Length;       // WS-STRING-LEN = LENGTH(TRIM(WS-INFO-MSG)). :1251-1254
        if (len > 40) { trimmed = trimmed.Substring(0, 40); len = 40; }
        int mid = (40 - len) / 2 + 1;   // WS-STRING-MID (1-based). :1255-1257
        var buf = new char[40];
        for (int i = 0; i < 40; i++) buf[i] = ' ';
        for (int i = 0; i < len; i++) buf[mid - 1 + i] = trimmed[i];
        return new string(buf);
    }

    /// <summary>
    /// WS-DISP-SQLCODE PIC ----9 rendering: a 5-char field with a floating minus over the leading four
    /// positions and a guaranteed digit in the last (e.g. 0 -> "    0", -1 -> "   -1", 100 -> "  100").
    /// Exact DB2 SQLCODE values are unavailable from SQLite, so this is characterization-only. source: :68,1484.
    /// </summary>
    private static string EditSqlcode(int sqlcode)
    {
        bool neg = sqlcode < 0;
        string digits = Math.Abs((long)sqlcode).ToString(CultureInfo.InvariantCulture); // always >= 1 char
        string body = neg ? "-" + digits : digits;
        if (body.Length > 5) body = body.Substring(body.Length - 5); // PIC has 5 positions
        return body.PadLeft(5, ' ');
    }

    /// <summary>SQLERRM OF SQLCA — DB2 message text, unavailable from SQLite (characterization-only).</summary>
    private static string Sqlerrm() => "";

    /// <summary>EXEC CICS SYNCPOINT — commit the pending unit of work after a successful write. source: :1557,1606,1637.</summary>
    private void Syncpoint()
    {
        // The repositories share RelationalDb.Connection; a SYNCPOINT commits any open transaction. In the
        // console runtime each repository op auto-commits (no explicit BEGIN), so SYNCPOINT is a no-op here.
        _ = _db;
    }

    // The WS-INVALID-KEY-PRESSED message flag, exposed to HandleDispatch's WHEN test. source: :539,193-194.
    // (Set only in 0001-CHECK-PFKEYS via SetInvalidKeyPressed.)

    // ============================================================================================
    //  Program-private commarea (WS-THIS-PROGCOMMAREA / TTUP-*). source: COTRTUPC.cbl:294-336
    // ============================================================================================

    /// <summary>The TTUP-CHANGE-ACTION states (one-byte flag, the master state). source: :296-327.</summary>
    private enum TtupAction
    {
        NotFetched,            // TTUP-DETAILS-NOT-FETCHED  LOW-VALUES/SPACES. :298-300
        InvalidSearchKeys,     // TTUP-INVALID-SEARCH-KEYS  'K'. :301
        DetailsNotFound,       // TTUP-DETAILS-NOT-FOUND    'X'. :302
        ShowDetails,           // TTUP-SHOW-DETAILS         'S'. :303
        CreateNewRecord,       // TTUP-CREATE-NEW-RECORD    'R'. :305
        ReviewNewRecord,       // TTUP-REVIEW-NEW-RECORD    'V' (declared, never used — FB-5). :306
        ConfirmDelete,         // TTUP-CONFIRM-DELETE       '9'. :310
        StartDelete,           // TTUP-START-DELETE         '8'. :311
        DeleteDone,            // TTUP-DELETE-DONE          '7'. :312
        DeleteFailed,          // TTUP-DELETE-FAILED        '6'. :313
        ChangesNotOk,          // TTUP-CHANGES-NOT-OK       'E'. :318
        ChangesOkNotConfirmed, // TTUP-CHANGES-OK-NOT-CONFIRMED 'N'. :319
        ChangesOkayedLockError,// TTUP-CHANGES-OKAYED-LOCK-ERROR 'L'. :323
        ChangesOkayedButFailed,// TTUP-CHANGES-OKAYED-BUT-FAILED 'F'. :324
        ChangesOkayedAndDone,  // TTUP-CHANGES-OKAYED-AND-DONE 'C'. :326
        ChangesBackedOut,      // TTUP-CHANGES-BACKED-OUT   'B'. :327
    }

    /// <summary>The serializable program-private trailer (TTUP-* state + OLD/NEW details). source: :295-335.</summary>
    private sealed class TtupProgCommArea
    {
        public TtupAction Action = TtupAction.NotFetched; // TTUP-CHANGE-ACTION. :296
        public string OldType = "";       // TTUP-OLD-TTYP-TYPE X(2). :330
        public string OldTypeDesc = "";   // TTUP-OLD-TTYP-TYPE-DESC X(50). :331
        public string NewType = "";       // TTUP-NEW-TTYP-TYPE X(2). :334
        public string NewTypeDesc = "";   // TTUP-NEW-TTYP-TYPE-DESC X(50). :335
    }

    /// <summary>
    /// Cross-turn store for the program-private trailer, keyed by the nav-area image — the console runtime
    /// round-trips only <see cref="CardDemoCommArea"/>, so the TTUP-* bytes survive the per-turn COMMAREA
    /// clone via this content-stable key. Same idiom as COACTUPC. source: spec §6,§8.
    /// </summary>
    private static class ProgStateStore
    {
        private static readonly Dictionary<string, TtupProgCommArea> _store = new(StringComparer.Ordinal);

        public static void Save(CardDemoCommArea ca, TtupProgCommArea s) => _store[Key(ca)] = s;
        public static TtupProgCommArea? Load(CardDemoCommArea ca) => _store.TryGetValue(Key(ca), out var s) ? s : null;
        public static void Forget(CardDemoCommArea ca) => _store.Remove(Key(ca));

        private static string Key(CardDemoCommArea ca) => ca.ToImage();
    }

    /// <summary>
    /// Generic edit-flag state mirroring a PIC X(1) with 88s FLG-*-ISVALID (LOW-VALUES) / -NOT-OK ('0') /
    /// -BLANK ('B'). <see cref="Unset"/> is the post-INITIALIZE SPACES value where NO 88 is true (so
    /// ISVALID is false until explicitly SET). source: :58-61,94-103,353.
    /// </summary>
    private enum Flg { Unset, Valid, NotOk, Blank }

    // ============================================================================================
    //  BMS map builder — CTRTUPA in mapset COTRTUP (24x80). source: COTRTUP.bms / SCREEN_COTRTUP.md
    // ============================================================================================

    /// <summary>The DFHMDI map name. source: SCREEN_COTRTUP.md.</summary>
    public const string MapName = LIT_THISMAP;

    /// <summary>The DFHMSD mapset name. source: SCREEN_COTRTUP.md.</summary>
    public const string MapsetName = LIT_THISMAPSET;

    /// <summary>
    /// Constructs a fresh <see cref="BmsMap"/> for CTRTUPA: every <c>DFHMDF</c> with its exact
    /// Row/Col/Length/attribute/colour/highlight/initial value, in source order. The two input fields are
    /// <c>TRTYPCD</c> (12,26) L2 (IC, cursor) and <c>TRTYDSC</c> (14,26) L50. source: SCREEN_COTRTUP.md.
    /// </summary>
    public static BmsMap BuildBmsMap()
    {
        var fields = new List<ScreenField>
        {
            // --- shared 3-line header ---
            Literal(1, 1, 5, "Tran:", BmsColor.Blue),                                                // (1,1)
            Named("TRNNAME", 1, 7, 4, Protected(BmsAttribute.Fset), BmsColor.Blue),                  // (1,7)
            Named("TITLE01", 1, 21, 40, ProtectedNorm(), BmsColor.Yellow),                           // (1,21)
            Literal(1, 65, 5, "Date:", BmsColor.Blue),                                               // (1,65)
            NamedInit("CURDATE", 1, 71, 8, ProtectedNorm(), BmsColor.Blue, "mm/dd/yy"),              // (1,71)

            Literal(2, 1, 5, "Prog:", BmsColor.Blue),                                                // (2,1)
            Named("PGMNAME", 2, 7, 8, ProtectedNorm(), BmsColor.Blue),                               // (2,7)
            Named("TITLE02", 2, 21, 40, ProtectedNorm(), BmsColor.Yellow),                           // (2,21)
            Literal(2, 65, 5, "Time:", BmsColor.Blue),                                               // (2,65)
            NamedInit("CURTIME", 2, 71, 8, ProtectedNorm(), BmsColor.Blue, "hh:mm:ss"),              // (2,71)

            // --- screen heading ---
            Literal(7, 28, 25, "Maintain Transaction Type", BmsColor.Neutral),                       // (7,28)

            // --- form labels + input fields ---
            Literal(12, 4, 19, "Transaction Type  :", BmsColor.Turquoise),                           // (12,4)
            // TRTYPCD: IC, UNPROT, HILIGHT=UNDERLINE, L2. Cursor (IC) lands here.
            new ScreenField
            {
                Name = "TRTYPCD",
                Row = 12,
                Col = 26,
                Length = 2,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal | BmsAttribute.Ic,
                Color = BmsColor.Default,
                Hilight = BmsHilight.Underline,
            },
            Stopper(12, 29, BmsColor.Default),                                                        // (12,29) L0

            Literal(14, 4, 19, "Description       :", BmsColor.Turquoise),                            // (14,4)
            // TRTYDSC: UNPROT, HILIGHT=UNDERLINE, L50.
            new ScreenField
            {
                Name = "TRTYDSC",
                Row = 14,
                Col = 26,
                Length = 50,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal,
                Color = BmsColor.Default,
                Hilight = BmsHilight.Underline,
            },
            Stopper(14, 77, BmsColor.Default),                                                        // (14,77) L0

            // --- info message line ---
            Named("INFOMSG", 22, 23, 45, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Neutral, BmsHilight.Off), // (22,23)
            Stopper(22, 69, BmsColor.Default),                                                        // (22,69) L0

            // --- error line (BRT RED, FSET) ---
            Named("ERRMSG", 23, 1, 78, Protected(BmsAttribute.Bright | BmsAttribute.Fset), BmsColor.Red), // (23,1)

            // --- function-key legend + conditional keys (DRK until lit) ---
            NamedInit("FKEYS", 24, 1, 21, ProtectedNorm(), BmsColor.Yellow, "ENTER=Process F3=Exit"),    // (24,1)
            NamedInit("FKEY04", 24, 23, 9, Dark(), BmsColor.Yellow, "F4=Delete"),                        // (24,23)
            NamedInit("FKEY05", 24, 33, 8, Dark(), BmsColor.Yellow, "F5=Save"),                          // (24,33)
            NamedInit("FKEY06", 24, 43, 6, Dark(), BmsColor.Yellow, "F6=Add"),                           // (24,43)
            NamedInit("FKEY12", 24, 69, 10, Dark(), BmsColor.Yellow, "F12=Cancel"),                      // (24,69)
        };

        return new BmsMap(MapName, MapsetName, fields);
    }

    private BmsMap BuildMap() => BuildBmsMap();

    // --- field-builder helpers (mirror COACTUPC's) ---
    private static BmsAttribute ProtectedNorm() => BmsAttribute.AutoSkip | BmsAttribute.Normal;
    private static BmsAttribute Protected(BmsAttribute extra) => BmsAttribute.AutoSkip | BmsAttribute.Normal | extra;
    private static BmsAttribute Dark() => BmsAttribute.AutoSkip | BmsAttribute.Dark;

    private static ScreenField Literal(int row, int col, int len, string text, BmsColor color) =>
        new()
        {
            Row = row,
            Col = col,
            Length = len,
            Attribute = BmsAttribute.AutoSkip | BmsAttribute.Normal,
            Color = color,
            Value = text,
        };

    private static ScreenField Named(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    private static ScreenField Named(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, BmsHilight hilight) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = hilight };

    private static ScreenField NamedInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    private static ScreenField Stopper(int row, int col, BmsColor color) =>
        new()
        {
            Row = row,
            Col = col,
            Length = 0,
            Attribute = BmsAttribute.AutoSkip | BmsAttribute.Normal,
            Color = color,
        };
}
