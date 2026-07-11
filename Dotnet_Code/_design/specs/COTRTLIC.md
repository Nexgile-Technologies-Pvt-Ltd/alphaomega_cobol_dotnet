# PORT SPEC — COTRTLIC (Maintain Transaction Type, online/CICS + Db2)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/cbl/COTRTLIC.cbl`
BMS map source: `Old_Cobol_Code/.../app-transaction-type-db2/bms/COTRTLI.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app-transaction-type-db2/cpy-bms/COTRTLI.cpy`
DCLGEN copybook: `Old_Cobol_Code/.../app-transaction-type-db2/dcl/DCLTRTYP.dcl` (table `CARDDEMO.TRANSACTION_TYPE`)
DDL: `Old_Cobol_Code/.../app-transaction-type-db2/ddl/TRNTYPE.ddl`
Logic copybooks: `CVCRD01Y.cpy` (CC-WORK-AREA + AID flags), `COCOM01Y.cpy` (CARDDEMO-COMMAREA), `CSSTRPFY.cpy` (PF-key store, paragraph `YYYY-STORE-PFKEY`), `CSDB2RWY.cpy` (Db2 common working storage), `CSDB2RPY.cpy` (Db2 common procedures `9998-PRIMING-QUERY`, `9999-FORMAT-DB2-MESSAGE`), `COTTL01Y.cpy` (titles), `CSDAT01Y.cpy` (date/time work area), `CSMSG01Y.cpy`, `CSUSR01Y.cpy`, `CVACT02Y.cpy` (CARD record — declared but unused), `COTRTLI.cpy` (symbolic map).
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.Db2` (TRANSACTION_TYPE table + EF Core context) per ARCHITECTURE.md (optional-module table **TRANSACTION_TYPE**, §"Optional-module tables").

All line citations use the form `// source: COTRTLIC.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COTRTLIC is the CICS pseudo-conversational **"Maintain Transaction Type"** transaction. It lists rows of the relational Db2 table `CARDDEMO.TRANSACTION_TYPE` (columns `TR_TYPE CHAR(2)`, `TR_DESCRIPTION VARCHAR(50)`), **7 rows per page**, on a 24×80 BMS screen, with optional **Type-Code filter** (2-digit numeric) and **Description filter** (substring `LIKE`). The program demonstrates Db2 cursor paging (a forward and a backward cursor) plus inline **single-row UPDATE and DELETE** driven from per-row action codes typed on the screen: `U` = update that row's description, `D` = delete that row (with an F10 confirm step). `// source: COTRTLIC.cbl:1-7`

**Invocation.**
- CICS TRANSID **`CTLI`** (`LIT-THISTRANID`). `// source: COTRTLIC.cbl:44`
- Program id **`COTRTLIC`** (`LIT-THISPGM`). `// source: COTRTLIC.cbl:43`
- Mapset **`COTRTLI`** (`LIT-THISMAPSET`) / map **`CTRTLIA`** (`LIT-THISMAP`). `// source: COTRTLIC.cbl:45-46`
- Pseudo-conversational: re-drives itself via `EXEC CICS RETURN TRANSID('CTLI') COMMAREA(WS-COMMAREA)`. `// source: COTRTLIC.cbl:910-914`
- It is **not** a called subroutine; all flow is via COMMAREA + XCTL/RETURN.
- **XCTL targets OUT:**
  - PF3 exit → `COADM01C` (admin menu, transid `CA00`) — or back to whatever `CDEMO-FROM-PROGRAM`/`CDEMO-FROM-TRANID` was, if it wasn't this program. `// source: COTRTLIC.cbl:591-623`
  - PF2 "Add" → `COTRTUPC` (Add/Update Transaction Type, transid `CTTU`, mapset `COTRTUP`, map `CTRTUPA`). `// source: COTRTLIC.cbl:630-651`
- **Reached by** XCTL from the admin menu (`COADM01C`) and returned-to from `COTRTUPC`. On entry, `EIBCALEN = 0` OR a `CDEMO-PGM-ENTER` with `CDEMO-FROM-PROGRAM ≠ COTRTLIC` triggers a fresh start. `// source: COTRTLIC.cbl:515-554`

---

## 2. FILE / TABLE ACCESS

This is a **Db2 program** — there is no VSAM file. All persistence is the single table **`CARDDEMO.TRANSACTION_TYPE`** (ARCHITECTURE.md optional-module table **TRANSACTION_TYPE**: `TR_TYPE CHAR(2)` PK, `TR_DESCRIPTION VARCHAR(50)`). The `COPY CVACT02Y` (CARD record) at line 490 is declared but **never referenced** in the procedure division — do not implement card access.

| Db2 object | ARCH table | Operation | COBOL site | SQL (verbatim shape) |
|---|---|---|---|---|
| `SYSIBM.SYSDUMMY1` | (none — connectivity probe) | priming SELECT | `9998-PRIMING-QUERY` (CSDB2RPY) | `SELECT 1 INTO :hv FROM SYSIBM.SYSDUMMY1 FETCH FIRST 1 ROW ONLY` |
| `CARDDEMO.TRANSACTION_TYPE` | TRANSACTION_TYPE | COUNT (filter validation) | `9100-CHECK-FILTERS` | `SELECT COUNT(1) INTO :WS-RECORDS-COUNT FROM CARDDEMO.TRANSACTION_TYPE WHERE (typeFlag predicate) AND (descFlag predicate)` `// source: COTRTLIC.cbl:1803-1815` |
| `CARDDEMO.TRANSACTION_TYPE` | TRANSACTION_TYPE | forward cursor (paging down/refresh) | cursor `C-TR-TYPE-FORWARD`, opened `9400`, fetched `8000`, closed `9450` | `SELECT TR_TYPE, TR_DESCRIPTION FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE >= :WS-START-KEY AND (typeFlag pred) AND (descFlag pred) ORDER BY TR_TYPE` `// source: COTRTLIC.cbl:338-352` |
| `CARDDEMO.TRANSACTION_TYPE` | TRANSACTION_TYPE | backward cursor (paging up) | cursor `C-TR-TYPE-BACKWARD`, opened `9500`, fetched `8100`, closed `9550` | `SELECT TR_TYPE, TR_DESCRIPTION FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE < :WS-START-KEY AND (typeFlag pred) AND (descFlag pred) ORDER BY TR_TYPE DESC` `// source: COTRTLIC.cbl:354-368` |
| `CARDDEMO.TRANSACTION_TYPE` | TRANSACTION_TYPE | single-row UPDATE | `9200-UPDATE-RECORD` | `UPDATE CARDDEMO.TRANSACTION_TYPE SET TR_DESCRIPTION = :DCL-TR-DESCRIPTION WHERE TR_TYPE = :DCL-TR-TYPE` `// source: COTRTLIC.cbl:1846-1850` |
| `CARDDEMO.TRANSACTION_TYPE` | TRANSACTION_TYPE | single-row DELETE | `9300-DELETE-RECORD` | `DELETE FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE = :DCL-TR-TYPE` `// source: COTRTLIC.cbl:1900-1903` |

**Filter predicates (shared by COUNT + both cursors).** Both `(typeFlag)` and `(descFlag)` are written so the host-variable flag toggles the column predicate on/off:
- type: `((:WS-EDIT-TYPE-FLAG = '1' AND TR_TYPE = :WS-TYPE-CD-FILTER) OR (:WS-EDIT-TYPE-FLAG <> '1'))` `// source: COTRTLIC.cbl:344-346`
- desc: `((:WS-EDIT-DESC-FLAG = '1' AND TR_DESCRIPTION LIKE TRIM(:WS-TYPE-DESC-FILTER)) OR (:WS-EDIT-DESC-FLAG <> '1'))` `// source: COTRTLIC.cbl:347-350`

**Repository contract notes (per ARCHITECTURE.md §VSAM→SQL / optional-module).**
- The forward cursor is the ordered scan `TR_TYPE >= @startKey ORDER BY TR_TYPE`; the backward cursor is `TR_TYPE < @startKey ORDER BY TR_TYPE DESC`. `TR_TYPE` is `CHAR(2)`; use **ordinal string comparison** (ARCHITECTURE.md collation guidance) so paging boundaries match.
- `WS-START-KEY` is `PIC X(02)`; for a fresh forward read it is set from `WS-CA-FIRST-TR-CODE` (which is `LOW-VALUES`/spaces initially → first row in the table). `// source: COTRTLIC.cbl:275, 399, 1645`
- `SQLCODE` mapping used throughout: `0` = row fetched / op OK; `+100` = no row / end-of-cursor; `-911` = deadlock (UPDATE only); `-532` = FK child rows still referencing (DELETE only); any other negative = hard error. `// source: COTRTLIC.cbl:1634, 1694, 1861, 1870, 1914`
- UPDATE/DELETE each issue `EXEC CICS SYNCPOINT` on success (commit). `// source: COTRTLIC.cbl:1856, 1909` PF3 exit also SYNCPOINTs before XCTL. `// source: COTRTLIC.cbl:616-618`
- The DCLGEN host structure `DCL-TR-DESCRIPTION` is a **VARCHAR**: a 2-byte length half-word `DCL-TR-DESCRIPTION-LEN PIC S9(4) COMP` + `DCL-TR-DESCRIPTION-TEXT PIC X(50)`. The UPDATE sends both length and text. In .NET, map `TR_DESCRIPTION` to a `string` and bind only the trimmed text; the explicit length must equal `LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))` = always **50** (see Faithful Bugs #3). `// source: DCLTRTYP.dcl:40-46; COTRTLIC.cbl:1841-1844`

---

## 3. DATA STRUCTURES USED

- **DCLTRANSACTION-TYPE** (`DCLTRTYP.dcl`): `DCL-TR-TYPE PIC X(2)`; `DCL-TR-DESCRIPTION` = `DCL-TR-DESCRIPTION-LEN PIC S9(4) COMP` + `DCL-TR-DESCRIPTION-TEXT PIC X(50)`. `// source: DCLTRTYP.dcl:35-46`
- **CARDDEMO-COMMAREA** (`COCOM01Y`, length used as a prefix of the saved commarea): general-info nav fields `CDEMO-FROM-TRANID X4`, `CDEMO-FROM-PROGRAM X8`, `CDEMO-TO-TRANID X4`, `CDEMO-TO-PROGRAM X8`, `CDEMO-USER-ID X8`, `CDEMO-USER-TYPE X1` (88 ADMIN='A', USER='U'), `CDEMO-PGM-CONTEXT 9(1)` (88 PGM-ENTER=0, PGM-REENTER=1); plus customer/account/card info and `CDEMO-LAST-MAP X7` / `CDEMO-LAST-MAPSET X7`. `// source: COCOM01Y.cpy:19-44`
- **WS-THIS-PROGCOMMAREA** (program-private commarea tail, appended after CARDDEMO-COMMAREA): `WS-CA-TYPE-CD X2` (redef `-N 9(2)`), `WS-CA-TYPE-DESC X50` (the saved/echoed filter values); FILLER → `WS-CA-ALL-ROWS-OUT X(364)` redefined as `WS-CA-SCREEN-ROWS-OUT OCCURS 7` of `WS-CA-EACH-ROW-OUT` = `WS-CA-ROW-TR-CODE-OUT X2` + `WS-CA-ROW-TR-DESC-OUT X50` (the 7-row page buffer, 52×7=364); `WS-CA-ROW-SELECTED S9(4) COMP`; `WS-CA-PAGING-VARIABLES` = `WS-CA-LAST-TTYPEKEY`/`WS-CA-LAST-TR-CODE X2`, `WS-CA-FIRST-TTYPEKEY`/`WS-CA-FIRST-TR-CODE X2`, `WS-CA-SCREEN-NUM 9(1)` (88 CA-FIRST-PAGE=1), `WS-CA-LAST-PAGE-DISPLAYED 9(1)` (88 CA-LAST-PAGE-SHOWN=0, CA-LAST-PAGE-NOT-SHOWN=9), `WS-CA-NEXT-PAGE-IND X1` (88 CA-NEXT-PAGE-NOT-EXISTS=LOW-VALUES, CA-NEXT-PAGE-EXISTS='Y'); `WS-CA-DELETE-FLAG X` (88 CA-DELETE-NOT-REQUESTED=LOW-VALUES, CA-DELETE-REQUESTED='Y', CA-DELETE-SUCCEEDED=LOW-VALUES); `WS-CA-UPDATE-FLAG X` (88 CA-UPDATE-NOT-REQUESTED=LOW-VALUES, CA-UPDATE-REQUESTED='Y', CA-UPDATE-SUCCEEDED=LOW-VALUES). `// source: COTRTLIC.cbl:377-418`
  - **NB:** `CA-DELETE-SUCCEEDED` and `CA-DELETE-NOT-REQUESTED` both = LOW-VALUES; likewise `CA-UPDATE-SUCCEEDED`/`CA-UPDATE-NOT-REQUESTED` (see Faithful Bugs #1). `// source: COTRTLIC.cbl:412-418`
- **WS-COMMAREA PIC X(2000)** — the physical return commarea: bytes `1..LEN(CARDDEMO-COMMAREA)` hold CARDDEMO-COMMAREA, bytes `LEN+1..` hold WS-THIS-PROGCOMMAREA. Split/reassembled by reference-modified MOVEs. `// source: COTRTLIC.cbl:420, 527-531, 904-907`
- **WS-SCREEN-DATA-IN**: `WS-ALL-ROWS-IN X(364)` redefined `WS-SCREEN-ROWS-IN OCCURS 7` of `WS-EACH-ROW-IN`/`WS-EACH-TTYP-IN` = `WS-ROW-TR-CODE-IN X2` + `WS-ROW-TR-DESC-IN X50` (the inbound page buffer from RECEIVE). `// source: COTRTLIC.cbl:165-172`
- **WS-EDIT-SELECT-FLAGS X(7)** (redef `WS-EDIT-SELECT OCCURS 7`): per-row action chars; 88s `SELECT-OK={'D','U'}`, `DELETE-REQUESTED-ON='D'`, `UPDATE-REQUESTED-ON='U'`, `SELECT-BLANK={' ',LOW-VALUES}`. Init LOW-VALUES. `// source: COTRTLIC.cbl:178-188`
- **WS-EDIT-SELECT-ERROR-FLAGS X(7)** (redef `WS-EDIT-SELECT-ERRORS OCCURS 7` → `WS-ROW-TRTSELECT-ERROR X1`, 88 WS-ROW-SELECT-ERROR='1'): per-row select-error markers. `// source: COTRTLIC.cbl:190-195`
- **WS-ROW-RECORDS-CHANGED X(1) OCCURS 7** (88 NO=LOW-VALUES, YES='Y'). `// source: COTRTLIC.cbl:117-120`
- **Action counters** (all `S9(4) COMP-3`): `WS-ACTIONS-REQUESTED` (88 ONLY-1-ACTION=1, MORETHAN1ACTION=2..7), `WS-DELETES-REQUESTED`, `WS-UPDATES-REQUESTED`, `WS-NO-ACTIONS-SELECTED`, `WS-VALID-ACTIONS-SELECTED` (88 ONLY-1-VALID-ACTION=1). `// source: COTRTLIC.cbl:202-220`
- **WS-DATA-FILTERS**: `WS-START-KEY X2`, `WS-TYPE-CD-FILTER X2`, `WS-TYPE-DESC-FILTER X52`. (Also `WS-TYPE-CD-DELETE-FILTER` — a quoted comma-list literal built for an `IN (…)` clause, but **never used** in the procedure division.) `// source: COTRTLIC.cbl:274-301`
- **WS-SCREEN-EDIT-VARS**: `WS-IN-TYPE-CD X2` (redef `WS-IN-TYPE-CD-N 9(2)`), `WS-IN-TYPE-DESC X50`. `// source: COTRTLIC.cbl:309-313`
- **Edit/flag scalars**: `WS-INPUT-FLAG` (88 INPUT-OK={'0',' ',LOW-VALUES}, INPUT-ERROR='1'); `WS-EDIT-TYPE-FLAG` (88 NOT-OK='0', ISVALID='1', BLANK=' '); `WS-EDIT-DESC-FLAG` (same triple); `WS-TYPEFILTER-CHANGED`/`WS-DESCFILTER-CHANGED` (88 …-NO=LOW-VALUES, …-YES='Y'); `WS-DELETE-STATUS`/`WS-UPDATE-STATUS`; `WS-ROW-SELECTION-CHANGED`; `WS-BAD-SELECTION-ACTION`; `WS-ARRAY-DESCRIPTION-FLGS` (88 ISVALID={LOW-VALUES,SPACES}, NOT-OK='0', BLANK='B'); `WS-DATACHANGED-FLAG` (88 NO-CHANGES-FOUND='0', CHANGES-HAVE-OCCURRED='1'); `FLG-PROTECT-SELECT-ROWS` (88 NO='0', YES='1'); `WS-PFK-FLAG` (88 PFK-VALID='0', PFK-INVALID='1'); `WS-RECORDS-TO-PROCESS-FLAG` (88 READ-LOOP-EXIT='0', MORE-RECORDS-TO-READ='1'). `// source: COTRTLIC.cbl:98-140, 263-265, 320-322`
- **Generic edit work area**: `WS-EDIT-VARIABLE-NAME X25`, `WS-EDIT-ALPHANUM-ONLY X256`, `WS-EDIT-ALPHANUM-LENGTH S9(4) COMP-3`, `WS-EDIT-ALPHANUM-ONLY-FLAGS X1` (88 ISVALID=LOW-VALUES, NOT-OK='0', BLANK='B'); INSPECT translate tables `LIT-ALL-ALPHANUM-FROM`/`LIT-ALPHANUM-SPACES-TO` (62 chars: A-Z, a-z, 0-9). `// source: COTRTLIC.cbl:65-82, 143-152`
- **Message constants** (88-level literals on `WS-INFO-MSG X45` and `WS-RETURN-MSG X75`) — exact texts in §8. `// source: COTRTLIC.cbl:235-262`
- **CC-WORK-AREA** (`CVCRD01Y`): AID flags `CCARD-AID X5` (88s ENTER/CLEAR/PA1-2/PFK01-12), `CCARD-NEXT-PROG X8`, `CCARD-NEXT-MAPSET X7`, `CCARD-NEXT-MAP X7`, `CCARD-ERROR-MSG X75`, `CCARD-RETURN-MSG X75`. `// source: CVCRD01Y.cpy:3-30`
- **Db2 common WS** (`CSDB2RWY`): `WS-DISP-SQLCODE PIC ----9` (edited), `WS-DUMMY-DB2-INT`, `WS-DB2-PROCESSING-FLAG` (88 WS-DB2-OK='0', WS-DB2-ERROR='1'), `WS-DB2-CURRENT-ACTION X72`, DSNTIAC message buffers. `// source: CSDB2RWY.cpy:21-46`

---

## 4. PROCEDURE DIVISION — PARAGRAPH-BY-PARAGRAPH

Each paragraph = one method. Order and PERFORM/GO TO flow are preserved. `…-EXIT` paragraphs are no-op return points (fold into the parent method).

### 4.1 `0000-MAIN` — entry / dispatcher `// source: COTRTLIC.cbl:498-915`
1. `INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA`; `MOVE LIT-THISTRANID TO WS-TRANID`; `SET WS-RETURN-MSG-OFF TO TRUE` (clear error msg). `// source: COTRTLIC.cbl:500-511`
2. **Restore commarea:** if `EIBCALEN = 0` → `INITIALIZE CARDDEMO-COMMAREA, WS-THIS-PROGCOMMAREA`, set FROM-TRANID/FROM-PROGRAM to self, `CDEMO-USRTYP-ADMIN`, `CDEMO-PGM-ENTER`, LAST-MAP/MAPSET, `CA-FIRST-PAGE`, `CA-LAST-PAGE-NOT-SHOWN`. Else split `DFHCOMMAREA` into `CARDDEMO-COMMAREA` (prefix) and `WS-THIS-PROGCOMMAREA` (tail). `// source: COTRTLIC.cbl:515-532`
3. `PERFORM YYYY-STORE-PFKEY` — map `EIBAID` → `CCARD-AID-*` 88. `// source: COTRTLIC.cbl:538-539`
4. **Fresh-start guard:** if (`CDEMO-PGM-ENTER` AND from-program ≠ self) OR (`CCARD-AID-PFK03` AND from-tranid = `CTTU`) → `INITIALIZE WS-THIS-PROGCOMMAREA`, set PGM-ENTER, AID-ENTER, LAST-MAP, FIRST-PAGE, LAST-PAGE-NOT-SHOWN. `// source: COTRTLIC.cbl:544-554`
5. **Re-entry receive:** if `EIBCALEN > 0` AND from-program = self → `PERFORM 1000-RECEIVE-MAP` (receive + edit inputs). `// source: COTRTLIC.cbl:561-566`
6. **PFKey validity gate:** `SET PFK-INVALID`; if AID ∈ {ENTER, PFK02, PFK03, PFK07, PFK08, or (PFK10 AND (CA-DELETE-REQUESTED or CA-UPDATE-REQUESTED))} → `SET PFK-VALID`. If still PFK-INVALID → force `CCARD-AID-ENTER`. `// source: COTRTLIC.cbl:574-587`
7. **PF3 exit:** if `CCARD-AID-PFK03` → compute `CDEMO-TO-TRANID` (if from-tranid is blank/self → `CA00`, else echo from-tranid) and `CDEMO-TO-PROGRAM` (if from-program blank/self → `COADM01C`, else echo from-program); set self as from-tranid/program, ADMIN, PGM-ENTER, LAST-MAPSET/MAP; `EXEC CICS SYNCPOINT`; `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COTRTLIC.cbl:591-625`
8. **PF2 Add:** if `CCARD-AID-PFK02` AND from-program = self → set nav fields, `CDEMO-USRTYP-USER`, `CDEMO-TO-PROGRAM=COTRTUPC`, `CCARD-NEXT-MAPSET=COTRTUP`, `CCARD-NEXT-MAP=CTRTUPA`, `SET WS-EXIT-MESSAGE`, `EXEC CICS XCTL PROGRAM(COTRTUPC) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COTRTLIC.cbl:630-652`
9. **Reset last-page flag** unless PF8: if NOT PFK08 → `SET CA-LAST-PAGE-NOT-SHOWN`. `// source: COTRTLIC.cbl:657-661`
10. **F10-changed-criteria demotion:** if PFK10 AND (CA-DELETE-REQUESTED or CA-UPDATE-REQUESTED) AND type-not-changed AND desc-not-changed AND row-selection-not-changed → keep PFK10; ELSE force `CCARD-AID-ENTER` (treat as fresh ENTER). `// source: COTRTLIC.cbl:666-678`
11. `PERFORM 9998-PRIMING-QUERY` (Db2 connectivity). If `WS-DB2-ERROR` → `PERFORM SEND-LONG-TEXT` then `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:684-691`
12. **Main `EVALUATE TRUE` dispatch** (first matching WHEN; preserve order) `// source: COTRTLIC.cbl:698-879`:
    - `WHEN INPUT-ERROR` → set error nav fields, `WS-START-KEY = WS-CA-FIRST-TR-CODE`; if neither filter flagged NOT-OK → `8000-READ-FORWARD`; `2000-SEND-MAP`; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:699-720`
    - `WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE` (first of two identical WHENs — see Faithful Bugs #2) → fall through into the body of the **second** `PFK07 AND CA-FIRST-PAGE`: `WS-START-KEY = WS-CA-FIRST-TR-CODE`; `8000-READ-FORWARD`; `2000-SEND-MAP`; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:721-734`
    - `WHEN CCARD-AID-PFK03` (dead — already handled at step 7) **/** `WHEN CDEMO-PGM-REENTER AND from-program ≠ self` → re-init commareas + WS-MISC-STORAGE, set nav/ADMIN/PGM-ENTER/FIRST-PAGE, `WS-START-KEY = WS-CA-FIRST-TR-CODE`, `8000-READ-FORWARD`, `2000-SEND-MAP`, `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:738-762`
    - `WHEN CCARD-AID-PFK08 AND CA-NEXT-PAGE-EXISTS` (page down) → `WS-START-KEY = WS-CA-LAST-TR-CODE`; `ADD +1 TO WS-CA-SCREEN-NUM`; `8000-READ-FORWARD`; `INITIALIZE WS-EDIT-SELECT-FLAGS`; `2000-SEND-MAP`; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:766-776`
    - `WHEN CCARD-AID-PFK07 AND NOT CA-FIRST-PAGE` (page up) → `WS-START-KEY = WS-CA-FIRST-TR-CODE`; `SUBTRACT 1 FROM WS-CA-SCREEN-NUM`; `8100-READ-BACKWARDS`; `INITIALIZE WS-EDIT-SELECT-FLAGS`; `2000-SEND-MAP`; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:780-790`
    - `WHEN CCARD-AID-ENTER AND WS-DELETES-REQUESTED > 0 AND from-program = self` → `WS-START-KEY = WS-CA-FIRST-TR-CODE`; if filters OK → `8000-READ-FORWARD`; `2000-SEND-MAP` (this is the "press F10 to confirm delete" prompt screen); `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:794-806`
    - `WHEN CCARD-AID-PFK10 AND WS-DELETES-REQUESTED > 0 AND from-program = self` → `9300-DELETE-RECORD`; if `CA-DELETE-SUCCEEDED` set FLG-DELETED-YES else FLG-DELETED-NO; `2000-SEND-MAP`; if FLG-DELETED-YES → re-init commareas + WS-MISC-STORAGE, set PGM-ENTER/FIRST-PAGE/LAST-PAGE-NOT-SHOWN; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:810-834`
    - `WHEN CCARD-AID-ENTER AND WS-UPDATES-REQUESTED > 0 AND from-program = self` → `WS-START-KEY = WS-CA-FIRST-TR-CODE`; if filters OK → `8000-READ-FORWARD`; `2000-SEND-MAP` (the "press F10 to save" update prompt); `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:838-850`
    - `WHEN CCARD-AID-PFK10 AND WS-UPDATES-REQUESTED > 0 AND from-program = self` → `9200-UPDATE-RECORD`; if `CA-UPDATE-SUCCEEDED` set FLG-UPDATE-COMPLETED; `WS-START-KEY = WS-CA-FIRST-TR-CODE`; `8000-READ-FORWARD`; `2000-SEND-MAP`. **Note: NO `GO TO COMMON-RETURN` here** — falls through to step 13. `// source: COTRTLIC.cbl:854-868`
    - `WHEN OTHER` → `WS-START-KEY = WS-CA-FIRST-TR-CODE`; `8000-READ-FORWARD`; `2000-SEND-MAP`; `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:870-878`
13. **Post-EVALUATE fall-through** (only reached via the PFK10-update WHEN, or no WHEN match): if `INPUT-ERROR` → set error nav fields, `GO TO COMMON-RETURN`. Else `MOVE LIT-THISPGM TO CCARD-NEXT-PROG`, `GO TO COMMON-RETURN`. `// source: COTRTLIC.cbl:882-896`

### 4.2 `COMMON-RETURN` — pseudo-conversational return `// source: COTRTLIC.cbl:899-915`
Set nav fields to self (from-tranid `CTLI`, from-program `COTRTLIC`, LAST-MAPSET/MAP); reassemble `WS-COMMAREA` = CARDDEMO-COMMAREA prefix + WS-THIS-PROGCOMMAREA tail; `EXEC CICS RETURN TRANSID('CTLI') COMMAREA(WS-COMMAREA) LENGTH(2000)`.

### 4.3 `1000-RECEIVE-MAP` `// source: COTRTLIC.cbl:919-928`
`PERFORM 1100-RECEIVE-SCREEN` then `PERFORM 1200-EDIT-INPUTS`.

### 4.4 `1100-RECEIVE-SCREEN` `// source: COTRTLIC.cbl:930-958`
1. `EXEC CICS RECEIVE MAP('CTRTLIA') MAPSET('COTRTLI') INTO(CTRTLIAI) RESP(WS-RESP-CD)`. `// source: COTRTLIC.cbl:931-935`
2. `MOVE TRTYPEI TO WS-IN-TYPE-CD`; `MOVE TRDESCI TO WS-IN-TYPE-DESC`. `// source: COTRTLIC.cbl:937-938`
3. `PERFORM VARYING I 1..7`: `MOVE TRTSELI(I) TO WS-EDIT-SELECT(I)`; `MOVE TRTTYPI(I) TO WS-ROW-TR-CODE-IN(I)`; `MOVE LOW-VALUES TO WS-ROW-TR-DESC-IN(I)`; if `TRTYPDI(I)` = `'*'` (LIT-ASTERISK) or SPACES → leave low-values, else `MOVE FUNCTION TRIM(TRTYPDI(I)) TO WS-ROW-TR-DESC-IN(I)`. `// source: COTRTLIC.cbl:940-953`

### 4.5 `1200-EDIT-INPUTS` `// source: COTRTLIC.cbl:960-980`
`SET INPUT-OK`; `SET FLG-PROTECT-SELECT-ROWS-NO`; then `PERFORM 1210-EDIT-ARRAY`, `1230-EDIT-DESC`, `1220-EDIT-TYPECD`, `1290-CROSS-EDITS` (in that order — desc before typecd). `// source: COTRTLIC.cbl:962-975`

### 4.6 `1210-EDIT-ARRAY` `// source: COTRTLIC.cbl:982-1057`
1. Zero the action counters (`WS-ACTIONS-REQUESTED`, `WS-NO-ACTIONS-SELECTED`, `WS-DELETES-REQUESTED`, `WS-UPDATES-REQUESTED`, `WS-VALID-ACTIONS-SELECTED`). `// source: COTRTLIC.cbl:984-988`
2. **If a filter changed** (TYPEFILTER-CHANGED-YES or DESCFILTER-CHANGED-YES): `INITIALIZE WS-EDIT-SELECT-FLAGS` and `GO TO 1210-EDIT-ARRAY-EXIT` (skip all action processing — a filter change discards row selections). `// source: COTRTLIC.cbl:991-994`

   **Caveat:** these `…-CHANGED-YES` 88s are only set later by 1220/1230, which run *after* 1210. So on this path the values are whatever they were on entry to 1200 (always …-NO after the `INITIALIZE WS-MISC-STORAGE` at top of MAIN unless restored). Reproduce the order exactly (see Faithful Bugs #4). `// source: COTRTLIC.cbl:991-995, 968-972`
3. ELSE: `INSPECT WS-EDIT-SELECT-FLAGS TALLYING WS-NO-ACTIONS-SELECTED FOR ALL SPACES LOW-VALUES, WS-DELETES-REQUESTED FOR ALL 'D', WS-UPDATES-REQUESTED FOR ALL 'U'`. `// source: COTRTLIC.cbl:997-1001`
4. `COMPUTE WS-ACTIONS-REQUESTED = WS-MAX-SCREEN-LINES(7) - WS-NO-ACTIONS-SELECTED`. `// source: COTRTLIC.cbl:1003-1006`
5. `COMPUTE WS-VALID-ACTIONS-SELECTED = WS-DELETES-REQUESTED + WS-UPDATES-REQUESTED`. `// source: COTRTLIC.cbl:1009-1012`
6. `MOVE ZERO TO I-SELECTED`; `SET FLG-BAD-ACTIONS-SELECTED-NO`. `// source: COTRTLIC.cbl:1014-1015`
7. `PERFORM VARYING I FROM 7 BY -1 UNTIL I = 0` (descending) `// source: COTRTLIC.cbl:1017-1040`:
   - `WHEN SELECT-OK(I)` (='D' or 'U'): `MOVE I TO I-SELECTED`; if `WS-MORETHAN1ACTION` (2..7) → `MOVE '1' TO WS-ROW-TRTSELECT-ERROR(I)`, `SET FLG-BAD-ACTIONS-SELECTED-YES`; if `UPDATE-REQUESTED-ON(I)` → `PERFORM 1211-EDIT-ARRAY-DESC`. `// source: COTRTLIC.cbl:1022-1031`
   - `WHEN SELECT-BLANK(I)` → CONTINUE. `// source: COTRTLIC.cbl:1032-1033`
   - `WHEN OTHER` (invalid action char) → `SET INPUT-ERROR`, `MOVE '1' TO WS-ROW-TRTSELECT-ERROR(I)`, `SET FLG-BAD-ACTIONS-SELECTED-YES`, `SET WS-MESG-INVALID-ACTION-CODE`. `// source: COTRTLIC.cbl:1034-1038`
8. If `I-SELECTED = WS-CA-ROW-SELECTED` → `SET FLG-ROW-SELECTION-CHANGED-NO`; ELSE `SET FLG-ROW-SELECTION-CHANGED-YES` and `MOVE I-SELECTED TO WS-CA-ROW-SELECTED`. `// source: COTRTLIC.cbl:1042-1047`
9. If `WS-MORETHAN1ACTION` → `SET INPUT-ERROR`, `SET WS-MESG-MORE-THAN-1-ACTION`. `// source: COTRTLIC.cbl:1049-1052`

### 4.7 `1211-EDIT-ARRAY-DESC` `// source: COTRTLIC.cbl:1060-1094`
Called only for an `U`-selected row. `// source: COTRTLIC.cbl:1028-1030`
1. `SET NO-CHANGES-FOUND`. `// source: COTRTLIC.cbl:1062`
2. If `UPPER(TRIM(WS-ROW-TR-DESC-IN(I))) = UPPER(TRIM(WS-CA-ROW-TR-DESC-OUT(I)))` **AND** `LENGTH(TRIM(in)) = LENGTH(TRIM(out))` → `SET WS-MESG-NO-CHANGES-DETECTED`, `GO TO …-EXIT`. ELSE `SET CHANGES-HAVE-OCCURRED`. `// source: COTRTLIC.cbl:1064-1076`
3. `SET FLG-ROW-DESCRIPTION-NOT-OK`; set up edit: `WS-EDIT-VARIABLE-NAME='Transaction Desc'`, `WS-EDIT-ALPHANUM-ONLY = WS-ROW-TR-DESC-IN(I)`, `WS-EDIT-ALPHANUM-LENGTH = 50`; `PERFORM 1240-EDIT-ALPHANUM-REQD`; `MOVE WS-EDIT-ALPHANUM-ONLY-FLAGS TO WS-ARRAY-DESCRIPTION-FLGS`. `// source: COTRTLIC.cbl:1078-1089`

### 4.8 `1220-EDIT-TYPECD` (+ `1220-EDIT-TYPECD-EXIT` carries logic) `// source: COTRTLIC.cbl:1096-1140`
1. `SET FLG-TYPEFILTER-BLANK`. `// source: COTRTLIC.cbl:1098`
2. If `WS-IN-TYPE-CD` ∈ {LOW-VALUES, SPACES, ZEROS} → `SET FLG-TYPEFILTER-BLANK`, `MOVE ZEROES TO WS-TYPE-CD-FILTER`, `GO TO 1220-EDIT-TYPECD-EXIT`. `// source: COTRTLIC.cbl:1101-1107`
3. If `WS-IN-TYPE-CD IS NOT NUMERIC` → `SET INPUT-ERROR`, `SET FLG-TYPEFILTER-NOT-OK`, `SET FLG-PROTECT-SELECT-ROWS-YES`, `MOVE 'TYPE CODE FILTER,IF SUPPLIED MUST BE A 2 DIGIT NUMBER' TO WS-RETURN-MSG`, `GO TO 1220-EDIT-TYPECD-EXIT`. ELSE `MOVE WS-IN-TYPE-CD TO WS-TYPE-CD-FILTER`, `SET FLG-TYPEFILTER-ISVALID`. `// source: COTRTLIC.cbl:1111-1122`
4. **`1220-EDIT-TYPECD-EXIT`** (carries change-detection logic, not a pure no-op): if `WS-IN-TYPE-CD = WS-CA-TYPE-CD` OR (`FLG-TYPEFILTER-BLANK` AND `WS-CA-TYPE-CD` ∈ {ZEROES, LOW-VALUES, SPACES}) → `SET FLG-TYPEFILTER-CHANGED-NO`. ELSE `INITIALIZE WS-CA-PAGING-VARIABLES`, `MOVE WS-IN-TYPE-CD TO WS-CA-TYPE-CD`, `SET FLG-TYPEFILTER-CHANGED-YES`. `// source: COTRTLIC.cbl:1125-1140`

### 4.9 `1230-EDIT-DESC` (+ `1230-EDIT-DESC-EXIT` carries logic) `// source: COTRTLIC.cbl:1142-1178`
1. `SET FLG-DESCFILTER-BLANK`. `// source: COTRTLIC.cbl:1144`
2. If `WS-IN-TYPE-DESC` ∈ {LOW-VALUES, SPACES} → `SET FLG-DESCFILTER-BLANK`, `GO TO 1230-EDIT-DESC-EXIT`. ELSE `SET FLG-DESCFILTER-ISVALID`. `// source: COTRTLIC.cbl:1147-1153`
3. If `FLG-DESCFILTER-ISVALID` → build the LIKE pattern: `STRING '%' FUNCTION TRIM(WS-IN-TYPE-DESC) '%' DELIMITED BY SIZE INTO WS-TYPE-DESC-FILTER`. `// source: COTRTLIC.cbl:1155-1163`
4. **`1230-EDIT-DESC-EXIT`** (carries change-detection): if `WS-IN-TYPE-DESC = WS-CA-TYPE-DESC` OR (`FLG-DESCFILTER-BLANK` AND `WS-CA-TYPE-DESC` ∈ {LOW-VALUES, SPACES}) → `SET FLG-DESCFILTER-CHANGED-NO`. ELSE `INITIALIZE WS-CA-PAGING-VARIABLES`, `MOVE WS-IN-TYPE-DESC TO WS-CA-TYPE-DESC`, `SET FLG-DESCFILTER-CHANGED-YES`. `// source: COTRTLIC.cbl:1166-1178`

### 4.10 `1240-EDIT-ALPHANUM-REQD` `// source: COTRTLIC.cbl:1181-1237`
Validates `WS-EDIT-ALPHANUM-ONLY(1:WS-EDIT-ALPHANUM-LENGTH)` (here the 50-char description).
1. `SET FLG-ALPHNANUM-NOT-OK`. `// source: COTRTLIC.cbl:1183`
2. If field is LOW-VALUES or SPACES or `LENGTH(TRIM())=0` → `SET INPUT-ERROR`, `SET FLG-ALPHNANUM-BLANK`; if `WS-RETURN-MSG-OFF` → `STRING TRIM(WS-EDIT-VARIABLE-NAME) ' must be supplied.' INTO WS-RETURN-MSG`; `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1186-1205`
3. Else `INSPECT … CONVERTING LIT-ALL-ALPHANUM-FROM TO LIT-ALPHANUM-SPACES-TO` (blank out A-Z/a-z/0-9). If remaining `LENGTH(TRIM())=0` → CONTINUE (all chars were alphanumeric/space — valid). ELSE `SET INPUT-ERROR`, `SET FLG-ALPHNANUM-NOT-OK`; if `WS-RETURN-MSG-OFF` → `STRING TRIM(name) ' can have numbers or alphabets only.' INTO WS-RETURN-MSG`; `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1208-1231`
4. `SET FLG-ALPHNANUM-ISVALID`. `// source: COTRTLIC.cbl:1233`

### 4.11 `1290-CROSS-EDITS` `// source: COTRTLIC.cbl:1239-1271`
1. If neither `FLG-TYPEFILTER-ISVALID` nor `FLG-DESCFILTER-ISVALID` → `GO TO …-EXIT` (no filter supplied → nothing to cross-check). `// source: COTRTLIC.cbl:1241-1246`
2. `PERFORM 9100-CHECK-FILTERS` (COUNT). `// source: COTRTLIC.cbl:1248-1249`
3. If `WS-RECORDS-COUNT = 0` → `SET INPUT-ERROR`; if type filter valid → `SET FLG-TYPEFILTER-NOT-OK`; if desc filter valid → `SET FLG-DESCFILTER-NOT-OK`; `SET FLG-PROTECT-SELECT-ROWS-YES`; `MOVE 'No Records found for these filter conditions' TO WS-RETURN-MSG`; `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1251-1266`

### 4.12 `2000-SEND-MAP` `// source: COTRTLIC.cbl:1274-1292`
Sequential: `2100-SCREEN-INIT`, `2200-SETUP-ARRAY-ATTRIBS`, `2300-SCREEN-ARRAY-INIT`, `2400-SETUP-SCREEN-ATTRS`, `2500-SETUP-MESSAGE`, `2600-SEND-SCREEN`.

### 4.13 `2100-SCREEN-INIT` `// source: COTRTLIC.cbl:1293-1327`
`MOVE LOW-VALUES TO CTRTLIAO`; `MOVE CURRENT-DATE TO WS-CURDATE-DATA`; fill titles (`CCDA-TITLE01/02`), `TRNNAMEO=CTLI`, `PGMNAMEO=COTRTLIC`; build `CURDATEO = mm/dd/yy` and `CURTIMEO = hh:mm:ss`; `MOVE WS-CA-SCREEN-NUM TO PAGENOO`; `SET WS-NO-INFO-MESSAGE`, blank `INFOMSGO`, `MOVE DFHBMDAR TO INFOMSGC` (info line dark). `// source: COTRTLIC.cbl:1294-1322`

### 4.14 `2200-SETUP-ARRAY-ATTRIBS` `// source: COTRTLIC.cbl:1329-1379`
`PERFORM VARYING I FROM 7 BY -1 UNTIL I = 0`:
- `MOVE DFHBMPRF TO TRTYPDA(I)` (desc field protected by default). `// source: COTRTLIC.cbl:1337`
- If `WS-CA-EACH-ROW-OUT(I) = LOW-VALUES` OR `FLG-PROTECT-SELECT-ROWS-YES` → `MOVE DFHBMPRO TO TRTSELA(I)` (select autoskip/protected). `// source: COTRTLIC.cbl:1339-1341`
- ELSE: if `WS-ROW-TRTSELECT-ERROR(I)='1'` → `MOVE DFHRED TO TRTSELC(I)`, `MOVE -1 TO TRTSELL(I)` (cursor); `// source: COTRTLIC.cbl:1343-1346`
  - if `DELETE-REQUESTED-ON(I)` AND `WS-ONLY-1-VALID-ACTION` AND `FLG-BAD-ACTIONS-SELECTED-NO` → `MOVE DFHNEUTR TO TRTTYPC(I), TRTYPDC(I)`, `MOVE -1 TO TRTSELL(I)` (highlight the delete-target row). `// source: COTRTLIC.cbl:1348-1354`
  - if `UPDATE-REQUESTED-ON(I)` AND `WS-ONLY-1-VALID-ACTION` AND `FLG-BAD-ACTIONS-SELECTED-NO` → `MOVE DFHNEUTR TO TRTTYPC(I)`; if `FLG-UPDATE-COMPLETED` → `MOVE -1 TO TRTSELL(I)`, `MOVE DFHNEUTR TO TRTYPDC(I)`; ELSE → `MOVE -1 TO TRTYPDL(I)`, `MOVE DFHBMFSE TO TRTYPDA(I)` (unprotect desc for edit), and if NOT `FLG-ROW-DESCRIPTION-ISVALID` → `MOVE DFHRED TO TRTYPDC(I)`. `// source: COTRTLIC.cbl:1356-1370`
  - `MOVE DFHBMFSE TO TRTSELA(I)` (select field modifiable). `// source: COTRTLIC.cbl:1371`

### 4.15 `2300-SCREEN-ARRAY-INIT` `// source: COTRTLIC.cbl:1383-1435`
`PERFORM VARYING I FROM 1 BY 1 UNTIL I > 7`; skip rows where `WS-CA-EACH-ROW-OUT(I) = LOW-VALUES`. For populated rows:
- if `DELETE-REQUESTED-ON(I)` AND `WS-ONLY-1-VALID-ACTION` AND `FLG-BAD-ACTIONS-SELECTED-NO`: if `FLG-DELETED-YES` → `SET SELECT-BLANK(I)` (clear after delete) ELSE `SET CA-DELETE-REQUESTED` (mark confirm pending). `// source: COTRTLIC.cbl:1391-1399`
- `MOVE WS-CA-ROW-TR-CODE-OUT(I) TO TRTTYPO(I)`. `// source: COTRTLIC.cbl:1402`
- Description: if `UPDATE-REQUESTED-ON(I)` AND `WS-ONLY-1-VALID-ACTION` AND `FLG-BAD-ACTIONS-SELECTED-NO`: if `FLG-UPDATE-COMPLETED` → `SET SELECT-BLANK(I)` ELSE `SET CA-UPDATE-REQUESTED`; then if `CHANGES-HAVE-OCCURRED` → (`FLG-ROW-DESCRIPTION-BLANK` → `MOVE '*' (LIT-ASTERISK) TO TRTYPDO(I)`; OTHER → `MOVE WS-ROW-TR-DESC-IN(I) TO TRTYPDO(I)`), ELSE → `MOVE WS-CA-ROW-TR-DESC-OUT(I) TO TRTYPDO(I)`. If not an update row → `MOVE WS-CA-ROW-TR-DESC-OUT(I) TO TRTYPDO(I)`. `// source: COTRTLIC.cbl:1404-1425`
- `MOVE WS-EDIT-SELECT(I) TO TRTSELO(I)` (echo the action char, possibly blanked above). `// source: COTRTLIC.cbl:1428`

### 4.16 `2400-SETUP-SCREEN-ATTRS` `// source: COTRTLIC.cbl:1438-1501`
1. If `EIBCALEN = 0` OR (`CDEMO-PGM-ENTER` AND from-program = `COADM01C`) → CONTINUE (leave filters blank on fresh menu entry). `// source: COTRTLIC.cbl:1440-1443`
2. ELSE set the **Type filter** echo via `EVALUATE TRUE` (first match): when `WS-ACTIONS-REQUESTED > 0` → echo `WS-IN-TYPE-CD` to `TRTYPEO`, set `TRTYPEA=DFHBMASF` (autoskip), `TRTYPEC=DFHBLUE`; when `FLG-TYPEFILTER-ISVALID` / `FLG-TYPEFILTER-NOT-OK` → echo `WS-IN-TYPE-CD`, `TRTYPEA=DFHBMFSE`; when `WS-IN-TYPE-CD = 0` → `MOVE LOW-VALUES TO TRTYPEO`; OTHER → `MOVE LOW-VALUES TO TRTYPEO`, `TRTYPEA=DFHBMFSE`. `// source: COTRTLIC.cbl:1445-1459`
3. Then the **Description filter** echo, same shape: ACTIONS>0 → echo `WS-IN-TYPE-DESC` to `TRDESCO`, `DFHBMASF`, `DFHBLUE`; ISVALID/NOT-OK → echo, `DFHBMFSE`; OTHER → `TRDESCA=DFHBMFSE`. `// source: COTRTLIC.cbl:1461-1473`
4. Cursor on errors: if `FLG-TYPEFILTER-NOT-OK` → `TRTYPEC=DFHRED`, `TRTYPEL=-1`; if `FLG-DESCFILTER-NOT-OK` → `TRDESCC=DFHRED`, `TRDESCL=-1`. `// source: COTRTLIC.cbl:1477-1485`
5. If `INPUT-OK`: if `WS-ACTIONS-REQUESTED > 0` AND not PFK07/PFK08 → leave cursor (it's on a row); ELSE `MOVE -1 TO TRTYPEL` (cursor to type filter). `// source: COTRTLIC.cbl:1489-1497`

### 4.17 `2500-SETUP-MESSAGE` `// source: COTRTLIC.cbl:1504-1584`
1. `EVALUATE TRUE` selects the info/return message (first match) — see §8 for the exact mapping:
   - `FLG-DELETED-YES` → `WS-INFORM-DELETE-SUCCESS`. `// source: COTRTLIC.cbl:1507-1508`
   - `FLG-UPDATE-COMPLETED` → `WS-INFORM-UPDATE-SUCCESS`. `// source: COTRTLIC.cbl:1509-1510`
   - `FLG-TYPEFILTER-NOT-OK` / `FLG-DESCFILTER-NOT-OK` → CONTINUE (keep the NOT-OK return message already set). `// source: COTRTLIC.cbl:1511-1513`
   - ENTER AND DELETES>0 AND ONLY-1-ACTION AND ONLY-1-VALID-ACTION AND no-info AND filters-not-changed → `WS-INFORM-DELETE`. `// source: COTRTLIC.cbl:1514-1522`
   - ENTER AND UPDATES>0 AND ONLY-1-ACTION AND ONLY-1-VALID-ACTION AND no-info AND filters-not-changed → `WS-INFORM-UPDATE`. `// source: COTRTLIC.cbl:1523-1531`
   - PFK07 AND CA-FIRST-PAGE → `MOVE 'No previous pages to display' TO WS-RETURN-MSG`. `// source: COTRTLIC.cbl:1532-1535`
   - PFK08 AND CA-NEXT-PAGE-NOT-EXISTS AND CA-LAST-PAGE-SHOWN → `MOVE 'No more pages to display' TO WS-RETURN-MSG`. `// source: COTRTLIC.cbl:1536-1540`
   - PFK08 AND CA-NEXT-PAGE-NOT-EXISTS → if no-info `SET WS-INFORM-REC-ACTIONS`; if CA-LAST-PAGE-NOT-SHOWN AND CA-NEXT-PAGE-NOT-EXISTS `SET CA-LAST-PAGE-SHOWN`. `// source: COTRTLIC.cbl:1541-1549`
   - `WS-NO-INFO-MESSAGE` / `CA-NEXT-PAGE-EXISTS` → `SET WS-INFORM-REC-ACTIONS`. `// source: COTRTLIC.cbl:1550-1552`
   - OTHER → `SET WS-NO-INFO-MESSAGE`. `// source: COTRTLIC.cbl:1553-1554`
2. `MOVE WS-RETURN-MSG TO ERRMSGO`. `// source: COTRTLIC.cbl:1557`
3. **Center-justify the info message** into `WS-STRING-OUT X45`: `WS-STRING-LEN = LENGTH(TRIM(WS-INFO-MSG))`; `WS-STRING-MID = (LENGTH(WS-INFO-MSG) - WS-STRING-LEN) / 2 + 1` (integer division, truncates); `MOVE WS-INFO-MSG(1:WS-STRING-LEN) TO WS-STRING-OUT(WS-STRING-MID:WS-STRING-LEN)`. `// source: COTRTLIC.cbl:1562-1571`
4. If NOT `WS-NO-INFO-MESSAGE` AND NOT `WS-MESG-NO-RECORDS-FOUND` → `MOVE WS-STRING-OUT TO INFOMSGO`, `MOVE DFHNEUTR TO INFOMSGC`. `// source: COTRTLIC.cbl:1575-1579`

### 4.18 `2600-SEND-SCREEN` `// source: COTRTLIC.cbl:1587-1599`
`EXEC CICS SEND MAP('CTRTLIA') MAPSET('COTRTLI') FROM(CTRTLIAO) CURSOR ERASE RESP(WS-RESP-CD) FREEKB`.

### 4.19 `8000-READ-FORWARD` `// source: COTRTLIC.cbl:1603-1726`
1. `MOVE LOW-VALUES TO WS-CA-ALL-ROWS-OUT` (clear page buffer). `// source: COTRTLIC.cbl:1604`
2. `PERFORM 9400-OPEN-FORWARD-CURSOR`; if `WS-DB2-ERROR` → `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1609-1614`
3. `MOVE ZEROES TO WS-ROW-NUMBER`; `SET CA-NEXT-PAGE-EXISTS`; `SET MORE-RECORDS-TO-READ`. `// source: COTRTLIC.cbl:1618-1620`
4. `PERFORM UNTIL READ-LOOP-EXIT`: `INITIALIZE DCLTRANSACTION-TYPE`; `EXEC SQL FETCH C-TR-TYPE-FORWARD INTO :DCL-TR-TYPE, :DCL-TR-DESCRIPTION`; `MOVE SQLCODE TO WS-DISP-SQLCODE`; `EVALUATE TRUE`:
   - `WHEN SQLCODE = 0`: `ADD 1 TO WS-ROW-NUMBER`; store `DCL-TR-TYPE → WS-CA-ROW-TR-CODE-OUT(WS-ROW-NUMBER)`, `DCL-TR-DESCRIPTION-TEXT → WS-CA-ROW-TR-DESC-OUT(WS-ROW-NUMBER)`. If row 1 → `MOVE DCL-TR-TYPE TO WS-CA-FIRST-TR-CODE`; if `WS-CA-SCREEN-NUM = 0` → `ADD +1 TO WS-CA-SCREEN-NUM`. If `WS-ROW-NUMBER = 7` (max) → `SET READ-LOOP-EXIT`, `MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE`, then **look-ahead fetch** one more row: if `SQLCODE=0` → `SET CA-NEXT-PAGE-EXISTS`, `MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE`; if `+100` → `SET CA-NEXT-PAGE-NOT-EXISTS`, and if (`WS-RETURN-MSG-OFF` AND PFK08) `SET WS-MESG-NO-MORE-RECORDS`; OTHER → `SET READ-LOOP-EXIT`, format Db2 error. `// source: COTRTLIC.cbl:1634-1693`
   - `WHEN SQLCODE = +100`: `SET READ-LOOP-EXIT`, `SET CA-NEXT-PAGE-NOT-EXISTS`, `MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE`; if (`WS-RETURN-MSG-OFF` AND PFK08) `SET WS-MESG-NO-MORE-RECORDS`; if (`WS-CA-SCREEN-NUM=1` AND `WS-ROW-NUMBER=0`) `SET WS-MESG-NO-RECORDS-FOUND`. `// source: COTRTLIC.cbl:1694-1705`
   - `WHEN OTHER`: `SET READ-LOOP-EXIT`, `SET WS-DB2-ERROR`; if `WS-RETURN-MSG-OFF` → action 'C-TR-TYPE-FORWARD close', `9999-FORMAT-DB2-MESSAGE`. `// source: COTRTLIC.cbl:1706-1718`
5. `PERFORM 9450-CLOSE-FORWARD-CURSOR`. `// source: COTRTLIC.cbl:1721-1722`

### 4.20 `8100-READ-BACKWARDS` (+ `8100-READ-BACKWARDS-EXIT` closes cursor) `// source: COTRTLIC.cbl:1727-1799`
1. `MOVE LOW-VALUES TO WS-CA-ALL-ROWS-OUT`; `MOVE WS-CA-FIRST-TTYPEKEY TO WS-CA-LAST-TTYPEKEY`. `// source: COTRTLIC.cbl:1729-1731`
2. `COMPUTE WS-ROW-NUMBER = 7`; `SET CA-NEXT-PAGE-EXISTS`; `SET MORE-RECORDS-TO-READ`; `PERFORM 9500-OPEN-BACKWARD-CURSOR`. `// source: COTRTLIC.cbl:1735-1747`
3. `PERFORM UNTIL READ-LOOP-EXIT`: `INITIALIZE DCLTRANSACTION-TYPE`; `EXEC SQL FETCH C-TR-TYPE-BACKWARD INTO :DCL-TR-TYPE, :DCL-TR-DESCRIPTION`; `EVALUATE TRUE`:
   - `WHEN SQLCODE = 0`: store into `WS-CA-ROW-TR-CODE-OUT(WS-ROW-NUMBER)` / `WS-CA-ROW-TR-DESC-OUT(WS-ROW-NUMBER)`; `SUBTRACT 1 FROM WS-ROW-NUMBER`; when it reaches 0 → `SET READ-LOOP-EXIT`, `MOVE DCL-TR-TYPE TO WS-CA-FIRST-TR-CODE` (fills the page bottom-up). `// source: COTRTLIC.cbl:1762-1776`
   - `WHEN OTHER` (incl. `+100`): `SET READ-LOOP-EXIT`, `SET WS-DB2-ERROR`; if `WS-RETURN-MSG-OFF` → action 'Error on fetch Cursor C-TR-TYPE-BACKWARD', `9999-FORMAT-DB2-MESSAGE`. **Note: `+100` is NOT special-cased here — backward end-of-cursor is treated as an error** (see Faithful Bugs #5). `// source: COTRTLIC.cbl:1777-1790`
4. `8100-READ-BACKWARDS-EXIT` → `PERFORM 9550-CLOSE-BACK-CURSOR`. `// source: COTRTLIC.cbl:1794-1796`

### 4.21 `9100-CHECK-FILTERS` `// source: COTRTLIC.cbl:1801-1836`
`EXEC SQL SELECT COUNT(1) INTO :WS-RECORDS-COUNT FROM CARDDEMO.TRANSACTION_TYPE WHERE (typeFlag pred) AND (descFlag pred)`; on `SQLCODE=0` CONTINUE; OTHER → `SET INPUT-ERROR`, format Db2 message 'Error reading TRANSACTION_TYPE table '. `// source: COTRTLIC.cbl:1803-1832`

### 4.22 `9200-UPDATE-RECORD` `// source: COTRTLIC.cbl:1837-1894`
1. `MOVE WS-ROW-TR-CODE-IN(I-SELECTED) TO DCL-TR-TYPE`; `MOVE FUNCTION TRIM(WS-ROW-TR-DESC-IN(I-SELECTED)) TO DCL-TR-DESCRIPTION-TEXT`; `COMPUTE DCL-TR-DESCRIPTION-LEN = FUNCTION LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))` (= 50 always — Faithful Bugs #3). `// source: COTRTLIC.cbl:1839-1844`
2. `EXEC SQL UPDATE CARDDEMO.TRANSACTION_TYPE SET TR_DESCRIPTION = :DCL-TR-DESCRIPTION WHERE TR_TYPE = :DCL-TR-TYPE`. `// source: COTRTLIC.cbl:1846-1850`
3. `EVALUATE TRUE`: `SQLCODE=0` → `EXEC CICS SYNCPOINT`, `SET CA-UPDATE-SUCCEEDED`, if no-info `SET WS-INFORM-UPDATE-SUCCESS`. `+100` → `SET CA-UPDATE-REQUESTED`, format msg 'Record not found. Deleted by others ? ', `GO TO …-EXIT`. `-911` → `SET CA-UPDATE-REQUESTED`, `SET INPUT-ERROR`, msg 'Deadlock. Someone else updating ?', `GO TO …-EXIT`. `SQLCODE < 0` → `SET CA-UPDATE-REQUESTED`, msg 'Update failed with', `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1854-1889`

### 4.23 `9300-DELETE-RECORD` `// source: COTRTLIC.cbl:1896-1940`
1. `MOVE WS-ROW-TR-CODE-IN(I-SELECTED) TO DCL-TR-TYPE`. `// source: COTRTLIC.cbl:1898`
2. `EXEC SQL DELETE FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE = :DCL-TR-TYPE`. `// source: COTRTLIC.cbl:1900-1903`
3. `EVALUATE TRUE`: `SQLCODE=0` → `EXEC CICS SYNCPOINT`, `SET CA-DELETE-SUCCEEDED`, if no-info `SET WS-INFORM-DELETE-SUCCESS`. `-532` (FK / child rows) → `SET CA-DELETE-REQUESTED`, msg 'Please delete associated child records first:', `GO TO …-EXIT`. OTHER → msg 'Delete failed with message:', `GO TO …-EXIT`. `// source: COTRTLIC.cbl:1907-1935`

### 4.24 Cursor open/close helpers
- `9400-OPEN-FORWARD-CURSOR`: `EXEC SQL OPEN C-TR-TYPE-FORWARD`; non-zero → `SET WS-DB2-ERROR`, format msg 'C-TR-TYPE-FORWARD Open'. `// source: COTRTLIC.cbl:1942-1967`
- `9450-CLOSE-FORWARD-CURSOR`: `CLOSE C-TR-TYPE-FORWARD`; non-zero → `SET WS-DB2-ERROR`, msg 'C-TR-TYPE-FORWARD close'. `// source: COTRTLIC.cbl:1970-1995`
- `9500-OPEN-BACKWARD-CURSOR`: `OPEN C-TR-TYPE-BACKWARD`; non-zero → `SET WS-DB2-ERROR`, msg 'C-TR-TYPE-BACKWARD Open'. `// source: COTRTLIC.cbl:1997-2023`
- `9550-CLOSE-BACK-CURSOR`: `CLOSE C-TR-TYPE-BACKWARD`; non-zero → `SET WS-DB2-ERROR`, msg 'C-TR-TYPE-BACKWARD close'. `// source: COTRTLIC.cbl:2026-2051`

### 4.25 Copybook-supplied paragraphs
- `9998-PRIMING-QUERY` (`CSDB2RPY`): `SELECT 1 INTO :WS-DUMMY-DB2-INT FROM SYSIBM.SYSDUMMY1 FETCH FIRST 1 ROW ONLY`; non-zero → `SET WS-DB2-ERROR`, msg 'Db2 access failure. '. In .NET this becomes a trivial "can I reach the DB" probe (always OK in-proc SQLite). `// source: CSDB2RPY.cpy:21-48`
- `9999-FORMAT-DB2-MESSAGE` (`CSDB2RPY`): calls `DSNTIAC` to format the SQLCA into `WS-DSNTIAC-FMTD-TEXT`, then `STRING TRIM(WS-DB2-CURRENT-ACTION) ' SQLCODE:' WS-DISP-SQLCODE ' ' WS-DSNTIAC-FMTD-TEXT INTO WS-LONG-MSG`; `MOVE WS-LONG-MSG TO WS-RETURN-MSG`. In .NET, synthesize an equivalent string: `"<action> SQLCODE:<edited-sqlcode> <formatted-text>"`; `WS-DISP-SQLCODE` is `PIC ----9` (leading-sign edited, 5 chars). `// source: CSDB2RPY.cpy:53-89; CSDB2RWY.cpy:22`
- `YYYY-STORE-PFKEY` (`CSSTRPFY`): `EVALUATE EIBAID` → set the matching `CCARD-AID-*` 88. **PF13-24 alias to PF1-12** (e.g. DFHPF13→PFK01 … DFHPF24→PFK12). `// source: CSSTRPFY.cpy:21-79`

### 4.26 Debug exits (declared, used only on Db2 error)
- `SEND-PLAIN-TEXT`: `EXEC CICS SEND TEXT FROM(WS-RETURN-MSG) … ERASE FREEKB` + `RETURN`. Declared, **not performed** anywhere. `// source: COTRTLIC.cbl:2066-2079`
- `SEND-LONG-TEXT`: `EXEC CICS SEND TEXT FROM(WS-LONG-MSG) … ERASE FREEKB` + `RETURN`. Performed once (priming-query failure, step 11). `// source: COTRTLIC.cbl:688, 2085-2095`

---

## 5. ONLINE / CICS DETAILS

### 5.1 COMMAREA layout (what is read vs written)
Saved/returned commarea = `CARDDEMO-COMMAREA` (prefix) ++ `WS-THIS-PROGCOMMAREA` (tail), inside `WS-COMMAREA X(2000)`. `// source: COTRTLIC.cbl:420, 904-907`
- **Read on entry:** `CDEMO-FROM-PROGRAM`, `CDEMO-FROM-TRANID`, `CDEMO-PGM-CONTEXT` (ENTER/REENTER), `CDEMO-USER-TYPE`; tail: saved filters `WS-CA-TYPE-CD`/`WS-CA-TYPE-DESC`, page buffer `WS-CA-ALL-ROWS-OUT`, `WS-CA-ROW-SELECTED`, paging vars (`WS-CA-FIRST-TR-CODE`, `WS-CA-LAST-TR-CODE`, `WS-CA-SCREEN-NUM`, `WS-CA-LAST-PAGE-DISPLAYED`, `WS-CA-NEXT-PAGE-IND`), `WS-CA-DELETE-FLAG`, `WS-CA-UPDATE-FLAG`. `// source: COTRTLIC.cbl:527-531`
- **Written on return:** nav fields set to self; tail updated with new filters, new page buffer, new paging/flag state. `// source: COTRTLIC.cbl:900-907`

### 5.2 Pseudo-conversational flow
`RECEIVE MAP` (only when re-entered from self) → edit → `EVALUATE` dispatch → (`8000`/`8100` read, or `9200`/`9300` update/delete) → `SEND MAP` → `RETURN TRANSID('CTLI')`. XCTL on PF3/PF2 ends the conversation instead. `// source: COTRTLIC.cbl:561-566, 698-879, 910-914`

### 5.3 EIBAID / PFKey handling
`EIBAID` mapped to `CCARD-AID-*` by `YYYY-STORE-PFKEY`. Valid keys at this screen: **ENTER, F2, F3, F7, F8, F10** (F10 only when a delete/update confirm is pending). Any other key is silently remapped to ENTER. `// source: COTRTLIC.cbl:574-587; CSSTRPFY.cpy`
- **ENTER** — re-list / arm a delete or update confirm (depending on which action char was typed).
- **F2** — XCTL to `COTRTUPC` (Add). `// source: COTRTLIC.cbl:630-651`
- **F3** — SYNCPOINT + XCTL back to `COADM01C`/caller. `// source: COTRTLIC.cbl:591-625`
- **F7** — page up (`8100-READ-BACKWARDS`); on first page just refreshes. `// source: COTRTLIC.cbl:721-790`
- **F8** — page down (`8000-READ-FORWARD` from last key). `// source: COTRTLIC.cbl:766-776`
- **F10** — confirm the armed delete (`9300`) or update (`9200`). If filters/selection changed since arming, demoted to ENTER. `// source: COTRTLIC.cbl:666-678, 810-868`

### 5.4 XCTL / LINK / RETURN targets
- `XCTL PROGRAM(CDEMO-TO-PROGRAM)` = `COADM01C` (or caller) on F3. `// source: COTRTLIC.cbl:620-623`
- `XCTL PROGRAM('COTRTUPC')` on F2. `// source: COTRTLIC.cbl:648-651`
- `RETURN TRANSID('CTLI')` otherwise. `// source: COTRTLIC.cbl:910-914`
- No `LINK`. `DSNTIAC` is `CALL`ed (static) by `9999-FORMAT-DB2-MESSAGE`. `// source: CSDB2RPY.cpy:57`

### 5.5 BMS map + mapset (COTRTLI / CTRTLIA, 24×80)
Mapset `COTRTLI`, map `CTRTLIA`, `SIZE=(24,80)`, `CTRL=(FREEKB)`. `// source: COTRTLI.bms:20-28`
Symbolic copybook `COTRTLI.cpy`: input struct `CTRTLIAI`, output redefine `CTRTLIAO`. Each field has `…L` (length/cursor S9(4) COMP), `…F`/`…A` (flag/attr X), `…I` (input) / `…C` `…P` `…H` `…V` (color/prog/highlight/validation attrs) `…O` (output).

| Field | Pos | Len | Read (I) | Written (O/attr) | Notes |
|---|---|---|---|---|---|
| `TRNNAME` | 1,7 | 4 | — | `TRNNAMEO` = 'CTLI' | header transid `// source: COTRTLIC.cbl:1300` |
| `TITLE01/02` | 1,21 / 2,21 | 40 | — | titles `// source: COTRTLIC.cbl:1298-1299` | |
| `CURDATE` | 1,71 | 8 | — | `CURDATEO` mm/dd/yy `// source: COTRTLIC.cbl:1309` | |
| `PGMNAME` | 2,7 | 8 | — | `PGMNAMEO`='COTRTLIC' `// source: COTRTLIC.cbl:1301` | |
| `CURTIME` | 2,71 | 8 | — | `CURTIMEO` hh:mm:ss `// source: COTRTLIC.cbl:1315` | |
| `PAGENO` | 4,76 | 3 | — | `PAGENOO` = `WS-CA-SCREEN-NUM` `// source: COTRTLIC.cbl:1318` | |
| `TRTYPE` | 6,44 | 2 | `TRTYPEI` → `WS-IN-TYPE-CD` | `TRTYPEO` echo + attrs | Type filter; `FSET,IC,UNPROT` `// source: COTRTLI.bms:89-93; COTRTLIC.cbl:937` |
| `TRDESC` | 8,25 | 50 | `TRDESCI` → `WS-IN-TYPE-DESC` | `TRDESCO` echo + attrs | Description filter; `FSET,UNPROT` `// source: COTRTLI.bms:101-105; COTRTLIC.cbl:938` |
| `TRTSEL1..7` | 12..18,6 | 1 | `TRTSELI(n)` → `WS-EDIT-SELECT(n)` | `TRTSELO(n)`, attrs `TRTSELA/C/L` | per-row action D/U `// source: COTRTLIC.cbl:941, 1428` |
| `TRTTYP1..7` | 12..18,17 | 2 | `TRTTYPI(n)` → `WS-ROW-TR-CODE-IN(n)` | `TRTTYPO(n)` = type code, attrs | row type code `// source: COTRTLIC.cbl:942, 1402` |
| `TRTYPD1..7` | 12..18,25 | 50 | `TRTYPDI(n)` → `WS-ROW-TR-DESC-IN(n)` (TRIM unless '*'/spaces) | `TRTYPDO(n)` = description, attrs | row description (editable when updating) `// source: COTRTLIC.cbl:945-950, 1404-1424` |
| `TRTSELA/TRTTYPA/TRTDSCA` | 19,* | 1/2/50 | — | (spare/"all" row, attrs only) | `// source: COTRTLI.bms:280-298` |
| `INFOMSG` | 21,19 | 45 | — | `INFOMSGO` centered info msg + `INFOMSGC` color `// source: COTRTLIC.cbl:1321, 1577` | |
| `ERRMSG` | 23,1 | 78 (cpy) / 78 | — | `ERRMSGO` = `WS-RETURN-MSG` `// source: COTRTLIC.cbl:1557` | BMS LEN=78 |
| `BUTNF02/03/07/08/10` | 24,* | label | — | static labels | F-key legend `// source: COTRTLI.bms:312-336` |

The header `…F` / `…A` attribute bytes use IBM copybook constants `DFHBMPRF` (protect), `DFHBMPRO` (autoskip), `DFHBMFSE` (unprotect+FSET+MDT), `DFHBMASF` (autoskip+FSET), `DFHBMDAR` (dark), `DFHRED/DFHBLUE/DFHNEUTR/DFHTURQ` (colors), `-1` in `…L` = place cursor here. `// source: COTRTLIC.cbl:1322, 1337-1371, 1447-1495`

---

## 6. ARITHMETIC / COMPUTE INVENTORY

All counters are `S9(4) COMP-3` (packed); subscripts `S9(4) COMP` (binary). No money math.
- `COMPUTE WS-ACTIONS-REQUESTED = 7 - WS-NO-ACTIONS-SELECTED` — no truncation possible (range 0..7). `// source: COTRTLIC.cbl:1003-1006`
- `COMPUTE WS-VALID-ACTIONS-SELECTED = WS-DELETES-REQUESTED + WS-UPDATES-REQUESTED`. `// source: COTRTLIC.cbl:1009-1012`
- `ADD 1 TO WS-ROW-NUMBER` / `SUBTRACT 1 FROM WS-ROW-NUMBER` (page row counters, S9(4) COMP). `// source: COTRTLIC.cbl:1636, 1769`
- `ADD +1 TO WS-CA-SCREEN-NUM` / `SUBTRACT 1 FROM WS-CA-SCREEN-NUM` (page number, **9(1) unsigned** — wraps/abends past 9; treat as int, no wrap in .NET, follow COBOL no-bound behavior). `// source: COTRTLIC.cbl:770, 784, 1647`
- `COMPUTE WS-ROW-NUMBER = 7` (backward read start). `// source: COTRTLIC.cbl:1735-1737`
- `COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))` → always 50 (Faithful Bugs #3). `// source: COTRTLIC.cbl:1843-1844`
- Message centering: `WS-STRING-LEN = LENGTH(TRIM(WS-INFO-MSG))`; `WS-STRING-MID = (LENGTH(WS-INFO-MSG) - WS-STRING-LEN) / 2 + 1` — **integer division truncates toward zero**; reproduce exactly (off-by-one favoring left for odd remainders). `// source: COTRTLIC.cbl:1562-1568`
- `COMPUTE WS-DSNTIAC-ERR-CD = RETURN-CODE` (DSNTIAC return code → 2-digit). `// source: CSDB2RPY.cpy:64`

---

## 7. VALIDATION RULES

1. **Type-Code filter** (`WS-IN-TYPE-CD`, 2 chars): blank/low-values/zeros → treated as "no type filter" (sets `WS-TYPE-CD-FILTER = 00`, BLANK flag). If non-blank and **not numeric** → INPUT-ERROR + message *"TYPE CODE FILTER,IF SUPPLIED MUST BE A 2 DIGIT NUMBER"*, protect select rows. If numeric → valid, used as exact `TR_TYPE = filter`. `// source: COTRTLIC.cbl:1101-1122`
2. **Description filter** (`WS-IN-TYPE-DESC`, 50 chars): blank/low-values → "no desc filter". Else wrapped as `%TRIM(value)%` for `LIKE`. `// source: COTRTLIC.cbl:1147-1163`
3. **Cross-edit (filters yield rows):** when a filter is valid, COUNT the matching rows; if 0 → INPUT-ERROR, mark the offending filter(s) NOT-OK, message *"No Records found for these filter conditions"*. `// source: COTRTLIC.cbl:1239-1267`
4. **Row action char** (`TRTSELn`): must be `D`, `U`, blank, or low-values. Any other char → INPUT-ERROR, row error flag, message *"Action code selected is invalid"*. `// source: COTRTLIC.cbl:1034-1038`
5. **Only one action per page:** if >1 row carries an action → INPUT-ERROR, message *"Please select only 1 action"*, all selected rows flagged. `// source: COTRTLIC.cbl:1024-1026, 1049-1052`
6. **Update description required + alphanumeric:** for a `U` row, the new description must not be blank → *"Transaction Desc must be supplied."*; must contain only letters/digits/spaces → *"Transaction Desc can have numbers or alphabets only."*. `// source: COTRTLIC.cbl:1083-1089, 1186-1231`
7. **No-change guard on update:** if the edited description equals the stored one (case-insensitive, trimmed, same trimmed length) → message *"No change detected with respect to database values."* and the row is not flagged for edit. `// source: COTRTLIC.cbl:1064-1076`

---

## 8. EXACT LITERAL MESSAGES

Info-line messages (`WS-INFO-MSG`, centered into `INFOMSGO`): `// source: COTRTLIC.cbl:237-248`
- `'Type U to update, D to delete any record'` (`WS-INFORM-REC-ACTIONS`)
- `'Delete HIGHLIGHTED row ? Press F10 to confirm'` (`WS-INFORM-DELETE`)
- `'Update HIGHLIGHTED row. Press F10 to save'` (`WS-INFORM-UPDATE`)
- `'HIGHLIGHTED row deleted.Hit Enter to continue'` (`WS-INFORM-DELETE-SUCCESS`) — note: no space after the period.
- `'HIGHLIGHTED row was updated'` (`WS-INFORM-UPDATE-SUCCESS`)

Return/error-line messages (`WS-RETURN-MSG` → `ERRMSGO`): `// source: COTRTLIC.cbl:250-262`
- `'PF03 pressed. Exiting'` (`WS-EXIT-MESSAGE`)
- `'No records found for this search condition.'` (`WS-MESG-NO-RECORDS-FOUND`)
- `'No more pages for these search conditions'` (`WS-MESG-NO-MORE-RECORDS`)
- `'Please select only 1 action'` (`WS-MESG-MORE-THAN-1-ACTION`)
- `'Action code selected is invalid'` (`WS-MESG-INVALID-ACTION-CODE`)
- `'No change detected with respect to database values.'` (`WS-MESG-NO-CHANGES-DETECTED`)

Inline literal messages (MOVEd directly to `WS-RETURN-MSG`):
- `'TYPE CODE FILTER,IF SUPPLIED MUST BE A 2 DIGIT NUMBER'` `// source: COTRTLIC.cbl:1115-1116`
- `'No Records found for these filter conditions'` `// source: COTRTLIC.cbl:1263-1264`
- `'No previous pages to display'` `// source: COTRTLIC.cbl:1534`
- `'No more pages to display'` `// source: COTRTLIC.cbl:1539`
- `'<var> must be supplied.'` (built via STRING; here var = `Transaction Desc`) `// source: COTRTLIC.cbl:1197-1198`
- `'<var> can have numbers or alphabets only.'` `// source: COTRTLIC.cbl:1224-1225`

Db2 action prefixes (fed to `9999-FORMAT-DB2-MESSAGE`):
- `'C-TR-TYPE-FORWARD fetch'` / `'C-TR-TYPE-FORWARD close'` / `'C-TR-TYPE-FORWARD Open'` `// source: COTRTLIC.cbl:1686, 1712, 1958, 1986`
- `'Error on fetch Cursor C-TR-TYPE-BACKWARD'` / `'C-TR-TYPE-BACKWARD Open'` / `'C-TR-TYPE-BACKWARD close'` `// source: COTRTLIC.cbl:1784, 2013, 2042`
- `'Error reading TRANSACTION_TYPE table '` `// source: COTRTLIC.cbl:1826`
- `'Record not found. Deleted by others ? '` `// source: COTRTLIC.cbl:1864`
- `'Deadlock. Someone else updating ?'` `// source: COTRTLIC.cbl:1874`
- `'Update failed with'` `// source: COTRTLIC.cbl:1883`
- `'Please delete associated child records first:'` `// source: COTRTLIC.cbl:1919`
- `'Delete failed with message:'` `// source: COTRTLIC.cbl:1929`
- `'Db2 access failure. '` `// source: CSDB2RPY.cpy:40`

---

## 9. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **`CA-DELETE-SUCCEEDED` / `CA-UPDATE-SUCCEEDED` collide with the "not-requested" value.** Both `CA-DELETE-NOT-REQUESTED` and `CA-DELETE-SUCCEEDED` are `VALUE LOW-VALUES` (same for update). So `IF CA-DELETE-SUCCEEDED` is true whenever the flag is low-values, including the "never requested" state. The program compensates by checking `WS-DELETES-REQUESTED > 0` in the dispatch before calling `9300`, but the 88-name overlap is a latent bug. Reproduce the literal `VALUE LOW-VALUES` semantics. `// source: COTRTLIC.cbl:411-418, 817, 860`
2. **Duplicate dead `WHEN` in the main EVALUATE.** Two identical `WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE` clauses appear back-to-back; the first has no body (immediately followed by comment lines then the second), so it falls through to the second's body. Keep both branches collapsing to the same forward-read behavior. `// source: COTRTLIC.cbl:721-734`
3. **Update description length is always 50, never the trimmed length.** `COMPUTE DCL-TR-DESCRIPTION-LEN = FUNCTION LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))` uses the **fixed PIC X(50)** length of the array element, not `LENGTH(TRIM(...))`. The VARCHAR length sent to Db2 is therefore 50 even though `DCL-TR-DESCRIPTION-TEXT` got the *trimmed* text (right-padded with spaces / low-values). Net effect with Db2: trailing spaces stored. Preserve exactly: store the trimmed text right-padded to 50, persisted length 50. `// source: COTRTLIC.cbl:1841-1844`
4. **`1210-EDIT-ARRAY` reads `…FILTER-CHANGED` flags before they are set.** `1200-EDIT-INPUTS` runs `1210-EDIT-ARRAY` *first*, but the `FLG-TYPEFILTER-CHANGED-YES`/`FLG-DESCFILTER-CHANGED-YES` flags are only assigned later by `1220`/`1230`. So the "filter changed → discard selections" branch in 1210 keys off **stale** flag values (whatever survived the entry `INITIALIZE`). Reproduce the call order (1210, 1230, 1220, 1290) and the stale read. `// source: COTRTLIC.cbl:965-975, 991-994`
5. **Backward read treats `+100` (end of cursor) as a hard Db2 error.** `8100-READ-BACKWARDS` has no `WHEN SQLCODE = +100`; end-of-data falls into `WHEN OTHER`, which `SET WS-DB2-ERROR` and formats 'Error on fetch Cursor C-TR-TYPE-BACKWARD'. Normally the loop fills exactly 7 rows and exits at `WS-ROW-NUMBER = 0` before hitting +100, but a short backward page surfaces a spurious error. Keep this behavior. `// source: COTRTLIC.cbl:1761-1790`
6. **`2400-SETUP-SCREEN-ATTRS` writes attribute bytes into the *input* map (`CTRTLIAI`).** Several MOVEs target `…A OF CTRTLIAI` (e.g. `TRTYPEA OF CTRTLIAI`, `TRDESCA OF CTRTLIAI`) rather than `CTRTLIAO`. Because `CTRTLIAO REDEFINES CTRTLIAI`, the bytes overlap and the effect is achieved, but it is written against the inbound structure. Preserve the resulting attribute outcome. `// source: COTRTLIC.cbl:1448, 1453, 1458, 1464, 1469, 1471, 1479, 1484, 1495`
7. **Unused `IN(...)` delete-filter literal.** `WS-TYPE-CD-DELETE-FILTER` builds a `('xx','xx',...)` quoted list (for a bulk delete `IN` clause) but the procedure division never references it. Do not implement; note for completeness. `// source: COTRTLIC.cbl:279-301`
8. **`WS-MESG-NO-RECORDS-FOUND` text differs from the filter-empty text.** End-of-file with empty first page sets *"No records found for this search condition."* (period, "condition" singular), while the cross-edit COUNT=0 path uses *"No Records found for these filter conditions"* (no period, plural). Keep both distinct strings. `// source: COTRTLIC.cbl:253-254, 1263-1264`

---

## 10. PORT NOTES (relational translation plan)

- **Table:** map `CARDDEMO.TRANSACTION_TYPE` to the ARCHITECTURE.md optional-module table **TRANSACTION_TYPE** (`TR_TYPE CHAR(2)` PK, `TR_DESCRIPTION VARCHAR(50)`), in `src/CardDemo.Db2`'s EF Core context. The base-app `TRAN_TYPE` table (CVTRA03Y) is a *different* file (PK `tran_type X2` + `tran_type_desc X50`); keep them distinct — this program uses the **DB2** table only.
- **Cursors → ordered queries.** `C-TR-TYPE-FORWARD` = `WHERE TR_TYPE >= @start [AND TR_TYPE=@type] [AND TR_DESCRIPTION LIKE @likeTrimmed] ORDER BY TR_TYPE` (ordinal). `C-TR-TYPE-BACKWARD` = same predicate with `TR_TYPE < @start ORDER BY TR_TYPE DESC`. Implement as a forward-only enumerator the handler pulls 7 (+1 look-ahead) rows from, matching `8000`/`8100`. The flag-toggle predicate `(:flag='1' AND col=:v) OR (:flag<>'1')` collapses in C# to: include the column filter only when the flag is `'1'`.
- **LIKE semantics.** COBOL builds `%TRIM(value)%` then the SQL does `LIKE TRIM(:filter)`. In SQLite, `LIKE` is case-insensitive for ASCII by default — Db2 `LIKE` is case-sensitive. **Pin the case behavior with a test**; if parity with Db2 matters, force a case-sensitive `LIKE` (`PRAGMA case_sensitive_like = ON` or `GLOB`)-equivalent comparison. Note `%`/`_` in the user's text are treated as wildcards (no escaping) — faithful.
- **Numeric type code.** `TR_TYPE` is `CHAR(2)` but the type-code filter is validated as a 2-digit number (`WS-IN-TYPE-CD IS NOT NUMERIC`). Store/compare as the 2-char string; the "numeric" check is on the *filter input*, not the column.
- **VARCHAR length bug.** Persist the description as the trimmed text right-padded to 50 chars (length 50) to mirror Faithful Bug #3. The domain string column should hold the 50-wide value; if EF trims trailing spaces, add an explicit pad on write.
- **Pseudo-conversational state.** Serialize `WS-THIS-PROGCOMMAREA` (filters + 7-row page buffer + paging vars + delete/update flags) into the COMMAREA store of `src/CardDemo.Online`. The split/concat reference-mod MOVEs become two serialized segments (CARDDEMO-COMMAREA prefix + program tail).
- **INITIALIZE / low-values.** `INITIALIZE` sets numerics to 0 and alphas to spaces; the many `VALUE LOW-VALUES` 88s mean "unset" is binary zero, distinct from spaces. Model the page-buffer "row present?" test (`WS-CA-EACH-ROW-OUT(I) = LOW-VALUES`) as a null/empty-slot flag, not space-comparison.
- **REDEFINES / OCCURS.** The page buffers (`WS-ALL-ROWS-IN` / `WS-CA-ALL-ROWS-OUT` redefined as `OCCURS 7`) and `WS-EDIT-SELECT-FLAGS` (X7 redefined `OCCURS 7`) become fixed-length 7-element arrays of `(code, desc)` / action chars.
- **INSPECT TALLYING / CONVERTING.** The action tally (`FOR ALL 'D'/'U'/SPACES/LOW-VALUES`) → count chars in the 7-element select array. The alphanumeric `CONVERTING` (blank out A-Za-z0-9) → a regex/char-class check that the field contains only letters, digits, spaces.
- **Edited PIC.** `WS-DISP-SQLCODE PIC ----9` (CSDB2RWY) is a 5-char leading-sign-suppress edited field used only inside the Db2 error string; format with the runtime `CobolEditedNumeric` helper.
- **Db2 error path.** `9998-PRIMING-QUERY` and `9999-FORMAT-DB2-MESSAGE` (DSNTIAC) are mainframe-Db2 plumbing; in-proc SQLite the priming probe always succeeds. Keep a thin equivalent so the `WS-DB2-ERROR → SEND-LONG-TEXT → COMMON-RETURN` path stays reachable for characterization, but most error branches are effectively dead under SQLite.
- **SYNCPOINT.** Map `EXEC CICS SYNCPOINT` (after a successful UPDATE/DELETE and before PF3 XCTL) to a transaction commit in the data layer.
- **No money / no batch.** Pure online maintenance; no decimal arithmetic, no file serialization to golden fixtures. Verification is online screen-parity (AID/field flows + post-turn COMMAREA + next TRANSID/XCTL) per ARCHITECTURE.md §Verification.4.

---

## 11. OPEN QUESTIONS / RISKS

1. **SQLite `LIKE` case-sensitivity vs Db2** — see Port Notes; needs a pinning test to lock the chosen behavior.
2. **`TR_TYPE` ordinal vs EBCDIC collation** — the keys are 2 digits in practice; ordinal ASCII compare should match Db2's order for the data CardDemo ships, but pin it.
3. **FK `-532` on delete** — relies on a child table (`TRANSACTION_TYPE_CATEGORY` / `TRNTYCAT`) FK existing. Under SQLite, enforce the FK (or simulate the child-row check) so the *"Please delete associated child records first:"* path is reachable; otherwise that branch is dead. `// source: COTRTLIC.cbl:1914-1923`
4. **`WS-DELETE-STATUS` / `WS-UPDATE-STATUS` vs `CA-*-SUCCEEDED` overlap (Faithful Bug #1)** — confirm the .NET model keeps a separate explicit "operation result" boolean so the screen ("row deleted"/"row updated") renders correctly despite the low-values 88 collision.
5. **F10 demotion-to-ENTER timing** — the change-detection flags (`FLG-*FILTER-CHANGED`, `FLG-ROW-SELECTION-CHANGED`) are computed in `1200-EDIT-INPUTS` which runs only when re-entered from self; verify the demotion gate at MAIN step 10 sees freshly-computed flags in all entry paths.
