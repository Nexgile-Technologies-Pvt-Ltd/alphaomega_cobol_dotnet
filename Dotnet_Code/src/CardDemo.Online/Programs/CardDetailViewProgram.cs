using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COCRDSLC</c> — the "View Credit Card Detail"
/// inquiry screen (TRANSID <c>CCDL</c>, BMS map <c>CCRDSLA</c> / mapset <c>COCRDSL</c>).
/// </summary>
/// <remarks>
/// <para>
/// COCRDSLC is the detail-view counterpart of the card-list program <c>COCRDLIC</c>. The operator supplies
/// an 11-digit account number and a 16-digit card number on a 24x80 BMS screen; the program validates both
/// filters, reads the CARD master by primary key (card number) and, on a hit, displays the card's embossed
/// name, active status (Y/N), and expiry month + year. It is read-only — it never writes/updates/deletes.
/// It is most often reached via <c>XCTL</c> from the list program (selection criteria pre-validated). It is
/// pseudo-conversational: it re-drives itself via <c>EXEC CICS RETURN TRANSID('CCDL') COMMAREA(...)</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COCRDSLC.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE TRUE</c> / <c>PERFORM</c> / <c>GO TO</c> control flow, the
/// COMMAREA field usage (<see cref="CardDemoCommArea"/> plus the program-private trailer
/// <c>WS-THIS-PROGCOMMAREA</c>), and every faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the CARD master is accessed, by a single primary-key READ
/// (card number): <c>READ FILE('CARDDAT') RIDFLD(WS-CARD-RID-CARDNUM)</c> =
/// <see cref="CardRepository.ReadByKey"/>. No write/rewrite/delete and no browse. The repository
/// FileStatus is mapped to the CICS RESP the COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→
/// NORMAL(0), RecordNotFound('23')→NOTFND(13), anything else→an OTHER/file-error.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — <b>Account number is validated but never used in the read.</b> The READ keys only on card
/// number; the line <c>MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID</c> is commented out. A format-valid
/// account with a valid card number displays that card even if it belongs to a different account. Modeled
/// by reading by card number alone. source: COCRDSLC.cbl:739-740,742-745</item>
/// <item>FB-2 — <b>Dead alt-index path.</b> <c>9150-GETCARD-BYACCT</c> (alt-index <c>CARDAIX</c> read by
/// account) and its message <c>DID-NOT-FIND-ACCT-IN-CARDXREF</c> are unreachable — <c>9150</c> is never
/// PERFORMed. Kept as documented dead code; not wired up. source: COCRDSLC.cbl:779-812</item>
/// <item>FB-3 — <b>PFKey coercion to ENTER.</b> Any AID other than ENTER/PF3 (CLEAR, PA1/PA2, PF1-2,
/// PF4-12) is silently turned into ENTER, so e.g. CLEAR re-validates instead of clearing/exiting.
/// source: COCRDSLC.cbl:297-299</item>
/// <item>FB-4 — <b><c>RECEIVE MAP</c> RESP captured but never checked.</b> <c>2100-RECEIVE-MAP</c> stores
/// RESP/RESP2 but never tests them; a MAPFAIL is not handled distinctly. No check added.
/// source: COCRDSLC.cbl:596-603</item>
/// <item>FB-5 — <b>NOTFND mislabels which filter failed.</b> On card-not-found the program reds BOTH the
/// account and card filters even though only the (card) lookup failed. Preserved.
/// source: COCRDSLC.cbl:755-758</item>
/// <item>FB-6 — <b><c>9100</c> OTHER branch only sets <c>FLG-ACCTFILTER-NOT-OK</c> when
/// <c>WS-RETURN-MSG-OFF</c></b>, so on a hard read error following a prior message the acct-NOT-OK red
/// highlight may not be applied. Guard placement preserved. source: COCRDSLC.cbl:762-766</item>
/// <item>FB-7 — <b>Cursor fallback always lands on <c>ACCTSIDL</c> in the cursor EVALUATE <c>WHEN OTHER</c></b>
/// even after a successful card display, re-positioning the cursor on the account field. Preserved.
/// source: COCRDSLC.cbl:522-523</item>
/// <item>FB-8 — <b>Mapset literal width quirk:</b> <c>LIT-THISMAPSET = 'COCRDSL '</c> (8 chars incl.
/// trailing space) but <c>CCARD-NEXT-MAPSET</c> / <c>CDEMO-LAST-MAPSET</c> are X(7); the trailing space is
/// truncated on MOVE. Comparisons against <c>LIT-CCLISTMAPSET = 'COCRDLI'</c> (7) work; literal widths
/// preserved. source: COCRDSLC.cbl:167-168,175-176,505,565</item>
/// </list>
/// </remarks>
public sealed class CardDetailViewProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-LITERALS — source: COCRDSLC.cbl:162-190
    // =============================================================================================
    private const string ThisProgramId = "COCRDSLC";        // LIT-THISPGM. source: COCRDSLC.cbl:163-164
    private const string ThisTranId = "CCDL";               // LIT-THISTRANID. source: COCRDSLC.cbl:165-166
    private const string ThisMapsetName = "COCRDSL";        // LIT-THISMAPSET. source: COCRDSLC.cbl:167-168 ('COCRDSL ' X(8); MOVEd to X(7) drops the trailing space — FB-8)
    private const string ThisMapName = "CCRDSLA";           // LIT-THISMAP. source: COCRDSLC.cbl:169-170
    private const string CardListProgramId = "COCRDLIC";    // LIT-CCLISTPGM. source: COCRDSLC.cbl:171-172
    private const string CardListTranId = "CCLI";           // LIT-CCLISTTRANID. source: COCRDSLC.cbl:173-174
    private const string CardListMapsetName = "COCRDLI";    // LIT-CCLISTMAPSET. source: COCRDSLC.cbl:175-176 (X(7))
    private const string CardListMapName = "CCRDSLA";       // LIT-CCLISTMAP. source: COCRDSLC.cbl:177-178
    private const string MenuProgramId = "COMEN01C";        // LIT-MENUPGM. source: COCRDSLC.cbl:179-180
    private const string MenuTranId = "CM00";               // LIT-MENUTRANID. source: COCRDSLC.cbl:181-182
    private const string MenuMapsetName = "COMEN01";        // LIT-MENUMAPSET. source: COCRDSLC.cbl:183-184
    private const string MenuMapName = "COMEN1A";           // LIT-MENUMAP. source: COCRDSLC.cbl:185-186
    private const string CardFileName = "CARDDAT ";         // LIT-CARDFILENAME. source: COCRDSLC.cbl:187-188
    private const string CardFileAcctPathName = "CARDAIX "; // LIT-CARDFILENAME-ACCT-PATH. source: COCRDSLC.cbl:189-190 (FB-2: dead alt path)

    // CCDA-TITLE01/02 (COTTL01Y) — shared screen header. source: COTTL01Y.cpy.
    private const string Title01 = "      AWS Mainframe Modernization       ";
    private const string Title02 = "              CardDemo                  ";

    // =============================================================================================
    //  WS-MISC-STORAGE — CICS vars + input/output edit flags. source: COCRDSLC.cbl:36-92
    // =============================================================================================

    // 07 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: COCRDSLC.cbl:41-44
    private int _responseCode; // WS-RESP-CD PIC S9(09) COMP
    private int _reasonCode;   // WS-REAS-CD PIC S9(09) COMP

    // 05 WS-INPUT-FLAG: 88 INPUT-OK='0' / INPUT-ERROR='1' / INPUT-PENDING=LOW-VALUES. source: :51-54
    // Initialised to LOW-VALUES (INPUT-PENDING) by INITIALIZE WS-MISC-STORAGE.
    private char _inputFlag = '\0'; // WS-INPUT-FLAG PIC X
    private bool InputOk => _inputFlag == '0';     // 88 INPUT-OK
    private bool InputError => _inputFlag == '1';  // 88 INPUT-ERROR
    private void SetInputOk() => _inputFlag = '0';
    private void SetInputError() => _inputFlag = '1';

    // 05 WS-EDIT-ACCT-FLAG: 88 FLG-ACCTFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :55-58
    private char _editAcctFlag = '\0'; // WS-EDIT-ACCT-FLAG PIC X
    private bool FlgAcctFilterNotOk => _editAcctFlag == '0';   // 88 FLG-ACCTFILTER-NOT-OK
    private bool FlgAcctFilterIsValid => _editAcctFlag == '1'; // 88 FLG-ACCTFILTER-ISVALID
    private bool FlgAcctFilterBlank => _editAcctFlag == ' ';   // 88 FLG-ACCTFILTER-BLANK
    private void SetFlgAcctFilterNotOk() => _editAcctFlag = '0';
    private void SetFlgAcctFilterIsValid() => _editAcctFlag = '1';
    private void SetFlgAcctFilterBlank() => _editAcctFlag = ' ';

    // 05 WS-EDIT-CARD-FLAG: 88 FLG-CARDFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :59-62
    private char _editCardFlag = '\0'; // WS-EDIT-CARD-FLAG PIC X
    private bool FlgCardFilterNotOk => _editCardFlag == '0';   // 88 FLG-CARDFILTER-NOT-OK
    private bool FlgCardFilterIsValid => _editCardFlag == '1'; // 88 FLG-CARDFILTER-ISVALID
    private bool FlgCardFilterBlank => _editCardFlag == ' ';   // 88 FLG-CARDFILTER-BLANK
    private void SetFlgCardFilterNotOk() => _editCardFlag = '0';
    private void SetFlgCardFilterIsValid() => _editCardFlag = '1';
    private void SetFlgCardFilterBlank() => _editCardFlag = ' ';

    // 05 WS-RETURN-FLAG: 88 WS-RETURN-FLAG-OFF=LOW-VALUES / ON='1'. source: :63-65 (declared; not used on live path)
    private char _returnFlag = '\0'; // WS-RETURN-FLAG PIC X

    // 05 WS-PFK-FLAG: 88 PFK-VALID='0' / PFK-INVALID='1'. source: COCRDSLC.cbl:66-68
    private char _pfkFlag = '\0'; // WS-PFK-FLAG PIC X
    private bool PfkInvalid => _pfkFlag == '1';   // 88 PFK-INVALID
    private void SetPfkValid() => _pfkFlag = '0';
    private void SetPfkInvalid() => _pfkFlag = '1';

    // =============================================================================================
    //  CICS-OUTPUT-EDIT-VARS — expiry date REDEFINES scratch. source: COCRDSLC.cbl:72-92
    // =============================================================================================
    // CARD-EXPIRAION-DATE-X X(10) redefined as YYYY(4) '-'(1) MM(2) '-'(1) DD(2). The slices read from the
    // CARD record's expiration_date TEXT. (CARD-ACCT-ID/CVV-CD edit vars are declared but not used here.)
    private string _cardExpirationDateX = ""; // CARD-EXPIRAION-DATE-X X(10)
    private string CardExpiryYear  => Slice(_cardExpirationDateX, 0, 4);  // CARD-EXPIRY-YEAR  X(4) (chars 1-4)
    private string CardExpiryMonth => Slice(_cardExpirationDateX, 5, 2);  // CARD-EXPIRY-MONTH X(2) (chars 6-7)
    private string CardExpiryDay   => Slice(_cardExpirationDateX, 8, 2);  // CARD-EXPIRY-DAY   X(2) (chars 9-10)

    // =============================================================================================
    //  WS-CARD-RID — VSAM key area. source: COCRDSLC.cbl:97-101
    // =============================================================================================
    // 10 WS-CARD-RID-CARDNUM X(16) ; 10 WS-CARD-RID-ACCT-ID 9(11) (redef -X X(11)).
    private string _cardRidCardNum = ""; // WS-CARD-RID-CARDNUM X(16)
    private long _cardRidAcctId;          // WS-CARD-RID-ACCT-ID 9(11) (used only by dead 9150 — FB-2)

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE group. source: COCRDSLC.cbl:102-121
    // =============================================================================================
    private string _errorOpname = "        "; // ERROR-OPNAME X(8)
    private string _errorFile = "         ";  // ERROR-FILE   X(9)
    private string _errorResp = "          "; // ERROR-RESP   X(10)
    private string _errorResp2 = "          "; // ERROR-RESP2  X(10)

    // =============================================================================================
    //  WS-INFO-MSG X(40) — 88 levels. source: COCRDSLC.cbl:126-132
    // =============================================================================================
    // 88 WS-NO-INFO-MESSAGE = SPACES/LOW-VALUES ;
    // 88 FOUND-CARDS-FOR-ACCOUNT = '   Displaying requested details' (3 leading spaces — preserve) ;
    // 88 WS-PROMPT-FOR-INPUT    = 'Please enter Account and Card Number'.
    private string _infoMsg = ""; // WS-INFO-MSG X(40)
    private const string FoundCardsForAccountMsg = "   Displaying requested details"; // 88 FOUND-CARDS-FOR-ACCOUNT. source: :129-130
    private const string PromptForInputMsg = "Please enter Account and Card Number"; // 88 WS-PROMPT-FOR-INPUT. source: :131-132
    private bool HasNoInfoMessage => IsSpacesOrLowValues(_infoMsg);  // 88 WS-NO-INFO-MESSAGE
    // FOUND-CARDS-FOR-ACCOUNT doubles as the "a card was just read" signal (set in 9100). source: :754
    private bool FoundCardsForAccount => _infoMsg == FoundCardsForAccountMsg;
    private void SetHasNoInfoMessage() => _infoMsg = "";             // SET WS-NO-INFO-MESSAGE (SPACES)
    private void SetFoundCardsForAccount() => _infoMsg = FoundCardsForAccountMsg;
    private void SetWsPromptForInput() => _infoMsg = PromptForInputMsg;

    // =============================================================================================
    //  WS-RETURN-MSG X(75) — 88-level message constants. source: COCRDSLC.cbl:134-158
    // =============================================================================================
    // 88 WS-RETURN-MSG-OFF = SPACES. The live-path messages are listed below; the remaining 88s
    // (WS-EXIT-MESSAGE, SEARCHED-*, DID-NOT-FIND-ACCT-IN-CARDXREF, XREF-READ-ERROR, CODING-TO-BE-DONE)
    // are defined but never SET on the live path — kept as constants, never emitted. source: §202 (spec)
    private string _returnMsg = ""; // WS-RETURN-MSG X(75)
    private const string ExitMessage = "PF03 pressed.Exiting              ";              // 88 WS-EXIT-MESSAGE. source: :136-137 (dead on live path)
    private const string PromptForAcctMsg = "Account number not provided";                  // 88 WS-PROMPT-FOR-ACCT. source: :138-139
    private const string PromptForCardMsg = "Card number not provided";                     // 88 WS-PROMPT-FOR-CARD. source: :140-141
    private const string NoSearchCriteriaReceivedMsg = "No input received";                   // 88 NO-SEARCH-CRITERIA-RECEIVED. source: :142-143
    private const string SearchedAcctZeroesMsg = "Account number must be a non zero 11 digit number"; // 88 SEARCHED-ACCT-ZEROES. source: :144-145 (dead)
    private const string SearchedAcctNotNumericMsg = "Account number must be a non zero 11 digit number"; // 88 SEARCHED-ACCT-NOT-NUMERIC. source: :146-147 (dead)
    private const string SearchedCardNotNumericMsg = "Card number if supplied must be a 16 digit number"; // 88 SEARCHED-CARD-NOT-NUMERIC. source: :148-149 (dead)
    private const string DidNotFindAcctInCardXrefMsg = "Did not find this account in cards database";   // 88 DID-NOT-FIND-ACCT-IN-CARDXREF. source: :151-152 (dead — only set in 9150)
    private const string DidNotFindAcctCardComboMsg = "Did not find cards for this search condition";    // 88 DID-NOT-FIND-ACCTCARD-COMBO. source: :153-154
    private const string XrefReadErrorMsg = "Error reading Card Data File";                    // 88 XREF-READ-ERROR. source: :155-156 (dead)
    private const string CodingToBeDoneMsg = "Looks Good.... so far";                         // 88 CODING-TO-BE-DONE. source: :157-158 (dead)
    private bool ReturnMessageIsBlank => IsSpaces(_returnMsg) || _returnMsg.Length == 0; // 88 WS-RETURN-MSG-OFF (SPACES)
    private void SetReturnMessageIsBlank() => _returnMsg = "";  // SET WS-RETURN-MSG-OFF (SPACES). source: :264
    private void SetWsPromptForAcct() => _returnMsg = PromptForAcctMsg;
    private void SetWsPromptForCard() => _returnMsg = PromptForCardMsg;
    private void SetNoSearchCriteriaReceived() => _returnMsg = NoSearchCriteriaReceivedMsg;
    private void SetDidNotFindAcctcardCombo() => _returnMsg = DidNotFindAcctCardComboMsg;

    // WS-LONG-MSG X(500) — SEND-LONG-TEXT debug only; never referenced. source: COCRDSLC.cbl:125,820-833

    // =============================================================================================
    //  CC-WORK-AREA (CVCRD01Y): filter inputs + AID flags. source: CVCRD01Y.cpy
    // =============================================================================================
    // CC-ACCT-ID X(11) (redef CC-ACCT-ID-N 9(11)); CC-CARD-NUM X(16) (redef CC-CARD-NUM-N 9(16)).
    // LOW-VALUES sentinel modeled as the 11/16-NUL string; SPACES as blanks; otherwise the keyed value.
    private string _ccAcctId = "";  // CC-ACCT-ID  X(11)
    private string _ccCardNum = ""; // CC-CARD-NUM X(16)

    // The CARD-RECORD currently read (CVACT02Y). source: COCRDSLC.cbl:234,746
    private Card? _cardRecord;
    private string CardEmbossedName => _cardRecord?.EmbossedName ?? "";     // CARD-EMBOSSED-NAME X(50)
    private string CardExpiraionDate => _cardRecord?.ExpirationDate ?? "";  // CARD-EXPIRAION-DATE X(10)
    private string CardActiveStatus => _cardRecord?.ActiveStatus ?? "";     // CARD-ACTIVE-STATUS X(1)

    // CCARD-AID — set by YYYY-STORE-PFKEY, then remapped by the validity gate. source: CVCRD01Y.cpy; :291-299
    private CcardAid _ccardAid = CcardAid.None;
    private bool CcardAidEnter => _ccardAid == CcardAid.Enter; // 88 CCARD-AID-ENTER
    private bool CcardAidPfk03 => _ccardAid == CcardAid.Pfk03; // 88 CCARD-AID-PFK03
    private void SetCcardAidEnter() => _ccardAid = CcardAid.Enter;

    // =============================================================================================
    //  WS-THIS-PROGCOMMAREA — program-private commarea trailer. source: COCRDSLC.cbl:200-203
    // =============================================================================================
    // CA-CALL-CONTEXT = CA-FROM-PROGRAM X(8) + CA-FROM-TRANID X(4) (12 bytes). INITIALIZEd, parsed,
    // written back, but never read in logic — informational only.
    private string _caFromProgram = "";
    private string _caFromTranid = "";

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map. source: COCRDSLC.cbl:198,205
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The CARD repository is created from
    /// <c>db.Connection</c> inside the read paragraph (no DB is opened here).
    /// </summary>
    public CardDetailViewProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public CardDetailViewProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => ThisProgramId; // PROGRAM-ID. COCRDSLC. source: COCRDSLC.cbl:23-24

    /// <inheritdoc/>
    public string TransId => ThisTranId;  // CSD: CCDL -> COCRDSLC. source: CSD_TRANSACTIONS.md:74; cbl:165-166

    // =============================================================================================
    //  0000-MAIN — source: COCRDSLC.cbl:248-407
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE). source: :250-252 (modeled as a try/catch wrapper).
        try
        {
            Main(ctx);
        }
        catch (Exception)
        {
            // ABEND-ROUTINE: send ABEND-DATA and ABEND ABCODE('9999'). source: :857-878.
            AbendRoutine(ctx);
        }
    }

    private void Main(CicsContext ctx) // COBOL paragraph: 0000-MAIN
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE re-initialised per turn).
        _map = BuildMap();

        // INITIALIZE CC-WORK-AREA WS-MISC-STORAGE WS-COMMAREA. source: :254-256
        // (Working-storage starts at COBOL VALUE/SPACES/LOW-VALUES; the handler instance is fresh.)
        _ = _returnFlag;                 // WS-RETURN-FLAG declared; unused on live path. source: :63-65
        _ = CardFileAcctPathName;    // FB-2: declared, referenced only by dead 9150.

        // MOVE LIT-THISTRANID TO WS-TRANID. source: :260 (WS-TRANID is informational only.)

        // SET WS-RETURN-MSG-OFF TO TRUE. source: :264
        SetReturnMessageIsBlank();

        // Store passed data if any. source: :268-279
        //   IF EIBCALEN = 0
        //   OR (CDEMO-FROM-PROGRAM = LIT-MENUPGM AND NOT CDEMO-PGM-REENTER)
        //      INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA
        //   ELSE
        //      MOVE DFHCOMMAREA(1:LEN OF CARDDEMO-COMMAREA) TO CARDDEMO-COMMAREA
        //      MOVE DFHCOMMAREA(LEN+1:LEN OF WS-THIS-PROGCOMMAREA) TO WS-THIS-PROGCOMMAREA
        // The COBOL evaluates this test BEFORE loading DFHCOMMAREA into CARDDEMO-COMMAREA (the load is in
        // the ELSE at :273-278). At the IF (:268-270) CARDDEMO-COMMAREA is still the INITIALIZEd (SPACES)
        // working-storage copy, so CDEMO-FROM-PROGRAM is blank and the second disjunct
        // (CDEMO-FROM-PROGRAM = LIT-MENUPGM AND NOT CDEMO-PGM-REENTER) is ALWAYS FALSE — only EIBCALEN = 0
        // takes the INITIALIZE path. (Reading the PASSED FROM-PROGRAM here would wrongly wipe a carried
        // COMMAREA on menu entry.)
        bool freshCommarea = ctx.EibCalen == 0;

        if (freshCommarea)
        {
            // INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA. source: :271-272
            _commArea = new CardDemoCommArea();
            _caFromProgram = "";
            _caFromTranid = "";
        }
        else
        {
            // Copy the carried COMMAREA + trailer. source: :274-278
            _commArea = ctx.CommArea!;
            _caFromProgram = ""; // trailer is not transported by the typed COMMAREA; INITIALIZE-equivalent.
            _caFromTranid = "";
        }

        // PERFORM YYYY-STORE-PFKEY. source: :284-285 — EIBAID -> CCARD-AID-*.
        StorePfKey(ctx);

        // Remap PFkeys: only ENTER / PF3 valid; force everything else to ENTER. source: :291-299
        SetPfkInvalid();                    // SET PFK-INVALID TO TRUE. source: :291
        if (CcardAidEnter || CcardAidPfk03) // IF CCARD-AID-ENTER OR CCARD-AID-PFK03. source: :292-293
            SetPfkValid();                  //    SET PFK-VALID TO TRUE. source: :294
        if (PfkInvalid)                     // IF PFK-INVALID. source: :297
            SetCcardAidEnter();             //    SET CCARD-AID-ENTER TO TRUE. source: :298 (FB-3 coercion)

        // EVALUATE TRUE — decide what to do based on inputs received. source: :304-381
        if (CcardAidPfk03)
        {
            // WHEN CCARD-AID-PFK03 — XCTL to calling program or main menu. source: :305-334
            // IF CDEMO-FROM-TRANID = LOW-VALUES OR SPACES -> CM00 ELSE FROM-TRANID. source: :309-314
            if (IsLowValuesOrSpaces(_commArea.FromTranId))
                _commArea.ToTranId = MenuTranId;
            else
                _commArea.ToTranId = _commArea.FromTranId;

            // IF CDEMO-FROM-PROGRAM = LOW-VALUES OR SPACES -> COMEN01C ELSE FROM-PROGRAM. source: :316-321
            if (IsLowValuesOrSpaces(_commArea.FromProgram))
                _commArea.ToProgram = MenuProgramId;
            else
                _commArea.ToProgram = _commArea.FromProgram;

            _commArea.FromTranId = ThisTranId;   // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :323
            _commArea.FromProgram = ThisProgramId;     // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :324

            _commArea.SetUser();                     // SET CDEMO-USRTYP-USER TO TRUE. source: :326
            _commArea.SetFirstEntry();               // SET CDEMO-PGM-ENTER   TO TRUE. source: :327
            _commArea.LastMapSet = ThisMapsetName;   // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET (X(7); 'COCRDSL '->'COCRDSL'). source: :328 (FB-8)
            _commArea.LastMap = ThisMapName;         // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :329

            // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :331-334
            ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
            return;
        }
        else if (_commArea.IsFirstEntry && _commArea.FromProgram.TrimEnd() == CardListProgramId)
        {
            // WHEN CDEMO-PGM-ENTER AND CDEMO-FROM-PROGRAM = LIT-CCLISTPGM. source: :339-348
            //   Coming from credit card list screen — selection criteria already validated.
            SetInputOk();                                    // SET INPUT-OK TO TRUE. source: :341
            _ccAcctId = Zoned(_commArea.AcctId, 11);         // MOVE CDEMO-ACCT-ID  TO CC-ACCT-ID-N. source: :342
            _ccCardNum = Zoned(_commArea.CardNum, 16);       // MOVE CDEMO-CARD-NUM TO CC-CARD-NUM-N. source: :343
            ReadData(ctx);                               // PERFORM 9000-READ-DATA. source: :344-345
            SendMap(ctx);                                // PERFORM 1000-SEND-MAP. source: :346-347
            CommonReturn(ctx);                               // GO TO COMMON-RETURN. source: :348
            return;
        }
        else if (_commArea.IsFirstEntry)
        {
            // WHEN CDEMO-PGM-ENTER — coming from some other context; gather selection criteria. source: :349-356
            SendMap(ctx);   // PERFORM 1000-SEND-MAP. source: :354-355
            CommonReturn(ctx);  // GO TO COMMON-RETURN. source: :356
            return;
        }
        else if (_commArea.IsReenter)
        {
            // WHEN CDEMO-PGM-REENTER — user pressed a key on our screen. source: :357-371
            ProcessInputs(ctx); // PERFORM 2000-PROCESS-INPUTS. source: :358-359
            if (InputError)         // IF INPUT-ERROR. source: :360
            {
                SendMap(ctx);   // PERFORM 1000-SEND-MAP. source: :361-362
                CommonReturn(ctx);  // GO TO COMMON-RETURN. source: :363
                return;
            }
            else
            {
                ReadData(ctx);  // PERFORM 9000-READ-DATA. source: :365-366
                SendMap(ctx);   // PERFORM 1000-SEND-MAP. source: :367-368
                CommonReturn(ctx);  // GO TO COMMON-RETURN. source: :369
                return;
            }
        }
        else
        {
            // WHEN OTHER — unexpected data scenario. source: :373-380
            // MOVE LIT-THISPGM TO ABEND-CULPRIT; MOVE '0001' TO ABEND-CODE; MOVE SPACES TO ABEND-REASON.
            // MOVE 'UNEXPECTED DATA SCENARIO' TO WS-RETURN-MSG. source: :377-378
            _returnMsg = "UNEXPECTED DATA SCENARIO";
            SendPlainText(ctx);     // PERFORM SEND-PLAIN-TEXT. source: :379-380 (SEND TEXT + RETURN, terminal end).
            return;
        }

        // NOTE: the fall-through guard below the EVALUATE (lines :386-391) is unreachable from the structured
        // dispatch above because every WHEN ends with GO TO COMMON-RETURN / XCTL / RETURN. Kept for fidelity:
        //   IF INPUT-ERROR MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG; PERFORM 1000-SEND-MAP; GO TO COMMON-RETURN.
    }

    // =============================================================================================
    //  COMMON-RETURN — source: COCRDSLC.cbl:394-407
    // =============================================================================================
    private void CommonReturn(CicsContext ctx)
    {
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. source: :395
        // (CCARD-ERROR-MSG is staged into the map's ERRMSG by 1200-SETUP-SCREEN-VARS already; this final
        //  move keeps the COMMAREA-side error field in sync. No observable screen change at this point.)

        // Reassemble WS-COMMAREA = [CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA] and RETURN. source: :397-406
        // The typed CardDemoCommArea carries the first segment; the 12-byte CA-CALL-CONTEXT trailer is
        // informational and not part of the typed image.
        _ = _caFromProgram;
        _ = _caFromTranid;

        // EXEC CICS RETURN TRANSID(LIT-THISTRANID) COMMAREA(WS-COMMAREA) LENGTH(LENGTH OF WS-COMMAREA).
        ctx.ReturnTransId(ThisTranId, _commArea); // source: :402-406
    }

    // =============================================================================================
    //  0000-MAIN-EXIT — source: COCRDSLC.cbl:408-410
    // =============================================================================================
    private static void MainExit() { /* EXIT. source: :408-410 */ } // COBOL paragraph: 0000-MAIN-EXIT

    // =============================================================================================
    //  1000-SEND-MAP — source: COCRDSLC.cbl:412-425
    // =============================================================================================
    private void SendMap(CicsContext ctx) // COBOL paragraph: 1000-SEND-MAP
    {
        ScreenInit(ctx);        // PERFORM 1100-SCREEN-INIT. source: :413-414
        SetupScreenVars(ctx);   // PERFORM 1200-SETUP-SCREEN-VARS. source: :415-416
        SetupScreenAttrs();     // PERFORM 1300-SETUP-SCREEN-ATTRS. source: :417-418
        SendScreen(ctx);  // PERFORM 1400-SEND-SCREEN. source: :419-420
    }

    // =============================================================================================
    //  1100-SCREEN-INIT — source: COCRDSLC.cbl:427-455
    // =============================================================================================
    private void ScreenInit(CicsContext ctx) // COBOL paragraph: 1100-SCREEN-INIT
    {
        // MOVE LOW-VALUES TO CCRDSLAO. source: :428
        MoveLowValuesToMapOut();

        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA (twice). source: :430,437
        DateTime now = ctx.Clock.Now;

        // MOVE CCDA-TITLE01/02, LIT-THISTRANID, LIT-THISPGM. source: :432-435
        _map.Field("TITLE01").SetValue(Title01);   // MOVE CCDA-TITLE01 TO TITLE01O. source: :432
        _map.Field("TITLE02").SetValue(Title02);   // MOVE CCDA-TITLE02 TO TITLE02O. source: :433
        _map.Field("TRNNAME").SetValue(ThisTranId); // MOVE LIT-THISTRANID TO TRNNAMEO. source: :434
        _map.Field("PGMNAME").SetValue(ThisProgramId);    // MOVE LIT-THISPGM    TO PGMNAMEO. source: :435

        // CURDATEO = mm-dd-yy: WS-CURDATE-MM/DD + WS-CURDATE-YEAR(3:2). source: :439-443
        string mm = Two(now.Month);
        string dd = Two(now.Day);
        string yy = Four(now.Year).Substring(2, 2);
        _map.Field("CURDATE").SetValue($"{mm}/{dd}/{yy}"); // WS-CURDATE-MM-DD-YY -> CURDATEO. source: :443

        // CURTIMEO = hh-mm-ss: WS-CURTIME-HH/MM/SS. source: :445-449
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}");
    }

    // =============================================================================================
    //  1200-SETUP-SCREEN-VARS — source: COCRDSLC.cbl:457-501
    // =============================================================================================
    private void SetupScreenVars(CicsContext ctx) // COBOL paragraph: 1200-SETUP-SCREEN-VARS
    {
        // INITIALIZE SEARCH CRITERIA. source: :458-486
        if (ctx.EibCalen == 0)
        {
            SetWsPromptForInput(); // SET WS-PROMPT-FOR-INPUT TO TRUE. source: :460
        }
        else
        {
            // Account id echo: LOW-VALUES if CDEMO-ACCT-ID = 0, else CC-ACCT-ID. source: :462-466
            if (_commArea.AcctId == 0)
                _map.Field("ACCTSID").SetValue("", setMdt: false);        // MOVE LOW-VALUES TO ACCTSIDO. source: :463
            else
                _map.Field("ACCTSID").SetValue(_ccAcctId, setMdt: false); // MOVE CC-ACCT-ID TO ACCTSIDO. source: :465

            // Card num echo: LOW-VALUES if CDEMO-CARD-NUM = 0, else CC-CARD-NUM. source: :468-472
            if (_commArea.CardNum == 0)
                _map.Field("CARDSID").SetValue("", setMdt: false);        // MOVE LOW-VALUES TO CARDSIDO. source: :469
            else
                _map.Field("CARDSID").SetValue(_ccCardNum, setMdt: false); // MOVE CC-CARD-NUM TO CARDSIDO. source: :471

            // IF FOUND-CARDS-FOR-ACCOUNT — paint the read card detail. source: :474-485
            if (FoundCardsForAccount)
            {
                _map.Field("CRDNAME").SetValue(CardEmbossedName, setMdt: false); // MOVE CARD-EMBOSSED-NAME TO CRDNAMEO. source: :475-476

                // MOVE CARD-EXPIRAION-DATE TO CARD-EXPIRAION-DATE-X (parse YYYY-MM-DD via REDEFINES). source: :477-478
                _cardExpirationDateX = PadX(CardExpiraionDate, 10);

                _map.Field("EXPMON").SetValue(CardExpiryMonth, setMdt: false);  // MOVE CARD-EXPIRY-MONTH TO EXPMONO. source: :480
                _map.Field("EXPYEAR").SetValue(CardExpiryYear, setMdt: false);  // MOVE CARD-EXPIRY-YEAR  TO EXPYEARO. source: :482
                _ = CardExpiryDay; // CARD-EXPIRY-DAY parsed by the REDEFINES but not displayed.

                _map.Field("CRDSTCD").SetValue(CardActiveStatus, setMdt: false); // MOVE CARD-ACTIVE-STATUS TO CRDSTCDO. source: :484
            }
        }

        // SETUP MESSAGE: IF WS-NO-INFO-MESSAGE SET WS-PROMPT-FOR-INPUT. source: :490-492
        if (HasNoInfoMessage)
            SetWsPromptForInput();

        // MOVE WS-RETURN-MSG TO ERRMSGO. source: :494
        _map.Field("ERRMSG").SetValue(_returnMsg, setMdt: false);

        // MOVE WS-INFO-MSG TO INFOMSGO. source: :496
        _map.Field("INFOMSG").SetValue(_infoMsg, setMdt: false);
    }

    // =============================================================================================
    //  1300-SETUP-SCREEN-ATTRS — source: COCRDSLC.cbl:502-560
    // =============================================================================================
    private void SetupScreenAttrs() // COBOL paragraph: 1300-SETUP-SCREEN-ATTRS
    {
        ScreenField acctsid = _map.Field("ACCTSID");
        ScreenField cardsid = _map.Field("CARDSID");

        // PROTECT OR UNPROTECT BASED ON CONTEXT. source: :504-512
        //   IF CDEMO-LAST-MAPSET = 'COCRDLI' AND CDEMO-FROM-PROGRAM = 'COCRDLIC' -> DFHBMPRF (protect)
        //   ELSE DFHBMFSE (unprotect, MDT forced).
        bool cameFromList =
            _commArea.LastMapSet.TrimEnd() == CardListMapsetName
            && _commArea.FromProgram.TrimEnd() == CardListProgramId;

        if (cameFromList)
        {
            // MOVE DFHBMPRF TO ACCTSIDA / CARDSIDA (protect + FSET). source: :507-508
            acctsid.AttributeOverride = BmsAttribute.Protected | BmsAttribute.Fset;
            cardsid.AttributeOverride = BmsAttribute.Protected | BmsAttribute.Fset;
        }
        else
        {
            // MOVE DFHBMFSE TO ACCTSIDA / CARDSIDA (unprotect + FSET). source: :510-511
            acctsid.AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset;
            cardsid.AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset;
        }

        // POSITION CURSOR — EVALUATE TRUE. source: :514-524
        if (FlgAcctFilterNotOk || FlgAcctFilterBlank)
        {
            acctsid.CursorLength = -1;  // MOVE -1 TO ACCTSIDL. source: :516-518
        }
        else if (FlgCardFilterNotOk || FlgCardFilterBlank)
        {
            cardsid.CursorLength = -1;  // MOVE -1 TO CARDSIDL. source: :519-521
        }
        else
        {
            // WHEN OTHER — FB-7: always lands on ACCTSIDL even after a successful card display. source: :522-523
            acctsid.CursorLength = -1;  // MOVE -1 TO ACCTSIDL. source: :523
        }

        // SETUP COLOR. source: :526-557
        if (cameFromList)
        {
            // MOVE DFHDFCOL TO ACCTSIDC / CARDSIDC (default colour). source: :529-530
            acctsid.ColorOverride = BmsColor.Default;
            cardsid.ColorOverride = BmsColor.Default;
        }

        if (FlgAcctFilterNotOk)
            acctsid.ColorOverride = BmsColor.Red; // MOVE DFHRED TO ACCTSIDC. source: :533-535

        if (FlgCardFilterNotOk)
            cardsid.ColorOverride = BmsColor.Red; // MOVE DFHRED TO CARDSIDC. source: :537-539

        // IF FLG-ACCTFILTER-BLANK AND CDEMO-PGM-REENTER -> ACCTSIDO='*' + DFHRED. source: :541-545
        if (FlgAcctFilterBlank && _commArea.IsReenter)
        {
            acctsid.SetValue("*", setMdt: false); // MOVE '*'    TO ACCTSIDO. source: :543
            acctsid.ColorOverride = BmsColor.Red; // MOVE DFHRED TO ACCTSIDC. source: :544
        }

        // IF FLG-CARDFILTER-BLANK AND CDEMO-PGM-REENTER -> CARDSIDO='*' + DFHRED. source: :547-551
        if (FlgCardFilterBlank && _commArea.IsReenter)
        {
            cardsid.SetValue("*", setMdt: false); // MOVE '*'    TO CARDSIDO. source: :549
            cardsid.ColorOverride = BmsColor.Red; // MOVE DFHRED TO CARDSIDC. source: :550
        }

        // INFOMSG colour: WS-NO-INFO-MESSAGE -> DFHBMDAR (dark) else DFHNEUTR. source: :553-557
        ScreenField infomsg = _map.Field("INFOMSG");
        if (HasNoInfoMessage)
            infomsg.AttributeOverride = infomsg.Attribute | BmsAttribute.Dark; // MOVE DFHBMDAR TO INFOMSGC. source: :554
        else
            infomsg.ColorOverride = BmsColor.Neutral; // MOVE DFHNEUTR TO INFOMSGC. source: :556
    }

    // =============================================================================================
    //  1400-SEND-SCREEN — source: COCRDSLC.cbl:563-580
    // =============================================================================================
    private void SendScreen(CicsContext ctx) // COBOL paragraph: 1400-SEND-SCREEN
    {
        // MOVE LIT-THISMAPSET TO CCARD-NEXT-MAPSET; MOVE LIT-THISMAP TO CCARD-NEXT-MAP. source: :565-566
        // (CCARD-NEXT-* are staging fields; mapset/map are passed explicitly to SendMap below.)
        // SET CDEMO-PGM-REENTER TO TRUE — next turn is a re-entry. source: :567
        _commArea.SetReenter();

        // EXEC CICS SEND MAP(CCARD-NEXT-MAP) MAPSET(CCARD-NEXT-MAPSET) FROM(CCRDSLAO) CURSOR ERASE FREEKB
        //   RESP(WS-RESP-CD). source: :569-576
        ctx.SendMap(ThisMapName, ThisMapsetName, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
        _responseCode = (int)Resp.Normal;
    }

    // =============================================================================================
    //  2000-PROCESS-INPUTS — source: COCRDSLC.cbl:582-595
    // =============================================================================================
    private void ProcessInputs(CicsContext ctx) // COBOL paragraph: 2000-PROCESS-INPUTS
    {
        ReceiveMap(ctx);  // PERFORM 2100-RECEIVE-MAP. source: :583-584
        EditMapInputs();  // PERFORM 2200-EDIT-MAP-INPUTS. source: :585-586
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG ; LIT-THISPGM/MAPSET/MAP TO CCARD-NEXT-PROG/MAPSET/MAP.
        // (CCARD-* are staging fields; the COMMAREA-side error is rendered via ERRMSGO at SEND time.) source: :587-590
    }

    // =============================================================================================
    //  2100-RECEIVE-MAP — source: COCRDSLC.cbl:596-607
    // =============================================================================================
    private void ReceiveMap(CicsContext ctx) // COBOL paragraph: 2100-RECEIVE-MAP
    {
        // EXEC CICS RECEIVE MAP(LIT-THISMAP) MAPSET(LIT-THISMAPSET) INTO(CCRDSLAI) RESP RESP2. source: :597-602
        ctx.ReceiveMap(ThisMapName, ThisMapsetName, _map);
        // FB-4: RESP/RESP2 are captured into WS-RESP-CD/WS-REAS-CD but never tested.
        _responseCode = (int)Resp.Normal;
        _reasonCode = 0;
    }

    // =============================================================================================
    //  2200-EDIT-MAP-INPUTS — source: COCRDSLC.cbl:608-645
    // =============================================================================================
    private void EditMapInputs() // COBOL paragraph: 2200-EDIT-MAP-INPUTS
    {
        SetInputOk();              // SET INPUT-OK TO TRUE. source: :610
        SetFlgCardFilterIsValid(); // SET FLG-CARDFILTER-ISVALID TO TRUE. source: :611
        SetFlgAcctFilterIsValid(); // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :612

        // REPLACE * WITH LOW-VALUES: IF ACCTSIDI = '*' OR SPACES -> CC-ACCT-ID = LOW-VALUES. source: :615-620
        string acctFilterInput = _map.Field("ACCTSID").Value; // ACCTSIDI OF CCRDSLAI (raw keyed value)
        if (acctFilterInput == "*" || IsSpaces(acctFilterInput) || string.IsNullOrEmpty(acctFilterInput))
            _ccAcctId = LowValues(11);            // MOVE LOW-VALUES TO CC-ACCT-ID. source: :617
        else
            _ccAcctId = PadX(acctFilterInput, 11);       // MOVE ACCTSIDI TO CC-ACCT-ID. source: :619

        // IF CARDSIDI = '*' OR SPACES -> CC-CARD-NUM = LOW-VALUES else CARDSIDI. source: :622-627
        string cardFilterInput = _map.Field("CARDSID").Value; // CARDSIDI OF CCRDSLAI
        if (cardFilterInput == "*" || IsSpaces(cardFilterInput) || string.IsNullOrEmpty(cardFilterInput))
            _ccCardNum = LowValues(16);           // MOVE LOW-VALUES TO CC-CARD-NUM. source: :624
        else
            _ccCardNum = PadX(cardFilterInput, 16);      // MOVE CARDSIDI TO CC-CARD-NUM. source: :626

        // INDIVIDUAL FIELD EDITS. source: :629-634
        EditAccount(); // PERFORM 2210-EDIT-ACCOUNT. source: :630-631
        EditCard();    // PERFORM 2220-EDIT-CARD. source: :633-634

        // CROSS FIELD EDITS: IF FLG-ACCTFILTER-BLANK AND FLG-CARDFILTER-BLANK -> NO-SEARCH-CRITERIA. source: :637-640
        if (FlgAcctFilterBlank && FlgCardFilterBlank)
            SetNoSearchCriteriaReceived();
    }

    // =============================================================================================
    //  2210-EDIT-ACCOUNT — source: COCRDSLC.cbl:647-683
    // =============================================================================================
    private void EditAccount() // COBOL paragraph: 2210-EDIT-ACCOUNT
    {
        SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :648

        // Not supplied: IF CC-ACCT-ID = LOW-VALUES OR SPACES OR CC-ACCT-ID-N = ZEROS. source: :651-661
        if (IsLowValues(_ccAcctId) || IsSpaces(_ccAcctId) || IsZeroesNum(_ccAcctId))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :654
            SetFlgAcctFilterBlank();  // SET FLG-ACCTFILTER-BLANK TO TRUE. source: :655
            if (ReturnMessageIsBlank)       // IF WS-RETURN-MSG-OFF. source: :656
                SetWsPromptForAcct(); // SET WS-PROMPT-FOR-ACCT TO TRUE. source: :657
            _commArea.AcctId = 0;     // MOVE ZEROES TO CDEMO-ACCT-ID. source: :659
            return;                   // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :660
        }

        // Not numeric / not 11 characters: IF CC-ACCT-ID IS NOT NUMERIC. source: :665-678
        if (!IsNumericX(_ccAcctId, 11))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :666
            SetFlgAcctFilterNotOk();  // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :667
            if (ReturnMessageIsBlank)       // IF WS-RETURN-MSG-OFF. source: :668
                _returnMsg = "ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER"; // source: :669-671
            _commArea.AcctId = 0;     // MOVE ZERO TO CDEMO-ACCT-ID. source: :673
            return;                   // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :674
        }
        else
        {
            _commArea.AcctId = ParseLong(_ccAcctId); // MOVE CC-ACCT-ID TO CDEMO-ACCT-ID. source: :676
            SetFlgAcctFilterIsValid();               // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :677
        }
    }

    // =============================================================================================
    //  2220-EDIT-CARD — source: COCRDSLC.cbl:685-724
    // =============================================================================================
    private void EditCard() // COBOL paragraph: 2220-EDIT-CARD
    {
        SetFlgCardFilterNotOk(); // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :688

        // Not supplied: IF CC-CARD-NUM = LOW-VALUES OR SPACES OR CC-CARD-NUM-N = ZEROS. source: :691-702
        if (IsLowValues(_ccCardNum) || IsSpaces(_ccCardNum) || IsZeroesNum(_ccCardNum))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :694
            SetFlgCardFilterBlank();  // SET FLG-CARDFILTER-BLANK TO TRUE. source: :695
            if (ReturnMessageIsBlank)       // IF WS-RETURN-MSG-OFF. source: :696
                SetWsPromptForCard(); // SET WS-PROMPT-FOR-CARD TO TRUE. source: :697
            _commArea.CardNum = 0;    // MOVE ZEROES TO CDEMO-CARD-NUM. source: :700
            return;                   // GO TO 2220-EDIT-CARD-EXIT. source: :701
        }

        // Not numeric / not 16 characters: IF CC-CARD-NUM IS NOT NUMERIC. source: :706-719
        if (!IsNumericX(_ccCardNum, 16))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :707
            SetFlgCardFilterNotOk();  // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :708
            if (ReturnMessageIsBlank)       // IF WS-RETURN-MSG-OFF. source: :709
                _returnMsg = "CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER"; // source: :710-712
            _commArea.CardNum = 0;    // MOVE ZERO TO CDEMO-CARD-NUM. source: :714
            return;                   // GO TO 2220-EDIT-CARD-EXIT. source: :715
        }
        else
        {
            _commArea.CardNum = ParseLong(_ccCardNum); // MOVE CC-CARD-NUM-N TO CDEMO-CARD-NUM. source: :717
            SetFlgCardFilterIsValid();                 // SET FLG-CARDFILTER-ISVALID TO TRUE. source: :718
        }
    }

    // =============================================================================================
    //  9000-READ-DATA — source: COCRDSLC.cbl:726-734
    // =============================================================================================
    private void ReadData(CicsContext ctx) // COBOL paragraph: 9000-READ-DATA
    {
        GetCardByAcctCard(ctx); // PERFORM 9100-GETCARD-BYACCTCARD. source: :728-729
    }

    // =============================================================================================
    //  9100-GETCARD-BYACCTCARD — READ CARDDAT by primary key (card number). source: COCRDSLC.cbl:736-777
    // =============================================================================================
    private void GetCardByAcctCard(CicsContext ctx) // COBOL paragraph: 9100-GETCARD-BYACCTCARD
    {
        // FB-1: MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID is commented out — the account is NOT used in the
        // read. Only the card number is keyed. source: :739-740
        _ = _cardRidAcctId; // WS-CARD-RID-ACCT-ID left unset; used only by dead 9150 (FB-2).
        _cardRidCardNum = PadX(_ccCardNum, 16); // MOVE CC-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :740

        // EXEC CICS READ FILE(LIT-CARDFILENAME) RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) INTO(CARD-RECORD)
        //   LENGTH(150) RESP RESP2. source: :742-750
        _ = CardFileName;
        var repo = new CardRepository(_db.Connection);
        string fileStatus = repo.ReadByKey(_cardRidCardNum, out _cardRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :752-772
        if (_responseCode == (int)Resp.Normal)
        {
            SetFoundCardsForAccount(); // WHEN NORMAL — SET FOUND-CARDS-FOR-ACCOUNT TO TRUE. source: :753-754
        }
        else if (_responseCode == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :755-761
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :756
            SetFlgAcctFilterNotOk();   // FB-5: SET FLG-ACCTFILTER-NOT-OK (reds the acct field too). source: :757
            SetFlgCardFilterNotOk();   // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :758
            if (ReturnMessageIsBlank)        // IF WS-RETURN-MSG-OFF. source: :759
                SetDidNotFindAcctcardCombo(); // SET DID-NOT-FIND-ACCTCARD-COMBO TO TRUE. source: :760
        }
        else
        {
            // WHEN OTHER — hard read error. source: :762-771
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :763
            if (ReturnMessageIsBlank)        // FB-6: IF WS-RETURN-MSG-OFF (guards the acct-NOT-OK set). source: :764
                SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :765
            _errorOpname = PadX("READ", 8);             // MOVE 'READ' TO ERROR-OPNAME. source: :767
            _errorFile = PadX(CardFileName, 9);     // MOVE LIT-CARDFILENAME TO ERROR-FILE. source: :768
            _errorResp = Alpha(_responseCode, 10);          // MOVE WS-RESP-CD TO ERROR-RESP. source: :769
            _errorResp2 = Alpha(_reasonCode, 10);         // MOVE WS-REAS-CD TO ERROR-RESP2. source: :770
            _returnMsg = Truncate75(BuildFileErrorMessage()); // MOVE WS-FILE-ERROR-MESSAGE TO WS-RETURN-MSG. source: :771
        }
    }

    // =============================================================================================
    //  9150-GETCARD-BYACCT — DEAD CODE (FB-2): alt-index READ on CARDAIX by account. Never PERFORMed.
    //  Kept for documentation only; not wired into any dispatch path. source: COCRDSLC.cbl:779-812
    // =============================================================================================
    //   EXEC CICS READ FILE(LIT-CARDFILENAME-ACCT-PATH) RIDFLD(WS-CARD-RID-ACCT-ID) ... INTO(CARD-RECORD)
    //   EVALUATE WS-RESP-CD:
    //     NORMAL -> SET FOUND-CARDS-FOR-ACCOUNT
    //     NOTFND -> SET INPUT-ERROR, FLG-ACCTFILTER-NOT-OK, DID-NOT-FIND-ACCT-IN-CARDXREF
    //     OTHER  -> file error message.
    //   These literals (DidNotFindAcctInCardXrefMsg etc.) are retained as constants but never emitted.

    // =============================================================================================
    //  SEND-LONG-TEXT — DEAD CODE (debug only, not referenced). source: COCRDSLC.cbl:820-833
    // =============================================================================================
    //   EXEC CICS SEND TEXT FROM(WS-LONG-MSG) LENGTH(500) ERASE FREEKB ; EXEC CICS RETURN.

    // =============================================================================================
    //  SEND-PLAIN-TEXT — source: COCRDSLC.cbl:838-851
    // =============================================================================================
    private void SendPlainText(CicsContext ctx)
    {
        // EXEC CICS SEND TEXT FROM(WS-RETURN-MSG) LENGTH(LENGTH OF WS-RETURN-MSG) ERASE FREEKB. source: :839-844
        ctx.SendText(PadX(_returnMsg, 75), erase: true, freeKb: true);
        // EXEC CICS RETURN (no TRANSID — ends the conversation). source: :846-847
        ctx.ReturnTerminal();
    }

    // =============================================================================================
    //  YYYY-STORE-PFKEY (copybook CSSTRPFY) — source: CSSTRPFY.cpy:17-82; COCRDSLC.cbl:284-285
    // =============================================================================================
    private void StorePfKey(CicsContext ctx) => _ccardAid = ctx.StorePfKey(); // COBOL paragraph: YYYY-STORE-PFKEY

    // =============================================================================================
    //  ABEND-ROUTINE — source: COCRDSLC.cbl:857-878
    // =============================================================================================
    private void AbendRoutine(CicsContext ctx)
    {
        // IF ABEND-MSG = LOW-VALUES MOVE 'UNEXPECTED ABEND OCCURRED.' TO ABEND-MSG. source: :859-861
        // MOVE LIT-THISPGM TO ABEND-CULPRIT. source: :863
        // EXEC CICS SEND FROM(ABEND-DATA) NOHANDLE ; HANDLE ABEND CANCEL ; ABEND ABCODE('9999'). source: :865-877
        // Headless model: emit the abend message and end the conversation (no real dump).
        if (ctx.Outcome is null)
        {
            ctx.SendText("UNEXPECTED ABEND OCCURRED.", erase: true, freeKb: true);
            ctx.ReturnTerminal();
        }
    }

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE builder. source: COCRDSLC.cbl:102-121
    // =============================================================================================
    // 'File Error: ' OPNAME(8) ' on ' FILE(9) ' returned RESP ' RESP(10) ',RESP2 ' RESP2(10) 5 spaces.
    private string BuildFileErrorMessage() =>
        "File Error: " + _errorOpname + " on " + _errorFile + " returned RESP " +
        _errorResp + ",RESP2 " + _errorResp2 + "     ";

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>Maps a repository FileStatus to the CICS RESP/RESP2 the EVALUATE branches on.</summary>
    private void SetResp(string fileStatus)
    {
        _responseCode = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _reasonCode = 0; // RESP2 (reason) unavailable from the relational repo; 0 for parity.
    }

    /// <summary>
    /// MOVE LOW-VALUES TO CCRDSLAO — blank every named output field and clear per-turn overrides before
    /// the first SEND. source: COCRDSLC.cbl:428.
    /// </summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>A LOW-VALUES image of <paramref name="width"/> NUL bytes (the not-supplied sentinel).</summary>
    private static string LowValues(int width) => new('\0', width);

    /// <summary>Renders a numeric as a zero-padded zoned-decimal DISPLAY string of width <paramref name="width"/>.</summary>
    private static string Zoned(long value, int width)
    {
        ulong mag = value < 0 ? (ulong)(-value) : (ulong)value;
        string s = mag.ToString();
        if (s.Length >= width) return s[^width..];
        return s.PadLeft(width, '0');
    }

    /// <summary>MOVE numeric (binary S9(09) COMP) TO alphanumeric X(width): 9-digit zoned display left-justified.</summary>
    private static string Alpha(int value, int width) => PadX(Zoned(value, 9), width);

    /// <summary>Truncates a built message to the X(75) WS-RETURN-MSG width (MOVE truncation).</summary>
    private static string Truncate75(string s) => s.Length > 75 ? s[..75] : s;

    /// <summary>A fixed-width substring of a value (REDEFINES slice); short/empty -> spaces.</summary>
    private static string Slice(string s, int start, int len)
    {
        string v = PadX(s, start + len);
        return v.Substring(start, len);
    }

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    /// <summary>True when a string is all spaces (and non-empty).</summary>
    private static bool IsSpaces(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (c != ' ') return false;
        return true;
    }

    /// <summary>True when a string is empty or all LOW-VALUES (modeled as NUL).</summary>
    private static bool IsLowValues(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        foreach (char c in s) if (c != '\0') return false;
        return true;
    }

    /// <summary>True when a value is all SPACES or all LOW-VALUES (or empty) — the common COBOL guard.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>True when a value is all LOW-VALUES (NUL) or all SPACES — used for FROM-* / filter tests.</summary>
    private static bool IsLowValuesOrSpaces(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// COBOL <c>field-N EQUAL ZEROS</c> on a numeric REDEFINES view of an X(n) char field: the numeric
    /// value of the (digit) characters is zero. Non-digit chars read as 0 in a zoned-decimal view, so a
    /// space/low-value field also tests equal to ZEROS here. source: COCRDSLC.cbl:653,693.
    /// </summary>
    private static bool IsZeroesNum(string? s) => ParseLong(s) == 0;

    /// <summary>
    /// COBOL class test <c>field IS NOT NUMERIC</c> on the X(width) field: every character of the fixed
    /// width must be a digit '0'-'9' (spaces/low-values fail). Returns true when the field IS numeric.
    /// source: COCRDSLC.cbl:665,706.
    /// </summary>
    private static bool IsNumericX(string? value, int width)
    {
        string v = PadX(value, width);
        foreach (char c in v) if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>Parses a digit string (ignoring non-digits) to a long; null/empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    // =============================================================================================
    //  BMS map builder — CCRDSLA in mapset COCRDSL (24x80).
    //  source: app/bms/COCRDSL.bms:20-153 / SCREEN_COCRDSL.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COCRDSL.bms:25.</summary>
    public const string MapName = ThisMapName;       // "CCRDSLA"

    /// <summary>The DFHMSD mapset name. source: COCRDSL.bms:20.</summary>
    public const string MapsetName = ThisMapsetName; // "COCRDSL"

    /// <summary>
    /// Constructs the <c>CCRDSLA</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the IC cursor on <c>ACCTSID</c>, the
    /// protected literals and zero-length stoppers, and the named in/out fields — in BMS source order. The
    /// two keyable filter fields are <c>ACCTSID</c> (7,45) L11 (IC) and <c>CARDSID</c> (8,45) L16; the four
    /// output detail fields <c>CRDNAME</c>/<c>CRDSTCD</c>/<c>EXPMON</c>/<c>EXPYEAR</c> are protected display.
    /// No PICIN/PICOUT clauses appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                                 // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                    // bms:34-37
            Out("TITLE01", 1, 21, 40, Askip, BmsColor.Yellow),                    // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                                // bms:42-46
            OutInit("CURDATE", 1, 71, 8, Askip, BmsColor.Blue, "mm/dd/yy"),       // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                                 // bms:52-56
            Out("PGMNAME", 2, 7, 8, Askip, BmsColor.Blue),                        // bms:57-60
            Out("TITLE02", 2, 21, 40, Askip, BmsColor.Yellow),                    // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                                // bms:65-69
            OutInit("CURTIME", 2, 71, 8, Askip, BmsColor.Blue, "hh:mm:ss"),       // bms:70-74

            // ----- 'View Credit Card Detail' heading (default ATTRB -> ASKIP, NEUTRAL) -----
            LitAttr(4, 30, 23, AskipDefault, BmsColor.Neutral, "View Credit Card Detail"), // bms:75-78

            // ----- Account / Card filter labels + input fields -----
            Lit(7, 23, 19, BmsColor.Turquoise, "Account Number    :"),            // bms:79-83
            // ACCTSID: ATTRB=(FSET,IC,NORM,UNPROT) DEFAULT UNDERLINE — the IC (initial cursor) field.
            new ScreenField
            {
                Name = "ACCTSID", Row = 7, Col = 45, Length = 11,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Default,
                Hilight = BmsHilight.Underline,
            },                                                                     // bms:84-88
            Stopper(7, 57),                                                        // bms:89-90
            Lit(8, 23, 19, BmsColor.Turquoise, "Card Number       :"),            // bms:91-95
            // CARDSID: ATTRB=(FSET,NORM,UNPROT) DEFAULT UNDERLINE.
            new ScreenField
            {
                Name = "CARDSID", Row = 8, Col = 45, Length = 16,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Default,
                Hilight = BmsHilight.Underline,
            },                                                                     // bms:96-100
            Stopper(8, 62),                                                        // bms:101-102

            // ----- Name on card (label TURQUOISE default ATTRB; CRDNAME protected display) -----
            LitAttr(11, 4, 20, AskipDefault, BmsColor.Turquoise, "Name on card      :"), // bms:103-106
            // CRDNAME: no ATTRB given -> default protected; HILIGHT=UNDERLINE, no COLOR.
            OutHi("CRDNAME", 11, 25, 50, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:107-109
            Stopper(11, 76),                                                       // bms:110-111

            // ----- Card Active Y/N (label + CRDSTCD ASKIP) -----
            LitAttr(13, 4, 20, AskipDefault, BmsColor.Turquoise, "Card Active Y/N   : "), // bms:112-115
            OutHi("CRDSTCD", 13, 25, 1, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:116-119
            Stopper(13, 27),                                                       // bms:120-121

            // ----- Expiry Date (label + EXPMON / '/' / EXPYEAR) -----
            LitAttr(15, 4, 20, AskipDefault, BmsColor.Turquoise, "Expiry Date       : "), // bms:122-125
            OutHi("EXPMON", 15, 25, 2, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:126-129
            LitAttr(15, 28, 1, AskipDefault, BmsColor.Default, "/"),              // bms:130-132 (date separator)
            OutHi("EXPYEAR", 15, 30, 4, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:133-136
            Stopper(15, 35),                                                       // bms:137-138

            // ----- info line (PROT NEUTRAL, HILIGHT=OFF) -----
            new ScreenField
            {
                Name = "INFOMSG", Row = 20, Col = 25, Length = 40,
                Attribute = BmsAttribute.Protected,
                Color = BmsColor.Neutral,
                Hilight = BmsHilight.Off,
            },                                                                     // bms:139-143

            // ----- error line (ASKIP,BRT,FSET RED, L80) -----
            Out("ERRMSG", 23, 1, 80, AskipBrtFset, BmsColor.Red),                 // bms:144-147

            // ----- footer F-key legend (ASKIP,NORM YELLOW, L75) -----
            Lit(24, 1, 75, BmsColor.Yellow, "ENTER=Search Cards  F3=Exit"),       // bms:148-152
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;          // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipDefault => BmsAttribute.AutoSkip;                          // default ATTRB omitted -> ASKIP
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) and the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. default-ASKIP for omitted ATTRB).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>Named output field carrying a HILIGHT (e.g. UNDERLINE display fields).</summary>
    private static ScreenField OutHi(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, BmsHilight hi) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = hi };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, default ATTRB omitted -> ASKIP).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = AskipDefault, Color = BmsColor.Default };
}
