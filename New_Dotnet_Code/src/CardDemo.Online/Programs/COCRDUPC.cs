using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COCRDUPC</c> — the "Update Credit Card Details"
/// transaction (TRANSID <c>CCUP</c>, BMS map <c>CCRDUPA</c> / mapset <c>COCRDUP</c>).
/// </summary>
/// <remarks>
/// <para>
/// COCRDUPC is the pseudo-conversational card edit/update screen. The operator supplies an 11-digit account
/// number and a 16-digit card number as search keys (typed on the screen or carried in COMMAREA from the
/// card-list program <c>COCRDLIC</c>), the program reads the single CARD record by card-number primary key,
/// displays its editable fields (embossed name, active Y/N status, expiry month, expiry year — the expiry
/// day is shown but protected/dark), validates the operator's edits, and on a two-phase confirm
/// (validate → press F5 to save) REWRITEs the CARD record with an optimistic-lock check (re-read under
/// UPDATE, re-compare the OLD snapshot, only then rewrite). It re-drives itself via
/// <c>EXEC CICS RETURN TRANSID('CCUP') COMMAREA(WS-COMMAREA) LENGTH(2000)</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COCRDUPC.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE TRUE</c> / <c>PERFORM</c> / <c>GO TO</c> control flow, the
/// COMMAREA field usage (<see cref="CardDemoCommArea"/> plus the program-private trailer
/// <c>WS-THIS-PROGCOMMAREA</c> = <c>CCUP-CHANGE-ACTION</c> + OLD snapshot + NEW edits + rewrite buffer),
/// the money truncate-toward-zero semantics, and <b>every faithful bug</b> are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the CARD master is accessed, by primary key (card number):
/// the plain READ in <c>9100-GETCARD-BYACCTCARD</c> = <see cref="CardRepository.ReadByKey"/>; the READ
/// UPDATE in <c>9200-WRITE-PROCESSING</c> = a second <see cref="CardRepository.ReadByKey"/> taking a
/// before-image (the CICS record enqueue has no relational analog — the optimistic re-compare in 9300 stands
/// in for "did someone change it"); the REWRITE = <see cref="CardRepository.Update"/>. RESP NORMAL→'00',
/// NOTFND→'23', anything else→a hard file error.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-8.1 — <b>Account number is never matched against the card.</b> The card is read by card number
/// only (<c>RIDFLD = WS-CARD-RID-CARDNUM</c>); the <c>MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID</c> line is
/// commented out and the <c>CARDAIX</c> alt path is never opened. The typed account is validated for format,
/// echoed and stored, but never used to verify the card↔account relationship. source: COCRDUPC.cbl:1379,1384,1424.</item>
/// <item>FB-8.2 — <b><c>CARD-UPDATE-ACCT-ID</c> is taken from the typed account, not the card's real
/// account.</b> 9200 sets the rewrite acct_id from <c>CC-ACCT-ID-N</c> (operator input), overwriting the
/// on-file <c>CARD-ACCT-ID</c>; combined with FB-8.1 a successful update can re-point the card to an
/// unrelated account number. source: COCRDUPC.cbl:1463.</item>
/// <item>FB-8.3 — <b>CVV is zeroed/garbled on every save.</b> 9200 sets <c>CARD-CVV-CD-X = CCUP-NEW-CVV-CD</c>
/// then <c>CARD-CVV-CD-N → CARD-UPDATE-CVV-CD</c>, but the screen has no CVV field so <c>CCUP-NEW-CVV-CD</c>
/// is never populated; after <c>INITIALIZE CCUP-NEW-DETAILS</c> (spaces) and the LOW-VALUES move at 1100 it
/// holds non-digits, which through the <c>9(3)</c> redefine yields 0. The rewrite therefore destroys the
/// on-file CVV rather than preserving it. source: COCRDUPC.cbl:306,586,1464-1465.</item>
/// <item>FB-8.4 — <b><c>9300</c> early-exit jumps to the caller's exit label.</b> The mismatch branch does
/// <c>GO TO 9200-WRITE-PROCESSING-EXIT</c> (the caller paragraph's exit), not 9300's own; it works by
/// coincidence because 9300 is PERFORM…THRU'd from 9200. Preserved as: on mismatch set DATA-WAS-CHANGED,
/// refresh the snapshot, and unwind out of 9200 with no REWRITE. source: COCRDUPC.cbl:1518-1519.</item>
/// <item>FB-8.5 — <b><c>1230-EDIT-NAME</c> treats a name of all-zero characters as "not supplied".</b> The
/// blank test includes <c>EQUAL ZEROS</c>. source: COCRDUPC.cbl:813.</item>
/// <item>FB-8.6 — <b>Expiry-day is protected/dark yet received and stored, and always re-sent from OLD.</b>
/// 1100 unconditionally moves <c>EXPDAYI → CCUP-NEW-EXPDAY</c> (no '*'/space scrub) but 3200 always re-sends
/// <c>CCUP-OLD-EXPDAY</c> even on the CHANGES-MADE branch. source: COCRDUPC.cbl:621,1122-1123.</item>
/// <item>FB-8.7 — <b>Disallowed PF keys silently become ENTER.</b> Any AID outside the gated set is coerced
/// to ENTER with no "invalid key" message. source: COCRDUPC.cbl:413-424.</item>
/// <item>FB-8.8 — <b>Year lower bound is a fixed 1950, not "current year".</b> Valid range 1950..2099, so a
/// long-expired year (e.g. 1951) passes. source: COCRDUPC.cbl:96-99,934.</item>
/// </list>
/// </remarks>
public sealed class Cocrdupc : ITransactionHandler
{
    // =============================================================================================
    //  WS-LITERALS — source: COCRDUPC.cbl:218-263
    // =============================================================================================
    private const string LIT_THISPGM = "COCRDUPC";        // source: COCRDUPC.cbl:219-220
    private const string LIT_THISTRANID = "CCUP";         // source: COCRDUPC.cbl:221-222
    private const string LIT_THISMAPSET = "COCRDUP";      // source: COCRDUPC.cbl:223-224 ('COCRDUP ' X(8); MOVEd to X(7) drops the trailing space)
    private const string LIT_THISMAP = "CCRDUPA";         // source: COCRDUPC.cbl:225-226
    private const string LIT_CCLISTPGM = "COCRDLIC";      // source: COCRDUPC.cbl:227-228
    private const string LIT_CCLISTTRANID = "CCLI";       // source: COCRDUPC.cbl:229-230
    private const string LIT_CCLISTMAPSET = "COCRDLI";    // source: COCRDUPC.cbl:231-232 (X(7))
    private const string LIT_CCLISTMAP = "CCRDSLA";       // source: COCRDUPC.cbl:233-234
    private const string LIT_MENUPGM = "COMEN01C";        // source: COCRDUPC.cbl:235-236
    private const string LIT_MENUTRANID = "CM00";         // source: COCRDUPC.cbl:237-238
    private const string LIT_MENUMAPSET = "COMEN01";      // source: COCRDUPC.cbl:239-240
    private const string LIT_MENUMAP = "COMEN1A";         // source: COCRDUPC.cbl:241-242
    private const string LIT_CARDDTLPGM = "COCRDSLC";     // source: COCRDUPC.cbl:243-244
    private const string LIT_CARDDTLTRANID = "CCDL";      // source: COCRDUPC.cbl:245-246
    private const string LIT_CARDFILENAME = "CARDDAT ";   // source: COCRDUPC.cbl:251-252
    private const string LIT_CARDFILENAME_ACCT_PATH = "CARDAIX "; // source: COCRDUPC.cbl:253-254 (FB-8.1: declared, never used)
    // LIT-ALL-ALPHA-FROM / LIT-ALL-SPACES-TO (name alpha check), LIT-UPPER / LIT-LOWER (name uppercasing).
    private const string LIT_ALL_ALPHA_FROM = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"; // source: :255-257
    private const string LIT_UPPER = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // source: :260-261
    private const string LIT_LOWER = "abcdefghijklmnopqrstuvwxyz"; // source: :262-263

    // CCDA-TITLE01/02 (COTTL01Y) — shared screen header. source: COTTL01Y.cpy.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    // =============================================================================================
    //  WS-MISC-STORAGE — CICS vars + input/output edit flags. source: COCRDUPC.cbl:36-99
    // =============================================================================================

    // 07 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: COCRDUPC.cbl:41-44
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-INPUT-FLAG: 88 INPUT-OK='0' / INPUT-ERROR='1' / INPUT-PENDING=LOW-VALUES. source: :53-56
    private char _wsInputFlag = '\0';
    private bool InputOk => _wsInputFlag == '0';     // 88 INPUT-OK
    private bool InputError => _wsInputFlag == '1';  // 88 INPUT-ERROR
    private void SetInputOk() => _wsInputFlag = '0';
    private void SetInputError() => _wsInputFlag = '1';

    // 05 WS-EDIT-ACCT-FLAG: 88 FLG-ACCTFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :57-60
    private char _wsEditAcctFlag = '\0';
    private bool FlgAcctFilterNotOk => _wsEditAcctFlag == '0';   // 88 FLG-ACCTFILTER-NOT-OK
    private bool FlgAcctFilterIsValid => _wsEditAcctFlag == '1'; // 88 FLG-ACCTFILTER-ISVALID
    private bool FlgAcctFilterBlank => _wsEditAcctFlag == ' ';   // 88 FLG-ACCTFILTER-BLANK
    private void SetFlgAcctFilterNotOk() => _wsEditAcctFlag = '0';
    private void SetFlgAcctFilterIsValid() => _wsEditAcctFlag = '1';
    private void SetFlgAcctFilterBlank() => _wsEditAcctFlag = ' ';

    // 05 WS-EDIT-CARD-FLAG: 88 FLG-CARDFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :61-64
    private char _wsEditCardFlag = '\0';
    private bool FlgCardFilterNotOk => _wsEditCardFlag == '0';   // 88 FLG-CARDFILTER-NOT-OK
    private bool FlgCardFilterIsValid => _wsEditCardFlag == '1'; // 88 FLG-CARDFILTER-ISVALID
    private bool FlgCardFilterBlank => _wsEditCardFlag == ' ';   // 88 FLG-CARDFILTER-BLANK
    private void SetFlgCardFilterNotOk() => _wsEditCardFlag = '0';
    private void SetFlgCardFilterIsValid() => _wsEditCardFlag = '1';
    private void SetFlgCardFilterBlank() => _wsEditCardFlag = ' ';

    // 05 WS-EDIT-CARDNAME-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :65-68
    private char _wsEditCardNameFlag = '\0';
    private bool FlgCardNameNotOk => _wsEditCardNameFlag == '0';
    private bool FlgCardNameBlank => _wsEditCardNameFlag == ' ';
    private void SetFlgCardNameNotOk() => _wsEditCardNameFlag = '0';
    private void SetFlgCardNameIsValid() => _wsEditCardNameFlag = '1';
    private void SetFlgCardNameBlank() => _wsEditCardNameFlag = ' ';

    // 05 WS-EDIT-CARDSTATUS-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :69-72
    private char _wsEditCardStatusFlag = '\0';
    private bool FlgCardStatusNotOk => _wsEditCardStatusFlag == '0';
    private bool FlgCardStatusBlank => _wsEditCardStatusFlag == ' ';
    private void SetFlgCardStatusNotOk() => _wsEditCardStatusFlag = '0';
    private void SetFlgCardStatusIsValid() => _wsEditCardStatusFlag = '1';
    private void SetFlgCardStatusBlank() => _wsEditCardStatusFlag = ' ';

    // 05 WS-EDIT-CARDEXPMON-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :73-76
    private char _wsEditCardExpMonFlag = '\0';
    private bool FlgCardExpMonNotOk => _wsEditCardExpMonFlag == '0';
    private bool FlgCardExpMonBlank => _wsEditCardExpMonFlag == ' ';
    private void SetFlgCardExpMonNotOk() => _wsEditCardExpMonFlag = '0';
    private void SetFlgCardExpMonIsValid() => _wsEditCardExpMonFlag = '1';
    private void SetFlgCardExpMonBlank() => _wsEditCardExpMonFlag = ' ';

    // 05 WS-EDIT-CARDEXPYEAR-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :77-80
    private char _wsEditCardExpYearFlag = '\0';
    private bool FlgCardExpYearNotOk => _wsEditCardExpYearFlag == '0';
    private bool FlgCardExpYearBlank => _wsEditCardExpYearFlag == ' ';
    private void SetFlgCardExpYearNotOk() => _wsEditCardExpYearFlag = '0';
    private void SetFlgCardExpYearIsValid() => _wsEditCardExpYearFlag = '1';
    private void SetFlgCardExpYearBlank() => _wsEditCardExpYearFlag = ' ';

    // 05 WS-RETURN-FLAG: 88 OFF=LOW-VALUES / ON='1'. source: :81-83 (declared; not used on live path)
    private char _wsReturnFlag = '\0';

    // 05 WS-PFK-FLAG: 88 PFK-VALID='0' / PFK-INVALID='1'. source: COCRDUPC.cbl:84-86
    private char _wsPfkFlag = '\0';
    private bool PfkInvalid => _wsPfkFlag == '1';   // 88 PFK-INVALID
    private void SetPfkValid() => _wsPfkFlag = '0';
    private void SetPfkInvalid() => _wsPfkFlag = '1';

    // 05 CARD-NAME-CHECK X(50) VALUE LOW-VALUES — name alpha-strip scratch. source: :87-88
    // 05 FLG-YES-NO-CHECK X(1) VALUE 'N', 88 FLG-YES-NO-VALID = 'Y','N'. source: :89-91
    // 05 CARD-MONTH-CHECK X(2) / -N 9(2), 88 VALID-MONTH = 1 THRU 12. source: :92-95
    // 05 CARD-YEAR-CHECK  X(4) / -N 9(4), 88 VALID-YEAR  = 1950 THRU 2099. source: :96-99 (FB-8.8)

    // =============================================================================================
    //  CICS-OUTPUT-EDIT-VARS — CVV redefine scratch (9200). source: COCRDUPC.cbl:103-123
    // =============================================================================================
    // 10 CARD-CVV-CD-X X(03) / 10 CARD-CVV-CD-N REDEFINES X(03) -> 9(03). Used in the FB-8.3 MOVE chain.
    private string _cardCvvCdX = ""; // CARD-CVV-CD-X X(3)
    private int CardCvvCdN => (int)ParseLong(_cardCvvCdX); // CARD-CVV-CD-N 9(3) (non-digits read as 0)

    // =============================================================================================
    //  WS-CARD-RID — VSAM key area. source: COCRDUPC.cbl:128-132
    // =============================================================================================
    // 10 WS-CARD-RID-CARDNUM X(16) ; 10 WS-CARD-RID-ACCT-ID 9(11) (redef -X X(11)). Only CARDNUM is RIDFLD.
    private string _wsCardRidCardnum = ""; // WS-CARD-RID-CARDNUM X(16)

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE group. source: COCRDUPC.cbl:133-152
    // =============================================================================================
    private string _errorOpname = "        "; // ERROR-OPNAME X(8)
    private string _errorFile = "         ";  // ERROR-FILE   X(9)
    private string _errorResp = "          "; // ERROR-RESP   X(10)
    private string _errorResp2 = "          "; // ERROR-RESP2  X(10)

    // =============================================================================================
    //  WS-INFO-MSG X(40) — 88 levels. source: COCRDUPC.cbl:157-171
    // =============================================================================================
    private string _wsInfoMsg = "";
    // 88 WS-NO-INFO-MESSAGE = SPACES / LOW-VALUES.
    private const string FOUND_CARDS_FOR_ACCOUNT = "Details of selected card shown above"; // source: :160-161
    private const string PROMPT_FOR_SEARCH_KEYS = "Please enter Account and Card Number";  // source: :162-163
    private const string PROMPT_FOR_CHANGES = "Update card details presented above.";      // source: :164-165
    private const string PROMPT_FOR_CONFIRMATION = "Changes validated.Press F5 to save";   // source: :166-167
    private const string CONFIRM_UPDATE_SUCCESS = "Changes committed to database";         // source: :168-169
    private const string INFORM_FAILURE = "Changes unsuccessful. Please try again";        // source: :170-171
    private bool WsNoInfoMessage => IsSpacesOrLowValues(_wsInfoMsg); // 88 WS-NO-INFO-MESSAGE
    // FOUND-CARDS-FOR-ACCOUNT doubles as the "a card was just read" signal (SET in 1200/9100). source: :668,1394
    private bool FoundCardsForAccount => _wsInfoMsg == FOUND_CARDS_FOR_ACCOUNT;
    private void SetFoundCardsForAccount() => _wsInfoMsg = FOUND_CARDS_FOR_ACCOUNT;
    private void SetPromptForSearchKeys() => _wsInfoMsg = PROMPT_FOR_SEARCH_KEYS;
    private void SetPromptForChanges() => _wsInfoMsg = PROMPT_FOR_CHANGES;
    private void SetPromptForConfirmation() => _wsInfoMsg = PROMPT_FOR_CONFIRMATION;
    private void SetConfirmUpdateSuccess() => _wsInfoMsg = CONFIRM_UPDATE_SUCCESS;
    private void SetInformFailure() => _wsInfoMsg = INFORM_FAILURE;
    // PROMPT-FOR-CONFIRMATION drives FKEYSC bright in 3300. source: :1315-1317
    private bool PromptForConfirmation => _wsInfoMsg == PROMPT_FOR_CONFIRMATION;

    // =============================================================================================
    //  WS-RETURN-MSG X(75) — 88-level message constants. source: COCRDUPC.cbl:173-214
    // =============================================================================================
    private string _wsReturnMsg = "";
    // 88 WS-RETURN-MSG-OFF = SPACES. The live-path message 88s/literals follow.
    private const string WS_PROMPT_FOR_ACCT = "Account number not provided";                      // source: :177-178
    private const string WS_PROMPT_FOR_CARD = "Card number not provided";                         // source: :179-180
    private const string WS_PROMPT_FOR_NAME = "Card name not provided";                           // source: :181-182
    private const string WS_NAME_MUST_BE_ALPHA = "Card name can only contain alphabets and spaces"; // source: :183-184
    private const string NO_SEARCH_CRITERIA_RECEIVED = "No input received";                       // source: :185-186
    private const string NO_CHANGES_DETECTED = "No change detected with respect to values fetched."; // source: :187-188
    private const string CARD_STATUS_MUST_BE_YES_NO = "Card Active Status must be Y or N";        // source: :195-196
    private const string CARD_EXPIRY_MONTH_NOT_VALID = "Card expiry month must be between 1 and 12"; // source: :197-198
    private const string CARD_EXPIRY_YEAR_NOT_VALID = "Invalid card expiry year";                 // source: :199-200
    private const string DID_NOT_FIND_ACCTCARD_COMBO = "Did not find cards for this search condition"; // source: :203-204
    private const string COULD_NOT_LOCK_FOR_UPDATE = "Could not lock record for update";          // source: :205-206
    private const string DATA_WAS_CHANGED_BEFORE_UPDATE = "Record changed by some one else. Please review"; // source: :207-208
    private const string LOCKED_BUT_UPDATE_FAILED = "Update of record failed";                    // source: :209-210
    // Other declared 88s (WS-EXIT-MESSAGE, SEARCHED-*, DID-NOT-FIND-ACCT-IN-CARDXREF, XREF-READ-ERROR,
    // CODING-TO-BE-DONE) are never SET on the live path. source: :175-214.
    private bool WsReturnMsgOff => IsSpaces(_wsReturnMsg) || _wsReturnMsg.Length == 0; // 88 WS-RETURN-MSG-OFF
    private void SetWsReturnMsgOff() => _wsReturnMsg = ""; // SET WS-RETURN-MSG-OFF (SPACES). source: :384
    // The 88 condition-name tests used by the dispatch / decide logic (compare WS-RETURN-MSG to its literal).
    private bool NoSearchCriteriaReceived => _wsReturnMsg == NO_SEARCH_CRITERIA_RECEIVED; // source: :658,185-186
    private bool NoChangesDetected => _wsReturnMsg == NO_CHANGES_DETECTED;                 // source: :682,187-188
    private bool CouldNotLockForUpdate => _wsReturnMsg == COULD_NOT_LOCK_FOR_UPDATE;       // source: :993,205-206
    private bool LockedButUpdateFailed => _wsReturnMsg == LOCKED_BUT_UPDATE_FAILED;        // source: :995,209-210
    private bool DataWasChangedBeforeUpdate => _wsReturnMsg == DATA_WAS_CHANGED_BEFORE_UPDATE; // source: :997,207-208

    // =============================================================================================
    //  CC-WORK-AREA (CVCRD01Y): filter inputs + AID flags. source: CVCRD01Y.cpy
    // =============================================================================================
    // CC-ACCT-ID X(11) (redef CC-ACCT-ID-N 9(11)); CC-CARD-NUM X(16) (redef CC-CARD-NUM-N 9(16)).
    private string _ccAcctId = "";  // CC-ACCT-ID  X(11)
    private string _ccCardNum = ""; // CC-CARD-NUM X(16)
    private long CcAcctIdN => ParseLong(_ccAcctId);   // CC-ACCT-ID-N  9(11)
    private long CcCardNumN => ParseLong(_ccCardNum);  // CC-CARD-NUM-N 9(16)

    // The CARD-RECORD currently read (CVACT02Y). source: COCRDUPC.cbl:353
    private Card? _cardRecord;
    private string CardEmbossedName = "";    // CARD-EMBOSSED-NAME X(50) (mutable: 9000/9300 uppercase it in place)
    private string CardExpiraionDate = "";   // CARD-EXPIRAION-DATE X(10)
    private int CardCvvCd;                    // CARD-CVV-CD 9(3)
    private string CardActiveStatus = "";    // CARD-ACTIVE-STATUS X(1)

    // CCARD-AID — set by YYYY-STORE-PFKEY, then remapped by the validity gate. source: CVCRD01Y.cpy; :413-424
    private CcardAid _ccardAid = CcardAid.None;
    private bool CcardAidEnter => _ccardAid == CcardAid.Enter; // 88 CCARD-AID-ENTER
    private bool CcardAidPfk03 => _ccardAid == CcardAid.Pfk03; // 88 CCARD-AID-PFK03
    private bool CcardAidPfk05 => _ccardAid == CcardAid.Pfk05; // 88 CCARD-AID-PFK05
    private bool CcardAidPfk12 => _ccardAid == CcardAid.Pfk12; // 88 CCARD-AID-PFK12
    private void SetCcardAidEnter() => _ccardAid = CcardAid.Enter;
    private void SetCcardAidPfk03() => _ccardAid = CcardAid.Pfk03;

    // =============================================================================================
    //  WS-THIS-PROGCOMMAREA — program-private commarea trailer carried across turns. source: :274-321
    // =============================================================================================
    // CCUP-CHANGE-ACTION X(1) state machine: LOW-VALUES/SPACES=DETAILS-NOT-FETCHED, 'S'=SHOW-DETAILS,
    // 'E'/'N'/'C'/'L'/'F'=CHANGES-MADE (E=NOT-OK, N=OK-NOT-CONFIRMED, C=OKAYED-AND-DONE, L/F=FAILED).
    private char _ccupChangeAction = '\0';
    private bool CcupDetailsNotFetched => _ccupChangeAction == '\0' || _ccupChangeAction == ' '; // 88 (LOW-VALUES,SPACES)
    private bool CcupShowDetails => _ccupChangeAction == 'S';                                      // 88 'S'
    private bool CcupChangesMade => _ccupChangeAction is 'E' or 'N' or 'C' or 'L' or 'F';          // 88 'E','N','C','L','F'
    private bool CcupChangesNotOk => _ccupChangeAction == 'E';                                     // 88 'E'
    private bool CcupChangesOkNotConfirmed => _ccupChangeAction == 'N';                            // 88 'N'
    private bool CcupChangesOkayedAndDone => _ccupChangeAction == 'C';                             // 88 'C'
    private bool CcupChangesFailed => _ccupChangeAction is 'L' or 'F';                             // 88 'L','F'
    private bool CcupChangesOkayedLockError => _ccupChangeAction == 'L';                           // 88 'L'
    private bool CcupChangesOkayedButFailed => _ccupChangeAction == 'F';                           // 88 'F'
    private void SetCcupDetailsNotFetched() => _ccupChangeAction = '\0'; // SET ... TO TRUE -> first VALUE (LOW-VALUES)
    private void SetCcupShowDetails() => _ccupChangeAction = 'S';
    private void SetCcupChangesNotOk() => _ccupChangeAction = 'E';
    private void SetCcupChangesOkNotConfirmed() => _ccupChangeAction = 'N';
    private void SetCcupChangesOkayedAndDone() => _ccupChangeAction = 'C';
    private void SetCcupChangesOkayedLockError() => _ccupChangeAction = 'L';
    private void SetCcupChangesOkayedButFailed() => _ccupChangeAction = 'F';

    // CCUP-OLD-DETAILS — the snapshot read from file. source: :291-301
    private string _oldAcctId = "";  // CCUP-OLD-ACCTID  X(11)
    private string _oldCardId = "";  // CCUP-OLD-CARDID  X(16)
    private string _oldCvvCd = "";   // CCUP-OLD-CVV-CD  X(3)
    private string _oldCrdName = ""; // CCUP-OLD-CRDNAME X(50)
    private string _oldExpYear = ""; // CCUP-OLD-EXPYEAR X(4)
    private string _oldExpMon = "";  // CCUP-OLD-EXPMON  X(2)
    private string _oldExpDay = "";  // CCUP-OLD-EXPDAY  X(2)
    private string _oldCrdStcd = ""; // CCUP-OLD-CRDSTCD X(1)
    // CCUP-OLD-CARDDATA = OLD-CRDNAME + OLD-EXPIRAION-DATE(year/mon/day) + OLD-CRDSTCD (59 bytes). source: :295-301
    private string OldCardData => PadX(_oldCrdName, 50) + PadX(_oldExpYear, 4) + PadX(_oldExpMon, 2)
                                  + PadX(_oldExpDay, 2) + PadX(_oldCrdStcd, 1);

    // CCUP-NEW-DETAILS — the operator's edited values. source: :303-313
    private string _newAcctId = "";  // CCUP-NEW-ACCTID  X(11)
    private string _newCardId = "";  // CCUP-NEW-CARDID  X(16)
    private string _newCvvCd = "";   // CCUP-NEW-CVV-CD  X(3)  (never populated from screen — FB-8.3)
    private string _newCrdName = ""; // CCUP-NEW-CRDNAME X(50)
    private string _newExpYear = ""; // CCUP-NEW-EXPYEAR X(4)
    private string _newExpMon = "";  // CCUP-NEW-EXPMON  X(2)
    private string _newExpDay = "";  // CCUP-NEW-EXPDAY  X(2)
    private string _newCrdStcd = ""; // CCUP-NEW-CRDSTCD X(1)
    // CCUP-NEW-CARDDATA = NEW-CRDNAME + NEW-EXPIRAION-DATE(year/mon/day) + NEW-CRDSTCD (59 bytes). source: :307-313
    private string NewCardData => PadX(_newCrdName, 50) + PadX(_newExpYear, 4) + PadX(_newExpMon, 2)
                                  + PadX(_newExpDay, 2) + PadX(_newCrdStcd, 1);

    // INITIALIZE CCUP-NEW-DETAILS — group default: alphanumeric -> SPACES, numeric -> '0'/spaces. source: :586,653
    private void InitializeNewDetails()
    {
        _newAcctId = PadX("", 11); _newCardId = PadX("", 16); _newCvvCd = PadX("", 3);
        _newCrdName = PadX("", 50); _newExpYear = PadX("", 4); _newExpMon = PadX("", 2);
        _newExpDay = PadX("", 2); _newCrdStcd = PadX("", 1);
    }

    // MOVE LOW-VALUES TO CCUP-NEW-CARDDATA — only the card-data sub-fields. source: :653
    private void NewCardDataToLowValues()
    {
        _newCrdName = LowValues(50); _newExpYear = LowValues(4); _newExpMon = LowValues(2);
        _newExpDay = LowValues(2); _newCrdStcd = LowValues(1);
    }

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map. source: COCRDUPC.cbl:272,324
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The CARD repository is created from
    /// <c>db.Connection</c> inside the read/write paragraphs (no DB is opened here).
    /// </summary>
    public Cocrdupc(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cocrdupc() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => LIT_THISPGM; // PROGRAM-ID. COCRDUPC. source: COCRDUPC.cbl:23-24

    /// <inheritdoc/>
    public string TransId => LIT_THISTRANID;  // CSD: CCUP -> COCRDUPC. source: CSD_TRANSACTIONS.md; cbl:221-222

    // =============================================================================================
    //  0000-MAIN — source: COCRDUPC.cbl:367-562
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE). source: :370-372 (modeled as a try/catch wrapper).
        try
        {
            Main0000(ctx);
        }
        catch (Exception)
        {
            // ABEND-ROUTINE: send ABEND-DATA and ABEND ABCODE('9999'). source: :1531-1556.
            AbendRoutine(ctx);
        }
    }

    private void Main0000(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE re-initialised per turn).
        _map = BuildMap();

        // INITIALIZE CC-WORK-AREA WS-MISC-STORAGE WS-COMMAREA. source: :374-376
        // (Working-storage starts at COBOL VALUE/SPACES/LOW-VALUES; the handler instance is fresh.)
        _ = _wsReturnFlag;              // WS-RETURN-FLAG declared; unused on live path. source: :81-83
        _ = LIT_CARDFILENAME_ACCT_PATH; // FB-8.1: CARDAIX declared, never used.
        _ = LIT_CCLISTTRANID; _ = LIT_CCLISTMAP; _ = LIT_MENUMAPSET; _ = LIT_MENUMAP;
        _ = LIT_CARDDTLPGM; _ = LIT_CARDDTLTRANID;

        // MOVE LIT-THISTRANID TO WS-TRANID. source: :380 (WS-TRANID is informational only.)
        // SET WS-RETURN-MSG-OFF TO TRUE. source: :384
        SetWsReturnMsgOff();

        // Store passed data if any. source: :388-401
        //   IF EIBCALEN = 0 OR (CDEMO-FROM-PROGRAM = LIT-MENUPGM AND NOT CDEMO-PGM-REENTER)
        //      INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA
        //      SET CDEMO-PGM-ENTER TO TRUE ; SET CCUP-DETAILS-NOT-FETCHED TO TRUE
        //   ELSE  copy the two segments out of DFHCOMMAREA
        // COBOL evaluates this BEFORE the DFHCOMMAREA->CARDDEMO-COMMAREA load (ELSE at :396-400). At the IF
        // (:388-390) CARDDEMO-COMMAREA is still the INITIALIZEd (SPACES) working-storage copy, so
        // CDEMO-FROM-PROGRAM is blank and the second disjunct (= LIT-MENUPGM AND NOT CDEMO-PGM-REENTER) is
        // ALWAYS FALSE — only EIBCALEN = 0 takes the INITIALIZE + SET-ENTER + SET-NOT-FETCHED path.
        bool freshCommarea = ctx.EibCalen == 0;

        if (freshCommarea)
        {
            _commArea = new CardDemoCommArea(); // INITIALIZE CARDDEMO-COMMAREA. source: :391
            InitializeProgTail();               // INITIALIZE WS-THIS-PROGCOMMAREA. source: :392
            _commArea.SetFirstEntry();          // SET CDEMO-PGM-ENTER TO TRUE. source: :393
            SetCcupDetailsNotFetched();         // SET CCUP-DETAILS-NOT-FETCHED TO TRUE. source: :394
        }
        else
        {
            // Copy the carried COMMAREA + program tail. source: :396-400
            _commArea = ctx.CommArea!;
            RestoreProgTail(ctx);
        }

        // PERFORM YYYY-STORE-PFKEY. source: :406-407 — EIBAID -> CCARD-AID-*.
        Yyyy_StorePfkey(ctx);

        // Remap PFkeys / validity gate. source: :413-424
        //   SET PFK-INVALID
        //   IF ENTER OR PFK03 OR (PFK05 AND CHANGES-OK-NOT-CONFIRMED) OR (PFK12 AND NOT DETAILS-NOT-FETCHED)
        //      SET PFK-VALID
        //   IF PFK-INVALID  SET CCARD-AID-ENTER  (FB-8.7 coercion)
        SetPfkInvalid();
        if (CcardAidEnter
            || CcardAidPfk03
            || (CcardAidPfk05 && CcupChangesOkNotConfirmed)
            || (CcardAidPfk12 && !CcupDetailsNotFetched))
        {
            SetPfkValid();
        }
        if (PfkInvalid)
            SetCcardAidEnter();

        // EVALUATE TRUE — decide what to do based on inputs received. source: :429-543
        bool cameFromList = _commArea.FromProgram.TrimEnd() == LIT_CCLISTPGM; // CDEMO-FROM-PROGRAM = LIT-CCLISTPGM

        // (a) Exit / done -> XCTL to caller or menu. source: :435-476
        if (CcardAidPfk03
            || (CcupChangesOkayedAndDone && _commArea.LastMapSet.TrimEnd() == LIT_CCLISTMAPSET)
            || (CcupChangesFailed && _commArea.LastMapSet.TrimEnd() == LIT_CCLISTMAPSET))
        {
            SetCcardAidPfk03(); // SET CCARD-AID-PFK03 TO TRUE. source: :440

            // IF CDEMO-FROM-TRANID = LOW-VALUES/SPACES -> CM00 ELSE FROM-TRANID. source: :442-447
            if (IsLowValuesOrSpaces(_commArea.FromTranId))
                _commArea.ToTranId = LIT_MENUTRANID;
            else
                _commArea.ToTranId = _commArea.FromTranId;

            // IF CDEMO-FROM-PROGRAM = LOW-VALUES/SPACES -> COMEN01C ELSE FROM-PROGRAM. source: :449-454
            if (IsLowValuesOrSpaces(_commArea.FromProgram))
                _commArea.ToProgram = LIT_MENUPGM;
            else
                _commArea.ToProgram = _commArea.FromProgram;

            _commArea.FromTranId = LIT_THISTRANID; // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :456
            _commArea.FromProgram = LIT_THISPGM;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :457

            // IF CDEMO-LAST-MAPSET = LIT-CCLISTMAPSET MOVE ZEROS TO CDEMO-ACCT-ID CDEMO-CARD-NUM. source: :459-462
            if (_commArea.LastMapSet.TrimEnd() == LIT_CCLISTMAPSET)
            {
                _commArea.AcctId = 0;
                _commArea.CardNum = 0;
            }

            _commArea.SetUser();                   // SET CDEMO-USRTYP-USER TO TRUE. source: :464
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER   TO TRUE. source: :465
            _commArea.LastMapSet = LIT_THISMAPSET; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :466
            _commArea.LastMap = LIT_THISMAP;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :467

            // EXEC CICS SYNCPOINT — no uncommitted CARD changes on the exit path; effectively a no-op. source: :469-471
            // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :473-476
            ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
            return;
        }

        // (b) Came from list, fetch for update. source: :482-497
        //   WHEN (CDEMO-PGM-ENTER AND FROM=COCRDLIC) OR (PFK12 AND FROM=COCRDLIC)
        if ((_commArea.IsFirstEntry && cameFromList) || (CcardAidPfk12 && cameFromList))
        {
            _commArea.SetReenter();           // SET CDEMO-PGM-REENTER TO TRUE. source: :486
            SetInputOk();                     // SET INPUT-OK TO TRUE. source: :487
            SetFlgAcctFilterIsValid();        // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :488
            SetFlgCardFilterIsValid();        // SET FLG-CARDFILTER-ISVALID TO TRUE. source: :489
            _ccAcctId = Zoned(_commArea.AcctId, 11);  // MOVE CDEMO-ACCT-ID  TO CC-ACCT-ID-N. source: :490
            _ccCardNum = Zoned(_commArea.CardNum, 16); // MOVE CDEMO-CARD-NUM TO CC-CARD-NUM-N. source: :491
            Read9000Data(ctx);                // PERFORM 9000-READ-DATA. source: :492-493
            SetCcupShowDetails();             // SET CCUP-SHOW-DETAILS TO TRUE. source: :494
            Send3000Map(ctx);                 // PERFORM 3000-SEND-MAP. source: :495-496
            CommonReturn(ctx);                // GO TO COMMON-RETURN. source: :497
            return;
        }

        // (c) Fresh entry — prompt for keys. source: :502-511
        //   WHEN (CCUP-DETAILS-NOT-FETCHED AND CDEMO-PGM-ENTER)
        //   WHEN (CDEMO-FROM-PROGRAM = LIT-MENUPGM AND NOT CDEMO-PGM-REENTER)
        if ((CcupDetailsNotFetched && _commArea.IsFirstEntry)
            || (_commArea.FromProgram.TrimEnd() == LIT_MENUPGM && !_commArea.IsReenter))
        {
            InitializeProgTail();             // INITIALIZE WS-THIS-PROGCOMMAREA. source: :506
            Send3000Map(ctx);                 // PERFORM 3000-SEND-MAP. source: :507-508
            _commArea.SetReenter();           // SET CDEMO-PGM-REENTER TO TRUE. source: :509
            SetCcupDetailsNotFetched();       // SET CCUP-DETAILS-NOT-FETCHED TO TRUE. source: :510
            CommonReturn(ctx);                // GO TO COMMON-RETURN. source: :511
            return;
        }

        // (d) Done/failed reset — ask for fresh criteria. source: :517-528
        //   WHEN CCUP-CHANGES-OKAYED-AND-DONE / WHEN CCUP-CHANGES-FAILED
        if (CcupChangesOkayedAndDone || CcupChangesFailed)
        {
            InitializeProgTail();             // INITIALIZE WS-THIS-PROGCOMMAREA. source: :519
            InitializeMiscStorage();          // INITIALIZE WS-MISC-STORAGE. source: :520
            _commArea.AcctId = 0;             // INITIALIZE CDEMO-ACCT-ID. source: :521
            _commArea.CardNum = 0;            // INITIALIZE CDEMO-CARD-NUM. source: :522
            _commArea.SetFirstEntry();        // SET CDEMO-PGM-ENTER TO TRUE. source: :523
            Send3000Map(ctx);                 // PERFORM 3000-SEND-MAP. source: :524-525
            _commArea.SetReenter();           // SET CDEMO-PGM-REENTER TO TRUE. source: :526
            SetCcupDetailsNotFetched();       // SET CCUP-DETAILS-NOT-FETCHED TO TRUE. source: :527
            CommonReturn(ctx);                // GO TO COMMON-RETURN. source: :528
            return;
        }

        // (e) WHEN OTHER — normal turn. source: :535-542
        Process1000Inputs(ctx);   // PERFORM 1000-PROCESS-INPUTS. source: :536-537
        Decide2000Action(ctx);    // PERFORM 2000-DECIDE-ACTION. source: :538-539
        Send3000Map(ctx);         // PERFORM 3000-SEND-MAP. source: :540-541
        CommonReturn(ctx);        // GO TO COMMON-RETURN. source: :542
    }

    // =============================================================================================
    //  COMMON-RETURN — source: COCRDUPC.cbl:546-559
    // =============================================================================================
    private void CommonReturn(CicsContext ctx)
    {
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. source: :547
        // (CCARD-ERROR-MSG is staged into ERRMSG by 3250 already; this keeps the COMMAREA-side error in sync.)

        // Reassemble WS-COMMAREA = [CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA] and RETURN. source: :549-558
        // The typed CardDemoCommArea carries the first segment; the program tail is stashed alongside it.
        StashProgTail(ctx);

        // EXEC CICS RETURN TRANSID(LIT-THISTRANID) COMMAREA(WS-COMMAREA) LENGTH(2000). source: :554-558
        ctx.ReturnTransId(LIT_THISTRANID, _commArea);
    }

    // =============================================================================================
    //  0000-MAIN-EXIT — source: COCRDUPC.cbl:560-562
    // =============================================================================================
    private static void Main0000Exit() { /* EXIT. source: :560-562 */ }

    // =============================================================================================
    //  1000-PROCESS-INPUTS — source: COCRDUPC.cbl:564-577
    // =============================================================================================
    private void Process1000Inputs(CicsContext ctx)
    {
        Receive1100Map(ctx);   // PERFORM 1100-RECEIVE-MAP. source: :565-566
        Edit1200MapInputs();   // PERFORM 1200-EDIT-MAP-INPUTS. source: :567-568
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG ; LIT-THISPGM/MAPSET/MAP TO CCARD-NEXT-PROG/MAPSET/MAP.
        // (CCARD-* are staging fields; the COMMAREA-side error is rendered via ERRMSGO at SEND time.) source: :569-572
    }

    // =============================================================================================
    //  1100-RECEIVE-MAP — source: COCRDUPC.cbl:578-640
    // =============================================================================================
    private void Receive1100Map(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP(LIT-THISMAP) MAPSET(LIT-THISMAPSET) INTO(CCRDUPAI) RESP RESP2. source: :579-584
        ctx.ReceiveMap(LIT_THISMAP, LIT_THISMAPSET, _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;

        // INITIALIZE CCUP-NEW-DETAILS. source: :586
        InitializeNewDetails();

        // REPLACE * WITH LOW-VALUES, per field. source: :589-635
        string acctsidi = _map.Field("ACCTSID").Value;     // ACCTSIDI OF CCRDUPAI
        if (acctsidi == "*" || IsSpaces(acctsidi) || string.IsNullOrEmpty(acctsidi))
        {
            _ccAcctId = LowValues(11);   // MOVE LOW-VALUES TO CC-ACCT-ID. source: :591
            _newAcctId = LowValues(11);  //                  CCUP-NEW-ACCTID. source: :592
        }
        else
        {
            _ccAcctId = PadX(acctsidi, 11);  // MOVE ACCTSIDI TO CC-ACCT-ID. source: :594
            _newAcctId = PadX(acctsidi, 11); //               CCUP-NEW-ACCTID. source: :595
        }

        string cardsidi = _map.Field("CARDSID").Value;     // CARDSIDI OF CCRDUPAI
        if (cardsidi == "*" || IsSpaces(cardsidi) || string.IsNullOrEmpty(cardsidi))
        {
            _ccCardNum = LowValues(16);  // MOVE LOW-VALUES TO CC-CARD-NUM. source: :600
            _newCardId = LowValues(16);  //                  CCUP-NEW-CARDID. source: :601
        }
        else
        {
            _ccCardNum = PadX(cardsidi, 16);  // MOVE CARDSIDI TO CC-CARD-NUM. source: :603
            _newCardId = PadX(cardsidi, 16);  //               CCUP-NEW-CARDID. source: :604
        }

        string crdnamei = _map.Field("CRDNAME").Value;     // CRDNAMEI OF CCRDUPAI
        if (crdnamei == "*" || IsSpaces(crdnamei) || string.IsNullOrEmpty(crdnamei))
            _newCrdName = LowValues(50);  // MOVE LOW-VALUES TO CCUP-NEW-CRDNAME. source: :609
        else
            _newCrdName = PadX(crdnamei, 50); // MOVE CRDNAMEI TO CCUP-NEW-CRDNAME. source: :611

        string crdstcdi = _map.Field("CRDSTCD").Value;     // CRDSTCDI OF CCRDUPAI
        if (crdstcdi == "*" || IsSpaces(crdstcdi) || string.IsNullOrEmpty(crdstcdi))
            _newCrdStcd = LowValues(1);   // MOVE LOW-VALUES TO CCUP-NEW-CRDSTCD. source: :616
        else
            _newCrdStcd = PadX(crdstcdi, 1); // MOVE CRDSTCDI TO CCUP-NEW-CRDSTCD. source: :618

        // FB-8.6: EXPDAYI moved unconditionally (no '*'/space scrub). source: :621
        _newExpDay = PadX(_map.Field("EXPDAY").Value, 2);

        string expmoni = _map.Field("EXPMON").Value;       // EXPMONI OF CCRDUPAI
        if (expmoni == "*" || IsSpaces(expmoni) || string.IsNullOrEmpty(expmoni))
            _newExpMon = LowValues(2);    // MOVE LOW-VALUES TO CCUP-NEW-EXPMON. source: :625
        else
            _newExpMon = PadX(expmoni, 2); // MOVE EXPMONI TO CCUP-NEW-EXPMON. source: :627

        string expyeari = _map.Field("EXPYEAR").Value;     // EXPYEARI OF CCRDUPAI
        if (expyeari == "*" || IsSpaces(expyeari) || string.IsNullOrEmpty(expyeari))
            _newExpYear = LowValues(4);   // MOVE LOW-VALUES TO CCUP-NEW-EXPYEAR. source: :632
        else
            _newExpYear = PadX(expyeari, 4); // MOVE EXPYEARI TO CCUP-NEW-EXPYEAR. source: :634
    }

    // =============================================================================================
    //  1200-EDIT-MAP-INPUTS — source: COCRDUPC.cbl:641-719
    // =============================================================================================
    private void Edit1200MapInputs()
    {
        SetInputOk(); // SET INPUT-OK TO TRUE. source: :643

        // IF CCUP-DETAILS-NOT-FETCHED — validate the search keys. source: :645-665
        if (CcupDetailsNotFetched)
        {
            Edit1210Account();      // PERFORM 1210-EDIT-ACCOUNT. source: :647-648
            Edit1220Card();         // PERFORM 1220-EDIT-CARD. source: :650-651
            NewCardDataToLowValues(); // MOVE LOW-VALUES TO CCUP-NEW-CARDDATA. source: :653

            // IF FLG-ACCTFILTER-BLANK AND FLG-CARDFILTER-BLANK -> NO-SEARCH-CRITERIA-RECEIVED. source: :656-659
            if (FlgAcctFilterBlank && FlgCardFilterBlank)
                _wsReturnMsg = NO_SEARCH_CRITERIA_RECEIVED;

            return; // GO TO 1200-EDIT-MAP-INPUTS-EXIT. source: :661
        }

        // ELSE — search keys already validated and data fetched (edit phase). source: :663-677
        SetFoundCardsForAccount();  // SET FOUND-CARDS-FOR-ACCOUNT TO TRUE. source: :668
        SetFlgAcctFilterIsValid();  // SET FLG-ACCTFILTER-ISVALID  TO TRUE. source: :669
        SetFlgCardFilterIsValid();  // SET FLG-CARDFILTER-ISVALID  TO TRUE. source: :670
        _commArea.AcctId = ParseLong(_oldAcctId);  // MOVE CCUP-OLD-ACCTID TO CDEMO-ACCT-ID. source: :671
        _commArea.CardNum = ParseLong(_oldCardId); // MOVE CCUP-OLD-CARDID TO CDEMO-CARD-NUM. source: :672
        // MOVE CCUP-OLD-CRDNAME/CRDSTCD/EXPDAY/EXPMON/EXPYEAR TO CARD-* display fields. source: :673-677
        CardEmbossedName = PadX(_oldCrdName, 50);
        CardActiveStatus = PadX(_oldCrdStcd, 1);
        // (CARD-EXPIRY-DAY/MONTH/YEAR are the X(10) date redefine slices; carried for fidelity.)

        // NEW DATA IS SAME AS OLD DATA — case-insensitive whole-CARDDATA compare. source: :680-683
        if (UpperCase(NewCardData) == UpperCase(OldCardData))
            _wsReturnMsg = NO_CHANGES_DETECTED; // SET NO-CHANGES-DETECTED TO TRUE.

        // IF NO-CHANGES-DETECTED OR CHANGES-OK-NOT-CONFIRMED OR CHANGES-OKAYED-AND-DONE -> all valid, skip. source: :685-693
        if (NoChangesDetected || CcupChangesOkNotConfirmed || CcupChangesOkayedAndDone)
        {
            SetFlgCardNameIsValid();    // source: :688
            SetFlgCardStatusIsValid();  // source: :689
            SetFlgCardExpMonIsValid();  // source: :690
            SetFlgCardExpYearIsValid(); // source: :691
            return; // GO TO 1200-EDIT-MAP-INPUTS-EXIT. source: :692
        }

        SetCcupChangesNotOk(); // SET CCUP-CHANGES-NOT-OK TO TRUE ('E'). source: :696

        Edit1230Name();        // PERFORM 1230-EDIT-NAME. source: :698-699
        Edit1240CardStatus();  // PERFORM 1240-EDIT-CARDSTATUS. source: :701-702
        Edit1250ExpiryMon();   // PERFORM 1250-EDIT-EXPIRY-MON. source: :704-705
        Edit1260ExpiryYear();  // PERFORM 1260-EDIT-EXPIRY-YEAR. source: :707-708

        // IF INPUT-ERROR CONTINUE ELSE SET CCUP-CHANGES-OK-NOT-CONFIRMED. source: :710-714
        if (InputError)
        {
            /* CONTINUE */
        }
        else
        {
            SetCcupChangesOkNotConfirmed(); // ('N')
        }
    }

    // =============================================================================================
    //  1210-EDIT-ACCOUNT — source: COCRDUPC.cbl:721-760
    // =============================================================================================
    private void Edit1210Account()
    {
        SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :722

        // Not supplied: IF CC-ACCT-ID = LOW-VALUES/SPACES OR CC-ACCT-ID-N = ZEROS. source: :725-736
        if (IsLowValues(_ccAcctId) || IsSpaces(_ccAcctId) || CcAcctIdN == 0)
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :728
            SetFlgAcctFilterBlank();  // SET FLG-ACCTFILTER-BLANK TO TRUE. source: :729
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :730
                _wsReturnMsg = WS_PROMPT_FOR_ACCT; // SET WS-PROMPT-FOR-ACCT TO TRUE. source: :731
            _commArea.AcctId = 0;     // MOVE ZEROES TO CDEMO-ACCT-ID. source: :733
            _newAcctId = LowValues(11); // MOVE LOW-VALUES TO CCUP-NEW-ACCTID. source: :734
            return;                   // GO TO 1210-EDIT-ACCOUNT-EXIT. source: :735
        }

        // Not numeric / not 11 characters: IF CC-ACCT-ID IS NOT NUMERIC. source: :740-755
        if (!IsNumericX(_ccAcctId, 11))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :741
            SetFlgAcctFilterNotOk();  // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :742
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :743
                _wsReturnMsg = "ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER"; // source: :744-746
            _commArea.AcctId = 0;     // MOVE ZERO TO CDEMO-ACCT-ID. source: :748
            _newAcctId = LowValues(11); // MOVE LOW-VALUES TO CCUP-NEW-ACCTID. source: :749
            return;                   // GO TO 1210-EDIT-ACCOUNT-EXIT. source: :750
        }
        else
        {
            _commArea.AcctId = ParseLong(_ccAcctId); // MOVE CC-ACCT-ID TO CDEMO-ACCT-ID. source: :752
            _newAcctId = PadX(_ccAcctId, 11);        //                  CCUP-NEW-ACCTID. source: :753
            SetFlgAcctFilterIsValid();               // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :754
        }
    }

    // =============================================================================================
    //  1220-EDIT-CARD — source: COCRDUPC.cbl:762-804
    // =============================================================================================
    private void Edit1220Card()
    {
        SetFlgCardFilterNotOk(); // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :765

        // Not supplied: IF CC-CARD-NUM = LOW-VALUES/SPACES OR CC-CARD-NUM-N = ZEROS. source: :768-780
        if (IsLowValues(_ccCardNum) || IsSpaces(_ccCardNum) || CcCardNumN == 0)
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :771
            SetFlgCardFilterBlank();  // SET FLG-CARDFILTER-BLANK TO TRUE. source: :772
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :773
                _wsReturnMsg = WS_PROMPT_FOR_CARD; // SET WS-PROMPT-FOR-CARD TO TRUE. source: :774
            _commArea.CardNum = 0;    // MOVE ZEROES TO CDEMO-CARD-NUM. source: :777
            _newCardId = Zoned(0, 16); // (same MOVE ZEROES TO CCUP-NEW-CARDID). source: :777-778
            return;                   // GO TO 1220-EDIT-CARD-EXIT. source: :779
        }

        // Not numeric / not 16 characters: IF CC-CARD-NUM IS NOT NUMERIC. source: :784-799
        if (!IsNumericX(_ccCardNum, 16))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :785
            SetFlgCardFilterNotOk();  // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :786
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :787
                _wsReturnMsg = "CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER"; // source: :788-790
            _commArea.CardNum = 0;    // MOVE ZERO TO CDEMO-CARD-NUM. source: :792
            _newCardId = LowValues(16); // MOVE LOW-VALUES TO CCUP-NEW-CARDID. source: :793
            return;                   // GO TO 1220-EDIT-CARD-EXIT. source: :794
        }
        else
        {
            _commArea.CardNum = CcCardNumN;   // MOVE CC-CARD-NUM-N TO CDEMO-CARD-NUM. source: :796
            _newCardId = PadX(_ccCardNum, 16); // MOVE CC-CARD-NUM TO CCUP-NEW-CARDID. source: :797
            SetFlgCardFilterIsValid();        // SET FLG-CARDFILTER-ISVALID TO TRUE. source: :798
        }
    }

    // =============================================================================================
    //  1230-EDIT-NAME — source: COCRDUPC.cbl:806-843
    // =============================================================================================
    private void Edit1230Name()
    {
        SetFlgCardNameNotOk(); // SET FLG-CARDNAME-NOT-OK TO TRUE. source: :808

        // Not supplied: IF CCUP-NEW-CRDNAME = LOW-VALUES/SPACES/ZEROS (FB-8.5). source: :811-820
        if (IsLowValues(_newCrdName) || IsSpaces(_newCrdName) || IsZeroChars(_newCrdName))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :814
            SetFlgCardNameBlank();    // SET FLG-CARDNAME-BLANK TO TRUE. source: :815
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :816
                _wsReturnMsg = WS_PROMPT_FOR_NAME; // SET WS-PROMPT-FOR-NAME TO TRUE. source: :817
            return;                   // GO TO 1230-EDIT-NAME-EXIT. source: :819
        }

        // Only Alphabets and space allowed: strip A-Z/a-z to spaces, then TRIM must be empty. source: :823-837
        // MOVE CCUP-NEW-CRDNAME TO CARD-NAME-CHECK ; INSPECT CONVERTING alpha->spaces.
        string cardNameCheck = ConvertAlphaToSpaces(PadX(_newCrdName, 50));
        if (cardNameCheck.Trim().Length == 0)
        {
            /* CONTINUE — letters & spaces only. source: :828-829 */
        }
        else
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :831
            SetFlgCardNameNotOk();    // SET FLG-CARDNAME-NOT-OK TO TRUE. source: :832
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :833
                _wsReturnMsg = WS_NAME_MUST_BE_ALPHA; // SET WS-NAME-MUST-BE-ALPHA TO TRUE. source: :834
            return;                   // GO TO 1230-EDIT-NAME-EXIT. source: :836
        }

        SetFlgCardNameIsValid(); // SET FLG-CARDNAME-ISVALID TO TRUE. source: :839
    }

    // =============================================================================================
    //  1240-EDIT-CARDSTATUS — source: COCRDUPC.cbl:845-876
    // =============================================================================================
    private void Edit1240CardStatus()
    {
        SetFlgCardStatusNotOk(); // SET FLG-CARDSTATUS-NOT-OK TO TRUE. source: :847

        // Not supplied: IF CCUP-NEW-CRDSTCD = LOW-VALUES/SPACES/ZEROS. source: :850-859
        if (IsLowValues(_newCrdStcd) || IsSpaces(_newCrdStcd) || IsZeroChars(_newCrdStcd))
        {
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :853
            SetFlgCardStatusBlank();   // SET FLG-CARDSTATUS-BLANK TO TRUE. source: :854
            if (WsReturnMsgOff)        // IF WS-RETURN-MSG-OFF. source: :855
                _wsReturnMsg = CARD_STATUS_MUST_BE_YES_NO; // source: :856
            return;                    // GO TO 1240-EDIT-CARDSTATUS-EXIT. source: :858
        }

        // MOVE CCUP-NEW-CRDSTCD TO FLG-YES-NO-CHECK ; IF FLG-YES-NO-VALID ('Y'/'N'). source: :861-872
        char yn = PadX(_newCrdStcd, 1)[0];
        if (yn == 'Y' || yn == 'N')
        {
            SetFlgCardStatusIsValid(); // SET FLG-CARDSTATUS-ISVALID TO TRUE. source: :864
        }
        else
        {
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :866
            SetFlgCardStatusNotOk();   // SET FLG-CARDSTATUS-NOT-OK TO TRUE. source: :867
            if (WsReturnMsgOff)        // IF WS-RETURN-MSG-OFF. source: :868
                _wsReturnMsg = CARD_STATUS_MUST_BE_YES_NO; // source: :869
            return;                    // GO TO 1240-EDIT-CARDSTATUS-EXIT. source: :871
        }
    }

    // =============================================================================================
    //  1250-EDIT-EXPIRY-MON — source: COCRDUPC.cbl:877-912
    // =============================================================================================
    private void Edit1250ExpiryMon()
    {
        SetFlgCardExpMonNotOk(); // SET FLG-CARDEXPMON-NOT-OK TO TRUE. source: :880

        // Not supplied: IF CCUP-NEW-EXPMON = LOW-VALUES/SPACES/ZEROS. source: :883-892
        if (IsLowValues(_newExpMon) || IsSpaces(_newExpMon) || IsZeroChars(_newExpMon))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :886
            SetFlgCardExpMonBlank();  // SET FLG-CARDEXPMON-BLANK TO TRUE. source: :887
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :888
                _wsReturnMsg = CARD_EXPIRY_MONTH_NOT_VALID; // source: :889
            return;                   // GO TO 1250-EDIT-EXPIRY-MON-EXIT. source: :891
        }

        // MOVE CCUP-NEW-EXPMON TO CARD-MONTH-CHECK ; IF VALID-MONTH (1..12). source: :896-907
        if (IsNumericX(_newExpMon, 2) && ParseLong(_newExpMon) is >= 1 and <= 12)
        {
            SetFlgCardExpMonIsValid(); // SET FLG-CARDEXPMON-ISVALID TO TRUE. source: :899
        }
        else
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :901
            SetFlgCardExpMonNotOk();  // SET FLG-CARDEXPMON-NOT-OK TO TRUE. source: :902
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :903
                _wsReturnMsg = CARD_EXPIRY_MONTH_NOT_VALID; // source: :904
            return;                   // GO TO 1250-EDIT-EXPIRY-MON-EXIT. source: :906
        }
    }

    // =============================================================================================
    //  1260-EDIT-EXPIRY-YEAR — source: COCRDUPC.cbl:913-947
    // =============================================================================================
    private void Edit1260ExpiryYear()
    {
        // Note: the not-supplied check comes BEFORE SET FLG-CARDEXPYEAR-NOT-OK (order differs). source: :916-925
        if (IsLowValues(_newExpYear) || IsSpaces(_newExpYear) || IsZeroChars(_newExpYear))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :919
            SetFlgCardExpYearBlank(); // SET FLG-CARDEXPYEAR-BLANK TO TRUE. source: :920
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :921
                _wsReturnMsg = CARD_EXPIRY_YEAR_NOT_VALID; // source: :922
            return;                   // GO TO 1260-EDIT-EXPIRY-YEAR-EXIT. source: :924
        }

        SetFlgCardExpYearNotOk(); // SET FLG-CARDEXPYEAR-NOT-OK TO TRUE. source: :930

        // MOVE CCUP-NEW-EXPYEAR TO CARD-YEAR-CHECK ; IF VALID-YEAR (1950..2099) (FB-8.8). source: :932-943
        if (IsNumericX(_newExpYear, 4) && ParseLong(_newExpYear) is >= 1950 and <= 2099)
        {
            SetFlgCardExpYearIsValid(); // SET FLG-CARDEXPYEAR-ISVALID TO TRUE. source: :935
        }
        else
        {
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :937
            SetFlgCardExpYearNotOk();  // SET FLG-CARDEXPYEAR-NOT-OK TO TRUE. source: :938
            if (WsReturnMsgOff)        // IF WS-RETURN-MSG-OFF. source: :939
                _wsReturnMsg = CARD_EXPIRY_YEAR_NOT_VALID; // source: :940
            return;                    // GO TO 1260-EDIT-EXPIRY-YEAR-EXIT. source: :942
        }
    }

    // =============================================================================================
    //  2000-DECIDE-ACTION — source: COCRDUPC.cbl:948-1031 (EVALUATE TRUE)
    // =============================================================================================
    private void Decide2000Action(CicsContext ctx)
    {
        // WHEN CCUP-DETAILS-NOT-FETCHED / WHEN CCARD-AID-PFK12 (shared). source: :954-966
        if (CcupDetailsNotFetched || CcardAidPfk12)
        {
            if (FlgAcctFilterIsValid && FlgCardFilterIsValid) // source: :959-960
            {
                Read9000Data(ctx);          // PERFORM 9000-READ-DATA. source: :961-962
                if (FoundCardsForAccount)   // IF FOUND-CARDS-FOR-ACCOUNT. source: :963
                    SetCcupShowDetails();   // SET CCUP-SHOW-DETAILS TO TRUE. source: :964
            }
        }
        // WHEN CCUP-SHOW-DETAILS. source: :971-977
        else if (CcupShowDetails)
        {
            if (InputError || NoChangesDetected) // source: :972-973
            {
                /* CONTINUE. source: :974 */
            }
            else
            {
                SetCcupChangesOkNotConfirmed(); // SET CCUP-CHANGES-OK-NOT-CONFIRMED TO TRUE. source: :976
            }
        }
        // WHEN CCUP-CHANGES-NOT-OK. source: :982-983
        else if (CcupChangesNotOk)
        {
            /* CONTINUE. source: :983 */
        }
        // WHEN CCUP-CHANGES-OK-NOT-CONFIRMED AND CCARD-AID-PFK05 — save. source: :988-1001
        else if (CcupChangesOkNotConfirmed && CcardAidPfk05)
        {
            WriteProcessing9200(ctx); // PERFORM 9200-WRITE-PROCESSING. source: :990-991
            // EVALUATE TRUE on the resulting message. source: :992-1001
            if (CouldNotLockForUpdate)
                SetCcupChangesOkayedLockError();  // SET CCUP-CHANGES-OKAYED-LOCK-ERROR ('L'). source: :993-994
            else if (LockedButUpdateFailed)
                SetCcupChangesOkayedButFailed();  // SET CCUP-CHANGES-OKAYED-BUT-FAILED ('F'). source: :995-996
            else if (DataWasChangedBeforeUpdate)
                SetCcupShowDetails();             // SET CCUP-SHOW-DETAILS. source: :997-998
            else
                SetCcupChangesOkayedAndDone();    // WHEN OTHER -> SET CCUP-CHANGES-OKAYED-AND-DONE ('C'). source: :999-1000
        }
        // WHEN CCUP-CHANGES-OK-NOT-CONFIRMED (no PF5). source: :1006-1007
        else if (CcupChangesOkNotConfirmed)
        {
            /* CONTINUE. source: :1007 */
        }
        // WHEN CCUP-CHANGES-OKAYED-AND-DONE. source: :1011-1018
        else if (CcupChangesOkayedAndDone)
        {
            SetCcupShowDetails(); // SET CCUP-SHOW-DETAILS TO TRUE. source: :1012
            if (IsLowValuesOrSpaces(_commArea.FromTranId)) // source: :1013-1014
            {
                _commArea.AcctId = 0;       // MOVE ZEROES TO CDEMO-ACCT-ID. source: :1015
                _commArea.CardNum = 0;      //              CDEMO-CARD-NUM. source: :1016
                _commArea.AcctStatus = "\0"; // MOVE LOW-VALUES TO CDEMO-ACCT-STATUS. source: :1017
            }
        }
        // WHEN OTHER — unexpected data scenario -> ABEND. source: :1019-1026
        else
        {
            // MOVE LIT-THISPGM TO ABEND-CULPRIT; '0001' TO ABEND-CODE; SPACES TO ABEND-REASON;
            // 'UNEXPECTED DATA SCENARIO' TO ABEND-MSG ; PERFORM ABEND-ROUTINE. source: :1020-1025
            AbendRoutine(ctx);
        }
    }

    // =============================================================================================
    //  2000-DECIDE-ACTION-EXIT — source: COCRDUPC.cbl:1029-1031
    // =============================================================================================

    // =============================================================================================
    //  3000-SEND-MAP — source: COCRDUPC.cbl:1035-1049
    // =============================================================================================
    private void Send3000Map(CicsContext ctx)
    {
        Screen3100Init(ctx);        // PERFORM 3100-SCREEN-INIT. source: :1036-1037
        Screen3200SetupVars();      // PERFORM 3200-SETUP-SCREEN-VARS. source: :1038-1039
        Screen3250SetupInfoMsg();   // PERFORM 3250-SETUP-INFOMSG. source: :1040-1041
        Screen3300SetupAttrs();     // PERFORM 3300-SETUP-SCREEN-ATTRS. source: :1042-1043
        Screen3400SendScreen(ctx);  // PERFORM 3400-SEND-SCREEN. source: :1044-1045
    }

    // =============================================================================================
    //  3100-SCREEN-INIT — source: COCRDUPC.cbl:1052-1080
    // =============================================================================================
    private void Screen3100Init(CicsContext ctx)
    {
        // MOVE LOW-VALUES TO CCRDUPAO. source: :1053
        MoveLowValuesToMapOut();

        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA (twice). source: :1055,1062
        DateTime now = ctx.Clock.Now;

        // MOVE CCDA-TITLE01/02, LIT-THISTRANID, LIT-THISPGM. source: :1057-1060
        _map.Field("TITLE01").SetValue(CCDA_TITLE01);   // MOVE CCDA-TITLE01 TO TITLE01O. source: :1057
        _map.Field("TITLE02").SetValue(CCDA_TITLE02);   // MOVE CCDA-TITLE02 TO TITLE02O. source: :1058
        _map.Field("TRNNAME").SetValue(LIT_THISTRANID); // MOVE LIT-THISTRANID TO TRNNAMEO. source: :1059
        _map.Field("PGMNAME").SetValue(LIT_THISPGM);    // MOVE LIT-THISPGM    TO PGMNAMEO. source: :1060

        // CURDATEO = mm/dd/yy. source: :1064-1068
        string mm = Two(now.Month);
        string dd = Two(now.Day);
        string yy = Four(now.Year).Substring(2, 2);
        _map.Field("CURDATE").SetValue($"{mm}/{dd}/{yy}"); // WS-CURDATE-MM-DD-YY -> CURDATEO. source: :1068

        // CURTIMEO = hh:mm:ss. source: :1070-1074
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}");
    }

    // =============================================================================================
    //  3200-SETUP-SCREEN-VARS — source: COCRDUPC.cbl:1082-1137
    // =============================================================================================
    private void Screen3200SetupVars()
    {
        // IF CDEMO-PGM-ENTER CONTINUE (leave the search fields blank). source: :1084-1085
        if (_commArea.IsFirstEntry)
            return;

        // ACCTSIDO = LOW-VALUES if CC-ACCT-ID-N = 0 else CC-ACCT-ID. source: :1087-1091
        if (CcAcctIdN == 0)
            _map.Field("ACCTSID").SetValue("", setMdt: false);
        else
            _map.Field("ACCTSID").SetValue(_ccAcctId, setMdt: false);

        // CARDSIDO = LOW-VALUES if CC-CARD-NUM-N = 0 else CC-CARD-NUM. source: :1093-1097
        if (CcCardNumN == 0)
            _map.Field("CARDSID").SetValue("", setMdt: false);
        else
            _map.Field("CARDSID").SetValue(_ccCardNum, setMdt: false);

        // EVALUATE TRUE for the editable data fields. source: :1099-1130
        if (CcupDetailsNotFetched)
        {
            // MOVE LOW-VALUES TO name/status/day/mon/year out-fields. source: :1100-1106
            _map.Field("CRDNAME").SetValue("", setMdt: false);
            _map.Field("CRDSTCD").SetValue("", setMdt: false);
            _map.Field("EXPDAY").SetValue("", setMdt: false);
            _map.Field("EXPMON").SetValue("", setMdt: false);
            _map.Field("EXPYEAR").SetValue("", setMdt: false);
        }
        else if (CcupShowDetails)
        {
            // MOVE CCUP-OLD-* into the out-fields. source: :1107-1112
            _map.Field("CRDNAME").SetValue(_oldCrdName, setMdt: false);
            _map.Field("CRDSTCD").SetValue(_oldCrdStcd, setMdt: false);
            _map.Field("EXPDAY").SetValue(_oldExpDay, setMdt: false);
            _map.Field("EXPMON").SetValue(_oldExpMon, setMdt: false);
            _map.Field("EXPYEAR").SetValue(_oldExpYear, setMdt: false);
        }
        else if (CcupChangesMade)
        {
            // MOVE CCUP-NEW-* into name/status/mon/year. source: :1113-1117
            _map.Field("CRDNAME").SetValue(_newCrdName, setMdt: false);
            _map.Field("CRDSTCD").SetValue(_newCrdStcd, setMdt: false);
            _map.Field("EXPMON").SetValue(_newExpMon, setMdt: false);
            _map.Field("EXPYEAR").SetValue(_newExpYear, setMdt: false);
            // FB-8.6: EXPDAYO always re-sent from CCUP-OLD-EXPDAY (not NEW). source: :1122-1123
            _map.Field("EXPDAY").SetValue(_oldExpDay, setMdt: false);
        }
        else
        {
            // WHEN OTHER — MOVE CCUP-OLD-* into all out-fields. source: :1124-1129
            _map.Field("CRDNAME").SetValue(_oldCrdName, setMdt: false);
            _map.Field("CRDSTCD").SetValue(_oldCrdStcd, setMdt: false);
            _map.Field("EXPDAY").SetValue(_oldExpDay, setMdt: false);
            _map.Field("EXPMON").SetValue(_oldExpMon, setMdt: false);
            _map.Field("EXPYEAR").SetValue(_oldExpYear, setMdt: false);
        }
    }

    // =============================================================================================
    //  3250-SETUP-INFOMSG — source: COCRDUPC.cbl:1138-1167 (EVALUATE TRUE)
    // =============================================================================================
    private void Screen3250SetupInfoMsg()
    {
        if (_commArea.IsFirstEntry)                 SetPromptForSearchKeys();   // source: :1141-1142
        else if (CcupDetailsNotFetched)             SetPromptForSearchKeys();   // source: :1143-1144
        else if (CcupShowDetails)                   SetFoundCardsForAccount();  // source: :1145-1146
        else if (CcupChangesNotOk)                  SetPromptForChanges();      // source: :1147-1148
        else if (CcupChangesOkNotConfirmed)         SetPromptForConfirmation(); // source: :1149-1150
        else if (CcupChangesOkayedAndDone)          SetConfirmUpdateSuccess();  // source: :1151-1152
        else if (CcupChangesOkayedLockError)        SetInformFailure();         // source: :1153-1154
        else if (CcupChangesOkayedButFailed)        SetInformFailure();         // source: :1155-1156
        else if (WsNoInfoMessage)                   SetPromptForSearchKeys();   // source: :1157-1158

        // MOVE WS-INFO-MSG TO INFOMSGO. source: :1161
        _map.Field("INFOMSG").SetValue(_wsInfoMsg, setMdt: false);

        // MOVE WS-RETURN-MSG TO ERRMSGO. source: :1163
        _map.Field("ERRMSG").SetValue(_wsReturnMsg, setMdt: false);
    }

    // =============================================================================================
    //  3300-SETUP-SCREEN-ATTRS — source: COCRDUPC.cbl:1168-1321
    // =============================================================================================
    private void Screen3300SetupAttrs()
    {
        ScreenField acctsid = _map.Field("ACCTSID");
        ScreenField cardsid = _map.Field("CARDSID");
        ScreenField crdname = _map.Field("CRDNAME");
        ScreenField crdstcd = _map.Field("CRDSTCD");
        ScreenField expmon = _map.Field("EXPMON");
        ScreenField expyear = _map.Field("EXPYEAR");
        ScreenField expday = _map.Field("EXPDAY");

        // (1) PROTECT OR UNPROTECT BASED ON CONTEXT. source: :1172-1208
        //   DFHBMFSE = unprotect + FSET (modified) ; DFHBMPRF = protect.
        BmsAttribute fse = BmsAttribute.Unprotected | BmsAttribute.Fset;
        BmsAttribute prf = BmsAttribute.Protected;
        if (CcupDetailsNotFetched)
        {
            // unprotect ACCTSID/CARDSID ; protect name/status/mon/year. source: :1173-1180
            acctsid.AttributeOverride = fse;
            cardsid.AttributeOverride = fse;
            crdname.AttributeOverride = prf;
            crdstcd.AttributeOverride = prf;
            expmon.AttributeOverride = prf;
            expyear.AttributeOverride = prf;
        }
        else if (CcupShowDetails || CcupChangesNotOk)
        {
            // protect ACCTSID/CARDSID ; unprotect name/status/mon/year. source: :1181-1190
            acctsid.AttributeOverride = prf;
            cardsid.AttributeOverride = prf;
            crdname.AttributeOverride = fse;
            crdstcd.AttributeOverride = fse;
            expmon.AttributeOverride = fse;
            expyear.AttributeOverride = fse;
        }
        else if (CcupChangesOkNotConfirmed || CcupChangesOkayedAndDone)
        {
            // protect everything. source: :1191-1199
            acctsid.AttributeOverride = prf;
            cardsid.AttributeOverride = prf;
            crdname.AttributeOverride = prf;
            crdstcd.AttributeOverride = prf;
            expmon.AttributeOverride = prf;
            expyear.AttributeOverride = prf;
        }
        else
        {
            // WHEN OTHER — unprotect ACCTSID/CARDSID ; protect the rest. source: :1200-1207
            acctsid.AttributeOverride = fse;
            cardsid.AttributeOverride = fse;
            crdname.AttributeOverride = prf;
            crdstcd.AttributeOverride = prf;
            expmon.AttributeOverride = prf;
            expyear.AttributeOverride = prf;
        }

        // (2) POSITION CURSOR — MOVE -1 to one length field. source: :1210-1235
        if (FoundCardsForAccount || NoChangesDetected)        crdname.CursorLength = -1; // source: :1212-1214
        else if (FlgAcctFilterNotOk || FlgAcctFilterBlank)    acctsid.CursorLength = -1; // source: :1215-1217
        else if (FlgCardFilterNotOk || FlgCardFilterBlank)    cardsid.CursorLength = -1; // source: :1218-1220
        else if (FlgCardNameNotOk || FlgCardNameBlank)        crdname.CursorLength = -1; // source: :1221-1223
        else if (FlgCardStatusNotOk || FlgCardStatusBlank)    crdstcd.CursorLength = -1; // source: :1224-1226
        else if (FlgCardExpMonNotOk || FlgCardExpMonBlank)    expmon.CursorLength = -1;  // source: :1227-1229
        else if (FlgCardExpYearNotOk || FlgCardExpYearBlank)  expyear.CursorLength = -1; // source: :1230-1232
        else                                                  acctsid.CursorLength = -1; // WHEN OTHER. source: :1233-1234

        // (3) SETUP COLOR / '*' placeholders. source: :1237-1318
        // IF CDEMO-LAST-MAPSET = LIT-CCLISTMAPSET -> ACCTSIDC/CARDSIDC default. source: :1238-1241
        if (_commArea.LastMapSet.TrimEnd() == LIT_CCLISTMAPSET)
        {
            acctsid.ColorOverride = BmsColor.Default;
            cardsid.ColorOverride = BmsColor.Default;
        }

        if (FlgAcctFilterNotOk)                                 // source: :1243-1245
            acctsid.ColorOverride = BmsColor.Red;
        if (FlgAcctFilterBlank && _commArea.IsReenter)         // source: :1247-1251
        {
            acctsid.SetValue("*", setMdt: false);
            acctsid.ColorOverride = BmsColor.Red;
        }

        if (FlgCardFilterNotOk)                                 // source: :1253-1255
            cardsid.ColorOverride = BmsColor.Red;
        if (FlgCardFilterBlank && _commArea.IsReenter)         // source: :1257-1261
        {
            cardsid.SetValue("*", setMdt: false);
            cardsid.ColorOverride = BmsColor.Red;
        }

        if (FlgCardNameNotOk && CcupChangesNotOk)              // source: :1263-1266
            crdname.ColorOverride = BmsColor.Red;
        if (FlgCardNameBlank && CcupChangesNotOk)             // source: :1268-1272
        {
            crdname.SetValue("*", setMdt: false);
            crdname.ColorOverride = BmsColor.Red;
        }

        if (FlgCardStatusNotOk && CcupChangesNotOk)           // source: :1274-1277
            crdstcd.ColorOverride = BmsColor.Red;
        if (FlgCardStatusBlank && CcupChangesNotOk)          // source: :1279-1283
        {
            crdstcd.SetValue("*", setMdt: false);
            crdstcd.ColorOverride = BmsColor.Red;
        }

        // MOVE DFHBMDAR TO EXPDAYC — expiry day always dark. source: :1285
        expday.AttributeOverride = (expday.AttributeOverride ?? expday.Attribute) | BmsAttribute.Dark;

        if (FlgCardExpMonNotOk && CcupChangesNotOk)           // source: :1287-1290
            expmon.ColorOverride = BmsColor.Red;
        if (FlgCardExpMonBlank && CcupChangesNotOk)          // source: :1292-1296
        {
            expmon.SetValue("*", setMdt: false);
            expmon.ColorOverride = BmsColor.Red;
        }

        if (FlgCardExpYearNotOk && CcupChangesNotOk)          // source: :1298-1301
            expyear.ColorOverride = BmsColor.Red;
        if (FlgCardExpYearBlank && CcupChangesNotOk)         // source: :1303-1307
        {
            expyear.SetValue("*", setMdt: false);
            expyear.ColorOverride = BmsColor.Red;
        }

        // INFOMSGA: WS-NO-INFO-MESSAGE -> DFHBMDAR (dark) else DFHBMBRY (bright). source: :1309-1313
        ScreenField infomsg = _map.Field("INFOMSG");
        if (WsNoInfoMessage)
            infomsg.AttributeOverride = infomsg.Attribute | BmsAttribute.Dark;
        else
            infomsg.AttributeOverride = infomsg.Attribute | BmsAttribute.Bright;

        // IF PROMPT-FOR-CONFIRMATION MOVE DFHBMBRY TO FKEYSCA (bright). source: :1315-1317
        if (PromptForConfirmation)
        {
            ScreenField fkeysc = _map.Field("FKEYSC");
            fkeysc.AttributeOverride = (fkeysc.Attribute & ~BmsAttribute.Dark) | BmsAttribute.Bright;
        }
    }

    // =============================================================================================
    //  3400-SEND-SCREEN — source: COCRDUPC.cbl:1324-1340
    // =============================================================================================
    private void Screen3400SendScreen(CicsContext ctx)
    {
        // MOVE LIT-THISMAPSET TO CCARD-NEXT-MAPSET ; MOVE LIT-THISMAP TO CCARD-NEXT-MAP. source: :1326-1327
        // EXEC CICS SEND MAP(CCARD-NEXT-MAP) MAPSET(CCARD-NEXT-MAPSET) FROM(CCRDUPAO) CURSOR ERASE FREEKB
        //   RESP(WS-RESP-CD). source: :1329-1336
        ctx.SendMap(LIT_THISMAP, LIT_THISMAPSET, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
        _wsRespCd = (int)Resp.Normal;
    }

    // =============================================================================================
    //  9000-READ-DATA — source: COCRDUPC.cbl:1343-1374
    // =============================================================================================
    private void Read9000Data(CicsContext ctx)
    {
        // INITIALIZE CCUP-OLD-DETAILS. source: :1345
        InitializeOldDetails();
        _oldAcctId = PadX(_ccAcctId, 11);  // MOVE CC-ACCT-ID  TO CCUP-OLD-ACCTID. source: :1346
        _oldCardId = PadX(_ccCardNum, 16); // MOVE CC-CARD-NUM TO CCUP-OLD-CARDID. source: :1347

        Read9100GetCardByAcctCard(ctx);    // PERFORM 9100-GETCARD-BYACCTCARD. source: :1349-1350

        // IF FOUND-CARDS-FOR-ACCOUNT — capture the OLD snapshot. source: :1352-1369
        if (FoundCardsForAccount)
        {
            _oldCvvCd = Zoned(CardCvvCd, 3); // MOVE CARD-CVV-CD TO CCUP-OLD-CVV-CD (9(3) -> X(3)). source: :1354

            // INSPECT CARD-EMBOSSED-NAME CONVERTING LIT-LOWER TO LIT-UPPER (uppercase in place). source: :1356-1358
            CardEmbossedName = UpperCase(CardEmbossedName);

            _oldCrdName = PadX(CardEmbossedName, 50);            // MOVE CARD-EMBOSSED-NAME TO CCUP-OLD-CRDNAME. source: :1360
            _oldExpYear = Slice(CardExpiraionDate, 0, 4);       // (1:4) -> CCUP-OLD-EXPYEAR. source: :1361-1362
            _oldExpMon = Slice(CardExpiraionDate, 5, 2);        // (6:2) -> CCUP-OLD-EXPMON. source: :1363-1364
            _oldExpDay = Slice(CardExpiraionDate, 8, 2);        // (9:2) -> CCUP-OLD-EXPDAY. source: :1365-1366
            _oldCrdStcd = PadX(CardActiveStatus, 1);            // MOVE CARD-ACTIVE-STATUS TO CCUP-OLD-CRDSTCD. source: :1367
        }
    }

    // =============================================================================================
    //  9100-GETCARD-BYACCTCARD — READ CARDDAT by primary key (card number). source: COCRDUPC.cbl:1376-1417
    // =============================================================================================
    private void Read9100GetCardByAcctCard(CicsContext ctx)
    {
        // FB-8.1: MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID is commented out; only the card number is keyed.
        _wsCardRidCardnum = PadX(_ccCardNum, 16); // MOVE CC-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :1380

        // EXEC CICS READ FILE(LIT-CARDFILENAME) RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) INTO(CARD-RECORD)
        //   LENGTH(150) RESP RESP2. source: :1382-1390
        _ = LIT_CARDFILENAME;
        var repo = new CardRepository(_db.Connection);
        string fileStatus = repo.ReadByKey(_wsCardRidCardnum, out _cardRecord);
        SetResp(fileStatus);
        LoadCardRecord(); // mirror the INTO(CARD-RECORD) copy into the working CARD fields.

        // EVALUATE WS-RESP-CD. source: :1392-1412
        if (_wsRespCd == (int)Resp.Normal)
        {
            SetFoundCardsForAccount(); // WHEN NORMAL — SET FOUND-CARDS-FOR-ACCOUNT TO TRUE. source: :1393-1394
        }
        else if (_wsRespCd == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :1395-1401
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :1396
            SetFlgAcctFilterNotOk();  // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :1397
            SetFlgCardFilterNotOk();  // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :1398
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :1399
                _wsReturnMsg = DID_NOT_FIND_ACCTCARD_COMBO; // SET DID-NOT-FIND-ACCTCARD-COMBO. source: :1400
        }
        else
        {
            // WHEN OTHER — hard read error. source: :1402-1411
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :1403
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :1404
                SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :1405
            _errorOpname = PadX("READ", 8);             // MOVE 'READ' TO ERROR-OPNAME. source: :1407
            _errorFile = PadX(LIT_CARDFILENAME, 9);     // MOVE LIT-CARDFILENAME TO ERROR-FILE. source: :1408
            _errorResp = Alpha(_wsRespCd, 10);          // MOVE WS-RESP-CD TO ERROR-RESP. source: :1409
            _errorResp2 = Alpha(_wsReasCd, 10);         // MOVE WS-REAS-CD TO ERROR-RESP2. source: :1410
            _wsReturnMsg = Truncate75(BuildFileErrorMessage()); // MOVE WS-FILE-ERROR-MESSAGE TO WS-RETURN-MSG. source: :1411
        }
    }

    // =============================================================================================
    //  9200-WRITE-PROCESSING — READ UPDATE -> optimistic check -> REWRITE. source: COCRDUPC.cbl:1420-1496
    // =============================================================================================
    private void WriteProcessing9200(CicsContext ctx)
    {
        // FB-8.1: acct-id RIDFLD line commented out; key on card number only. source: :1424-1425
        _wsCardRidCardnum = PadX(_ccCardNum, 16); // MOVE CC-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :1425

        // EXEC CICS READ FILE(LIT-CARDFILENAME) UPDATE RIDFLD(...) INTO(CARD-RECORD). source: :1427-1436
        var repo = new CardRepository(_db.Connection);
        string fileStatus = repo.ReadByKey(_wsCardRidCardnum, out _cardRecord);
        SetResp(fileStatus);
        LoadCardRecord();

        // Could we lock the record? IF WS-RESP-CD = NORMAL CONTINUE ELSE could-not-lock. source: :1441-1449
        if (_wsRespCd == (int)Resp.Normal)
        {
            /* CONTINUE. source: :1442 */
        }
        else
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :1444
            if (WsReturnMsgOff)       // IF WS-RETURN-MSG-OFF. source: :1445
                _wsReturnMsg = COULD_NOT_LOCK_FOR_UPDATE; // SET COULD-NOT-LOCK-FOR-UPDATE. source: :1446
            return;                   // GO TO 9200-WRITE-PROCESSING-EXIT. source: :1448
        }

        // PERFORM 9300-CHECK-CHANGE-IN-REC ; IF DATA-WAS-CHANGED-BEFORE-UPDATE -> exit. source: :1453-1457
        bool unwound = CheckChangeInRec9300();
        if (unwound || DataWasChangedBeforeUpdate)
            return; // GO TO 9200-WRITE-PROCESSING-EXIT. source: :1456 (and FB-8.4 cross-paragraph GO TO)

        // Prepare the update: build CARD-UPDATE-RECORD. source: :1461-1475
        // INITIALIZE CARD-UPDATE-RECORD (numeric -> 0, alphanumeric -> spaces). source: :1461
        // FB-8.3: CVV chain — MOVE CCUP-NEW-CVV-CD (never populated) TO CARD-CVV-CD-X ; CARD-CVV-CD-N -> update.
        _cardCvvCdX = PadX(_newCvvCd, 3); // MOVE CCUP-NEW-CVV-CD TO CARD-CVV-CD-X. source: :1464
        int updateCvv = CardCvvCdN;       // MOVE CARD-CVV-CD-N TO CARD-UPDATE-CVV-CD (non-digits -> 0). source: :1465

        // STRING NEW-EXPYEAR '-' NEW-EXPMON '-' NEW-EXPDAY DELIMITED BY SIZE INTO CARD-UPDATE-EXPIRAION-DATE. source: :1467-1474
        string updateExpDate = ClampOrPad(PadX(_newExpYear, 4) + "-" + PadX(_newExpMon, 2) + "-" + PadX(_newExpDay, 2), 10);

        var upd = new Card
        {
            CardNum = PadX(_newCardId, 16).TrimEnd(),    // MOVE CCUP-NEW-CARDID TO CARD-UPDATE-NUM. source: :1462
            AcctId = CcAcctIdN,                          // FB-8.2: MOVE CC-ACCT-ID-N TO CARD-UPDATE-ACCT-ID. source: :1463
            CvvCd = updateCvv,                           // FB-8.3: zeroed/garbled CVV. source: :1465
            EmbossedName = PadX(_newCrdName, 50),        // MOVE CCUP-NEW-CRDNAME TO CARD-UPDATE-EMBOSSED-NAME. source: :1466
            ExpirationDate = updateExpDate,              // source: :1467-1474
            ActiveStatus = PadX(_newCrdStcd, 1),         // MOVE CCUP-NEW-CRDSTCD TO CARD-UPDATE-ACTIVE-STATUS. source: :1475
        };

        // EXEC CICS REWRITE FILE(LIT-CARDFILENAME) FROM(CARD-UPDATE-RECORD) LENGTH(150). source: :1477-1483
        string rewriteStatus = repo.Update(upd);
        SetResp(rewriteStatus);

        // IF WS-RESP-CD = NORMAL CONTINUE ELSE SET LOCKED-BUT-UPDATE-FAILED. source: :1488-1492
        if (_wsRespCd == (int)Resp.Normal)
        {
            /* CONTINUE. source: :1489 */
        }
        else
        {
            _wsReturnMsg = LOCKED_BUT_UPDATE_FAILED; // SET LOCKED-BUT-UPDATE-FAILED TO TRUE. source: :1491
        }
    }

    // =============================================================================================
    //  9300-CHECK-CHANGE-IN-REC — optimistic re-compare. source: COCRDUPC.cbl:1498-1523
    //  Returns true when the mismatch branch unwound out of 9200 (FB-8.4 cross-paragraph GO TO).
    // =============================================================================================
    private bool CheckChangeInRec9300()
    {
        // INSPECT CARD-EMBOSSED-NAME CONVERTING LIT-LOWER TO LIT-UPPER (uppercase in place). source: :1499-1501
        CardEmbossedName = UpperCase(CardEmbossedName);

        // Compare the just-read CARD fields to the OLD snapshot. source: :1503-1508
        bool same =
            Zoned(CardCvvCd, 3) == PadX(_oldCvvCd, 3)
            && PadX(CardEmbossedName, 50) == PadX(_oldCrdName, 50)
            && Slice(CardExpiraionDate, 0, 4) == PadX(_oldExpYear, 4)
            && Slice(CardExpiraionDate, 5, 2) == PadX(_oldExpMon, 2)
            && Slice(CardExpiraionDate, 8, 2) == PadX(_oldExpDay, 2)
            && PadX(CardActiveStatus, 1) == PadX(_oldCrdStcd, 1);

        if (same)
        {
            return false; // CONTINUE (no change). source: :1509
        }
        else
        {
            // SET DATA-WAS-CHANGED-BEFORE-UPDATE ; refresh the OLD snapshot with the just-read values. source: :1511-1517
            _wsReturnMsg = DATA_WAS_CHANGED_BEFORE_UPDATE;
            _oldCvvCd = Zoned(CardCvvCd, 3);
            _oldCrdName = PadX(CardEmbossedName, 50);
            _oldExpYear = Slice(CardExpiraionDate, 0, 4);
            _oldExpMon = Slice(CardExpiraionDate, 5, 2);
            _oldExpDay = Slice(CardExpiraionDate, 8, 2);
            _oldCrdStcd = PadX(CardActiveStatus, 1);
            return true; // GO TO 9200-WRITE-PROCESSING-EXIT (FB-8.4). source: :1518
        }
    }

    // =============================================================================================
    //  YYYY-STORE-PFKEY (copybook CSSTRPFY) — source: CSSTRPFY.cpy:17-82; COCRDUPC.cbl:406-407
    // =============================================================================================
    private void Yyyy_StorePfkey(CicsContext ctx) => _ccardAid = ctx.StorePfKey();

    // =============================================================================================
    //  ABEND-ROUTINE — source: COCRDUPC.cbl:1531-1556
    // =============================================================================================
    private void AbendRoutine(CicsContext ctx)
    {
        // IF ABEND-MSG = LOW-VALUES MOVE 'UNEXPECTED ABEND OCCURRED.' TO ABEND-MSG. source: :1533-1535
        // MOVE LIT-THISPGM TO ABEND-CULPRIT ; SEND FROM(ABEND-DATA) NOHANDLE ERASE ; HANDLE ABEND CANCEL ;
        // ABEND ABCODE('9999'). source: :1537-1552. Headless model: emit the message and end the conversation.
        if (ctx.Outcome is null)
        {
            ctx.SendText("UNEXPECTED ABEND OCCURRED.", erase: true, freeKb: true);
            ctx.ReturnTerminal();
        }
    }

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE builder. source: COCRDUPC.cbl:133-152
    // =============================================================================================
    // 'File Error: ' OPNAME(8) ' on ' FILE(9) ' returned RESP ' RESP(10) ',RESP2 ' RESP2(10) 5 spaces.
    private string BuildFileErrorMessage() =>
        "File Error: " + _errorOpname + " on " + _errorFile + " returned RESP " +
        _errorResp + ",RESP2 " + _errorResp2 + "     ";

    // =============================================================================================
    //  Program-tail / misc-storage (re)initialisation helpers.
    // =============================================================================================
    // INITIALIZE WS-THIS-PROGCOMMAREA — reset state machine + OLD/NEW snapshots. source: :392,506,519
    private void InitializeProgTail()
    {
        SetCcupDetailsNotFetched();
        InitializeOldDetails();
        InitializeNewDetails();
    }

    private void InitializeOldDetails()
    {
        _oldAcctId = PadX("", 11); _oldCardId = PadX("", 16); _oldCvvCd = PadX("", 3);
        _oldCrdName = PadX("", 50); _oldExpYear = PadX("", 4); _oldExpMon = PadX("", 2);
        _oldExpDay = PadX("", 2); _oldCrdStcd = PadX("", 1);
    }

    // INITIALIZE WS-MISC-STORAGE — clear edit flags + messages. source: :520
    private void InitializeMiscStorage()
    {
        _wsInputFlag = '\0'; _wsEditAcctFlag = '\0'; _wsEditCardFlag = '\0';
        _wsEditCardNameFlag = '\0'; _wsEditCardStatusFlag = '\0';
        _wsEditCardExpMonFlag = '\0'; _wsEditCardExpYearFlag = '\0';
        _wsReturnFlag = '\0'; _wsPfkFlag = '\0';
        _wsInfoMsg = ""; _wsReturnMsg = "";
    }

    // Copy the INTO(CARD-RECORD) result into the working CARD-* fields (mirrors the COBOL record). source: :1386
    private void LoadCardRecord()
    {
        CardEmbossedName = PadX(_cardRecord?.EmbossedName ?? "", 50);
        CardExpiraionDate = PadX(_cardRecord?.ExpirationDate ?? "", 10);
        CardCvvCd = _cardRecord?.CvvCd ?? 0;
        CardActiveStatus = PadX(_cardRecord?.ActiveStatus ?? "", 1);
    }

    // ---- program-tail transport across the pseudo-conversational turn ----
    // The typed CardDemoCommArea carries CARDDEMO-COMMAREA; the program tail is stashed in a per-instance
    // static keyed by the COMMAREA image so it survives the RETURN/RECEIVE round trip in the console runtime.
    private static readonly Dictionary<string, ProgTail> _tailStore = new();

    private sealed record ProgTail(
        char ChangeAction,
        string OldAcctId, string OldCardId, string OldCvvCd, string OldCrdName,
        string OldExpYear, string OldExpMon, string OldExpDay, string OldCrdStcd,
        string NewAcctId, string NewCardId, string NewCvvCd, string NewCrdName,
        string NewExpYear, string NewExpMon, string NewExpDay, string NewCrdStcd);

    private void StashProgTail(CicsContext ctx)
    {
        string key = _commArea.ToImage();
        _tailStore[key] = new ProgTail(
            _ccupChangeAction,
            _oldAcctId, _oldCardId, _oldCvvCd, _oldCrdName, _oldExpYear, _oldExpMon, _oldExpDay, _oldCrdStcd,
            _newAcctId, _newCardId, _newCvvCd, _newCrdName, _newExpYear, _newExpMon, _newExpDay, _newCrdStcd);
    }

    private void RestoreProgTail(CicsContext ctx)
    {
        string key = ctx.CommArea!.ToImage();
        if (_tailStore.TryGetValue(key, out ProgTail? t))
        {
            _ccupChangeAction = t.ChangeAction;
            _oldAcctId = t.OldAcctId; _oldCardId = t.OldCardId; _oldCvvCd = t.OldCvvCd; _oldCrdName = t.OldCrdName;
            _oldExpYear = t.OldExpYear; _oldExpMon = t.OldExpMon; _oldExpDay = t.OldExpDay; _oldCrdStcd = t.OldCrdStcd;
            _newAcctId = t.NewAcctId; _newCardId = t.NewCardId; _newCvvCd = t.NewCvvCd; _newCrdName = t.NewCrdName;
            _newExpYear = t.NewExpYear; _newExpMon = t.NewExpMon; _newExpDay = t.NewExpDay; _newCrdStcd = t.NewCrdStcd;
        }
        else
        {
            // No stashed tail (e.g. carried by an external caller) — INITIALIZE-equivalent.
            InitializeProgTail();
        }
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>Maps a repository FileStatus to the CICS RESP/RESP2 the EVALUATE branches on.</summary>
    private void SetResp(string fileStatus)
    {
        _wsRespCd = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 (reason) unavailable from the relational repo; 0 for parity.
    }

    /// <summary>
    /// MOVE LOW-VALUES TO CCRDUPAO — blank every named output field and clear per-turn overrides before the
    /// first SEND. source: COCRDUPC.cbl:1053.
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

    /// <summary>Clamp-or-pad to width (STRING DELIMITED BY SIZE into a fixed field).</summary>
    private static string ClampOrPad(string? value, int width) => PadX(value, width);

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

    /// <summary>FUNCTION UPPER-CASE — only A-Z/a-z folded; everything else (incl. LOW-VALUES) preserved.</summary>
    private static string UpperCase(string? s) => (s ?? "").ToUpperInvariant();

    /// <summary>
    /// INSPECT ... CONVERTING LIT-ALL-ALPHA-FROM TO LIT-ALL-SPACES-TO: every A-Z/a-z byte becomes a space;
    /// all other bytes (digits, punctuation, spaces, LOW-VALUES) are kept. source: COCRDUPC.cbl:824-826.
    /// </summary>
    private static string ConvertAlphaToSpaces(string s)
    {
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (LIT_ALL_ALPHA_FROM.IndexOf(chars[i]) >= 0)
                chars[i] = ' ';
        return new string(chars);
    }

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
    /// COBOL <c>field EQUAL ZEROS</c> on an alphanumeric X(n): true when every character is the digit '0'.
    /// Used by 1230/1240/1250/1260 not-supplied tests (FB-8.5). source: COCRDUPC.cbl:813,852,885,918.
    /// </summary>
    private static bool IsZeroChars(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (c != '0') return false;
        return true;
    }

    /// <summary>
    /// COBOL class test <c>field IS NOT NUMERIC</c> on the X(width) field: every character of the fixed
    /// width must be a digit '0'-'9' (spaces/low-values fail). Returns true when the field IS numeric.
    /// source: COCRDUPC.cbl:740,784.
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
    //  BMS map builder — CCRDUPA in mapset COCRDUP (24x80).
    //  source: app/bms/COCRDUP.bms:20-169 / SCREEN_COCRDUP.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COCRDUP.bms:25.</summary>
    public const string MapName = LIT_THISMAP;       // "CCRDUPA"

    /// <summary>The DFHMSD mapset name. source: COCRDUP.bms:20.</summary>
    public const string MapsetName = LIT_THISMAPSET; // "COCRDUP"

    /// <summary>
    /// Constructs the <c>CCRDUPA</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the IC cursor on <c>ACCTSID</c>, the
    /// protected literals and zero-length stoppers, and the named in/out fields — in BMS source order. The
    /// keyable filter fields are <c>ACCTSID</c> (7,45) L11 (IC) and <c>CARDSID</c> (8,45) L16; the editable
    /// detail fields are <c>CRDNAME</c> (11,25) L50, <c>CRDSTCD</c> (13,25) L1, <c>EXPMON</c> (15,25) L2 and
    /// <c>EXPYEAR</c> (15,30) L4 (both JUSTIFY=RIGHT), and the protected/dark <c>EXPDAY</c> (15,36) L2. No
    /// PICIN/PICOUT clauses appear in this map.
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

            // ----- 'Update Credit Card Details' heading (default ATTRB -> ASKIP, NEUTRAL) -----
            LitAttr(4, 30, 26, AskipDefault, BmsColor.Neutral, "Update Credit Card Details"), // bms:75-78

            // ----- Account / Card filter labels + input fields -----
            Lit(7, 23, 19, BmsColor.Turquoise, "Account Number    :"),            // bms:79-83
            // ACCTSID: ATTRB=(FSET,IC,NORM,PROT) DEFAULT UNDERLINE — the IC (initial cursor) field.
            new ScreenField
            {
                Name = "ACCTSID", Row = 7, Col = 45, Length = 11,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Protected,
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

            // ----- Name on card (label TURQUOISE default ATTRB; CRDNAME unprotected editable) -----
            LitAttr(11, 4, 20, AskipDefault, BmsColor.Turquoise, "Name on card      :"), // bms:103-106
            // CRDNAME: ATTRB=(UNPROT); HILIGHT=UNDERLINE, no COLOR.
            OutHi("CRDNAME", 11, 25, 50, BmsAttribute.Unprotected, BmsColor.Default, BmsHilight.Underline), // bms:107-110
            Stopper(11, 76),                                                       // bms:111-112

            // ----- Card Active Y/N (label + CRDSTCD UNPROT) -----
            LitAttr(13, 4, 20, AskipDefault, BmsColor.Turquoise, "Card Active Y/N   : "), // bms:113-116
            OutHi("CRDSTCD", 13, 25, 1, BmsAttribute.Unprotected, BmsColor.Default, BmsHilight.Underline), // bms:117-120
            Stopper(13, 27),                                                       // bms:121-122

            // ----- Expiry Date (label + EXPMON / '/' / EXPYEAR + EXPDAY) -----
            LitAttr(15, 4, 20, AskipDefault, BmsColor.Turquoise, "Expiry Date       : "), // bms:123-126
            // EXPMON: ATTRB=(UNPROT) HILIGHT=UNDERLINE JUSTIFY=(RIGHT) L2.
            new ScreenField
            {
                Name = "EXPMON", Row = 15, Col = 25, Length = 2,
                Attribute = BmsAttribute.Unprotected, Color = BmsColor.Default,
                Hilight = BmsHilight.Underline, RightJustify = true,
            },                                                                     // bms:127-131
            LitAttr(15, 28, 1, AskipDefault, BmsColor.Default, "/"),              // bms:132-134 (date separator)
            // EXPYEAR: ATTRB=(UNPROT) HILIGHT=UNDERLINE JUSTIFY=(RIGHT) L4.
            new ScreenField
            {
                Name = "EXPYEAR", Row = 15, Col = 30, Length = 4,
                Attribute = BmsAttribute.Unprotected, Color = BmsColor.Default,
                Hilight = BmsHilight.Underline, RightJustify = true,
            },                                                                     // bms:135-139
            Stopper(15, 35),                                                       // bms:140-141
            // EXPDAY: ATTRB=(DRK,FSET,PROT) HILIGHT=OFF JUSTIFY=(RIGHT) L2 — protected & dark.
            new ScreenField
            {
                Name = "EXPDAY", Row = 15, Col = 36, Length = 2,
                Attribute = BmsAttribute.Dark | BmsAttribute.Fset | BmsAttribute.Protected,
                Color = BmsColor.Default, Hilight = BmsHilight.Off, RightJustify = true,
            },                                                                     // bms:142-146
            Stopper(15, 39),                                                       // bms:147-148

            // ----- info line (PROT NEUTRAL, HILIGHT=OFF) -----
            new ScreenField
            {
                Name = "INFOMSG", Row = 20, Col = 25, Length = 40,
                Attribute = BmsAttribute.Protected,
                Color = BmsColor.Neutral,
                Hilight = BmsHilight.Off,
            },                                                                     // bms:149-153

            // ----- error line (ASKIP,BRT,FSET RED, L80) -----
            Out("ERRMSG", 23, 1, 80, AskipBrtFset, BmsColor.Red),                 // bms:154-157

            // ----- footer F-key legends -----
            Lit(24, 1, 21, BmsColor.Yellow, "ENTER=Process F3=Exit"),            // bms:158-162
            // FKEYSC: ATTRB=(ASKIP,DRK) YELLOW L18 — normally dark, made bright when prompting for confirm.
            new ScreenField
            {
                Name = "FKEYSC", Row = 24, Col = 23, Length = 18,
                Attribute = BmsAttribute.AutoSkip | BmsAttribute.Dark,
                Color = BmsColor.Yellow,
                Value = "F5=Save F12=Cancel",
            },                                                                     // bms:163-167
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
