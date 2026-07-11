# PORT SPEC — COCRDUPC (Credit Card Update, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COCRDUPC.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COCRDUP.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COCRDUP.CPY`
Logic copybooks: `CVCRD01Y.cpy` (CC-WORK-AREA + AID flags + filter fields), `COCOM01Y.cpy` (CARDDEMO-COMMAREA), `CVACT02Y.cpy` (CARD-RECORD), `CSDAT01Y.cpy` (date/time work area), `CSSTRPFY.cpy` (PF-key store routine, COPY'd inline as a paragraph), `CSMSG01Y/CSMSG02Y` (common + abend messages), `COCRDUP.CPY` (symbolic map).
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.Data` (CARD repository) per ARCHITECTURE.md.

All line citations use the form `// source: COCRDUPC.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COCRDUPC is the CICS pseudo-conversational **"Update Credit Card Details"** transaction. It accepts an Account Number (11 digits) + Card Number (16 digits) as search keys (either typed on screen or passed in COMMAREA from the card-list program), reads the single matching **CARD** record by card-number primary key, displays its editable fields (embossed name, active Y/N status, expiry month, expiry year — expiry day is shown but protected/dark), validates user edits, and on a two-phase confirm (validate → press F5 to save) **REWRITEs** the CARD record with optimistic-lock checking (re-reads under UPDATE, re-compares the snapshot, and only then rewrites). It is a single-record edit/update screen, not a browse. `// source: COCRDUPC.cbl:1-5, 564-1031, 1420-1523`

**Invocation.**
- CICS TRANSID **`CCUP`** (`LIT-THISTRANID`). `// source: COCRDUPC.cbl:221-222`
- Program id **`COCRDUPC`** (`LIT-THISPGM`). `// source: COCRDUPC.cbl:219-220`
- Mapset **`COCRDUP`** (`LIT-THISMAPSET`, declared as `'COCRDUP '` X(8)) / map **`CCRDUPA`** (`LIT-THISMAP`). `// source: COCRDUPC.cbl:223-226`
- Reached by `EXEC CICS XCTL` from the **card-list** program `COCRDLIC`/`CCLI` (selection 'U'), or from the **main menu** `COMEN01C`/`CM00`. Entry is detected via `CDEMO-FROM-PROGRAM` / `CDEMO-PGM-CONTEXT`. `// source: COCRDUPC.cbl:227-242, 388-401, 482-505`
- Pseudo-conversational: re-drives itself via `EXEC CICS RETURN TRANSID('CCUP') COMMAREA(WS-COMMAREA) LENGTH(2000)`. `// source: COCRDUPC.cbl:554-558`
- It is **not** a called subroutine; all flow is via COMMAREA + XCTL/RETURN.
- XCTL targets out: on PF3 exit (or completion when entered from the list screen) it XCTLs to `CDEMO-TO-PROGRAM` — which defaults to `COMEN01C` (menu, `LIT-MENUPGM`) when no origin program was supplied, otherwise back to `CDEMO-FROM-PROGRAM`. `// source: COCRDUPC.cbl:435-476`

---

## 2. FILE / TABLE ACCESS

Only **one** file is accessed: the CARD master, by **primary key = card number**. Note the account is *not* used as the file key (the alt-path literal `CARDAIX`/`LIT-CARDFILENAME-ACCT-PATH` is declared `// source: COCRDUPC.cbl:253-254` but **never used**; account is only carried for display/COMMAREA — see Faithful Bugs §8.1). The RIDFLD is always `WS-CARD-RID-CARDNUM X(16)`.

| COBOL DATASET (DDNAME literal) | Logical file | ARCH table | Key (RIDFLD) | CICS op | SQL equivalent |
|---|---|---|---|---|---|
| `CARDDAT` (`LIT-CARDFILENAME`, `'CARDDAT '`) | Card master | **CARD** | `WS-CARD-RID-CARDNUM` X(16) | READ (no update) | `SELECT * FROM CARD WHERE card_num = @cardNum` (PK) |
| `CARDDAT` | Card master | **CARD** | `WS-CARD-RID-CARDNUM` X(16) | READ **UPDATE** (lock for rewrite) | begin tracked read of same PK row (acquire row for update) |
| `CARDDAT` | Card master | **CARD** | (current updated row) | REWRITE | `UPDATE CARD SET ... WHERE card_num = @cardNum` |

Citations: plain READ in 9100-GETCARD-BYACCTCARD `// source: COCRDUPC.cbl:1382-1390`; READ UPDATE in 9200-WRITE-PROCESSING `// source: COCRDUPC.cbl:1427-1436`; REWRITE in 9200-WRITE-PROCESSING `// source: COCRDUPC.cbl:1477-1483`.

**Repository contract notes (per ARCHITECTURE.md §VSAM→SQL).**
- `READ FILE(CARDDAT) RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) INTO(CARD-RECORD)` = keyed read by primary key `card_num`. RESP handling: `DFHRESP(NORMAL)` → found; `DFHRESP(NOTFND)` → not found (set both acct & card filter flags not-ok, message "Did not find cards for this search condition"); `WHEN OTHER` → hard file error → build `WS-FILE-ERROR-MESSAGE`. `// source: COCRDUPC.cbl:1392-1412`
- `READ … UPDATE` = the lock-for-update read. In the relational port there is no CICS record lock; emulate with: re-`SELECT` the row inside the same logical unit of work / EF change-tracker, and treat "row not returned NORMAL" as `COULD-NOT-LOCK-FOR-UPDATE`. Then the optimistic-lock re-compare in 9300 stands in for "did someone change it." `// source: COCRDUPC.cbl:1427-1457`
- `REWRITE FILE(CARDDAT) FROM(CARD-UPDATE-RECORD)` = `UPDATE CARD` of the row just read; `DFHRESP(NORMAL)` → success, anything else → `LOCKED-BUT-UPDATE-FAILED`. `// source: COCRDUPC.cbl:1477-1492`
- **Full-record rewrite:** the REWRITE writes the entire 150-byte `CARD-UPDATE-RECORD` built fresh (see 9200 below), so the port UPDATE must set all CARD columns: card_num, acct_id, cvv_cd, embossed_name, expiration_date, active_status (FILLER X(59) reconstructed as spaces on serialize). `// source: COCRDUPC.cbl:314-321, 1461-1483`
- `EXEC CICS SYNCPOINT` is issued on the PF3/exit path before XCTL. In the port, this maps to commit-of-unit-of-work; there is no DB write on the exit path so it is effectively a no-op (no uncommitted CARD changes exist at that point). `// source: COCRDUPC.cbl:469-471`

---

## 3. DATA STRUCTURES USED

- **CARD-RECORD** (`CVACT02Y`, RECLN 150): `CARD-NUM X(16)`, `CARD-ACCT-ID 9(11)`, `CARD-CVV-CD 9(3)`, `CARD-EMBOSSED-NAME X(50)`, `CARD-EXPIRAION-DATE X(10)`, `CARD-ACTIVE-STATUS X(1)`, FILLER X(59). All fields except FILLER are consumed. `CARD-EXPIRAION-DATE` is sliced as `(1:4)`=year, `(6:2)`=month, `(9:2)`=day (format `YYYY-MM-DD`). `// source: CVACT02Y.cpy:4-11; COCRDUPC.cbl:1361-1366, 1505-1507`
- **CC-WORK-AREA** (`CVCRD01Y`): AID flags (`CCARD-AID` X5, 88s ENTER/CLEAR/PA1-2/PFK01-12), `CCARD-NEXT-PROG X8`, `CCARD-NEXT-MAPSET X7`, `CCARD-NEXT-MAP X7`, `CCARD-ERROR-MSG X75`, `CCARD-RETURN-MSG X75`, filter inputs `CC-ACCT-ID X11` (redef `CC-ACCT-ID-N 9(11)`), `CC-CARD-NUM X16` (redef `CC-CARD-NUM-N 9(16)`), `CC-CUST-ID X9`. `// source: CVCRD01Y.cpy:1-42`
- **WS-THIS-PROGCOMMAREA** (program-private commarea tail, carried across turns): `CARD-UPDATE-SCREEN-DATA` containing `CCUP-CHANGE-ACTION X1` (the state machine flag, see §4); `CCUP-OLD-DETAILS` (the snapshot read from file: OLD-ACCTID X11, OLD-CARDID X16, OLD-CVV-CD X3, OLD-CRDNAME X50, OLD-EXPIRAION-DATE = OLD-EXPYEAR X4 + OLD-EXPMON X2 + OLD-EXPDAY X2, OLD-CRDSTCD X1); `CCUP-NEW-DETAILS` (the user's edited values, same shape: NEW-ACCTID, NEW-CARDID, NEW-CVV-CD, NEW-CRDNAME, NEW-EXPYEAR/EXPMON/EXPDAY, NEW-CRDSTCD); `CARD-UPDATE-RECORD` (150-byte rewrite buffer: NUM X16, ACCT-ID 9(11), CVV-CD 9(3), EMBOSSED-NAME X50, EXPIRAION-DATE X10, ACTIVE-STATUS X1, FILLER X59). `// source: COCRDUPC.cbl:274-321`
- **CCUP-CHANGE-ACTION** 88-levels (the screen state machine):
  - `CCUP-DETAILS-NOT-FETCHED` = LOW-VALUES or SPACES — no card fetched yet (initial search-key screen).
  - `CCUP-SHOW-DETAILS` = `'S'` — details fetched & displayed.
  - `CCUP-CHANGES-MADE` = any of {'E','N','C','L','F'}.
  - `CCUP-CHANGES-NOT-OK` = `'E'` — edits failed validation.
  - `CCUP-CHANGES-OK-NOT-CONFIRMED` = `'N'` — edits valid, awaiting F5 confirm.
  - `CCUP-CHANGES-OKAYED-AND-DONE` = `'C'` — rewrite succeeded.
  - `CCUP-CHANGES-FAILED` = {'L','F'}.
  - `CCUP-CHANGES-OKAYED-LOCK-ERROR` = `'L'`; `CCUP-CHANGES-OKAYED-BUT-FAILED` = `'F'`. `// source: COCRDUPC.cbl:276-290`
- **WS-MISC-STORAGE** edit flags (each X1, one per field): `WS-INPUT-FLAG` (88 INPUT-OK='0', INPUT-ERROR='1', INPUT-PENDING=LOW-VALUES); `WS-EDIT-ACCT-FLAG` (88 FLG-ACCTFILTER-NOT-OK='0', -ISVALID='1', -BLANK=' '); analogous `WS-EDIT-CARD-FLAG`, `WS-EDIT-CARDNAME-FLAG`, `WS-EDIT-CARDSTATUS-FLAG`, `WS-EDIT-CARDEXPMON-FLAG`, `WS-EDIT-CARDEXPYEAR-FLAG`; `WS-RETURN-FLAG`; `WS-PFK-FLAG` (88 PFK-VALID='0', PFK-INVALID='1'). `// source: COCRDUPC.cbl:53-86`
- **Validation work fields:** `CARD-NAME-CHECK X(50)` (init LOW-VALUES); `FLG-YES-NO-CHECK X(1)` (88 FLG-YES-NO-VALID = 'Y','N'); `CARD-MONTH-CHECK X(2)` / redef `CARD-MONTH-CHECK-N 9(2)` (88 VALID-MONTH = 1 THRU 12); `CARD-YEAR-CHECK X(4)` / redef `CARD-YEAR-CHECK-N 9(4)` (88 VALID-YEAR = 1950 THRU 2099). `// source: COCRDUPC.cbl:87-99`
- **CICS-OUTPUT-EDIT-VARS:** `CARD-ACCT-ID-X X11` / `CARD-ACCT-ID-N 9(11)`; `CARD-CVV-CD-X X3` / `CARD-CVV-CD-N 9(3)`; `CARD-CARD-NUM-X X16` / `-N 9(16)`; `CARD-NAME-EMBOSSED-X X50`; `CARD-STATUS-X X1`; `CARD-EXPIRAION-DATE-X X10` (redef into `CARD-EXPIRY-YEAR X4` + filler + `CARD-EXPIRY-MONTH X2` + filler + `CARD-EXPIRY-DAY X2`) / `CARD-EXPIRAION-DATE-N 9(10)`. `// source: COCRDUPC.cbl:103-123`
- **WS-CARD-RID:** `WS-CARD-RID-CARDNUM X16` + `WS-CARD-RID-ACCT-ID 9(11)` (redef `-X X11`). Only CARDNUM is used as RIDFLD. `// source: COCRDUPC.cbl:128-132`
- **WS-FILE-ERROR-MESSAGE** group: `'File Error: '` + ERROR-OPNAME X8 + `' on '` + ERROR-FILE X9 + `' returned RESP '` + ERROR-RESP X10 + `,RESP2 ` + ERROR-RESP2 X10 + filler. `// source: COCRDUPC.cbl:133-152`
- **WS-DATE-TIME** (`CSDAT01Y`): used to render header date/time mm/dd/yy and hh:mm:ss from `FUNCTION CURRENT-DATE`. `// source: CSDAT01Y.cpy:17-55; COCRDUPC.cbl:1055-1074`

---

## 4. COMMAREA FIELDS (CARDDEMO-COMMAREA, COCOM01Y)

On-the-wire COMMAREA = `WS-COMMAREA PIC X(2000)` split as `[CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA]`; `LENGTH OF CARDDEMO-COMMAREA` is the split offset. On entry: if `EIBCALEN=0` OR (from menu AND not re-enter) → INITIALIZE both halves & set PGM-ENTER + DETAILS-NOT-FETCHED; else copy the two segments out of DFHCOMMAREA. On RETURN: re-concatenate `CARDDEMO-COMMAREA` + `WS-THIS-PROGCOMMAREA` into `WS-COMMAREA` and RETURN it with LENGTH 2000. Port: model COMMAREA as a typed object (segment A = CARDDEMO-COMMAREA, segment B = program-private state) and preserve total 2000-byte length. `// source: COCRDUPC.cbl:324, 388-401, 546-558`

Fields used by this program (CARDDEMO-COMMAREA / COCOM01Y):
- `CDEMO-FROM-TRANID` X4 — origin tranid; decides `CDEMO-TO-TRANID` on exit; if low/space → `CM00`. `// source: COCRDUPC.cbl:442-447, 1013-1018`
- `CDEMO-FROM-PROGRAM` X8 — origin pgm; `LIT-MENUPGM` ('COMEN01C') vs `LIT-CCLISTPGM` ('COCRDLIC') drives dispatch; decides `CDEMO-TO-PROGRAM` on exit (→ `COMEN01C` if low/space). `// source: COCRDUPC.cbl:389, 449-454, 483, 504`
- `CDEMO-TO-TRANID` X4, `CDEMO-TO-PROGRAM` X8 — set for the XCTL on exit. `// source: COCRDUPC.cbl:444-454, 474`
- `CDEMO-USER-TYPE` (88 CDEMO-USRTYP-USER='U') — SET to USER on exit path. `// source: COCRDUPC.cbl:464`
- `CDEMO-PGM-CONTEXT` 9(1): `CDEMO-PGM-ENTER`=0, `CDEMO-PGM-REENTER`=1 — fresh-start & dispatch. `// source: COCOM01Y.cpy:29-31; COCRDUPC.cbl:393, 465, 486, 509, 523, 526`
- `CDEMO-ACCT-ID` 9(11) — written from edited acct filter / passed-in key; zeroed on certain resets. `// source: COCRDUPC.cbl:460, 490, 521, 671, 733, 748, 1015`
- `CDEMO-CARD-NUM` 9(16) — written from edited card filter / passed-in key; zeroed on resets. `// source: COCRDUPC.cbl:461, 491, 522, 672, 777, 796, 1016`
- `CDEMO-ACCT-STATUS` X1 — set LOW-VALUES on success reset path. `// source: COCRDUPC.cbl:1017`
- `CDEMO-LAST-MAP` X7, `CDEMO-LAST-MAPSET` X7 — set to this map/mapset on exit; `CDEMO-LAST-MAPSET = LIT-CCLISTMAPSET` ('COCRDLI') means "came from list". `// source: COCRDUPC.cbl:437-439, 459, 466-467, 1238`

Program-private tail (`WS-THIS-PROGCOMMAREA`): carries `CCUP-CHANGE-ACTION` state + OLD snapshot + NEW edits + the rewrite buffer across pseudo-conversational turns. `// source: COCRDUPC.cbl:274-321`

---

## 5. SCREEN (BMS map CCRDUPA / mapset COCRDUP)

24×80, `CTRL=(FREEKB)`, `SIZE=(24,80)`, DSATTS/MAPATTS=(COLOR,HILIGHT,PS,VALIDN). `// source: COCRDUP.bms:25-28`

### Header / footer (output-only, written every send)
| Field | Len | Pos | Source |
|---|---|---|---|
| `TRNNAME` | 4 | (1,7) | = `CCUP` `// source: COCRDUPC.cbl:1059` |
| `TITLE01` | 40 | (1,21) | CCDA-TITLE01 `// source: COCRDUPC.cbl:1057` |
| `CURDATE` | 8 | (1,71) | mm/dd/yy from CURRENT-DATE `// source: COCRDUPC.cbl:1068` |
| `PGMNAME` | 8 | (2,7) | = `COCRDUPC` `// source: COCRDUPC.cbl:1060` |
| `TITLE02` | 40 | (2,21) | CCDA-TITLE02 `// source: COCRDUPC.cbl:1058` |
| `CURTIME` | 8 | (2,71) | hh:mm:ss `// source: COCRDUPC.cbl:1074` |
| `INFOMSG` | 40 | (20,25) | info message (see §6 table B) `// source: COCRDUPC.cbl:1161; COCRDUP.bms:149-153` |
| `ERRMSG` | 80 | (23,1) | error message = `WS-RETURN-MSG` `// source: COCRDUPC.cbl:1163; COCRDUP.bms:154-157` |
| `FKEYS` | 21 | (24,1) | static `'ENTER=Process F3=Exit'` `// source: COCRDUP.bms:158-162` |
| `FKEYSC` | 18 | (24,23) | `'F5=Save F12=Cancel'`; normally DARK, made bright when prompting for confirmation `// source: COCRDUP.bms:163-167; COCRDUPC.cbl:1315-1317` |

### Editable / data fields (read on RECEIVE, written on SEND)
| Field (symbolic in/out) | Len | Pos | BMS attrb | Read? | Written? | Notes |
|---|---|---|---|---|---|---|
| `ACCTSID` (ACCTSIDI/O) | 11 | (7,45) | FSET,IC,NORM,PROT (default) | yes | yes | Account number search key. Initial cursor (IC). `// source: COCRDUP.bms:84-88` |
| `CARDSID` (CARDSIDI/O) | 16 | (8,45) | FSET,NORM,UNPROT | yes | yes | Card number search key. `// source: COCRDUP.bms:96-100` |
| `CRDNAME` (CRDNAMEI/O) | 50 | (11,25) | UNPROT | yes | yes | Embossed name (editable). `// source: COCRDUP.bms:107-110` |
| `CRDSTCD` (CRDSTCDI/O) | 1 | (13,25) | UNPROT | yes | yes | Active status Y/N (editable). `// source: COCRDUP.bms:117-120` |
| `EXPMON` (EXPMONI/O) | 2 | (15,25) | UNPROT, JUSTIFY=RIGHT | yes | yes | Expiry month (editable). `// source: COCRDUP.bms:127-131` |
| `EXPYEAR` (EXPYEARI/O) | 4 | (15,30) | UNPROT, JUSTIFY=RIGHT | yes | yes | Expiry year (editable). `// source: COCRDUP.bms:135-139` |
| `EXPDAY` (EXPDAYI/O) | 2 | (15,36) | DRK,FSET,PROT, JUSTIFY=RIGHT | yes (received) | yes | Expiry day — **protected & dark**, never user-changeable; always carried from OLD value. `// source: COCRDUP.bms:142-146; COCRDUPC.cbl:621, 1122-1123` |

**Attribute byte fields** (`...A`, e.g. `ACCTSIDA`) and color fields (`...C`) are set in 3300-SETUP-SCREEN-ATTRS to PROTECT/UNPROTECT, color (RED/DEFAULT/NEUTRAL), and `*`-placeholders; length field (`...L`) set to `-1` to position the cursor. The symbolic map layout (lengths/attr/color/output offsets) is in `COCRDUP.CPY`. `// source: COCRDUP.CPY:17-225; COCRDUPC.cbl:1168-1318`

Constants used in attribute setup (from DFHBMSCA): `DFHBMFSE` (unprotect+modified/FSET), `DFHBMPRF` (protect), `DFHBMDAR` (dark), `DFHBMBRY` (bright), `DFHRED`, `DFHDFCOL` (default color), `DFHBMASB` etc. `// source: COCRDUPC.cbl:327, 1174-1316`

---

## 6. PSEUDO-CONVERSATIONAL FLOW & STATE MACHINE

### 6.1 Top-level dispatch (0000-MAIN, `EVALUATE TRUE`) — `// source: COCRDUPC.cbl:367-559`
1. `EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE)`. `// source: COCRDUPC.cbl:370-372`
2. INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA; set WS-TRANID='CCUP'; set WS-RETURN-MSG-OFF (LOW-VALUES). `// source: COCRDUPC.cbl:374-384`
3. Fresh-start vs continue (COMMAREA split, see §4). `// source: COCRDUPC.cbl:388-401`
4. `PERFORM YYYY-STORE-PFKEY` → map EIBAID to `CCARD-AID-*`. `// source: COCRDUPC.cbl:406-407`
5. **PFKey gate:** SET PFK-INVALID; then SET PFK-VALID **only if** AID is ENTER, or PFK03, or (PFK05 AND state=CHANGES-OK-NOT-CONFIRMED), or (PFK12 AND NOT DETAILS-NOT-FETCHED). If still PFK-INVALID, force `CCARD-AID-ENTER`. (Any disallowed PF key is silently treated as ENTER.) `// source: COCRDUPC.cbl:413-424`
6. `EVALUATE TRUE` dispatch branches:
   - **(a) Exit / done:** `CCARD-AID-PFK03` OR (CHANGES-OKAYED-AND-DONE AND came-from-list) OR (CHANGES-FAILED AND came-from-list) → set up TO-PROGRAM/TO-TRANID, `SYNCPOINT`, `XCTL` to TO-PROGRAM. `// source: COCRDUPC.cbl:435-476`
   - **(b) Came from list, fetch for update:** (PGM-ENTER AND FROM=COCRDLIC) OR (PFK12 AND FROM=COCRDLIC) → set REENTER, INPUT-OK, both filter-valid, move CDEMO keys into CC keys, `PERFORM 9000-READ-DATA`, set SHOW-DETAILS, `PERFORM 3000-SEND-MAP`, GO TO COMMON-RETURN. `// source: COCRDUPC.cbl:482-497`
   - **(c) Fresh entry — prompt for keys:** (DETAILS-NOT-FETCHED AND PGM-ENTER) OR (FROM=COMEN01C AND NOT REENTER) → INITIALIZE program tail, `PERFORM 3000-SEND-MAP`, set REENTER + DETAILS-NOT-FETCHED, GO TO COMMON-RETURN. `// source: COCRDUPC.cbl:502-511`
   - **(d) Done/failed reset:** CHANGES-OKAYED-AND-DONE OR CHANGES-FAILED → INITIALIZE tail+misc+CDEMO keys, set PGM-ENTER, `PERFORM 3000-SEND-MAP`, set REENTER+DETAILS-NOT-FETCHED, GO TO COMMON-RETURN. `// source: COCRDUPC.cbl:517-528`
   - **(e) WHEN OTHER (normal turn):** `PERFORM 1000-PROCESS-INPUTS`, `2000-DECIDE-ACTION`, `3000-SEND-MAP`, GO TO COMMON-RETURN. `// source: COCRDUPC.cbl:535-542`
7. **COMMON-RETURN:** move WS-RETURN-MSG → CCARD-ERROR-MSG; re-concatenate COMMAREA; `EXEC CICS RETURN TRANSID('CCUP') COMMAREA(WS-COMMAREA) LENGTH(2000)`. `// source: COCRDUPC.cbl:546-559`

### 6.2 EIBAID / PF-key semantics
| AID | Meaning here | Effect |
|---|---|---|
| ENTER | process / validate | runs the normal turn (branch e) |
| PF3 | exit | XCTL back to caller/menu (branch a) `// source: COCRDUPC.cbl:435` |
| PF5 | save (confirm) — only valid when CHANGES-OK-NOT-CONFIRMED | triggers 9200-WRITE-PROCESSING `// source: COCRDUPC.cbl:416, 988-1001` |
| PF12 | cancel — only valid when NOT DETAILS-NOT-FETCHED | re-reads & re-shows details (branch b / 2000 WHEN PFK12) `// source: COCRDUPC.cbl:418, 484, 958-966` |
| anything else | invalid → forced to ENTER | `// source: COCRDUPC.cbl:422-424` |

### 6.3 Info message selection (3250-SETUP-INFOMSG) — written to INFOMSG (table B)
`// source: COCRDUPC.cbl:1138-1164`
| State (EVALUATE TRUE order) | INFOMSG text |
|---|---|
| CDEMO-PGM-ENTER | `Please enter Account and Card Number` |
| CCUP-DETAILS-NOT-FETCHED | `Please enter Account and Card Number` |
| CCUP-SHOW-DETAILS | `Details of selected card shown above` |
| CCUP-CHANGES-NOT-OK | `Update card details presented above.` |
| CCUP-CHANGES-OK-NOT-CONFIRMED | `Changes validated.Press F5 to save` |
| CCUP-CHANGES-OKAYED-AND-DONE | `Changes committed to database` |
| CCUP-CHANGES-OKAYED-LOCK-ERROR | `Changes unsuccessful. Please try again` |
| CCUP-CHANGES-OKAYED-BUT-FAILED | `Changes unsuccessful. Please try again` |
| WS-NO-INFO-MESSAGE (default) | `Please enter Account and Card Number` |

(The 88-values of `WS-INFO-MSG` are defined `// source: COCRDUPC.cbl:157-171`.)

---

## 7. PARAGRAPH-BY-PARAGRAPH OUTLINE (each = one method)

> Preserve statement order, all `GO TO …-EXIT` early returns, and `MOVE`/`SET` order. Flags are sticky across the turn unless reset.

**0000-MAIN** — `// source: COCRDUPC.cbl:367-544`
- HANDLE ABEND; INITIALIZE work areas; set TRANID; clear return msg; COMMAREA split (fresh vs continue); PERFORM YYYY-STORE-PFKEY; PFKey validity gate (force ENTER if invalid); big EVALUATE dispatch (branches a–e above). Each non-exit branch ends `GO TO COMMON-RETURN`; the exit branch ends with XCTL.

**COMMON-RETURN** — `// source: COCRDUPC.cbl:546-559`
- Move WS-RETURN-MSG → CCARD-ERROR-MSG; rebuild WS-COMMAREA = CARDDEMO-COMMAREA ++ WS-THIS-PROGCOMMAREA; `RETURN TRANSID('CCUP')` length 2000.

**0000-MAIN-EXIT** — EXIT. `// source: COCRDUPC.cbl:560-562`

**1000-PROCESS-INPUTS** — `// source: COCRDUPC.cbl:564-573`
- PERFORM 1100-RECEIVE-MAP; PERFORM 1200-EDIT-MAP-INPUTS; move WS-RETURN-MSG → CCARD-ERROR-MSG; set CCARD-NEXT-PROG/MAPSET/MAP to this program's literals.

**1100-RECEIVE-MAP** — `// source: COCRDUPC.cbl:578-636`
- `EXEC CICS RECEIVE MAP('CCRDUPA') MAPSET('COCRDUP ') INTO(CCRDUPAI)` (RESP/RESP2 captured). INITIALIZE CCUP-NEW-DETAILS. For ACCTSIDI/CARDSIDI/CRDNAMEI/CRDSTCDI/EXPMONI/EXPYEARI: if input = `'*'` OR SPACES → MOVE LOW-VALUES to the corresponding CC-* / CCUP-NEW-* field; else MOVE input. **EXPDAYI is moved unconditionally** to CCUP-NEW-EXPDAY (no `*`/space normalization). `// source: COCRDUPC.cbl:589-635`
  - Note: ACCTSID → both CC-ACCT-ID and CCUP-NEW-ACCTID; CARDSID → CC-CARD-NUM and CCUP-NEW-CARDID.

**1200-EDIT-MAP-INPUTS** — `// source: COCRDUPC.cbl:641-718`
- SET INPUT-OK.
- **IF CCUP-DETAILS-NOT-FETCHED** (search-key phase): PERFORM 1210-EDIT-ACCOUNT; PERFORM 1220-EDIT-CARD; MOVE LOW-VALUES TO CCUP-NEW-CARDDATA; if both filters BLANK → SET NO-SEARCH-CRITERIA-RECEIVED; `GO TO 1200-…-EXIT`. `// source: COCRDUPC.cbl:645-662`
- **ELSE** (data already shown — edit phase): SET FOUND-CARDS-FOR-ACCOUNT; force both filter flags valid; copy CCUP-OLD-* into CDEMO keys + CARD-* display fields. `// source: COCRDUPC.cbl:663-677`
  - If `UPPER-CASE(CCUP-NEW-CARDDATA) = UPPER-CASE(CCUP-OLD-CARDDATA)` → SET NO-CHANGES-DETECTED. `// source: COCRDUPC.cbl:680-683`
  - If NO-CHANGES-DETECTED OR CHANGES-OK-NOT-CONFIRMED OR CHANGES-OKAYED-AND-DONE → mark all four edit flags valid, `GO TO …-EXIT` (skip field edits). `// source: COCRDUPC.cbl:685-693`
  - Else SET CCUP-CHANGES-NOT-OK ('E'); PERFORM 1230-EDIT-NAME, 1240-EDIT-CARDSTATUS, 1250-EDIT-EXPIRY-MON, 1260-EDIT-EXPIRY-YEAR; if NOT INPUT-ERROR → SET CCUP-CHANGES-OK-NOT-CONFIRMED ('N'). `// source: COCRDUPC.cbl:696-715`

**1210-EDIT-ACCOUNT** — `// source: COCRDUPC.cbl:721-760`
- SET FLG-ACCTFILTER-NOT-OK. If CC-ACCT-ID = LOW-VALUES/SPACES OR CC-ACCT-ID-N = ZEROS → INPUT-ERROR, FLG-ACCTFILTER-BLANK, (if msg empty) set "Account number not provided", zero CDEMO-ACCT-ID, LOW-VALUES CCUP-NEW-ACCTID, exit. Else if CC-ACCT-ID NOT NUMERIC → INPUT-ERROR, NOT-OK, (if msg empty) move literal `ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER`, zero CDEMO-ACCT-ID, LOW-VALUES NEW-ACCTID, exit. Else move CC-ACCT-ID → CDEMO-ACCT-ID & CCUP-NEW-ACCTID, SET FLG-ACCTFILTER-ISVALID.

**1220-EDIT-CARD** — `// source: COCRDUPC.cbl:762-804`
- SET FLG-CARDFILTER-NOT-OK. If CC-CARD-NUM = LOW-VALUES/SPACES OR CC-CARD-NUM-N = ZEROS → INPUT-ERROR, FLG-CARDFILTER-BLANK, (if msg empty) set "Card number not provided", zero CDEMO-CARD-NUM & CCUP-NEW-CARDID, exit. Else if CC-CARD-NUM NOT NUMERIC → INPUT-ERROR, NOT-OK, (if msg empty) move literal `CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER`, zero CDEMO-CARD-NUM, LOW-VALUES NEW-CARDID, exit. Else MOVE CC-CARD-NUM-N → CDEMO-CARD-NUM, CC-CARD-NUM → CCUP-NEW-CARDID, SET FLG-CARDFILTER-ISVALID.

**1230-EDIT-NAME** — `// source: COCRDUPC.cbl:806-843`
- SET FLG-CARDNAME-NOT-OK. If CCUP-NEW-CRDNAME = LOW-VALUES/SPACES/ZEROS → INPUT-ERROR, FLG-CARDNAME-BLANK, (if msg empty) set "Card name not provided", exit. Else MOVE name → CARD-NAME-CHECK; `INSPECT CARD-NAME-CHECK CONVERTING LIT-ALL-ALPHA-FROM TO LIT-ALL-SPACES-TO` (turns A-Z/a-z into spaces); if `LENGTH(TRIM(CARD-NAME-CHECK)) = 0` (only letters+spaces remained) → valid; else INPUT-ERROR, NOT-OK, (if msg empty) set "Card name can only contain alphabets and spaces", exit. Finally SET FLG-CARDNAME-ISVALID. `// source: COCRDUPC.cbl:823-839`

**1240-EDIT-CARDSTATUS** — `// source: COCRDUPC.cbl:845-876`
- SET FLG-CARDSTATUS-NOT-OK. If CCUP-NEW-CRDSTCD = LOW-VALUES/SPACES/ZEROS → INPUT-ERROR, FLG-CARDSTATUS-BLANK, (if msg empty) set "Card Active Status must be Y or N", exit. Else MOVE → FLG-YES-NO-CHECK; if FLG-YES-NO-VALID ('Y'/'N') → ISVALID; else INPUT-ERROR, NOT-OK, (if msg empty) set "Card Active Status must be Y or N", exit.

**1250-EDIT-EXPIRY-MON** — `// source: COCRDUPC.cbl:877-912`
- SET FLG-CARDEXPMON-NOT-OK. If CCUP-NEW-EXPMON = LOW-VALUES/SPACES/ZEROS → INPUT-ERROR, FLG-CARDEXPMON-BLANK, (if msg empty) set "Card expiry month must be between 1 and 12", exit. Else MOVE → CARD-MONTH-CHECK; if VALID-MONTH (1..12) → ISVALID; else INPUT-ERROR, NOT-OK, (if msg empty) set "Card expiry month must be between 1 and 12", exit.

**1260-EDIT-EXPIRY-YEAR** — `// source: COCRDUPC.cbl:913-947`
- If CCUP-NEW-EXPYEAR = LOW-VALUES/SPACES/ZEROS → INPUT-ERROR, FLG-CARDEXPYEAR-BLANK, (if msg empty) set "Invalid card expiry year", exit. (Note: not-supplied check comes **before** SET FLG-CARDEXPYEAR-NOT-OK here — order differs from the other edits.) Then SET FLG-CARDEXPYEAR-NOT-OK; MOVE → CARD-YEAR-CHECK; if VALID-YEAR (1950..2099) → ISVALID; else INPUT-ERROR, NOT-OK, (if msg empty) set "Invalid card expiry year", exit. `// source: COCRDUPC.cbl:916-943`

**2000-DECIDE-ACTION** — `// source: COCRDUPC.cbl:948-1031` (`EVALUATE TRUE`)
- **WHEN CCUP-DETAILS-NOT-FETCHED / WHEN CCARD-AID-PFK12:** (shared) if both filters valid → PERFORM 9000-READ-DATA; if FOUND-CARDS-FOR-ACCOUNT → SET CCUP-SHOW-DETAILS. `// source: COCRDUPC.cbl:954-966`
- **WHEN CCUP-SHOW-DETAILS:** if INPUT-ERROR OR NO-CHANGES-DETECTED → CONTINUE; else SET CCUP-CHANGES-OK-NOT-CONFIRMED. `// source: COCRDUPC.cbl:971-977`
- **WHEN CCUP-CHANGES-NOT-OK:** CONTINUE. `// source: COCRDUPC.cbl:982-983`
- **WHEN CCUP-CHANGES-OK-NOT-CONFIRMED AND CCARD-AID-PFK05:** PERFORM 9200-WRITE-PROCESSING; then EVALUATE: COULD-NOT-LOCK-FOR-UPDATE → set CHANGES-OKAYED-LOCK-ERROR ('L'); LOCKED-BUT-UPDATE-FAILED → set CHANGES-OKAYED-BUT-FAILED ('F'); DATA-WAS-CHANGED-BEFORE-UPDATE → set CCUP-SHOW-DETAILS; WHEN OTHER → set CHANGES-OKAYED-AND-DONE ('C'). `// source: COCRDUPC.cbl:988-1001`
- **WHEN CCUP-CHANGES-OK-NOT-CONFIRMED (no PF5):** CONTINUE. `// source: COCRDUPC.cbl:1006-1007`
- **WHEN CCUP-CHANGES-OKAYED-AND-DONE:** SET CCUP-SHOW-DETAILS; if FROM-TRANID low/space → zero CDEMO-ACCT-ID & CDEMO-CARD-NUM & LOW-VALUES CDEMO-ACCT-STATUS. `// source: COCRDUPC.cbl:1011-1018`
- **WHEN OTHER:** abend — move ABEND-CULPRIT/CODE '0001'/REASON spaces/MSG 'UNEXPECTED DATA SCENARIO', PERFORM ABEND-ROUTINE. `// source: COCRDUPC.cbl:1019-1026`

**3000-SEND-MAP** — `// source: COCRDUPC.cbl:1035-1046`
- PERFORM 3100-SCREEN-INIT, 3200-SETUP-SCREEN-VARS, 3250-SETUP-INFOMSG, 3300-SETUP-SCREEN-ATTRS, 3400-SEND-SCREEN.

**3100-SCREEN-INIT** — `// source: COCRDUPC.cbl:1052-1080`
- MOVE LOW-VALUES TO CCRDUPAO; MOVE CURRENT-DATE → WS-CURDATE-DATA; set TITLE01O/TITLE02O/TRNNAMEO/PGMNAMEO; build CURDATEO (mm/dd/yy) and CURTIMEO (hh:mm:ss) from the date/time work area.

**3200-SETUP-SCREEN-VARS** — `// source: COCRDUPC.cbl:1082-1137`
- If CDEMO-PGM-ENTER → CONTINUE (leave blank). Else: ACCTSIDO = (CC-ACCT-ID-N=0 ? LOW-VALUES : CC-ACCT-ID); CARDSIDO similarly. Then `EVALUATE TRUE`: DETAILS-NOT-FETCHED → LOW-VALUES into name/status/day/mon/year out-fields; SHOW-DETAILS → move CCUP-OLD-* into out-fields; CHANGES-MADE → move CCUP-NEW-* into name/status/mon/year out-fields and **CCUP-OLD-EXPDAY** (not NEW) into EXPDAYO; WHEN OTHER → move CCUP-OLD-* into all out-fields.

**3250-SETUP-INFOMSG** — `// source: COCRDUPC.cbl:1138-1167`
- Choose INFOMSG per the §6.3 table; MOVE WS-INFO-MSG → INFOMSGO; MOVE WS-RETURN-MSG → ERRMSGO.

**3300-SETUP-SCREEN-ATTRS** — `// source: COCRDUPC.cbl:1168-1321`
- (1) **Protect/unprotect** by state: DETAILS-NOT-FETCHED → unprotect ACCTSID/CARDSID, protect name/status/mon/year; SHOW-DETAILS or CHANGES-NOT-OK → protect ACCTSID/CARDSID, unprotect name/status/mon/year; CHANGES-OK-NOT-CONFIRMED or CHANGES-OKAYED-AND-DONE → protect everything; OTHER → unprotect ACCTSID/CARDSID, protect rest. `// source: COCRDUPC.cbl:1172-1208`
- (2) **Cursor** (move -1 to one length field): FOUND-CARDS / NO-CHANGES → CRDNAMEL; acct flag bad → ACCTSIDL; card flag bad → CARDSIDL; name flag bad → CRDNAMEL; status flag bad → CRDSTCDL; expmon flag bad → EXPMONL; expyear flag bad → EXPYEARL; OTHER → ACCTSIDL. `// source: COCRDUPC.cbl:1210-1235`
- (3) **Color / `*` placeholders:** if came-from-list set ACCTSID/CARDSID color default; per-flag RED coloring; if filter BLANK & REENTER → put `'*'` into the out-field and RED; same `*`+RED pattern (guarded by CCUP-CHANGES-NOT-OK) for name/status/mon/year; EXPDAYC = DARK; INFOMSGA = DARK if no info msg else BRIGHT; FKEYSCA = BRIGHT if PROMPT-FOR-CONFIRMATION. `// source: COCRDUPC.cbl:1237-1318`

**3400-SEND-SCREEN** — `// source: COCRDUPC.cbl:1324-1340`
- Set CCARD-NEXT-MAPSET/MAP; `EXEC CICS SEND MAP('CCRDUPA') MAPSET('COCRDUP') FROM(CCRDUPAO) CURSOR ERASE FREEKB`.

**9000-READ-DATA** — `// source: COCRDUPC.cbl:1343-1374`
- INITIALIZE CCUP-OLD-DETAILS; move CC-ACCT-ID → CCUP-OLD-ACCTID; CC-CARD-NUM → CCUP-OLD-CARDID; PERFORM 9100-GETCARD-BYACCTCARD. If FOUND-CARDS-FOR-ACCOUNT: move CARD-CVV-CD → OLD-CVV-CD; `INSPECT CARD-EMBOSSED-NAME CONVERTING LIT-LOWER TO LIT-UPPER` (uppercase the name); move name → OLD-CRDNAME; slice CARD-EXPIRAION-DATE (1:4)→OLD-EXPYEAR, (6:2)→OLD-EXPMON, (9:2)→OLD-EXPDAY; move CARD-ACTIVE-STATUS → OLD-CRDSTCD. `// source: COCRDUPC.cbl:1352-1369`

**9100-GETCARD-BYACCTCARD** — `// source: COCRDUPC.cbl:1376-1417`
- Move CC-CARD-NUM → WS-CARD-RID-CARDNUM (acct-id line is commented out). `EXEC CICS READ FILE('CARDDAT ') RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) INTO(CARD-RECORD)`. EVALUATE WS-RESP-CD: NORMAL → SET FOUND-CARDS-FOR-ACCOUNT; NOTFND → INPUT-ERROR + both filter flags NOT-OK + (if msg empty) "Did not find cards for this search condition"; OTHER → INPUT-ERROR + (if msg empty) FLG-ACCTFILTER-NOT-OK + build WS-FILE-ERROR-MESSAGE (op 'READ', file CARDDAT, RESP/RESP2). `// source: COCRDUPC.cbl:1392-1412`

**9200-WRITE-PROCESSING** — `// source: COCRDUPC.cbl:1420-1496`
- Move CC-CARD-NUM → WS-CARD-RID-CARDNUM; `EXEC CICS READ FILE('CARDDAT ') UPDATE RIDFLD(...) KEYLENGTH(16) INTO(CARD-RECORD)`. If RESP ≠ NORMAL → INPUT-ERROR, (if msg empty) SET COULD-NOT-LOCK-FOR-UPDATE, `GO TO 9200-…-EXIT`. `// source: COCRDUPC.cbl:1425-1449`
- PERFORM 9300-CHECK-CHANGE-IN-REC; if DATA-WAS-CHANGED-BEFORE-UPDATE → `GO TO 9200-…-EXIT`. `// source: COCRDUPC.cbl:1453-1457`
- Build rewrite record: INITIALIZE CARD-UPDATE-RECORD; MOVE CCUP-NEW-CARDID → CARD-UPDATE-NUM; CC-ACCT-ID-N → CARD-UPDATE-ACCT-ID; CCUP-NEW-CVV-CD → CARD-CVV-CD-X then CARD-CVV-CD-N → CARD-UPDATE-CVV-CD (zoned→display normalization); CCUP-NEW-CRDNAME → CARD-UPDATE-EMBOSSED-NAME; `STRING NEW-EXPYEAR '-' NEW-EXPMON '-' NEW-EXPDAY DELIMITED BY SIZE INTO CARD-UPDATE-EXPIRAION-DATE`; CCUP-NEW-CRDSTCD → CARD-UPDATE-ACTIVE-STATUS. `// source: COCRDUPC.cbl:1461-1475`
- `EXEC CICS REWRITE FILE('CARDDAT ') FROM(CARD-UPDATE-RECORD) LENGTH(150)`; if RESP ≠ NORMAL → SET LOCKED-BUT-UPDATE-FAILED. `// source: COCRDUPC.cbl:1477-1492`

**9300-CHECK-CHANGE-IN-REC** — `// source: COCRDUPC.cbl:1498-1523`
- `INSPECT CARD-EMBOSSED-NAME CONVERTING LIT-LOWER TO LIT-UPPER`. Compare current CARD fields (CVV, uppercased name, expiry year/mon/day slices, active status) to the OLD snapshot (CCUP-OLD-*). If all equal → CONTINUE (no change). Else SET DATA-WAS-CHANGED-BEFORE-UPDATE; refresh CCUP-OLD-* with the just-read values; `GO TO 9200-WRITE-PROCESSING-EXIT`. `// source: COCRDUPC.cbl:1503-1519`

**YYYY-STORE-PFKEY** (from CSSTRPFY.cpy, COPY'd inline) — maps EIBAID to `CCARD-AID-*` 88s. PF13–24 fold onto PFK01–12. `// source: CSSTRPFY.cpy:17-82`

**ABEND-ROUTINE** — `// source: COCRDUPC.cbl:1531-1556`
- If ABEND-MSG empty → default 'UNEXPECTED ABEND OCCURRED.'; set ABEND-CULPRIT; `EXEC CICS SEND FROM(ABEND-DATA) ERASE NOHANDLE`; `HANDLE ABEND CANCEL`; `EXEC CICS ABEND ABCODE('9999')`.

---

## 8. VALIDATION RULES & EXACT LITERAL MESSAGES

All messages land in `WS-RETURN-MSG` (X75) → CCARD-ERROR-MSG → ERRMSG field (and "if-msg-empty" guard means **the first error of the turn wins**; later edits do not overwrite). `// source: COCRDUPC.cbl:730, 743, 816, 833, 855, 868, 888, 903, 921, 939`

| # | Rule (field) | Condition | Exact message |
|---|---|---|---|
| V1 | Account filter required | blank/zero acct (search phase) | `Account number not provided` `// source: COCRDUPC.cbl:177-178, 731` |
| V2 | Account numeric/length | acct present but NOT NUMERIC | `ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER` (moved as a literal, not an 88) `// source: COCRDUPC.cbl:744-746` |
| V3 | Card filter required | blank/zero card | `Card number not provided` `// source: COCRDUPC.cbl:179-180, 774` |
| V4 | Card numeric/length | card present but NOT NUMERIC | `CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER` (literal) `// source: COCRDUPC.cbl:788-790` |
| V5 | No criteria | both filters blank | `No input received` (NO-SEARCH-CRITERIA-RECEIVED 88) `// source: COCRDUPC.cbl:185-186, 658` |
| V6 | Name required | new name blank/spaces/zeros | `Card name not provided` `// source: COCRDUPC.cbl:181-182, 817` |
| V7 | Name alpha only | non-letter, non-space chars remain after stripping A–Z/a–z | `Card name can only contain alphabets and spaces` `// source: COCRDUPC.cbl:183-184, 834` |
| V8 | Status required/valid | new status blank, or not 'Y'/'N' | `Card Active Status must be Y or N` `// source: COCRDUPC.cbl:195-196, 856, 869` |
| V9 | Expiry month | blank, or not 1..12 | `Card expiry month must be between 1 and 12` `// source: COCRDUPC.cbl:197-198, 889, 904` |
| V10 | Expiry year | blank, or not 1950..2099 | `Invalid card expiry year` `// source: COCRDUPC.cbl:199-200, 922, 940` |
| V11 | No change | UPPER(new card data) = UPPER(old card data) | `No change detected with respect to values fetched.` `// source: COCRDUPC.cbl:187-188, 682` |
| V12 | Card not found | READ → NOTFND | `Did not find cards for this search condition` `// source: COCRDUPC.cbl:203-204, 1400` |
| V13 | Lock failed | READ UPDATE ≠ NORMAL | `Could not lock record for update` `// source: COCRDUPC.cbl:205-206, 1446` |
| V14 | Concurrent change | snapshot mismatch on re-read | `Record changed by some one else. Please review` `// source: COCRDUPC.cbl:207-208, 1511` |
| V15 | Rewrite failed | REWRITE ≠ NORMAL | `Update of record failed` `// source: COCRDUPC.cbl:209-210, 1491` |
| V16 | File read error | READ → other RESP | `File Error: READ     on CARDDAT   returned RESP <r>,RESP2 <r2>` (assembled group) `// source: COCRDUPC.cbl:133-152, 1407-1411` |

Other declared 88 messages (not always reached): `WS-EXIT-MESSAGE` = `PF03 pressed.Exiting`, `DID-NOT-FIND-ACCT-IN-CARDXREF` = `Did not find this account in cards database`, `XREF-READ-ERROR` = `Error reading Card Data File`, `CODING-TO-BE-DONE` = `Looks Good.... so far`. `// source: COCRDUPC.cbl:175-214`

**Validation order (edit phase):** name → status → expiry-month → expiry-year (each adds error but only first message sticks). `// source: COCRDUPC.cbl:698-708`

**"NOT NUMERIC" semantics:** COBOL `IS NUMERIC` on a `X(11)`/`X(16)` display field is true only if every byte is `'0'..'9'` (spaces and low-values fail; leading/trailing spaces fail). Port must reproduce: the search keys are validated as exactly-N-digit all-numeric strings, **not** parsed as integers. `// source: COCRDUPC.cbl:740, 784`

---

## 9. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

**8.1 — Account number is never matched against the card.** The card is read by **card number only** (RIDFLD = WS-CARD-RID-CARDNUM); the account-id line `MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID` is commented out, and the `CARDAIX` alt-path is never opened. So the displayed/updated card may belong to a **different account** than the one the user typed — the entered account number is validated for format only, then echoed/stored but never used to verify the card↔account relationship. Reproduce: do not add an `acct_id` predicate to the lookup. `// source: COCRDUPC.cbl:1379, 1384, 1424; 253-254 (CARDAIX unused)`

**8.2 — `CARD-UPDATE-ACCT-ID` is taken from the *typed* account, not the card's real account.** In 9200 the rewrite sets `CARD-UPDATE-ACCT-ID = CC-ACCT-ID-N` (user input), overwriting whatever `CARD-ACCT-ID` was on file. Combined with 8.1, a successful update can **re-point the card's acct_id to an unrelated account number**. Reproduce exactly: write acct_id from CC-ACCT-ID-N. `// source: COCRDUPC.cbl:1463`

**8.3 — CVV round-trips through the *new* details which the screen never collects.** 9200 sets `CARD-CVV-CD-X = CCUP-NEW-CVV-CD`, but `CCUP-NEW-CVV-CD` is never populated from any screen field (the BMS map has no CVV field; 1100-RECEIVE-MAP never touches it). After `INITIALIZE CCUP-NEW-DETAILS` it is LOW-VALUES, so `CARD-CVV-CD-X`←LOW-VALUES then `CARD-CVV-CD-N`→`CARD-UPDATE-CVV-CD`. The rewrite therefore zeroes/garbles the CVV on every save. Reproduce the exact MOVE chain (LOW-VALUES into a `9(3)` redefine → resulting numeric value) rather than preserving the on-file CVV. `// source: COCRDUPC.cbl:306, 586, 1464-1465`

**8.4 — `9300` early-exit jumps to the *caller's* exit label.** In 9300-CHECK-CHANGE-IN-REC the mismatch branch does `GO TO 9200-WRITE-PROCESSING-EXIT` (the *caller* paragraph's exit), not `9300-…-EXIT`. Because 9300 is PERFORM…THRU'd from 9200, this works by coincidence (control returns up the PERFORM stack), but is a cross-paragraph GO TO. Also the `END-IF EXIT` on the same physical line is malformed-looking but parses as END-IF then the EXIT of the period. Preserve the control flow: on mismatch, set DATA-WAS-CHANGED, refresh snapshot, and unwind out of 9200 (no REWRITE). `// source: COCRDUPC.cbl:1518-1519`

**8.5 — `1230-EDIT-NAME` treats `CCUP-NEW-CRDNAME EQUAL ZEROS` as "not supplied".** A name of all-zero characters ('0…0') would be flagged blank. Harmless in practice but faithful. `// source: COCRDUPC.cbl:813`

**8.6 — Expiry-day is shown but never editable, yet EXPDAYI is received and stored.** 1100 unconditionally moves EXPDAYI→CCUP-NEW-EXPDAY (no `*`/space scrub), but the BMS field is `DRK,PROT`; 3200 always re-sends `CCUP-OLD-EXPDAY` (even on the CHANGES-MADE branch). So the day field is functionally read-only but its received value is briefly captured. Preserve the unconditional receive + always-old re-send. `// source: COCRDUPC.cbl:621, 1122-1123` (the commented-out line 1122 documents the intent)

**8.7 — Disallowed PF keys silently become ENTER.** Any AID other than the gated set is coerced to ENTER (no "invalid key" message), so e.g. PF7/PF8 act like ENTER. Reproduce. `// source: COCRDUPC.cbl:413-424`

**8.8 — Year lower bound 1950, not "current year".** Expiry year valid range is a fixed `1950 THRU 2099`, so a long-expired card year (e.g. 1951) passes validation. Faithful. `// source: COCRDUPC.cbl:96-99, 934`

---

## 10. PORT NOTES (relational translation + tricky COBOL semantics)

- **Single keyed CARD read.** Implement the CARD repository with: `Read(cardNum)` → `SELECT … WHERE card_num=@n` returning FileStatus '00'/'23'; `ReadForUpdate(cardNum)` → tracked read (begin UoW); `Rewrite(record)` → `UPDATE … WHERE card_num=@n`. Map RESP NORMAL→'00', NOTFND→'23', other→hard error. Do **not** filter by acct_id (faithful bug 8.1). `// source: COCRDUPC.cbl:1382-1390, 1427-1436, 1477-1483`
- **Optimistic lock = re-read + field compare**, not a DB lock. The original relies on CICS READ-UPDATE enqueue; the relational port reproduces the *observable* behavior via 9300's snapshot compare. With single-process console execution, DATA-WAS-CHANGED is effectively unreachable unless the OLD snapshot was stale across turns — keep the compare so the SHOW-DETAILS re-loop is preserved. `// source: COCRDUPC.cbl:1453-1457, 1498-1519`
- **REDEFINES X/N pairs** (`CC-ACCT-ID`/`-N`, `CC-CARD-NUM`/`-N`, `CARD-MONTH-CHECK`/`-N`, `CARD-YEAR-CHECK`/`-N`, `CARD-CVV-CD-X`/`-N`): model each as one string field plus a numeric accessor; the "X" view holds the raw display chars (incl. low-values/spaces), the "N" view interprets them as zoned digits. `IS NUMERIC` must be evaluated on the **X view** (all bytes 0–9), and `= ZEROS` on the **N view**. `// source: CVCRD01Y.cpy:36,39; COCRDUPC.cbl:93-98, 107-123`
- **`INSPECT … CONVERTING`** is a per-character translate: (a) name-alpha check converts A–Z/a–z→space then trims to test "letters & spaces only"; (b) name uppercasing converts lower→upper. Port: `string.Translate`/replace, not regex semantics; preserve that digits/punctuation are *kept* in the alpha-check (so a name with digits fails). `// source: COCRDUPC.cbl:824-826, 1356-1358, 1499-1501`
- **`STRING … DELIMITED BY SIZE`** builds `YYYY-MM-DD` (10 chars) from the three new-detail parts; if any part contains LOW-VALUES it is copied verbatim into the date (so a malformed date can be written — but edits should have caught blanks). Port: concatenate fixed-width slices with `-` separators, no trimming. `// source: COCRDUPC.cbl:1467-1474`
- **`INITIALIZE`** sets group items to their type defaults (numeric→0, alphanumeric→spaces) **except** items with an explicit VALUE LOW-VALUES 88 default keep program semantics; e.g. `INITIALIZE CCUP-NEW-DETAILS` leaves CVV as spaces (faithful bug 8.3 depends on the subsequent LOW-VALUES move at 1100 not the INITIALIZE). Reproduce per-field defaults exactly. `// source: COCRDUPC.cbl:374-376, 391-392, 506, 519-522, 586, 1345, 1461`
- **`FUNCTION UPPER-CASE` equality** for the no-change test compares the **whole CARDDATA group** (name+expiry+status, 59 bytes) case-insensitively; because OLD-CRDNAME was already uppercased on load (9000) and NEW-CRDNAME is uppercased here, this is effectively case-insensitive name comparison plus exact compare of the rest. `// source: COCRDUPC.cbl:680-683`
- **Edited/zoned numeric** — `CARD-CVV-CD-N` (`9(3)`) populated from LOW-VALUES then moved into a `9(3)` target: emulate COBOL zoned-display semantics (each byte's zone nibble), which for LOW-VALUES yields an implementation-defined value; pin it with a characterization test. `// source: COCRDUPC.cbl:107-109, 1464-1465`
- **Header date/time** from `FUNCTION CURRENT-DATE` → use `IClock` (mask in tests). Format CURDATEO = `mm/dd/yy`, CURTIMEO = `hh:mm:ss`. `// source: COCRDUPC.cbl:1055-1074; CSDAT01Y.cpy`
- **BMS attribute model.** The console renderer needs: per-field protect/unprotect, color (RED/DEFAULT/NEUTRAL), bright/dark, `*` placeholder injection, and cursor position (the `…L = -1` convention selects which field gets the cursor). Replicate 3300's decision tree exactly so screen-parity tests match field attributes. `// source: COCRDUPC.cbl:1168-1318`
- **COMMAREA length is fixed at 2000.** Always RETURN a 2000-byte commarea (CARDDEMO segment + program-private tail). Model as two typed segments with a fixed serialized width so the tail offset (`LENGTH OF CARDDEMO-COMMAREA`) is stable. `// source: COCRDUPC.cbl:324, 396-400, 549-557`

---

## 11. OPEN QUESTIONS / RISKS

- **CVV destruction (8.3):** confirm with the conversion owner that the CVV-zeroing-on-save bug must be reproduced in the relational port (it materially corrupts data). Default per the faithful-bug rule: **reproduce it**, pin with a test, log in `_design/faithful-bugs.md`.
- **Acct re-point (8.1/8.2):** same — reproduce; document that a "successful update" can change `card.acct_id` to the typed value with no integrity check.
- **Zoned-display value of LOW-VALUES → 9(3):** the exact numeric result of moving LOW-VALUES through `CARD-CVV-CD-X`/`-N` is implementation-defined; pin to whatever the .NET CobolDecimal/zoned helper produces and treat that as the golden value (no COBOL oracle available per ARCHITECTURE.md §Verification).
- **READ UPDATE lock semantics:** in single-process console mode there is no real contention; the COULD-NOT-LOCK path is only reachable if the row was deleted between the display read and the save read. Confirm test coverage strategy (characterization) for that branch.
- **`CARDAIX` alt-path:** declared but unused; no alt-index needed. Confirm no other program in the suite relies on COCRDUPC opening it (it does not).
```
