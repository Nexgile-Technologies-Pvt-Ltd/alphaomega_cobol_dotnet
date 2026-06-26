# PORT SPEC — COTRTUPC (Transaction Type Update)

Online CICS pseudo-conversational, DB2-backed program that maintains (view / add / change / delete)
rows of the `CARDDEMO.TRANSACTION_TYPE` table (the 2-char transaction-type code and its 50-char
description). It is the *update* counterpart of the list program COTRTLIC.
Source: `Old_Cobol_Code/.../app/app-transaction-type-db2/cbl/COTRTUPC.cbl`.

This is a **DB2 module** (one of the optional `*-db2` apps). Unlike the base-app VSAM screens it talks to
DB2 via embedded `EXEC SQL` (SELECT/UPDATE/INSERT/DELETE) against the relational table — so the relational
port is almost a one-for-one translation of the SQL already in the source.

---

## 1. Purpose & Invocation

**Purpose.** COTRTUPC is the admin "Maintain Transaction Type" screen. The operator enters a 2-digit
Transaction Type code and presses Enter to **fetch** the existing row (code + description) into the screen.
Once shown, the operator can: edit the description and press **F5** (with a validate→confirm→save handshake)
to UPDATE the row; press **F4** to DELETE the row (with an F4-again confirm step); or, if the key was valid
but no row exists, press **F5** to switch into "add" mode and INSERT a new row. **F12** cancels the current
pending action, **F3** exits back to the caller. The program is heavily state-driven through a single
`TTUP-CHANGE-ACTION` flag carried in its private commarea. // source: COTRTUPC.cbl:2-4,40-44,295-327

**How invoked.**
- **CICS TRANSID:** `CTTU` (`LIT-THISTRANID`). // source: COTRTUPC.cbl:203-204
- **Program id:** `COTRTUPC` (`LIT-THISPGM`). // source: COTRTUPC.cbl:201-202
- **Mapset/Map:** `COTRTUP ` / `CTRTUPA` (`LIT-THISMAPSET` / `LIT-THISMAP`). // source: COTRTUPC.cbl:205-208
- Pseudo-conversational: every turn ends with `EXEC CICS RETURN TRANSID('CTTU') COMMAREA(WS-COMMAREA)`.
  // source: COTRTUPC.cbl:567-571
- Reached via **XCTL** from the Admin menu `COADM01C` (transid `CA00`) or from the Transaction-Type list
  program `COTRTLIC` (transid `CTLI`); both are recognized as "from program" values that force a fresh
  entry. // source: COTRTUPC.cbl:209-224,366-374,465-468
- On `EIBCALEN = 0` (cold start) it INITIALIZEs the commarea and treats the turn as first-entry.
  // source: COTRTUPC.cbl:366-374
- **Exit target (XCTL on F3):** `CDEMO-FROM-PROGRAM` if set, else `COADM01C` (`LIT-ADMINPGM`); transid set
  to `CDEMO-FROM-TRANID` if set, else `CA00` (`LIT-ADMINTRANID`). A `SYNCPOINT` is issued before the XCTL.
  // source: COTRTUPC.cbl:429-460

---

## 2. FILE / TABLE Access

No VSAM/QSAM files. All persistence is embedded DB2 SQL against one table.

| Logical object | Relational table (ARCHITECTURE.md) | Operation(s) | SQL in source | Lines |
|---|---|---|---|---|
| `CARDDEMO.TRANSACTION_TYPE` (DCLGEN `DCLTRTYP`) | **TRANSACTION_TYPE** (`TR_TYPE CHAR(2)` PK, `TR_DESCRIPTION VARCHAR(50)`) — see ARCHITECTURE.md "Optional-module tables" | keyed SELECT; UPDATE; INSERT; DELETE | see below | 1469-1662 |

**SELECT (read one row by key)** — paragraph `9100-GET-TRANSACTION-TYPE`:
```sql
SELECT TR_TYPE, TR_DESCRIPTION
  INTO :DCL-TR-TYPE, :DCL-TR-DESCRIPTION
  FROM CARDDEMO.TRANSACTION_TYPE
 WHERE TR_TYPE = :DCL-TR-TYPE
```
SQLCODE handling: `0` = found (`FOUND-TRANTYPE-IN-TABLE`); `+100` = not found (`WS-RECORD-NOT-FOUND`);
`<0` = error (build SQLCODE message). // source: COTRTUPC.cbl:1475-1509

**UPDATE** — paragraph `9600-WRITE-PROCESSING`:
```sql
UPDATE CARDDEMO.TRANSACTION_TYPE
   SET TR_DESCRIPTION = :DCL-TR-DESCRIPTION
 WHERE TR_TYPE = :DCL-TR-TYPE
```
SQLCODE handling: `0` = SYNCPOINT (commit); `+100` = row absent → fall through to INSERT
(`9700-INSERT-RECORD`); `-911` = could-not-lock (deadlock/timeout); `<0` = update failed.
// source: COTRTUPC.cbl:1544-1578

**INSERT** — paragraph `9700-INSERT-RECORD`:
```sql
INSERT INTO CARDDEMO.TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION)
VALUES (:DCL-TR-TYPE, :DCL-TR-DESCRIPTION)
```
SQLCODE handling: `0` = SYNCPOINT; other = update-failed message. // source: COTRTUPC.cbl:1596-1619

**DELETE** — paragraph `9800-DELETE-PROCESSING`:
```sql
DELETE FROM CARDDEMO.TRANSACTION_TYPE
 WHERE TR_TYPE = :DCL-TR-TYPE
```
SQLCODE handling: `0` = delete done + SYNCPOINT; `-532` = child-record referential-integrity violation
("delete child records first"); other = delete failed. // source: COTRTUPC.cbl:1624-1662

**Host variable `DCL-TR-DESCRIPTION` is a DB2 VARCHAR** — a group of `DCL-TR-DESCRIPTION-LEN PIC S9(4) COMP`
(2-byte length prefix) + `DCL-TR-DESCRIPTION-TEXT PIC X(50)`. // source: DCLTRTYP.dcl:40-46

**Repository / port contract (per ARCHITECTURE.md §VSAM->SQL and optional-module tables):**
- SELECT by PK → `SELECT ... WHERE TR_TYPE = @t`. Not-found (`+100`) → the port's "no row" result.
- UPDATE → `UPDATE ... WHERE TR_TYPE = @t`; if 0 rows affected the COBOL returns SQLCODE `+100`, which the
  program turns into an INSERT — **preserve this "update-then-insert-on-+100" idiom at the program layer**
  (do NOT replace with a single upsert that skips the +100 branch, because the screen-state transitions
  differ slightly and the faithful path is update-first).
- INSERT → `INSERT`. Duplicate-key would be a negative SQLCODE → "update failed" branch.
- DELETE → `DELETE`; `-532` (FK child rows) and other negatives map to distinct error messages.
- `SYNCPOINT` after each successful write → in the relational port, commit the EF Core transaction at that
  point. There is **no separate read-for-update lock**; the SELECT and the later UPDATE/DELETE happen in
  *different* pseudo-conversational turns, so a held lock is not modeled (the `-911` branch is reproduced
  only as a possible SQLCODE outcome of the UPDATE).

---

## 3. Working storage, COMMAREA & symbolic map

### 3.1 Private program commarea `WS-THIS-PROGCOMMAREA`
Appended in the RETURN commarea immediately after `CARDDEMO-COMMAREA`; on entry it is sliced back out by
byte offset `LENGTH OF CARDDEMO-COMMAREA + 1`. // source: COTRTUPC.cbl:294-336,376-380,559-571

- `TTUP-CHANGE-ACTION PIC X(1)` (init `LOW-VALUES`) — **the master state flag**, with these 88s
  (// source: COTRTUPC.cbl:296-327):

| 88-name | Value | Meaning |
|---|---|---|
| `TTUP-DETAILS-NOT-FETCHED` | LOW-VALUES, SPACES | nothing fetched yet (initial state) |
| `TTUP-INVALID-SEARCH-KEYS` | `'K'` | key failed validation |
| `TTUP-DETAILS-NOT-FOUND` | `'X'` | key valid but no DB row |
| `TTUP-SHOW-DETAILS` | `'S'` | row fetched and displayed |
| `TTUP-CREATE-NEW-RECORD` | `'R'` | add-new mode confirmed |
| `TTUP-REVIEW-NEW-RECORD` | `'V'` | (declared, not used in flow) |
| `TTUP-DELETE-IN-PROGRESS` | `'9','8','7','6'` | any delete sub-state |
| `TTUP-CONFIRM-DELETE` | `'9'` | awaiting F4 delete confirm |
| `TTUP-START-DELETE` | `'8'` | delete being executed |
| `TTUP-DELETE-DONE` | `'7'` | delete succeeded |
| `TTUP-DELETE-FAILED` | `'6'` | delete failed |
| `TTUP-CHANGES-MADE` | `'E','N','L','F'` | any post-edit state |
| `TTUP-CHANGES-NOT-OK` | `'E'` | edits found errors |
| `TTUP-CHANGES-OK-NOT-CONFIRMED` | `'N'` | edits valid, awaiting F5 confirm |
| `TTUP-CHANGES-FAILED` | `'L','F'` | save attempted, failed |
| `TTUP-CHANGES-OKAYED-LOCK-ERROR` | `'L'` | save blocked by lock (-911) |
| `TTUP-CHANGES-OKAYED-BUT-FAILED` | `'F'` | save failed (other neg SQLCODE) |
| `TTUP-CHANGES-OKAYED-AND-DONE` | `'C'` | save committed |
| `TTUP-CHANGES-BACKED-OUT` | `'B'` | update cancelled |

- `TTUP-OLD-DETAILS` = `TTUP-OLD-TTYP-TYPE X(2)` + `TTUP-OLD-TTYP-TYPE-DESC X(50)` — the last-fetched DB
  values (used for change detection & "show original"). // source: COTRTUPC.cbl:328-331
- `TTUP-NEW-DETAILS` = `TTUP-NEW-TTYP-TYPE X(2)` + `TTUP-NEW-TTYP-TYPE-DESC X(50)` — the values the user
  typed (normalized). // source: COTRTUPC.cbl:332-335

### 3.2 `CARDDEMO-COMMAREA` (COCOM01Y) fields used
`CDEMO-FROM-TRANID`, `CDEMO-FROM-PROGRAM`, `CDEMO-TO-TRANID`, `CDEMO-TO-PROGRAM`,
`CDEMO-PGM-CONTEXT` (88s `CDEMO-PGM-ENTER`=0 / `CDEMO-PGM-REENTER`=1), `CDEMO-USRTYP-ADMIN`,
`CDEMO-LAST-MAP`, `CDEMO-LAST-MAPSET`, `CDEMO-ACCT-ID`, `CDEMO-CARD-NUM`, `CDEMO-ACCT-STATUS`.
// source: COCOM01Y.cpy:19-44; COTRTUPC.cbl:366-374,429-451,471-477,1067-1072

### 3.3 Other working storage of note
- `CC-WORK-AREA` from `CVCRD01Y` provides `CCARD-AID` (88s `CCARD-AID-ENTER`, `-PFK03`, `-PFK04`,
  `-PFK05`, `-PFK12`, etc.), `CCARD-NEXT-PROG/MAPSET/MAP`, `CCARD-ERROR-MSG`. // source: CVCRD01Y.cpy:3-31; COTRTUPC.cbl:241
- `WS-INFO-MSG X(40)` (88s = the info-prompt literals, §6) and `WS-RETURN-MSG X(75)` (88s = the error/return
  literals, §6). // source: COTRTUPC.cbl:142-196
- Edit flags: `WS-EDIT-TTYP-FLAG` (88s `FLG-TRANFILTER-ISVALID`=LOW-VALUES / `-NOT-OK`='0' / `-BLANK`='B'),
  `WS-EDIT-DESC-FLAGS` (88s `FLG-DESCRIPTION-*`), generic `WS-EDIT-ALPHANUM-ONLY-FLAGS` (88s `FLG-ALPHNANUM-*`).
  // source: COTRTUPC.cbl:94-103,58-61
- `WS-INPUT-FLAG` (88s `INPUT-OK`='0' / `INPUT-ERROR`='1' / `INPUT-PENDING`=LOW-VALUES);
  `WS-DATACHANGED-FLAG` (88s `NO-CHANGES-FOUND`='0' / `CHANGE-HAS-OCCURRED`='1');
  `WS-PFK-FLAG` (88s `PFK-VALID`='0' / `PFK-INVALID`='1');
  `WS-TABLE-READ-FLAGS` → `WS-TRANTYPE-MASTER-READ-FLAG` (88 `FOUND-TRANTYPE-IN-TABLE`='1').
  // source: COTRTUPC.cbl:81-90,78-80,125-127
- `WS-DISP-SQLCODE PIC ----9` — edited SQLCODE for messages. // source: COTRTUPC.cbl:68
- Inspect helpers `LIT-ALL-ALPHANUM-FROM` / `LIT-ALPHANUM-SPACES-TO` for the alphanumeric scrub.
  // source: COTRTUPC.cbl:230-254

### 3.4 BMS map `CTRTUPA` (mapset `COTRTUP`, 24×80) — symbolic copybook COTRTUP.cpy
DFHMSD `MODE=INOUT`, `TIOAPFX=YES`, `DSATTS/MAPATTS=(COLOR,HILIGHT,PS,VALIDN)`.
// source: COTRTUP.bms:20-28

| Field | Pos | Len | Attr (BMS) | Read on RECEIVE | Written on SEND |
|---|---|---|---|---|---|
| TRNNAME | 1,7 | 4 | ASKIP FSET | no | yes (`'CTTU'`) |
| TITLE01 | 1,21 | 40 | ASKIP | no | yes (`CCDA-TITLE01`) |
| CURDATE | 1,71 | 8 | ASKIP | no | yes (`mm/dd/yy`) |
| PGMNAME | 2,7 | 8 | ASKIP | no | yes (`'COTRTUPC'`) |
| TITLE02 | 2,21 | 40 | ASKIP | no | yes (`CCDA-TITLE02`) |
| CURTIME | 2,71 | 8 | ASKIP | no | yes (`hh:mm:ss`) |
| (labels) | various | — | ASKIP | no | static |
| **TRTYPCD** | 12,26 | 2 | IC UNPROT, HILIGHT=UNDERLINE | **yes** (`TRTYPCDI`) | yes (`TRTYPCDO`) |
| **TRTYDSC** | 14,26 | 50 | UNPROT, HILIGHT=UNDERLINE | **yes** (`TRTYDSCI`) | yes (`TRTYDSCO`) |
| INFOMSG | 22,23 | 45 | ASKIP HILIGHT=OFF | no | yes (`INFOMSGO`, centered) |
| ERRMSG | 23,1 | 78 | ASKIP BRT FSET, RED | no | yes (`ERRMSGO`) |
| FKEYS | 24,1 | 21 | ASKIP NORM | no | yes (legend `ENTER=Process F3=Exit`) |
| FKEY04 | 24,23 | 9 | ASKIP **DRK** | no | attr toggled BRT/DRK |
| FKEY05 | 24,33 | 8 | ASKIP **DRK** | no | attr toggled BRT/DRK |
| FKEY06 | 24,43 | 6 | ASKIP **DRK** | no | (static "F6=Add"; never lit by code) |
| FKEY12 | 24,69 | 10 | ASKIP **DRK** | no | attr toggled BRT/DRK |

Only **TRTYPCD** and **TRTYDSC** are input fields. Each symbolic field has the standard
`...L` (length/cursor), `...F`/`...A` (input flag / output attribute), `...I` (input), `...O` (output),
`...C` (color), `...H` (highlight) members. // source: COTRTUP.bms:29-137; COTRTUP.cpy:17-201

DFHBMSCA attribute bytes used: `DFHBMPRF` (protect), `DFHBMFSE` (unprotect + FSET/modified),
`DFHBMASB` (autoskip bright), `DFHBMDAR` (dark), `DFHRED`, `DFHRED`/`DFHBMDAR` color/visibility.
// source: COTRTUPC.cbl:1284,1297,1369,1379-1380,1388,1390,1401,1403,1408,1413,1421,1333,1339

---

## 4. PARAGRAPH-BY-PARAGRAPH outline

> Method names mirror paragraph names. PERFORM ... THRU ...-EXIT pairs are single methods.
> `GO TO COMMON-RETURN` ends the turn; `GO TO <para>-EXIT` is an early return from that method.

### 0000-MAIN (`COTRTUPC.cbl:345-557`)
1. `EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE)`; INITIALIZE `CC-WORK-AREA`, `WS-MISC-STORAGE`,
   `WS-COMMAREA`; MOVE `'CTTU'`→`WS-TRANID`; `SET WS-RETURN-MSG-OFF` (LOW-VALUES). // src:348-362
2. **Commarea load:** IF `EIBCALEN=0` OR (from `COADM01C` and not re-enter) OR (from `COTRTLIC` and not
   re-enter): INITIALIZE both commareas, `SET CDEMO-PGM-ENTER`, `SET TTUP-DETAILS-NOT-FETCHED`. ELSE slice
   `DFHCOMMAREA` into `CARDDEMO-COMMAREA` and the trailing `WS-THIS-PROGCOMMAREA`. // src:366-381
3. PERFORM `YYYY-STORE-PFKEY` (map EIBAID→`CCARD-AID-*`). // src:386-387
4. `SET PFK-INVALID`; PERFORM `0001-CHECK-PFKEYS`. // src:398-401
5. **Simulate-initial EVALUATE:** if (F12 while SHOW/CREATE/NOT-FOUND) OR CHANGES-OKAYED-AND-DONE OR
   CHANGES-FAILED OR (CHANGES-BACKED-OUT and OLD-DETAILS empty) OR DELETE-DONE OR DELETE-FAILED →
   `SET CDEMO-PGM-ENTER` + `SET TTUP-DETAILS-NOT-FETCHED` (force a clean fetch screen next). // src:405-419
6. **Main dispatch EVALUATE TRUE** (first matching WHEN wins; statement order preserved):
   - **WHEN `CCARD-AID-PFK03`** → set TO-TRANID/PROGRAM (from-or-default), stamp FROM-TRANID/PROGRAM =
     this pgm, `SET CDEMO-USRTYP-ADMIN`, `SET CDEMO-PGM-ENTER`, save LAST-MAP/MAPSET, `SYNCPOINT`,
     `XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. // src:429-460
   - **WHEN (not re-enter and from `COADM01C`) / (not re-enter and from `COTRTLIC`) / (PGM-ENTER and
     NOT-FETCHED)** → INITIALIZE `WS-THIS-PROGCOMMAREA`, `WS-MISC-STORAGE`, `CDEMO-ACCT-ID`; PERFORM
     `3000-SEND-MAP`; `SET CDEMO-PGM-REENTER`; `SET TTUP-DETAILS-NOT-FETCHED`; GO TO COMMON-RETURN.
     // src:465-478
   - **WHEN F04 and CONFIRM-DELETE** → `SET TTUP-START-DELETE`; PERFORM `9800-DELETE-PROCESSING`; PERFORM
     `3000-SEND-MAP`; GO TO COMMON-RETURN. // src:482-489
   - **WHEN F04 and SHOW-DETAILS** → `SET TTUP-CONFIRM-DELETE`; SEND-MAP; COMMON-RETURN. // src:493-498
   - **WHEN F05 and DETAILS-NOT-FOUND** → `SET TTUP-CREATE-NEW-RECORD`; SEND-MAP; COMMON-RETURN. // src:503-508
   - **WHEN F05 and CHANGES-OK-NOT-CONFIRMED** → PERFORM `9600-WRITE-PROCESSING`; SEND-MAP; COMMON-RETURN.
     // src:514-520
   - **WHEN F12 and (CHANGES-OK-NOT-CONFIRMED / CONFIRM-DELETE / SHOW-DETAILS)** → `SET FOUND-TRANTYPE-IN-TABLE`;
     PERFORM `2000-DECIDE-ACTION`; SEND-MAP; COMMON-RETURN. // src:524-533
   - **WHEN `WS-INVALID-KEY-PRESSED`** (i.e. message flag set by check-pfkeys) → SEND-MAP; COMMON-RETURN.
     // src:539-542
   - **WHEN OTHER** → PERFORM `1000-PROCESS-INPUTS`; PERFORM `2000-DECIDE-ACTION`; PERFORM `3000-SEND-MAP`;
     GO TO COMMON-RETURN. // src:548-555

### COMMON-RETURN (`COTRTUPC.cbl:559-572`)
MOVE `WS-RETURN-MSG`→`CCARD-ERROR-MSG`; copy `CARDDEMO-COMMAREA` then `WS-THIS-PROGCOMMAREA` into
`WS-COMMAREA` (byte-concatenated); `EXEC CICS RETURN TRANSID('CTTU') COMMAREA(WS-COMMAREA) LENGTH(...)`.

### 0001-CHECK-PFKEYS (`COTRTUPC.cbl:577-623`)
Validate the AID against current state. PFK-VALID iff: F03; OR (Enter and NOT CONFIRM-DELETE);
OR (F04 and (SHOW-DETAILS or CONFIRM-DELETE)); OR (F05 and (CHANGES-OK-NOT-CONFIRMED or DETAILS-NOT-FOUND or
DELETE-IN-PROGRESS)); OR (F12 and (CHANGES-OK-NOT-CONFIRMED or SHOW-DETAILS or DETAILS-NOT-FOUND or
CONFIRM-DELETE or CREATE-NEW-RECORD)). Else `SET PFK-INVALID`; and if message is still off,
`SET WS-INVALID-KEY-PRESSED`. (Must mirror `3391-SETUP-PFKEY-ATTRS`.) // src:577-623

### 1000-PROCESS-INPUTS (`COTRTUPC.cbl:625-640`)
PERFORM `1100-RECEIVE-MAP`, `1150-STORE-MAP-IN-NEW`, `1200-EDIT-MAP-INPUTS`; then MOVE `WS-RETURN-MSG`→
`CCARD-ERROR-MSG`, and set `CCARD-NEXT-PROG/MAPSET/MAP` to this program's identifiers.

### 1100-RECEIVE-MAP (`COTRTUPC.cbl:641-650`)
`EXEC CICS RECEIVE MAP('CTRTUPA') MAPSET('COTRTUP') INTO(CTRTUPAI) RESP/RESP2`. Reads `TRTYPCDI` and
`TRTYDSCI`.

### 1150-STORE-MAP-IN-NEW (`COTRTUPC.cbl:652-688`)
- **Guard:** IF `TTUP-DETAILS-NOT-FOUND` AND not F05 AND `TRIM(TRTYPCDI)=TTUP-NEW-TTYP-TYPE` → return
  (keep prior NEW values). // src:654-661
- INITIALIZE `TTUP-NEW-DETAILS`. // src:663
- Type: IF `TRTYPCDI='*'` OR SPACES → `MOVE LOW-VALUES TO TTUP-NEW-TTYP-TYPE`; ELSE MOVE
  `TRIM(TRTYPCDI)`. // src:667-673
- Desc: IF `TRTYDSCI='*'` OR SPACES → `MOVE LOW-VALUES TO TTUP-NEW-TTYP-TYPE-DESC`; ELSE MOVE
  `TRIM(TRTYDSCI)`. // src:678-684

### 1200-EDIT-MAP-INPUTS (`COTRTUPC.cbl:689-781`)
1. `SET INPUT-OK`. // src:690
2. **Re-prompt-same-not-found shortcut:** IF DETAILS-NOT-FOUND AND `TRIM(TRTYPCDI)=TTUP-NEW-TTYP-TYPE`:
   if F05 CONTINUE else `SET TTUP-DETAILS-NOT-FETCHED`; then `SET FLG-TRANFILTER-ISVALID` and exit. // src:698-710
3. IF CREATE-NEW-RECORD OR CHANGES-OK-NOT-CONFIRMED → skip key edit (CONTINUE). // src:712-714
4. ELSE PERFORM `1210-EDIT-TRANTYPE`; then:
   - IF `FLG-TRANFILTER-BLANK`: if msg off `SET NO-SEARCH-CRITERIA-RECEIVED`; `SET TTUP-DETAILS-NOT-FETCHED`;
     exit. // src:720-726
   - IF `FLG-TRANFILTER-NOT-OK`: `SET TTUP-INVALID-SEARCH-KEYS`; `SET TTUP-DETAILS-NOT-FETCHED`; exit. // src:728-732
   - IF `TTUP-DETAILS-NOT-FETCHED`: exit. // src:734-736
5. `SET FLG-TRANFILTER-ISVALID`; PERFORM `1205-COMPARE-OLD-NEW`. // src:741-744
6. IF NO-CHANGES-FOUND OR CHANGES-OK-NOT-CONFIRMED OR CHANGES-OKAYED-AND-DONE →
   `MOVE LOW-VALUES TO WS-NON-KEY-FLAGS`; exit. // src:746-751
7. `SET TTUP-CHANGES-NOT-OK`. // src:753
8. Edit description: name='Transaction Desc', value=`TTUP-NEW-TTYP-TYPE-DESC`, len=50; PERFORM
   `1230-EDIT-ALPHANUM-REQD`; MOVE result flags → `WS-EDIT-DESC-FLAGS`. // src:758-764
9. IF INPUT-ERROR CONTINUE; ELSE `SET TTUP-CHANGES-OK-NOT-CONFIRMED`. // src:772-776

### 1205-COMPARE-OLD-NEW (`COTRTUPC.cbl:783-816`)
`SET NO-CHANGES-FOUND`. IF (UPPER(NEW-TYPE)=UPPER(OLD-TYPE)) AND (UPPER(TRIM(NEW-DESC))=UPPER(TRIM(OLD-DESC)))
AND (LENGTH(TRIM(NEW-DESC))=LENGTH(TRIM(OLD-DESC))) → if msg off `SET NO-CHANGES-DETECTED`.
ELSE (something changed) → if msg off `SET CHANGE-HAS-OCCURRED`; then GO TO ...-EXIT. // src:783-812
(Case-insensitive type+desc compare; description compared trimmed for value but also by trimmed length.)

### 1210-EDIT-TRANTYPE (`COTRTUPC.cbl:820-847`)
`SET FLG-TRANFILTER-NOT-OK`. name='Tran Type code', value=`TTUP-NEW-TTYP-TYPE`, len=2; PERFORM
`1245-EDIT-NUM-REQD`; MOVE result flags → `WS-EDIT-TTYP-FLAG`.
IF valid: `COMPUTE WS-EDIT-NUMERIC-2 = NUMVAL(TTUP-NEW-TTYP-TYPE)` (PIC 9(2), **truncates to 2 digits**),
MOVE to `WS-EDIT-ALPHANUMERIC-2` (X(2)), `INSPECT ... REPLACING ALL SPACES BY ZEROS`, MOVE back to
`TTUP-NEW-TTYP-TYPE` → **left-zero-pads a 1-digit code to 2 digits** (e.g. "5"→"05"). // src:820-843

### 1230-EDIT-ALPHANUM-REQD (`COTRTUPC.cbl:849-905`)
Reusable "required alphanumeric" edit on `WS-EDIT-ALPHANUM-ONLY(1:WS-EDIT-ALPHANUM-LENGTH)`:
1. `SET FLG-ALPHNANUM-NOT-OK`. // src:851
2. **Blank:** if the slice is LOW-VALUES or SPACES or `LENGTH(TRIM)=0` → `SET INPUT-ERROR`,
   `SET FLG-ALPHNANUM-BLANK`; if msg off STRING `"<name> must be supplied."`; exit. // src:854-873
3. **Charset:** MOVE `LIT-ALL-ALPHANUM-FROM-X`→`LIT-ALL-ALPHANUM-FROM`; `INSPECT ... CONVERTING
   LIT-ALL-ALPHANUM-FROM TO LIT-ALPHANUM-SPACES-TO` (blanks out all letters/digits). If
   `LENGTH(TRIM(...))=0` (only allowed chars were present) CONTINUE; ELSE `SET INPUT-ERROR`,
   `SET FLG-ALPHNANUM-NOT-OK`; if msg off STRING `"<name> can have numbers or alphabets only."`; exit.
   // src:876-899
4. `SET FLG-ALPHNANUM-ISVALID`. // src:901

### 1245-EDIT-NUM-REQD (`COTRTUPC.cbl:907-976`)
Reusable "required numeric" edit on the same slice:
1. `SET FLG-ALPHNANUM-NOT-OK`. // src:909
2. **Blank:** same blank test → `SET INPUT-ERROR` + `SET FLG-ALPHNANUM-BLANK`; STRING
   `"<name> must be supplied."`; exit. // src:912-930
3. **Numeric:** IF `TEST-NUMVAL(slice)=0` (valid number) CONTINUE; ELSE `SET INPUT-ERROR` +
   `SET FLG-ALPHNANUM-NOT-OK`; STRING `"<name> must be numeric."`; exit. // src:934-949
4. **Non-zero:** IF `NUMVAL(slice)=0` → `SET INPUT-ERROR` + `SET FLG-ALPHNANUM-NOT-OK`; STRING
   `"<name> must not be zero."`; exit. // src:954-969
5. `SET FLG-ALPHNANUM-ISVALID`. // src:972

### 2000-DECIDE-ACTION (`COTRTUPC.cbl:978-1085`) — EVALUATE TRUE (first match wins)
- **WHEN DETAILS-NOT-FETCHED / WHEN F12** (two WHENs share the body): IF `FLG-TRANFILTER-ISVALID`:
  `SET WS-RETURN-MSG-OFF`; PERFORM `9000-READ-TRANTYPE`; if found `SET TTUP-SHOW-DETAILS` else
  `SET TTUP-DETAILS-NOT-FOUND`. ELSE (filter not valid) nested EVALUATE: CONFIRM-DELETE→
  `SET WS-DELETE-WAS-CANCELLED` + `SET TTUP-DETAILS-NOT-FETCHED`; CHANGES-OK-NOT-CONFIRMED→
  `SET WS-UPDATE-WAS-CANCELLED` + `SET TTUP-CHANGES-BACKED-OUT`; OTHER→`SET TTUP-DETAILS-NOT-FETCHED`.
  // src:984-1010
- **WHEN CONFIRM-DELETE and F12** → `SET TTUP-CONFIRM-DELETE` (stays in confirm). // src:1016-1018
- **WHEN SHOW-DETAILS** → IF INPUT-ERROR OR NO-CHANGES-DETECTED OR WS-INVALID-KEY CONTINUE; ELSE
  `SET TTUP-CHANGES-OK-NOT-CONFIRMED`. // src:1023-1030
- **WHEN CHANGES-NOT-OK** → CONTINUE. // src:1035-1036
- **WHEN CHANGES-BACKED-OUT** → `SET TTUP-CHANGES-NOT-OK`. // src:1041-1042
- **WHEN INVALID-SEARCH-KEYS** → CONTINUE. // src:1046-1047
- **WHEN F05 and DETAILS-NOT-FOUND** → `SET TTUP-CREATE-NEW-RECORD`. // src:1053-1055
- **WHEN CHANGES-OK-NOT-CONFIRMED** → CONTINUE. // src:1060-1061
- **WHEN CHANGES-OKAYED-AND-DONE** → `SET TTUP-SHOW-DETAILS`; if FROM-TRANID empty MOVE ZEROES→
  `CDEMO-ACCT-ID`/`CDEMO-CARD-NUM`, MOVE LOW-VALUES→`CDEMO-ACCT-STATUS`. // src:1065-1072
- **WHEN OTHER** → abend: ABEND-CULPRIT=this pgm, ABEND-CODE='0001', ABEND-MSG='UNEXPECTED DATA SCENARIO';
  PERFORM `ABEND-ROUTINE`. // src:1073-1080

### 3000-SEND-MAP (`COTRTUPC.cbl:1089-1108`)
PERFORM in order: `3100-SCREEN-INIT`, `3200-SETUP-SCREEN-VARS`, `3250-SETUP-INFOMSG`,
`3300-SETUP-SCREEN-ATTRS`, `3390-SETUP-INFOMSG-ATTRS`, `3391-SETUP-PFKEY-ATTRS`, `3400-SEND-SCREEN`.

### 3100-SCREEN-INIT (`COTRTUPC.cbl:1110-1138`)
MOVE LOW-VALUES→`CTRTUPAO`; `CURRENT-DATE`→`WS-CURDATE-DATA`; titles (`CCDA-TITLE01/02`), `'CTTU'`→TRNNAMEO,
`'COTRTUPC'`→PGMNAMEO; build `mm/dd/yy` into CURDATEO and `hh:mm:ss` into CURTIMEO from current date/time.
(Note `CURRENT-DATE` moved twice; harmless.) // src:1110-1134

### 3200-SETUP-SCREEN-VARS (`COTRTUPC.cbl:1140-1174`)
IF `CDEMO-PGM-ENTER` CONTINUE (leave fields empty). ELSE EVALUATE TRUE:
- DETAILS-NOT-FETCHED → `3201-SHOW-INITIAL-VALUES` (clear key field). // src:1146-1148
- SHOW-DETAILS / CONFIRM-DELETE / DELETE-FAILED / DELETE-DONE / CHANGES-BACKED-OUT → INITIALIZE
  NEW-DETAILS, `3202-SHOW-ORIGINAL-VALUES` (show OLD-* values). // src:1149-1156
- CHANGES-MADE / CHANGES-NOT-OK / DETAILS-NOT-FOUND / INVALID-SEARCH-KEYS / CREATE-NEW-RECORD /
  CHANGES-OKAYED-AND-DONE → `3203-SHOW-UPDATED-VALUES` (show NEW-* values). // src:1157-1164
- OTHER → INITIALIZE NEW-DETAILS, `3202-SHOW-ORIGINAL-VALUES`. // src:1165-1168

### 3201/3202/3203-SHOW-*-VALUES
- `3201`: MOVE LOW-VALUES→`TRTYPCDO` (clear key). // src:1176-1179
- `3202`: MOVE LOW-VALUES→`WS-NON-KEY-FLAGS`; MOVE `TTUP-OLD-TTYP-TYPE`→TRTYPCDO, `TTUP-OLD-TTYP-TYPE-DESC`
  →TRTYDSCO. // src:1185-1192
- `3203`: MOVE `TTUP-NEW-TTYP-TYPE`→TRTYPCDO, `TTUP-NEW-TTYP-TYPE-DESC`→TRTYDSCO. // src:1197-1201

### 3250-SETUP-INFOMSG (`COTRTUPC.cbl:1210-1268`)
Choose the info prompt via EVALUATE TRUE (PGM-ENTER → PROMPT-FOR-SEARCH-KEYS; NOT-FETCHED/INVALID-KEYS →
PROMPT-FOR-SEARCH-KEYS; NOT-FOUND → PROMPT-CREATE-NEW-RECORD; SHOW-DETAILS / (BACKED-OUT with empty OLD)
→ PROMPT-FOR-SEARCH-KEYS; BACKED-OUT / CHANGES-NOT-OK → PROMPT-FOR-CHANGES; CONFIRM-DELETE →
PROMPT-DELETE-CONFIRM; DELETE-FAILED → INFORM-FAILURE; DELETE-DONE → CONFIRM-DELETE-SUCCESS;
CREATE-NEW-RECORD → PROMPT-FOR-NEWDATA; CHANGES-OK-NOT-CONFIRMED → PROMPT-FOR-CONFIRMATION;
CHANGES-OKAYED-AND-DONE → CONFIRM-UPDATE-SUCCESS; LOCK-ERROR/BUT-FAILED → INFORM-FAILURE;
WS-NO-INFO-MESSAGE → PROMPT-FOR-SEARCH-KEYS). Then **center-justify** the chosen message:
`WS-STRING-LEN = LENGTH(TRIM(WS-INFO-MSG))`; `WS-STRING-MID = (LENGTH(WS-INFO-MSG) - WS-STRING-LEN)/2 + 1`
(40-char field, integer divide → truncates); place into `WS-STRING-OUT`; MOVE to INFOMSGO; MOVE
`WS-RETURN-MSG`→ERRMSGO. // src:1210-1265

### 3300-SETUP-SCREEN-ATTRS (`COTRTUPC.cbl:1269-1366`)
1. PERFORM `3310-PROTECT-ALL-ATTRS`. // src:1272
2. **Unprotect EVALUATE:** NOT-FETCHED / INVALID-KEYS / NOT-FOUND / (BACKED-OUT with empty OLD) → make
   TRTYPCD editable (`DFHBMFSE`); SHOW-DETAILS / CHANGES-NOT-OK / CREATE-NEW-RECORD / CHANGES-BACKED-OUT →
   `3320-UNPROTECT-FEW-ATTRS` (description editable); CHANGES-OK-NOT-CONFIRMED / CHANGES-OKAYED-AND-DONE /
   DELETE-IN-PROGRESS → keep protected; OTHER → make TRTYPCD editable. // src:1276-1298
3. **Cursor EVALUATE (`-1` to L field):** NOT-FETCHED / NOT-FOUND / INVALID-KEYS / FILTER-NOT-OK /
   FILTER-BLANK / OKAYED-AND-DONE / (BACKED-OUT empty OLD) → cursor on `TRTYPCDL`; CREATE-NEW-RECORD /
   NO-CHANGES-DETECTED / DESC-NOT-OK / DESC-BLANK / CHANGES-MADE / CHANGES-BACKED-OUT / SHOW-DETAILS →
   cursor on `TRTYDSCL`; OTHER → `TRTYPCDL`. // src:1303-1325
4. **Color:** IF FILTER-NOT-OK OR DELETE-FAILED → TRTYPCDC=RED; IF FILTER-BLANK AND PGM-REENTER →
   TRTYPCDO='*' and TRTYPCDC=RED. // src:1331-1340
5. IF NOT-FETCHED/NOT-FOUND/INVALID-KEYS/FILTER-BLANK/FILTER-NOT-OK → exit early. // src:1342-1350
6. ELSE expand `CSSETATY` (REPLACING TESTVAR1=DESCRIPTION, SCRNVAR2=TRTYDSC, MAPNAME3=CTRTUPA): IF
   (FLG-DESCRIPTION-NOT-OK OR FLG-DESCRIPTION-BLANK) AND PGM-REENTER → TRTYDSCC=RED, and if BLANK
   TRTYDSCO='*'. // src:1358-1361 + CSSETATY.cpy:17-27

### 3310 / 3320 (attr helpers)
- `3310-PROTECT-ALL-ATTRS`: `DFHBMPRF`→TRTYPCDA, TRTYDSCA, INFOMSGA. // src:1368-1372
- `3320-UNPROTECT-FEW-ATTRS`: `DFHBMFSE`→TRTYDSCA (description editable); `DFHBMPRF`→INFOMSGA. // src:1377-1381

### 3390-SETUP-INFOMSG-ATTRS (`COTRTUPC.cbl:1386-1395`)
IF WS-NO-INFO-MESSAGE → INFOMSGA=`DFHBMDAR` (dark) ELSE `DFHBMASB` (bright).

### 3391-SETUP-PFKEY-ATTRS (`COTRTUPC.cbl:1397-1426`)
Toggle the legend visibility (must mirror `0001-CHECK-PFKEYS`):
- Enter/FKEYS: CONFIRM-DELETE → FKEYSA=`DFHBMDAR` else `DFHBMASB`. // src:1400-1404
- F04: SHOW-DETAILS or CONFIRM-DELETE → FKEY04A=`DFHBMASB`. // src:1406-1409
- F05: CHANGES-OK-NOT-CONFIRMED or DETAILS-NOT-FOUND → FKEY05A=`DFHBMASB`. // src:1411-1414
- F12: CHANGES-OK-NOT-CONFIRMED or SHOW-DETAILS or DETAILS-NOT-FOUND or CONFIRM-DELETE or
  CREATE-NEW-RECORD → FKEY12A=`DFHBMASB`. // src:1416-1422

### 3400-SEND-SCREEN (`COTRTUPC.cbl:1428-1444`)
MOVE map/mapset → `CCARD-NEXT-MAPSET/MAP`; `EXEC CICS SEND MAP('CTRTUPA') MAPSET('COTRTUP') FROM(CTRTUPAO)
CURSOR ERASE FREEKB RESP(...)`.

### 9000-READ-TRANTYPE (`COTRTUPC.cbl:1447-1468`)
INITIALIZE `TTUP-OLD-DETAILS`; `SET WS-NO-INFO-MESSAGE`; PERFORM `9100-GET-TRANSACTION-TYPE`; if
`FLG-TRANFILTER-NOT-OK` exit; else PERFORM `9500-STORE-FETCHED-DATA`.

### 9100-GET-TRANSACTION-TYPE (`COTRTUPC.cbl:1469-1514`)
MOVE `TTUP-NEW-TTYP-TYPE`→`DCL-TR-TYPE`; run SELECT (§2); EVALUATE SQLCODE: 0 → `SET FOUND-TRANTYPE-IN-TABLE`;
+100 → `SET INPUT-ERROR` + `SET FLG-TRANFILTER-NOT-OK`, if msg off `SET WS-RECORD-NOT-FOUND`; <0 →
`SET INPUT-ERROR` + `SET FLG-TRANFILTER-NOT-OK`, if msg off STRING the SQLCODE error.

### 9500-STORE-FETCHED-DATA (`COTRTUPC.cbl:1517-1530`)
INITIALIZE `TTUP-OLD-DETAILS`; MOVE `DCL-TR-TYPE`→`TTUP-OLD-TTYP-TYPE`; MOVE
`DCL-TR-DESCRIPTION-TEXT(1:DCL-TR-DESCRIPTION-LEN)`→`TTUP-OLD-TTYP-TYPE-DESC` (VARCHAR unpacked by its
length prefix). // src:1523-1525

### 9600-WRITE-PROCESSING (`COTRTUPC.cbl:1531-1595`)
MOVE `TTUP-NEW-TTYP-TYPE`→`DCL-TR-TYPE`; MOVE `TRIM(TTUP-NEW-TTYP-TYPE-DESC)`→`DCL-TR-DESCRIPTION-TEXT`;
`COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(TTUP-NEW-TTYP-TYPE-DESC)` (**= 50 always**, see FAITHFUL BUGS);
run UPDATE (§2). EVALUATE SQLCODE: 0 → SYNCPOINT; +100 → PERFORM `9700-INSERT-RECORD`; -911 →
`SET INPUT-ERROR`, if msg off `SET COULD-NOT-LOCK-REC-FOR-UPDATE`; <0 → `SET TABLE-UPDATE-FAILED` + STRING
the error. Then second EVALUATE maps message flags to state: LOCK-ERROR→`TTUP-CHANGES-OKAYED-LOCK-ERROR`;
TABLE-UPDATE-FAILED→`TTUP-CHANGES-OKAYED-BUT-FAILED`; DATA-WAS-CHANGED-BEFORE-UPDATE→`TTUP-SHOW-DETAILS`;
OTHER→`TTUP-CHANGES-OKAYED-AND-DONE`. // src:1538-1589

### 9700-INSERT-RECORD (`COTRTUPC.cbl:1596-1623`)
Run INSERT (§2). EVALUATE SQLCODE: 0 → SYNCPOINT; OTHER → `SET TABLE-UPDATE-FAILED` + STRING the error +
exit.

### 9800-DELETE-PROCESSING (`COTRTUPC.cbl:1624-1666`)
MOVE `TTUP-OLD-TTYP-TYPE`→`DCL-TR-TYPE`; run DELETE (§2). EVALUATE SQLCODE: 0 → `SET TTUP-DELETE-DONE` +
SYNCPOINT; -532 → `SET RECORD-DELETE-FAILED` + STRING the child-record message (**SQLERRM listed twice —
faithful bug**); OTHER → `SET RECORD-DELETE-FAILED` + `SET TTUP-DELETE-FAILED` + STRING the error.

### YYYY-STORE-PFKEY (copybook CSSTRPFY, `COTRTUPC.cbl:1671`)
EVALUATE EIBAID → set the matching `CCARD-AID-*` 88; **PF13-24 wrap to PF1-12** (e.g. DFHPF15→PFK03).
// source: CSSTRPFY.cpy:21-78

### ABEND-ROUTINE (`COTRTUPC.cbl:1675-1701`)
Default ABEND-MSG if empty; ABEND-CULPRIT=this pgm, ABEND-CODE='9999'; SEND ABEND-DATA (ERASE, NOHANDLE);
HANDLE ABEND CANCEL; `EXEC CICS ABEND ABCODE('9999')`.

---

## 5. Pseudo-conversational flow (key paths)

1. **Cold/entry from menu or list** → `CDEMO-PGM-ENTER` + `TTUP-DETAILS-NOT-FETCHED`; second WHEN of main
   dispatch sends an empty key-entry screen, sets PGM-REENTER, info = "Enter transaction type to be
   maintained". // src:465-478,1215
2. **Enter with a key** → WHEN OTHER: RECEIVE → STORE-IN-NEW → EDIT (validate key numeric/non-zero, then,
   since DETAILS-NOT-FETCHED, exit edit) → DECIDE-ACTION reads DB → SHOW-DETAILS (found) or DETAILS-NOT-FOUND
   (not found) → SEND-MAP. // src:548-555,984-997
3. **Found, edit description, Enter** → SHOW-DETAILS; EDIT compares OLD vs NEW; if changed and desc valid →
   CHANGES-OK-NOT-CONFIRMED, info = "Changes validated.Press F5 to save". // src:1023-1029,1238
4. **F5 confirm** → `9600-WRITE-PROCESSING`: UPDATE (or INSERT if +100) → CHANGES-OKAYED-AND-DONE, info =
   "Changes committed to database". // src:514-520,1240
5. **Not found, F5** → CREATE-NEW-RECORD, info = "Enter new transaction type details."; next Enter validates
   desc; F5 → INSERT path. // src:503-508,1236
6. **F4 (delete)** → from SHOW-DETAILS sets CONFIRM-DELETE (info "Delete this record ? Press F4 to confirm");
   second F4 runs `9800-DELETE-PROCESSING` → DELETE-DONE ("Delete successful."). // src:482-498,1234
7. **F12 (cancel)** → from CHANGES-OK-NOT-CONFIRMED/CONFIRM-DELETE/SHOW-DETAILS → re-reads DB and returns to
   SHOW-DETAILS/NOT-FOUND, or cancels with "Update was cancelled" / "Delete was cancelled". // src:524-533,984-1010
8. **F3 (exit)** → SYNCPOINT + XCTL to caller / COADM01C. // src:429-460
9. **Invalid key** → WS-INVALID-KEY-PRESSED branch just re-sends the map. // src:539-542

---

## 6. VALIDATION RULES & exact literal messages

**Info-prompt messages (`WS-INFO-MSG`, centered into INFOMSGO):** // source: COTRTUPC.cbl:143-165
- `'Selected transaction type shown above'` (FOUND-TRANTYPE-DATA, defined; not set in flow)
- `'Enter transaction type to be maintained'` (PROMPT-FOR-SEARCH-KEYS)
- `'Press F05 to add. F12 to cancel'` (PROMPT-CREATE-NEW-RECORD, defined; not set — NOT-FOUND uses it? no:
  NOT-FOUND sets PROMPT-CREATE-NEW-RECORD via 3250) — **PROMPT-CREATE-NEW-RECORD IS set for NOT-FOUND**
- `'Delete this record ? Press F4 to confirm'` (PROMPT-DELETE-CONFIRM)
- `'Delete successful.'` (CONFIRM-DELETE-SUCCESS)
- `'Update transaction type details shown.'` (PROMPT-FOR-CHANGES)
- `'Enter new transaction type details.'` (PROMPT-FOR-NEWDATA)
- `'Changes validated.Press F5 to save'` (PROMPT-FOR-CONFIRMATION)
- `'Changes committed to database'` (CONFIRM-UPDATE-SUCCESS)
- `'Changes unsuccessful'` (INFORM-FAILURE)

**Return/error messages (`WS-RETURN-MSG`, → ERRMSGO):** // source: COTRTUPC.cbl:167-196
- `'PF03 pressed.Exiting              '` (WS-EXIT-MESSAGE, defined; not set in flow)
- `'Invalid Key pressed. '` (WS-INVALID-KEY)
- `'Name can only contain alphabets and spaces'` (WS-NAME-MUST-BE-ALPHA, defined; not set)
- `'No record found for this key in database'` (WS-RECORD-NOT-FOUND)
- `'No input received'` (NO-SEARCH-CRITERIA-RECEIVED)
- `'No change detected with respect to values fetched.'` (NO-CHANGES-DETECTED)
- `'Could not lock record for update'` (COULD-NOT-LOCK-REC-FOR-UPDATE)
- `'Record changed by some one else. Please review'` (DATA-WAS-CHANGED-BEFORE-UPDATE, defined; never set)
- `'Update was cancelled'` (WS-UPDATE-WAS-CANCELLED)
- `'Update of record failed'` (TABLE-UPDATE-FAILED)
- `'Delete of record failed'` (RECORD-DELETE-FAILED)
- `'Delete was cancelled'` (WS-DELETE-WAS-CANCELLED)
- `'Invalid key pressed'` (WS-INVALID-KEY-PRESSED)
- `'Looks Good.... so far'` (CODING-TO-BE-DONE, defined; not set)

**Dynamically STRINGed messages (built with the field name):** // source: COTRTUPC.cbl:864-868,891-895,922-926,941-945,959-963
- `"<TRIM(name)> must be supplied."` (blank — name is "Tran Type code" or "Transaction Desc")
- `"<TRIM(name)> can have numbers or alphabets only."` (alphanum rule, used for description)
- `"<TRIM(name)> must be numeric."` (numeric rule, used for type)
- `"<TRIM(name)> must not be zero."` (non-zero rule, used for type)

**SQL-error STRINGed messages:** // source: COTRTUPC.cbl:1499-1507,1569-1577,1609-1617,1640-1661
- `"Error accessing: TRANSACTION_TYPE table. SQLCODE:<n>:<SQLERRM>"`
- `"Error updating: TRANSACTION_TYPE Table. SQLCODE:<n>:<SQLERRM>"`
- `"Error inserting record into: TRANSACTION_TYPE Table. SQLCODE:<n>:<SQLERRM>"`
- `"Please delete associated child records first:SQLCODE :<n>:<SQLERRM><SQLERRM>"` (DELETE -532; SQLERRM
  concatenated twice — faithful bug)
- `"Delete failed with message:SQLCODE :<n>:<SQLERRM>"`

**Field validation rules:**
- **Transaction Type code (TRTYPCD, X(2)):** required (not blank/`*`); must be numeric (`TEST-NUMVAL=0`);
  must not be zero (`NUMVAL≠0`). On success it is normalized to a 2-digit zero-padded numeric string via
  NUMVAL→PIC 9(2)→INSPECT spaces→zeros (e.g. "5"→"05"). // src:820-843,907-972
- **Description (TRTYDSC, X(50)):** required (not blank/`*`); only letters/digits/space allowed (the
  alphanumeric INSPECT scrub; note: this validator's message says "numbers or alphabets only"). // src:758-764,849-901
- **Change detection:** case-insensitive on type and trimmed description; also compares trimmed *length* of
  description. No change → "No change detected with respect to values fetched." // src:783-812
- **Allowed AID by state** is enforced twice (0001-CHECK-PFKEYS for validation, 3391 for legend display);
  they must stay in sync. // src:577-623,1397-1426

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **VARCHAR length always 50 on write.** `9600-WRITE-PROCESSING` puts `TRIM(desc)` into
   `DCL-TR-DESCRIPTION-TEXT` but then `COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(TTUP-NEW-TTYP-TYPE-DESC)`,
   where `TTUP-NEW-TTYP-TYPE-DESC` is a fixed `PIC X(50)` — so `FUNCTION LENGTH` is **always 50**, not the
   trimmed length. The VARCHAR stored is the trimmed text right-padded with spaces to length 50. (On read,
   `9500` instead trims via the *fetched* `DCL-TR-DESCRIPTION-LEN`, an asymmetry.) Reproduce: store
   description as the trimmed text padded with spaces to 50, length=50. // source: COTRTUPC.cbl:1539-1542,335

2. **`SQLERRM` concatenated twice in the DELETE -532 message.** The STRING lists `SQLERRM OF SQLCA` on two
   consecutive lines, doubling it in the output text. // source: COTRTUPC.cbl:1645-1646

3. **`TRTYPCDO` MOVE LOW-VALUES duplicated.** In `3201-SHOW-INITIAL-VALUES` the same receiver `TRTYPCDO OF
   CTRTUPAO` is listed twice in one MOVE — harmless but verbatim. // source: COTRTUPC.cbl:1177-1178

4. **`FUNCTION CURRENT-DATE` moved to `WS-CURDATE-DATA` twice** in `3100-SCREEN-INIT`. Harmless; keep both
   moves. // source: COTRTUPC.cbl:1113,1120

5. **Dead/never-reached message 88s.** `FOUND-TRANTYPE-DATA`, `WS-EXIT-MESSAGE`, `WS-NAME-MUST-BE-ALPHA`,
   `DATA-WAS-CHANGED-BEFORE-UPDATE`, `CODING-TO-BE-DONE`, and 88 `TTUP-REVIEW-NEW-RECORD` ('V') are declared
   but never SET in the flow. Keep declared; do not invent code paths that set them. // source:
   COTRTUPC.cbl:145,169,173,183,195,306

6. **Comment vs reality in 9100.** Comment says "Read the Card file. Access via alternate index ACCTID" but
   the actual SQL reads `TRANSACTION_TYPE` by `TR_TYPE` (copy-paste comment). Keep behavior; ignore comment.
   // source: COTRTUPC.cbl:1471,1475-1482

7. **`-911` (lock) on UPDATE sets INPUT-ERROR but the secondary EVALUATE still maps it to LOCK-ERROR state.**
   The two-stage flag→state translation in 9600 is order-dependent (first EVALUATE sets message 88s, second
   maps to TTUP-* states). Preserve the exact two-EVALUATE structure. // source: COTRTUPC.cbl:1555-1589

---

## 8. PORT NOTES (relational translation plan & tricky COBOL)

- **DB2 → EF Core:** target the existing **TRANSACTION_TYPE** table (`TR_TYPE` CHAR(2) PK, `TR_DESCRIPTION`
  VARCHAR(50)). The four SQL statements translate directly to: keyed `SingleOrDefault`/`SELECT`, `UPDATE`,
  `INSERT`, `DELETE`. Map SQLCODEs from EF results: row-found→0, no row on SELECT→+100, `UPDATE` affecting
  0 rows→+100 (drives the insert fallback), unique/duplicate or other DB error→negative; emulate `-911`
  (lock/timeout) and `-532` (FK) only insofar as they reach distinct message branches — they are unlikely in
  SQLite, so the lock/FK branches are characterization-only (guard tests may simulate them).
- **SYNCPOINT** → commit the unit of work after each successful UPDATE/INSERT/DELETE.
- **VARCHAR host var (`DCL-TR-DESCRIPTION` = LEN + TEXT(50)):** model description as a 50-char fixed string
  in the domain; on **read** unpack `TEXT(1:LEN)` (trimmed to stored length); on **write** store the trimmed
  text padded to 50 with `LEN=50` (faithful bug #1). When persisting to a real VARCHAR column, store the
  50-char padded value to match COBOL behavior, OR store trimmed text but pin the round-trip with the bug
  documented — recommend storing the trimmed text padded to 50 chars to mirror the byte image.
- **TRIM semantics:** COBOL `FUNCTION TRIM` strips leading & trailing spaces. Inputs `'*'` or all-spaces are
  treated as "cleared" and stored as LOW-VALUES (model as empty/`\0`-filled in the field's fixed width, or a
  sentinel "not entered"). // src:667-684
- **Type normalization (1210):** `NUMVAL(type)`→`PIC 9(2)`→string→`INSPECT REPLACING SPACES BY ZEROS` is a
  zero-pad-left to 2 digits. Implement as: parse to int, format `D2`. Truncation: `PIC 9(2)` keeps only the
  low 2 digits of NUMVAL (NUMVAL of a 2-char field can't exceed 2 digits, so no overflow in practice).
- **INSPECT CONVERTING (1230 charset scrub):** converts every uppercase letter, lowercase letter, and digit
  to space, then checks if anything non-space remains. Implement as "reject if the slice contains any char
  outside `[A-Za-z0-9 ]`". Note the *message* says "numbers or alphabets only" though space is also allowed.
- **NUMVAL / TEST-NUMVAL (1245):** `TEST-NUMVAL=0` means "is a valid number". Implement as a strict numeric
  parse of the slice; `NUMVAL=0` for the non-zero check.
- **Center-justify (3250):** `WS-STRING-OUT` is X(40); place trimmed message starting at
  `(40 - len)/2 + 1` (integer divide). Reproduce integer truncation of the midpoint. // src:1251-1262
- **REDEFINES in working storage:** `CICS-OUTPUT-EDIT-VARS` has overlapping `WS-EDIT-DATE-X` definitions
  (X(10) / sub-fields / 9(10)) — only the date sub-field uses matter; not on the active path here. // src:108-120
- **INITIALIZE semantics:** INITIALIZE sets numeric to 0 and alphanumeric to spaces; but several places MOVE
  LOW-VALUES explicitly to flags — keep the LOW-VALUES (0x00) vs SPACES (0x20) distinction because the state
  88s (`TTUP-DETAILS-NOT-FETCHED`) test for both LOW-VALUES and SPACES. // src:298-300
- **Commarea concatenation by byte offset:** the private commarea is appended after CARDDEMO-COMMAREA using
  `LENGTH OF CARDDEMO-COMMAREA + 1`. In the port, model two POCOs and a single serialized commarea blob, or
  carry the private state object alongside the shared commarea object across turns. CARDDEMO-COMMAREA is the
  COCOM01Y layout; WS-THIS-PROGCOMMAREA is the TTUP-* block. // src:294-336,376-380,559-571
- **Two parallel state machines must stay synchronized:** `0001-CHECK-PFKEYS` (which AIDs are valid) and
  `3391-SETUP-PFKEY-ATTRS` (which legend keys are lit). Implement once and call from both spots, or duplicate
  faithfully and pin with a parity test.
- **DFH attribute bytes** map to console-renderer attributes: PRF=protected, FSE=unprotected+modified,
  ASB=autoskip/bright, DAR=dark/hidden, RED color. Cursor positioning uses `MOVE -1 TO <field>L`.

---

## 9. OPEN QUESTIONS / RISKS

- **SQLite has no `-911`/`-532`/`-911` lock or RI-child SQLCODEs by default.** Those branches (lock error,
  delete-child-records-first) are reachable in DB2 only. Decide whether the relational port enforces a
  foreign key from `TRANSACTION_TYPE_CATEGORY` (TRC_TYPE_CODE) to `TRANSACTION_TYPE` to make the -532 path
  testable; otherwise treat both as characterization-only (simulate via injected SQLCODE in tests).
- **VARCHAR storage choice (faithful bug #1):** store description trimmed-then-padded-to-50 (matches the
  COBOL write) vs trimmed (matches the COBOL read). The spec recommends padded-to-50 on write; confirm with
  the schema round-trip harness which form the golden expects.
- **`PROMPT-CREATE-NEW-RECORD` literal is `'Press F05 to add. F12 to cancel'`** yet the NOT-FOUND screen in
  3250 sets it — verify against captured screen golden that this is the intended prompt for NOT-FOUND
  (it is the only setter). // src:1219-1220,149-150
- **`COMMON-RETURN` always returns transid `CTTU`** even after an exit XCTL path (F3) — but F3 does XCTL
  before reaching COMMON-RETURN, so the RETURN TRANSID only applies to non-exit turns. Confirm the console
  dispatcher treats XCTL as terminating the turn (no fall-through to RETURN). // src:457-460,567-571
- No JCL: this is a CICS-only transaction; there is no batch step name. Invocation is purely via TRANSID
  `CTTU` / XCTL.
