# PORT SPEC — COACTVWC (Account View, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COACTVWC.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COACTVW.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COACTVW.CPY`
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.Data` (repositories) per ARCHITECTURE.md.

All line citations use the form `// source: COACTVWC.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COACTVWC is the CICS pseudo-conversational "Account View" transaction. It accepts an 11-digit account number on a 24×80 BMS screen, validates it, then reads three keyed files to assemble and display a read-only account + customer detail screen: it resolves the account to a customer via the **card cross-reference alternate index** (by ACCT-ID), then reads the **account master** and **customer master**. It never updates anything — it is a pure inquiry/display screen. `// source: COACTVWC.cbl:1-5`

**Invocation.**
- CICS TRANSID **`CAVW`** (`LIT-THISTRANID`). `// source: COACTVWC.cbl:145-146`
- Program id **`COACTVWC`** (`LIT-THISPGM`). `// source: COACTVWC.cbl:143-144`
- Mapset **`COACTVW`** / map **`CACTVWA`**. `// source: COACTVWC.cbl:147-150`
- Reached by `EXEC CICS XCTL` from the main menu (`COMEN01C`/`CM00`) or from another transaction that sets `CDEMO-TO-PROGRAM='COACTVWC'`; it is pseudo-conversational and re-drives itself via `RETURN TRANSID('CAVW')`. `// source: COACTVWC.cbl:402-406`
- It is **not** a called subroutine; flow control is entirely via COMMAREA + XCTL/RETURN.

**Note on declared XCTL targets that are unused here.** WORKING-STORAGE declares literals for COCRDLIC/CCLI, COCRDUPC/CCUP, COCRDSLC/CCDL (`// source: COACTVWC.cbl:151-183`) but the PROCEDURE DIVISION never XCTLs to them — the only XCTL is the PF3 path to `CDEMO-TO-PROGRAM`. They are dead literals; do not implement transfers to them.

---

## 2. FILE / TABLE ACCESS

All three files are accessed **READ by key only** (random keyed read). No browse, no write/rewrite/delete. Per ARCHITECTURE.md §"VSAM-semantics -> SQL mapping", each `EXEC CICS READ ... RIDFLD` becomes a `SELECT ... WHERE pk = @key`; RESP `NORMAL`→found ('00'), `NOTFND`→not found ('23'/'13'), anything else→error.

| COBOL DATASET (DDNAME literal) | Logical file | ARCH table | Key used (RIDFLD) | CICS op | SQL equivalent | Source |
|---|---|---|---|---|---|---|
| `CXACAIX` (`LIT-CARDXREFNAME-ACCT-PATH`) | Card-xref **alternate index by ACCT-ID** | **CARD_XREF** | `WS-CARD-RID-ACCT-ID-X` X(11) | READ (alt-key, returns first/unique row) | `SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF WHERE acct_id = @acctId` | `// source: COACTVWC.cbl:192-193,727-735` |
| `ACCTDAT` (`LIT-ACCTFILENAME`) | Account master | **ACCOUNT** | `WS-CARD-RID-ACCT-ID-X` X(11) | READ (primary key) | `SELECT * FROM ACCOUNT WHERE acct_id = @acctId` | `// source: COACTVWC.cbl:184-185,776-784` |
| `CUSTDAT` (`LIT-CUSTFILENAME`) | Customer master | **CUSTOMER** | `WS-CARD-RID-CUST-ID-X` X(9) | READ (primary key) | `SELECT * FROM CUSTOMER WHERE cust_id = @custId` | `// source: COACTVWC.cbl:188-189,826-834` |

Repository contract notes:
- **CXACAIX** is the CARD_XREF file accessed on its *acct_id* index (the base CARD_XREF PK is `xref_card_num`). In the relational model this is just `WHERE acct_id = @acctId`. The mainframe alt-index over ACCTDAT-equivalent returns a single record per acct in this data set; reproduce by returning the first matching row (ORDER BY xref_card_num for determinism if duplicates ever exist). `// source: COACTVWC.cbl:725-735`
- The key field passed is the **redefinition `-X` (alphanumeric X(11)/X(9))** of the numeric RID, i.e. zero-padded character form of the account/customer id. `// source: COACTVWC.cbl:73-80, 729, 778, 828`
- The numeric IDs are filled from `CDEMO-ACCT-ID` PIC 9(11) / `CDEMO-CUST-ID` PIC 9(09) (mapped to `long`). `// source: COACTVWC.cbl:691, 708`

---

## 3. DATA STRUCTURES USED

- **CARD-XREF-RECORD** (`CVACT03Y`): `XREF-CARD-NUM X(16)`, `XREF-CUST-ID 9(09)`, `XREF-ACCT-ID 9(11)`, FILLER X(14). Only `XREF-CUST-ID`→`CDEMO-CUST-ID` and `XREF-CARD-NUM`→`CDEMO-CARD-NUM` are consumed. `// source: COACTVWC.cbl:739-740`, `CVACT03Y.cpy:4-8`
- **ACCOUNT-RECORD** (`CVACT01Y`): acct id, active status X1, three S9(10)V99 money amounts (curr-bal, credit-limit, cash-credit-limit), open/expiraion/reissue date X10, curr-cyc-credit, curr-cyc-debit S9(10)V99, addr-zip X10, group-id X10. `// source: CVACT01Y.cpy:4-17`
- **CUSTOMER-RECORD** (`CVCUS01Y`): cust id 9(9), names X25×3, addr lines X50×3, state X2, country X3, zip X10, phones X15×2, SSN 9(9), govt id X20, DOB X10, EFT acct id X10, pri-card-holder-ind X1, FICO 9(3). `// source: CVCUS01Y.cpy:4-23`
- **CARD-RECORD** (`CVACT02Y`) is COPYed (`// source: COACTVWC.cbl:248`) but **never referenced** in PROCEDURE DIVISION — dead copybook. Do not port.
- **COMMAREA**: `CARDDEMO-COMMAREA` (COCOM01Y) + appended `WS-THIS-PROGCOMMAREA` (CA-FROM-PROGRAM X8 / CA-FROM-TRANID X4). `// source: COACTVWC.cbl:211-216`, `COCOM01Y.cpy:19-44`

---

## 4. COMMAREA FIELDS (CARDDEMO-COMMAREA, COCOM01Y)

Fields actually read/written by this program:
- `CDEMO-FROM-TRANID` X4, `CDEMO-FROM-PROGRAM` X8 — set/checked for menu-origin & PF3 return. `// source: COACTVWC.cbl:283, 328-342`
- `CDEMO-TO-TRANID` X4, `CDEMO-TO-PROGRAM` X8 — XCTL target on PF3. `// source: COACTVWC.cbl:330-339, 350`
- `CDEMO-USER-TYPE` (88 `CDEMO-USRTYP-USER`='U') — set to USER on PF3. `// source: COACTVWC.cbl:344`
- `CDEMO-PGM-CONTEXT` 9(1): `CDEMO-PGM-ENTER`=0, `CDEMO-PGM-REENTER`=1 — drives the main dispatch. `// source: COCOM01Y.cpy:29-31; COACTVWC.cbl:345,581`
- `CDEMO-CUST-ID` 9(9), `CDEMO-CARD-NUM` 9(16) — filled from xref read. `// source: COACTVWC.cbl:739-740`
- `CDEMO-ACCT-ID` 9(11), `CDEMO-ACCT-STATUS` X1 — acct id from edit; status not set here. `// source: COACTVWC.cbl:660,675,678`
- `CDEMO-LAST-MAP` X7, `CDEMO-LAST-MAPSET` X7 — set on PF3 path. `// source: COACTVWC.cbl:346-347`

Appended trailer (`WS-THIS-PROGCOMMAREA`): `CA-FROM-PROGRAM`/`CA-FROM-TRANID` — INITIALIZEd, parsed from/written to the 2000-byte commarea tail, but not otherwise used in logic. `// source: COACTVWC.cbl:213-216, 288-292, 398-400`

The on-the-wire COMMAREA is a fixed `WS-COMMAREA PIC X(2000)` split as `[CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA]`. Port: model COMMAREA as a typed object whose first segment is CARDDEMO-COMMAREA; preserve the 2000-byte length on RETURN. `// source: COACTVWC.cbl:218, 288-292, 397-405`

---

## 5. SCREEN (BMS map CACTVWA / mapset COACTVW)

24×80, CTRL=FREEKB, SIZE=(24,80). `// source: COACTVW.bms:25-28`

### Input field (read from RECEIVE MAP, `CACTVWAI`)
| Field | PIC | BMS attrs | Notes |
|---|---|---|---|
| `ACCTSIDI` | `99999999999` (11) | `FSET,IC,NORM,UNPROT`, GREEN, UNDERLINE, `PICIN='99999999999'`, `VALIDN=(MUSTFILL)` | The ONLY user-enterable field. `// source: COACTVW.bms:84-90`, `COACTVW.CPY:60` |

All other fields are output/display-only. On RECEIVE only `ACCTSIDI` is consumed; the program also reads `EIBAID` for the PF key and `EIBCALEN` for commarea length. `// source: COACTVWC.cbl:628-632`

### Output fields written (SEND MAP FROM `CACTVWAO`)
Header: `TITLE01O`, `TITLE02O`, `TRNNAMEO`, `PGMNAMEO`, `CURDATEO` (mm/dd/yy), `CURTIMEO` (hh:mm:ss). `// source: COACTVWC.cbl:436-453`
Account block: `ACCTSIDO`, `ACSTTUSO` (active Y/N), `ADTOPENO`, `ACRDLIMO`(+ZZZ,ZZZ,ZZZ.99), `AEXPDTO`, `ACSHLIMO`(edited), `AREISDTO`, `ACURBALO`(edited), `ACRCYCRO`(edited), `AADDGRPO`, `ACRCYDBO`(edited). `// source: COACTVWC.cbl:466-490`
Customer block: `ACSTNUMO`, `ACSTSSNO` (formatted NNN-NN-NNNN), `ACSTFCOO`, `ACSTDOBO`, `ACSFNAMO`, `ACSMNAMO`, `ACSLNAMO`, `ACSADL1O`, `ACSADL2O`, `ACSCITYO`, `ACSSTTEO`, `ACSZIPCO`, `ACSCTRYO`, `ACSPHN1O`, `ACSPHN2O`, `ACSGOVTO`, `ACSEFTCO`, `ACSPFLGO`. `// source: COACTVWC.cbl:494-522`
Messages: `ERRMSGO` (from WS-RETURN-MSG), `INFOMSGO` (from WS-INFO-MSG). `// source: COACTVWC.cbl:532-534`

**Edited-numeric output fields.** `ACRDLIMO ACSHLIMO ACURBALO ACRCYCRO ACRCYDBO` are `PIC +ZZZ,ZZZ,ZZZ.99` (`// source: COACTVW.CPY:302,314,326,332,344`). The MOVE of an `S9(10)V99` value into this edited PIC must reproduce COBOL edited-numeric formatting: leading sign (`+`/`-`), zero-suppressed thousands with commas, fixed 2 decimals. Use `CobolEditedNumeric` from CardDemo.Runtime. `// source: COACTVWC.cbl:475-485`

**Attribute / color manipulation (1300-SETUP-SCREEN-ATTRS).**
- `ACCTSIDA := DFHBMFSE` (modified+freekb attribute byte). `// source: COACTVWC.cbl:543`
- Cursor: `ACCTSIDL := -1` in all evaluate branches (i.e. cursor always parked on account field). `// source: COACTVWC.cbl:546-552`
- Color: default `ACCTSIDC := DFHDFCOL`; if `FLG-ACCTFILTER-NOT-OK` → `DFHRED`. `// source: COACTVWC.cbl:555-559`
- If `FLG-ACCTFILTER-BLANK AND CDEMO-PGM-REENTER`: `ACCTSIDO := '*'` and color `DFHRED`. `// source: COACTVWC.cbl:561-565`
- INFOMSG color: no info msg → `DFHBMDAR` (dark/non-display); else `DFHNEUTR`. `// source: COACTVWC.cbl:567-571`

Port the attribute model per ARCHITECTURE.md online shim: store logical attributes (protected/unprotect, color, hilite, cursor position, dark) on the screen field model; the console renderer + screen-parity tests assert these.

---

## 6. PSEUDO-CONVERSATIONAL FLOW

Pseudo-conversational pattern: each invocation does at most one RECEIVE and one SEND, then `EXEC CICS RETURN TRANSID('CAVW') COMMAREA(WS-COMMAREA) LENGTH(2000)`. The `CDEMO-PGM-CONTEXT` flag (ENTER vs REENTER) distinguishes first-display from input-processing turns. `1400-SEND-SCREEN` sets `CDEMO-PGM-REENTER` before each send so the next turn is treated as a re-entry. `// source: COACTVWC.cbl:402-406, 581`

### EIBAID / PFKey handling
`YYYY-STORE-PFKEY` (copybook CSSTRPFY) maps `EIBAID` → `CCARD-AID-*` 88-levels (ENTER, CLEAR, PA1/2, PFK01..PFK12; PF13-24 wrap to PFK01-12). `// source: CSSTRPFY.cpy:21-78`

After mapping, only **ENTER** and **PF03** are accepted; any other AID is remapped to ENTER:
```
SET PFK-INVALID
IF CCARD-AID-ENTER OR CCARD-AID-PFK03 -> SET PFK-VALID
IF PFK-INVALID -> SET CCARD-AID-ENTER          (force unknown keys to behave as ENTER)
```
`// source: COACTVWC.cbl:306-314`

### Main dispatch (`EVALUATE TRUE`, 0000-MAIN)
`// source: COACTVWC.cbl:323-383`
1. **WHEN CCARD-AID-PFK03** — exit path. Compute return TRANID/PROGRAM (use CDEMO-FROM-* if present else menu `CM00`/`COMEN01C`), stamp FROM=this prog/tranid, set USRTYP-USER + PGM-ENTER + LAST-MAP/MAPSET, then `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COACTVWC.cbl:324-352`
2. **WHEN CDEMO-PGM-ENTER** (first entry / fresh context) — `PERFORM 1000-SEND-MAP`; `GO TO COMMON-RETURN`. Shows the empty prompt screen. `// source: COACTVWC.cbl:353-360`
3. **WHEN CDEMO-PGM-REENTER** (returning from a prior send) — `PERFORM 2000-PROCESS-INPUTS`; if `INPUT-ERROR` → resend map & return; else `PERFORM 9000-READ-ACCT` then `1000-SEND-MAP` & return. `// source: COACTVWC.cbl:361-374`
4. **WHEN OTHER** — abend scenario: set ABEND-CULPRIT/CODE='0001', message 'UNEXPECTED DATA SCENARIO', `PERFORM SEND-PLAIN-TEXT` (SEND TEXT + RETURN, terminates conversation). `// source: COACTVWC.cbl:375-382`

After the EVALUATE, a fall-through guard: `IF INPUT-ERROR` → move WS-RETURN-MSG to error field, send map, return. `// source: COACTVWC.cbl:387-392`

### Entry initialization (0000-MAIN top)
- `EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE)`. `// source: COACTVWC.cbl:264-266`
- INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA; set WS-TRANID='CAVW'; SET WS-RETURN-MSG-OFF. `// source: COACTVWC.cbl:268-278`
- **Commarea load rule:** if `EIBCALEN = 0` OR (`CDEMO-FROM-PROGRAM = COMEN01C` AND NOT `CDEMO-PGM-REENTER`) → INITIALIZE CARDDEMO-COMMAREA + WS-THIS-PROGCOMMAREA (fresh); ELSE copy DFHCOMMAREA bytes into CARDDEMO-COMMAREA and the trailer. `// source: COACTVWC.cbl:282-293`

---

## 7. PARAGRAPH-BY-PARAGRAPH OUTLINE (each = one method)

- **0000-MAIN** — HANDLE ABEND; init storages; load/split COMMAREA; PERFORM YYYY-STORE-PFKEY; gate AID to ENTER/PF3 (else force ENTER); EVALUATE dispatch (PF3-XCTL / ENTER-send / REENTER-process / OTHER-abend); fall-through INPUT-ERROR resend; fall into COMMON-RETURN. `// source: COACTVWC.cbl:262-393`
- **COMMON-RETURN** — move WS-RETURN-MSG→CCARD-ERROR-MSG; reassemble WS-COMMAREA from CARDDEMO-COMMAREA + trailer; `EXEC CICS RETURN TRANSID('CAVW') COMMAREA LENGTH(2000)`. `// source: COACTVWC.cbl:394-407`
- **0000-MAIN-EXIT** — EXIT. (Note: paragraph is **duplicated verbatim** — see Faithful Bugs.) `// source: COACTVWC.cbl:408-413`
- **1000-SEND-MAP** — PERFORM 1100→1200→1300→1400 in order. `// source: COACTVWC.cbl:416-425`
- **1100-SCREEN-INIT** — MOVE LOW-VALUES to CACTVWAO; CURRENT-DATE→WS-CURDATE-DATA (twice); set titles, tranid, pgmname; build CURDATEO=mm/dd/yy (year from positions 3:2 of CCYY), CURTIMEO=hh:mm:ss. `// source: COACTVWC.cbl:431-455`
- **1200-SETUP-SCREEN-VARS** — if EIBCALEN=0 set WS-PROMPT-FOR-INPUT; else show ACCTSIDO (LOW-VALUES if filter blank, else CC-ACCT-ID); if account/cust found, move all ACCT-* to screen; if cust found, move all CUST-* incl. **STRING-formatted SSN** `NNN-NN-NNNN`; if no info message set prompt; move WS-RETURN-MSG→ERRMSGO, WS-INFO-MSG→INFOMSGO. `// source: COACTVWC.cbl:460-535`
- **1300-SETUP-SCREEN-ATTRS** — set ACCTSID attribute byte; force cursor to ACCTSID (−1); set color default/red; star+red when blank-on-reenter; INFOMSG dark vs neutral. `// source: COACTVWC.cbl:541-572`
- **1400-SEND-SCREEN** — set CCARD-NEXT-MAP/MAPSET; SET CDEMO-PGM-REENTER; `EXEC CICS SEND MAP(CACTVWA) MAPSET(COACTVW) FROM(CACTVWAO) CURSOR ERASE FREEKB`. `// source: COACTVWC.cbl:577-591`
- **2000-PROCESS-INPUTS** — PERFORM 2100-RECEIVE-MAP; PERFORM 2200-EDIT-MAP-INPUTS; move WS-RETURN-MSG→CCARD-ERROR-MSG; set next prog/map/mapset literals. `// source: COACTVWC.cbl:596-605`
- **2100-RECEIVE-MAP** — `EXEC CICS RECEIVE MAP(CACTVWA) MAPSET(COACTVW) INTO(CACTVWAI) RESP RESP2`. `// source: COACTVWC.cbl:610-617`
- **2200-EDIT-MAP-INPUTS** — SET INPUT-OK + FLG-ACCTFILTER-ISVALID; if ACCTSIDI='*' or spaces → CC-ACCT-ID=LOW-VALUES else CC-ACCT-ID=ACCTSIDI; PERFORM 2210-EDIT-ACCOUNT; cross-field: if FLG-ACCTFILTER-BLANK set NO-SEARCH-CRITERIA-RECEIVED. `// source: COACTVWC.cbl:622-643`
- **2210-EDIT-ACCOUNT** — SET FLG-ACCTFILTER-NOT-OK; if CC-ACCT-ID low-values/spaces → INPUT-ERROR, FLG-ACCTFILTER-BLANK, msg 'Account number not provided' (only if msg-off), CDEMO-ACCT-ID=0, exit; elseif CC-ACCT-ID not numeric OR =zeroes → INPUT-ERROR, NOT-OK, msg 'Account Filter must be a non-zero 11 digit number' (if msg-off), CDEMO-ACCT-ID=0, exit; else CDEMO-ACCT-ID=CC-ACCT-ID, set ISVALID. `// source: COACTVWC.cbl:649-681`
- **9000-READ-ACCT** — SET WS-NO-INFO-MESSAGE; move CDEMO-ACCT-ID→RID-ACCT-ID; PERFORM 9200 (xref); if FLG-ACCTFILTER-NOT-OK exit; PERFORM 9300 (acct); if DID-NOT-FIND-ACCT-IN-ACCTDAT exit; move CDEMO-CUST-ID→RID-CUST-ID; PERFORM 9400 (cust); if DID-NOT-FIND-CUST-IN-CUSTDAT exit. `// source: COACTVWC.cbl:687-718`
- **9200-GETCARDXREF-BYACCT** — READ CXACAIX by acct-id-X; NORMAL→CDEMO-CUST-ID/CARD-NUM from xref; NOTFND→INPUT-ERROR + NOT-OK + 'Account:… not found in Cross ref file…' STRING; OTHER→file-error message. `// source: COACTVWC.cbl:723-770`
- **9300-GETACCTDATA-BYACCT** — READ ACCTDAT by acct-id-X; NORMAL→FOUND-ACCT-IN-MASTER; NOTFND→INPUT-ERROR + NOT-OK + 'Account:… not found in Acct Master file…' STRING; OTHER→file-error. `// source: COACTVWC.cbl:774-823`
- **9400-GETCUSTDATA-BYCUST** — READ CUSTDAT by cust-id-X; NORMAL→FOUND-CUST-IN-MASTER; NOTFND→INPUT-ERROR + FLG-CUSTFILTER-NOT-OK + 'CustId:… not found in customer master…' STRING; OTHER→file-error. `// source: COACTVWC.cbl:825-872`
- **SEND-PLAIN-TEXT** — `EXEC CICS SEND TEXT FROM(WS-RETURN-MSG) ERASE FREEKB`; `EXEC CICS RETURN` (no TRANSID → ends conversation). `// source: COACTVWC.cbl:877-890`
- **SEND-LONG-TEXT** — debug-only SEND TEXT(WS-LONG-MSG)+RETURN; not reached (all callers commented out). `// source: COACTVWC.cbl:896-909`
- **YYYY-STORE-PFKEY** (CSSTRPFY copybook) — EIBAID→CCARD-AID-* mapping. `// source: CSSTRPFY.cpy:17-82`
- **ABEND-ROUTINE** — default ABEND-MSG; set culprit; SEND ABEND-DATA NOHANDLE; HANDLE ABEND CANCEL; `EXEC CICS ABEND ABCODE('9999')`. `// source: COACTVWC.cbl:916-937`

---

## 8. VALIDATION RULES & EXACT LITERAL MESSAGES

Reproduce these literals **byte-for-byte** (including the trailing/double spaces noted).

1. **Account not provided** (blank/spaces/`*`): `Account number not provided` — set via 88 `WS-PROMPT-FOR-ACCT`, only if WS-RETURN-MSG-OFF. `// source: COACTVWC.cbl:121-122, 656-659`
2. **Account not numeric / zero**: literal `Account Filter must  be a non-zero 11 digit number` (note **two spaces** between "must" and "be"), only if WS-RETURN-MSG-OFF. `// source: COACTVWC.cbl:671-673`
   - Compare: the 88-level constants `SEARCHED-ACCT-ZEROES` / `SEARCHED-ACCT-NOT-NUMERIC` = `Account number must be a non zero 11 digit number` (single spaces) are **declared but never SET** — dead. `// source: COACTVWC.cbl:125-128`
3. **Cross-ref not found**: STRING → `Account:` + acct-id-X(11) + ` not found in` + ` Cross ref file.  Resp:` + ERROR-RESP(10) + ` Reas:` + ERROR-RESP2(10) (only if msg-off). `// source: COACTVWC.cbl:747-757`
4. **Account master not found**: STRING → `Account:` + acct-id-X(11) + ` not found in` + ` Acct Master file.Resp:` + ERROR-RESP + ` Reas:` + ERROR-RESP2 (only if msg-off). `// source: COACTVWC.cbl:796-806`
5. **Customer master not found**: STRING → `CustId:` + cust-id-X(9) + ` not found` + ` in customer master.Resp: ` + ERROR-RESP + ` REAS:` + ERROR-RESP2 (only if msg-off; note ERROR-RESP/RESP2 set unconditionally here, before the msg-off guard). `// source: COACTVWC.cbl:843-857`
6. **Generic file error (WHEN OTHER on any read)**: `WS-FILE-ERROR-MESSAGE` = `File Error: ` + ERROR-OPNAME(8,='READ') + ` on ` + ERROR-FILE(9) + ` returned RESP ` + ERROR-RESP(10) + `,RESP2 ` + ERROR-RESP2(10) + 5 spaces. `// source: COACTVWC.cbl:86-105, 762-766`
7. **OTHER dispatch abend**: `UNEXPECTED DATA SCENARIO` via SEND-PLAIN-TEXT. `// source: COACTVWC.cbl:379-381`
8. **Info/prompt messages**: `Enter or update id of account to display` (WS-PROMPT-FOR-INPUT), `Displaying details of given Account` (WS-INFORM-OUTPUT, declared but not SET). `// source: COACTVWC.cbl:113-116`
9. **PF3 exit text**: 88 `WS-EXIT-MESSAGE` = `PF03 pressed.Exiting              ` — declared, not used on the PF3 path (PF3 just XCTLs). `// source: COACTVWC.cbl:119-120`

**Numeric edit semantics.** "not numeric" test = COBOL class test `IS NOT NUMERIC` on the X(11) field `CC-ACCT-ID`; a non-digit (e.g. space-padded) input fails. `CC-ACCT-ID EQUAL ZEROES` also rejected. `// source: COACTVWC.cbl:666-667`

---

## 9. FAITHFUL BUGS (reproduce verbatim; do NOT fix)

1. **Duplicate `0000-MAIN-EXIT` paragraph.** The label `0000-MAIN-EXIT.` with `EXIT.` appears twice in immediate succession. Harmless in COBOL (the second is unreachable/dead), but it is part of the source. Reproduce as a no-op duplicate. `// source: COACTVWC.cbl:408-413`

2. **Stray sequence-number text `00` inside a statement.** Line 672 ends the MOVE literal with a trailing `      00` (columns past the literal). This is a leftover sequence-area artifact compiled as a comment/continuation; the message text itself remains `'Account Filter must  be a non-zero 11 digit number'`. Keep the message exactly (incl. the double space "must  be"). `// source: COACTVWC.cbl:671-673`

3. **Double-spaced / inconsistent error literals.** `'Account Filter must  be a non-zero 11 digit number'` has a double space; the unused 88s use a different wording (`'Account number must be a non zero 11 digit number'`). Use the literal that is actually MOVEd (the double-spaced one). `// source: COACTVWC.cbl:125-128, 671-673`

4. **CUSTDAT NOTFND sets the wrong flag.** On customer not-found it sets `FLG-CUSTFILTER-NOT-OK` but `9000-READ-ACCT` tests `DID-NOT-FIND-CUST-IN-CUSTDAT` (which is **never SET** — the SET is commented out at line 842). Net effect: after a customer NOTFND, INPUT-ERROR is set (so the screen still shows the error via the fall-through at 387), but the `GO TO 9000-READ-ACCT-EXIT` guard at 713-715 is **not** taken because its condition can't be true. Preserve this: do not branch on a customer-not-found-specific flag; rely on INPUT-ERROR. `// source: COACTVWC.cbl:839-842, 713-715`

5. **ACCTDAT NOTFND check is likewise on an unset 88.** `9300` sets `INPUT-ERROR`+`FLG-ACCTFILTER-NOT-OK` but the `SET DID-NOT-FIND-ACCT-IN-ACCTDAT` is commented out (line 792); yet `9000-READ-ACCT` guards with `IF DID-NOT-FIND-ACCT-IN-ACCTDAT` (line 704). That guard is therefore dead; control still continues to 9400 unless caught earlier. **However** because 9300 sets INPUT-ERROR and FLG-ACCTFILTER-NOT-OK, and 9000 only early-exits on `FLG-ACCTFILTER-NOT-OK` after the *xref* read (line 697) — not after the acct read — execution **falls through to 9400-GETCUSTDATA with CDEMO-CUST-ID still from the xref**. Reproduce exactly: after an ACCTDAT NOTFND, the program proceeds to read CUSTDAT anyway. `// source: COACTVWC.cbl:789-806, 704-706, 708-711`

6. **Unconditional ERROR-RESP/RESP2 move in 9400 before the msg-off guard.** Lines 843-844 MOVE WS-RESP-CD/REAS-CD to ERROR-RESP/RESP2 outside the `IF WS-RETURN-MSG-OFF`, unlike 9200/9300 which do it inside. Faithfully keep the ordering. `// source: COACTVWC.cbl:843-845`

7. **`WS-INFORM-OUTPUT` / `WS-PROMPT-FOR-ACCT`(as info) and several 88 messages declared but never used.** Listed as dead literals (do not wire up). `// source: COACTVWC.cbl:115-138`

---

## 10. PORT NOTES (relational-access translation plan + COBOL semantics)

**Relational reads.**
- 9200 → `CARD_XREF` query by `acct_id`. Map COBOL X(11) RID to the numeric acct id (the `-X` redefine is the zero-padded char form of the 9(11) value). Found → set CDEMO-CUST-ID, CDEMO-CARD-NUM. NOTFND → error path. `// source: COACTVWC.cbl:727-740`
- 9300 → `ACCOUNT` by PK acct_id. `// source: COACTVWC.cbl:776-788`
- 9400 → `CUSTOMER` by PK cust_id. `// source: COACTVWC.cbl:826-838`
- RESP→FileStatus: NORMAL='00', NOTFND→ the program's NOTFND branch; any other RESP → WHEN OTHER file-error branch. Surface `WS-RESP-CD`/`WS-REAS-CD` as the numeric CICS RESP/RESP2 values for the message text (port can use the FileStatus / a synthesized RESP code; screen-parity tests only assert the assembled message string, so keep the formatting/padding of ERROR-RESP X(10)).

**REDEFINES.** `WS-CARD-RID-*` numeric vs `-X` char redefines — in C# keep both a numeric (`long`) account/customer id and its 11/9-char zero-padded string for message building and the (conceptual) key. `CC-ACCT-ID` X(11) vs `CC-ACCT-ID-N` 9(11) redefine in CVCRD01Y — the edit uses the X(11) form for class/space tests. `// source: CVCRD01Y.cpy:34-42; COACTVWC.cbl:73-80`

**INITIALIZE.** `INITIALIZE CC-WORK-AREA WS-MISC-STORAGE WS-COMMAREA` and conditional `INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA` set numerics→0, alphanumerics→spaces (NOT low-values). Replicate COBOL INITIALIZE rules (group fields by elementary type). `// source: COACTVWC.cbl:268-270, 285-286`

**LOW-VALUES vs SPACES.** Several screen/flag fields are tested against LOW-VALUES (e.g. `CDEMO-FROM-TRANID EQUAL LOW-VALUES`, `MOVE LOW-VALUES TO ACCTSIDO`). In the .NET model, LOW-VALUES = binary 0x00 fill; preserve the distinction from SPACES where the code branches on it. `// source: COACTVWC.cbl:328-329, 466, 630, 653-654`

**STRING (SSN format).** `1200` builds `ACSTSSNO` = `CUST-SSN(1:3) '-' CUST-SSN(4:2) '-' CUST-SSN(6:4) DELIMITED BY SIZE`. CUST-SSN is 9(9); the reference-mod slices treat it positionally → `NNN-NN-NNNN` (11 chars into a 12-char field, left-justified, trailing space). `// source: COACTVWC.cbl:496-504`

**Edited PIC.** The 5 money fields move S9(10)V99 → `+ZZZ,ZZZ,ZZZ.99`. Use `CobolEditedNumeric` (leading sign, comma grouping, zero suppression, fixed 2 dp). Negative balances render with `-`. `// source: COACTVWC.cbl:475-485; COACTVW.CPY:302,314,326,332,344`

**Signed-zoned display.** ACCOUNT money fields are S9(10)V99 zoned-decimal on file → `decimal` per ARCH; sign carried in the value, truncation-toward-zero / silent overflow semantics from CobolDecimal apply on any arithmetic (none here — pure display).

**Date/time.** `FUNCTION CURRENT-DATE` → WS-CURDATE-DATA; CURDATEO = MM/DD/YY (year is `WS-CURDATE-YEAR(3:2)`, the last two digits of CCYY), CURTIMEO = HH:MM:SS. Use IClock from Runtime; screen-parity tests mask the timestamp. `// source: COACTVWC.cbl:434-453; CSDAT01Y.cpy:17-41`

**AID wrap.** PF13-24 collapse to PF01-12 in CSSTRPFY; only ENTER/PF03 are functional. Implement the full mapping for parity even though only two are honored. `// source: CSSTRPFY.cpy:54-77; COACTVWC.cbl:306-314`

**Pseudo-conversational state machine.** Model as: handler receives COMMAREA → branch on PGM-CONTEXT/AID → produce (screen state + next COMMAREA + RETURN TRANSID 'CAVW' | XCTL target | end). The online shim stores COMMAREA between turns. `// source: COACTVWC.cbl:323-406`

**ABEND.** `HANDLE ABEND LABEL(ABEND-ROUTINE)` + final `ABEND ABCODE('9999')` → map to Runtime `Abend` with code 9999; the WHEN OTHER dispatch path uses abend code '0001' but actually only calls SEND-PLAIN-TEXT (no real abend). `// source: COACTVWC.cbl:264-266, 376-382, 916-937`

---

## 11. OPEN QUESTIONS / RISKS

1. **RESP/RESP2 numeric values in messages.** The relational repository returns FileStatus, not raw CICS RESP integers. The not-found message strings embed `WS-RESP-CD`/`WS-REAS-CD` (PIC X(10) of a binary value). For screen-parity, decide on a canonical RESP code mapping (e.g. NOTFND=13, NORMAL=0) and pin it in a characterization test; the exact embedded number is otherwise unobservable from data alone.
2. **CXACAIX duplicate handling.** If a single account maps to multiple card-xref rows, the CICS alt-index READ returns the first by VSAM order. The relational `WHERE acct_id=` must impose a deterministic order (xref_card_num ASC) to match; verify against seeded data whether any account has >1 card.
3. **Faithful-bug #5 (read CUSTDAT after ACCTDAT NOTFND)** materially changes the displayed message (it would overwrite the acct-not-found message with a cust message if the unconditional moves fired). Confirm with a scripted online test that this fall-through is intended-as-observed before locking the golden screen.
4. **`PICIN='99999999999'` + `VALIDN=(MUSTFILL)`** on ACCTSID: the mainframe terminal enforces all-11-digits at the device; the program *also* re-validates. The console renderer must decide whether to emulate MUSTFILL device rejection or rely solely on program-side `IS NOT NUMERIC`. Recommend program-side only (device-edit not reproduced), since the COBOL covers it. `// source: COACTVW.bms:88-90`
