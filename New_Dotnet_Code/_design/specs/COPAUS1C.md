# PORT SPEC — COPAUS1C (Pending Authorization **Detail** View + Fraud Mark/Remove, ONLINE CICS/IMS/BMS)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl`
> Module: `app-authorization-ims-db2-mq` (CardDemo Authorization extension — IMS + DB2 + MQ).
> Program kind: **ONLINE CICS, pseudo-conversational, BMS map, IMS DL/I (EXEC DLI), LINK to a DB2 subprogram.**
> Target: `src/CardDemo.Online/COPAUS1C.cs` (transaction handler) + `src/CardDemo.Ims` (PAUT_SUMMARY/PAUT_DETAIL reads + detail REPL) + generated BMS screen model for mapset `COPAU01`, over the relational SQLite schema in `_design/ARCHITECTURE.md` and the IMS re-host in `_design/specs/optional/IMS_SCHEMA.md`.
> All line citations below refer to `COPAUS1C.cbl` unless otherwise noted.
> Cross-refs: `_design/specs/optional/IMS_SCHEMA.md` (PAUT_SUMMARY/PAUT_DETAIL relational schema + DL/I→SQL §3.1/§3.3/§3.5), `_design/specs/COPAUA0C.md` (the MQ processor that *writes* what this screen reads), `_design/specs/COPAUS0C.md` (the **summary** screen `CPVS` that XCTLs here), and the fraud DB2 subprogram **COPAUS2C** (`cbl/COPAUS2C.cbl`, table `AUTHFRDS`).

---

## 1. Purpose & Invocation

**Purpose.** COPAUS1C is the **Detail View of an Authorization Message**. Given an account id (`CDEMO-ACCT-ID`) and a selected 8-byte authorization key (`CDEMO-CPVD-PAU-SELECTED`) passed in the COMMAREA from the summary screen, it reads the IMS Pending-Authorization **summary** root (`PAUTSUM0`) and the keyed **detail** child (`PAUTDTL1`), formats all detail fields onto BMS map `COPAU1A` (card number, auth date/time, approved amount, approve/decline response + decoded decline-reason text, processing/POS/source/MCC, card expiry, auth type, tran id, match status, fraud status, merchant name/id/city/state/zip), and displays them. PF8 advances to the **next** authorization detail under the same account (forward GNP). PF5 toggles the **fraud** flag on the displayed detail: it LINKs the DB2 fraud subprogram **COPAUS2C** (which inserts/deletes a row in `AUTHFRDS`); on success it REPLs the IMS detail segment with the new fraud flag + report date, on failure it rolls back. PF3 returns to the summary screen (`COPAUS0C`). // source: COPAUS1C.cbl:1-6 (header — Type "CICS COBOL IMS BMS Program", Function "Detail View of Authorization Message"); 156-206 (MAIN-PARA dispatcher); 291-358 (POPULATE-AUTH-DETAILS)

**Invocation.** CICS transaction **`CPVD`** (`WS-CICS-TRANID = 'CPVD'`, line 36). Pseudo-conversational: each turn ends with `EXEC CICS RETURN TRANSID('CPVD') COMMAREA(CARDDEMO-COMMAREA)`. // source: COPAUS1C.cbl:36, 202-205
- **Entered via XCTL** from the summary screen `COPAUS0C` (TRANID `CPVS`) which sets `CDEMO-TO-PROGRAM='COPAUS1C'` and passes `CDEMO-ACCT-ID` + `CDEMO-CPVD-PAU-SELECTED` in the COMMAREA. (COPAUS0C is `WS-PGM-AUTH-SMRY='COPAUS0C'`, line 34.) // source: COPAUS1C.cbl:34,168,185
- **Returns via XCTL** to `COPAUS0C` on first-entry-with-empty-COMMAREA, on PF3, or via RETURN-TO-PREV-SCREEN. // source: COPAUS1C.cbl:165-169,184-186,360-370
- **LINK (synchronous call)** to **COPAUS2C** (`WS-PGM-AUTH-FRAUD='COPAUS2C'`, line 35) passing `WS-FRAUD-DATA` as COMMAREA, on PF5. // source: COPAUS1C.cbl:35,248-252

**External objects:**
| Object | Name (literal) | Role | Relational/runtime target |
|---|---|---|---|
| IMS DB | PSB `PSBPAUTB`, PCB `PAUT-PCB-NUM=+1`, DBD `DBPAUTP0`, segments `PAUTSUM0`(root)/`PAUTDTL1`(child) | SCHD / GU(root) / GNP(child, keyed) / GNP(next) / REPL(child) / TERM-via-syncpoint | `PAUT_SUMMARY` / `PAUT_DETAIL` (IMS_SCHEMA.md §2). // source: COPAUS1C.cbl:76-78, 439-468, 495-498, 525-528; PSBPAUTB.psb:17-20 |
| BMS map / mapset | map `COPAU1A`, mapset `COPAU01` | SEND/RECEIVE the detail screen | generated screen model `COPAU01.COPAU1A`. // source: COPAUS1C.cbl:382-383, 401-402; bms/COPAU01.bms:19,26 |
| Called subprogram | `COPAUS2C` (DB2, table `AUTHFRDS`) | LINK with `WS-FRAUD-DATA` COMMAREA | injected fraud service (`AUTHFRDS` repo); see §4 MARK-AUTH-FRAUD. // source: COPAUS1C.cbl:35,248-252; COPAUS2C.cbl:73-86 |
| XCTL target | `COPAUS0C` | back to summary screen | online handler for `CPVS`. // source: COPAUS1C.cbl:34,367-370 |

> The PCB mask copybook `PAUTBPCB.CPY` and `IMSFUNCS.cpy` are **NOT** `COPY`d here — this program uses the **EXEC DLI** macro interface; status is read from **`DIBSTAT`** and the only PCB reference is the symbolic `PCB(PAUT-PCB-NUM)` with `PAUT-PCB-NUM = +1`. // source: COPAUS1C.cbl:77-78, 439, 445

---

## 2. FILE / TABLE ACCESS TABLE

| COBOL access | Org / op | Key | Relational target | SQL mapping |
|---|---|---|---|---|
| `EXEC DLI GU SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) WHERE(ACCNTID = PA-ACCT-ID)` | IMS root **Get-Unique** | `PA-ACCT-ID` S9(11) COMP-3 ← `WS-ACCT-ID` ← `CDEMO-ACCT-ID` | `PAUT_SUMMARY` | `SELECT * FROM PAUT_SUMMARY WHERE ACCT_ID = @acctid LIMIT 1`. `'  '`/`FW`→`AUTHS-NOT-EOF`; `GE`/`GB`→`AUTHS-EOF`; other→error message + SEND. Establishes parentage for the following GNP. // source: COPAUS1C.cbl:439-462; IMS_SCHEMA.md §3.1 |
| `EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS) WHERE(PAUT9CTS = PA-AUTHORIZATION-KEY)` | IMS child **Get-Next-within-Parent, qualified by key** | `PA-AUTHORIZATION-KEY` X(8) ← `WS-AUTH-KEY` ← `CDEMO-CPVD-PAU-SELECTED` | `PAUT_DETAIL` | `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=@acctid AND AUTH_KEY >= @auth_key ORDER BY AUTH_KEY ASC LIMIT 1` (reposition-at-or-after, per IMS_SCHEMA.md §3.3 qualified-GNP). `'  '`/`FW`→loaded; `GE`/`GB`→`AUTHS-EOF`; other→error+SEND. **Also positions the child cursor** so a later unqualified GNP returns the *next* detail. // source: COPAUS1C.cbl:464-489; IMS_SCHEMA.md §3.3 |
| `EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS)` (unqualified) | IMS child **Get-Next-within-Parent** (advance) | (held position from the prior GNP/GU) | `PAUT_DETAIL` | `cursor.MoveNext()` over `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=@acctid ORDER BY AUTH_KEY ASC` positioned after the current key. `'  '`→loaded; `GE`/`GB`→`AUTHS-EOF`; other→error+SEND. Used by PF8 (PROCESS-PF8-KEY→READ-NEXT-AUTH-RECORD). // source: COPAUS1C.cbl:495-518; IMS_SCHEMA.md §3.3 |
| `EXEC DLI REPL SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS)` | IMS child **Replace** (held position) | (current detail) acct+auth_key | `PAUT_DETAIL` | `UPDATE PAUT_DETAIL SET AUTH_FRAUD=@f, FRAUD_RPT_DATE=@d, MATCH_STATUS=@m, …all data columns from io-area… WHERE ACCT_ID=@acctid AND AUTH_KEY=@auth_key` (must NOT change PK). `'  '`→syncpoint + success msg; other→ROLLBACK + error+SEND. // source: COPAUS1C.cbl:520-552; IMS_SCHEMA.md §3.5 |
| `EXEC DLI SCHD PSB((PSB-NAME)) NODHABEND` | IMS schedule PSB | — | (unit of work) | open connection / unit of work. `TC` ("scheduled more than once")→`TERM` then re-`SCHD`. // source: COPAUS1C.cbl:574-589; IMS_SCHEMA.md §3.8 |
| `EXEC CICS SYNCPOINT` | commit | — | (unit of work) | `COMMIT`. // source: COPAUS1C.cbl:557-560; IMS_SCHEMA.md §3.8 |
| `EXEC CICS SYNCPOINT ROLLBACK` | rollback | — | (unit of work) | `ROLLBACK`. // source: COPAUS1C.cbl:565-569; IMS_SCHEMA.md §3.8 |
| `EXEC CICS LINK PROGRAM('COPAUS2C') COMMAREA(WS-FRAUD-DATA) NOHANDLE` | synchronous subprogram call | — | `AUTHFRDS` (DB2, via COPAUS2C) | invoke fraud service; success/failure returned in `WS-FRD-UPDATE-STATUS`. // source: COPAUS1C.cbl:248-252 |

**Repository / status contract (per IMS_SCHEMA.md §3 + ARCHITECTURE.md §VSAM→SQL):**
- DL/I GU → `SELECT … LIMIT 1`; 1 row→`'  '`, 0 rows→`GE`.
- DL/I qualified GNP (reposition) → `SELECT … WHERE AUTH_KEY >= :key ORDER BY AUTH_KEY ASC LIMIT 1`.
- DL/I unqualified GNP → forward `cursor.MoveNext()`; exhausted→`GE`/`GB`.
- DL/I REPL → `UPDATE` non-key columns by PK; rows affected 1→`'  '`.
- IMS status is read from **`DIBSTAT`** (EXEC-DLI interface block); the `IMS-RETURN-CODE` 88-levels (`STATUS-OK='  '|'FW'`, `SEGMENT-NOT-FOUND='GE'`, `END-OF-DB='GB'`, etc.) ARE used here (unlike CBPAUP0C). // source: COPAUS1C.cbl:79-88,445-451

---

## 3. WORKING-STORAGE / record layouts (typed)

### 3.1 WS-VARIABLES (constants & scratch) // source: COPAUS1C.cbl:32-54
- `WS-PGM-AUTH-DTL` X(08) `'COPAUS1C'` (this program). // source: COPAUS1C.cbl:33
- `WS-PGM-AUTH-SMRY` X(08) `'COPAUS0C'` (XCTL-back target / summary). // source: COPAUS1C.cbl:34
- `WS-PGM-AUTH-FRAUD` X(08) `'COPAUS2C'` (LINK fraud subprogram). // source: COPAUS1C.cbl:35
- `WS-CICS-TRANID` X(04) `'CPVD'`. // source: COPAUS1C.cbl:36
- `WS-MESSAGE` X(80) — error/status line shown in `ERRMSGO`. // source: COPAUS1C.cbl:37,377
- `WS-ERR-FLG` X(01) `'N'`, 88 `ERR-FLG-ON`='Y'/`ERR-FLG-OFF`='N'. // source: COPAUS1C.cbl:38-40
- `WS-AUTHS-EOF` X(01) `'N'`, 88 `AUTHS-EOF`='Y'/`AUTHS-NOT-EOF`='N'. // source: COPAUS1C.cbl:41-43
- `WS-SEND-ERASE-FLG` X(01) `'Y'`, 88 `SEND-ERASE-YES`='Y'/`SEND-ERASE-NO`='N'. // source: COPAUS1C.cbl:44-46
- `WS-RESP-CD`/`WS-REAS-CD` S9(09) COMP — declared, **never referenced** in PROCEDURE DIVISION (dead). // source: COPAUS1C.cbl:47-48
- `WS-ACCT-ID` 9(11) — working account id (← `CDEMO-ACCT-ID`). // source: COPAUS1C.cbl:50
- `WS-AUTH-KEY` X(08) — working auth key (← `CDEMO-CPVD-PAU-SELECTED`). // source: COPAUS1C.cbl:51
- `WS-AUTH-AMT` **PIC -zzzzzzz9.99** (edited, signed float-suppress) — amount display field. // source: COPAUS1C.cbl:52
- `WS-AUTH-DATE` X(08) `'00/00/00'`; `WS-AUTH-TIME` X(08) `'00:00:00'` — formatted date/time scratch. // source: COPAUS1C.cbl:53-54

### 3.2 WS-TABLES — decline-reason lookup (SEARCH ALL) // source: COPAUS1C.cbl:56-73
- `WS-DECLINE-REASON-TABLE`: 10 hard-coded `X(20)` rows, each = 4-char code + 16-char description:
  `'0000APPROVED        '`, `'3100INVALID CARD    '`, `'4100INSUFFICNT FUND'`, `'4200CARD NOT ACTIVE '`, `'4300ACCOUNT CLOSED  '`, `'4400EXCED DAILY LMT '`, `'5100CARD FRAUD      '`, `'5200MERCHANT FRAUD  '`, `'5300LOST CARD       '`, `'9000UNKNOWN         '`. // source: COPAUS1C.cbl:57-67
- `WS-DECLINE-REASON-TAB REDEFINES … OCCURS 10 ASCENDING KEY IS DECL-CODE INDEXED BY WS-DECL-RSN-IDX`: `DECL-CODE X(4)` + `DECL-DESC X(16)`. Searched by `SEARCH ALL` (binary search; requires ascending `DECL-CODE` — the literals are in ascending order). // source: COPAUS1C.cbl:68-73, 319-328
  - **Reproduce verbatim:** exact 16-char descriptions including the truncated spellings `INSUFFICNT FUND`, `EXCED DAILY LMT` and the trailing-space padding. // source: COPAUS1C.cbl:60,63

### 3.3 WS-IMS-VARIABLES // source: COPAUS1C.cbl:75-91
- `PSB-NAME` X(8) `'PSBPAUTB'`. // source: COPAUS1C.cbl:76
- `PCB-OFFSET.PAUT-PCB-NUM` S9(4) COMP `+1` (DB PCB number used in every `PCB(PAUT-PCB-NUM)`). // source: COPAUS1C.cbl:77-78
- `IMS-RETURN-CODE` X(02) with 88s: `STATUS-OK`='  '|'FW', `SEGMENT-NOT-FOUND`='GE', `DUPLICATE-SEGMENT-FOUND`='II', `WRONG-PARENTAGE`='GP', `END-OF-DB`='GB', `DATABASE-UNAVAILABLE`='BA', `PSB-SCHEDULED-MORE-THAN-ONCE`='TC', `COULD-NOT-SCHEDULE-PSB`='TE', `RETRY-CONDITION`='BA'|'FH'|'TE'. (`STATUS-OK`, `SEGMENT-NOT-FOUND`, `END-OF-DB`, `PSB-SCHEDULED-MORE-THAN-ONCE` are USED; the rest are inert.) // source: COPAUS1C.cbl:79-88
- `WS-IMS-PSB-SCHD-FLG` X(1), 88 `IMS-PSB-SCHD`='Y'/`IMS-PSB-NOT-SCHD`='N'. **Note:** declared with no VALUE clause → initial content is undefined/spaces; `IMS-PSB-SCHD` is tested before being set in some paths (see §7 faithful bug #2). // source: COPAUS1C.cbl:89-91

### 3.4 WS-FRAUD-DATA — COMMAREA passed to COPAUS2C on LINK // source: COPAUS1C.cbl:93-104
- `WS-FRD-ACCT-ID` 9(11), `WS-FRD-CUST-ID` 9(9), `WS-FRAUD-AUTH-RECORD` X(200) (the 200-byte detail segment image), then `WS-FRAUD-STATUS-RECORD` = `WS-FRD-ACTION` X(01) (88 `WS-REPORT-FRAUD`='F'/`WS-REMOVE-FRAUD`='R'), `WS-FRD-UPDATE-STATUS` X(01) (88 `WS-FRD-UPDT-SUCCESS`='S'/`WS-FRD-UPDT-FAILED`='F'), `WS-FRD-ACT-MSG` X(50). Layout matches COPAUS2C's `DFHCOMMAREA`. // source: COPAUS1C.cbl:93-104; COPAUS2C.cbl:73-86

### 3.5 COMMAREA — COCOM01Y `CARDDEMO-COMMAREA` + CPVD extension // source: COPAUS1C.cbl:109-120; cpy/COCOM01Y.cpy:19-44
Base `CARDDEMO-COMMAREA` (general/customer/account/card/more info; key fields used here: `CDEMO-TO-PROGRAM`, `CDEMO-FROM-TRANID`, `CDEMO-FROM-PROGRAM`, `CDEMO-PGM-CONTEXT` (88 `CDEMO-PGM-ENTER`=0/`CDEMO-PGM-REENTER`=1), `CDEMO-ACCT-ID` 9(11), `CDEMO-CUST-ID` 9(9)). // source: cpy/COCOM01Y.cpy:20-44
**CPVD extension `CDEMO-CPVD-INFO`** (appended after COCOM01Y — these are the screen-state fields shared with COPAUS0C):
- `CDEMO-CPVD-PAU-SEL-FLG` X(01); `CDEMO-CPVD-PAU-SELECTED` X(08) (**the selected auth key** read here). // source: COPAUS1C.cbl:111-112
- `CDEMO-CPVD-PAUKEY-PREV-PG` X(08) OCCURS 20; `CDEMO-CPVD-PAUKEY-LAST` X(08); `CDEMO-CPVD-PAGE-NUM` S9(04) COMP; `CDEMO-CPVD-NEXT-PAGE-FLG` X(01) (88 `NEXT-PAGE-YES`/`NO`); `CDEMO-CPVD-AUTH-KEYS` X(08) OCCURS 5 — paging state owned by COPAUS0C, carried through unchanged. // source: COPAUS1C.cbl:113-119
- `CDEMO-CPVD-FRAUD-DATA` X(100) — cleared each turn (line 172); not otherwise used. // source: COPAUS1C.cbl:120,172

### 3.6 IMS segment io-areas (COPYd) // source: COPAUS1C.cbl:141-146
- `01 PENDING-AUTH-SUMMARY` ← `COPY CIPAUSMY` (root `PAUTSUM0`, 100 bytes) → `PAUT_SUMMARY`. Fields: `PA-ACCT-ID` S9(11) COMP-3, `PA-CUST-ID` 9(9), `PA-AUTH-STATUS` X(1), `PA-ACCOUNT-STATUS` X(2) OCCURS 5, `PA-CREDIT-LIMIT`/`PA-CASH-LIMIT`/`PA-CREDIT-BALANCE`/`PA-CASH-BALANCE` S9(9)V99 COMP-3, `PA-APPROVED-AUTH-CNT`/`PA-DECLINED-AUTH-CNT` S9(4) COMP, `PA-APPROVED-AUTH-AMT`/`PA-DECLINED-AUTH-AMT` S9(9)V99 COMP-3, FILLER X(34). (Only used here as the GU target to establish parentage; **no summary field is displayed**.) // source: cpy/CIPAUSMY.cpy:19-31
- `01 PENDING-AUTH-DETAILS` ← `COPY CIPAUDTY` (child `PAUTDTL1`, 200 bytes) → `PAUT_DETAIL`. Key fields and all fields used by the display:
  - `PA-AUTHORIZATION-KEY` group = `PA-AUTH-DATE-9C` S9(05) COMP-3 + `PA-AUTH-TIME-9C` S9(09) COMP-3 (8-byte child seq key `PAUT9CTS` / `AUTH_KEY`). // source: cpy/CIPAUDTY.cpy:19-21
  - `PA-AUTH-ORIG-DATE` X(06) (YYMMDD), `PA-AUTH-ORIG-TIME` X(06) (HHMMSS) — formatted to screen. // source: cpy/CIPAUDTY.cpy:22-23; COPAUS1C.cbl:297-306
  - `PA-CARD-NUM` X(16), `PA-AUTH-TYPE` X(04), `PA-CARD-EXPIRY-DATE` X(04). // source: cpy/CIPAUDTY.cpy:24-26
  - `PA-MESSAGE-SOURCE` X(06), `PA-AUTH-RESP-CODE` X(02) (88 `PA-AUTH-APPROVED`='00'), `PA-AUTH-RESP-REASON` X(04), `PA-PROCESSING-CODE` 9(06). // source: cpy/CIPAUDTY.cpy:28-33
  - `PA-APPROVED-AMT` S9(10)V99 COMP-3 (amount displayed). // source: cpy/CIPAUDTY.cpy:35
  - `PA-MERCHANT-CATAGORY-CODE` X(04) *(sic 'CATAGORY')*, `PA-POS-ENTRY-MODE` 9(02), `PA-MERCHANT-ID` X(15), `PA-MERCHANT-NAME` X(22), `PA-MERCHANT-CITY` X(13), `PA-MERCHANT-STATE` X(02), `PA-MERCHANT-ZIP` X(09), `PA-TRANSACTION-ID` X(15). // source: cpy/CIPAUDTY.cpy:36-44
  - `PA-MATCH-STATUS` X(01) (88 P/D/E/M), `PA-AUTH-FRAUD` X(01) (88 `PA-FRAUD-CONFIRMED`='F'/`PA-FRAUD-REMOVED`='R'), `PA-FRAUD-RPT-DATE` X(08), FILLER X(17). // source: cpy/CIPAUDTY.cpy:45-54

### 3.7 Other COPYs
- `COTTL01Y` (titles `CCDA-TITLE01`/`02`), `CSDAT01Y` (date/time work group `WS-DATE-TIME`: `WS-CURDATE-DATA`, `WS-CURDATE-MM-DD-YY` = `WS-CURDATE-MM`/`-DD`/`-YY`, `WS-CURTIME-HH-MM-SS` = `WS-CURTIME-HH`/`-MM`/`-SS`, etc.), `CSMSG01Y` (`CCDA-MSG-INVALID-KEY = 'Invalid key pressed. Please see below...         '`), `CSMSG02Y` (abend vars), `DFHAID` (PFKey constants), `DFHBMSCA` (attribute/color constants `DFHGREEN`/`DFHRED`). // source: COPAUS1C.cbl:122-149; cpy/CSDAT01Y.cpy:17-55; cpy/CSMSG01Y.cpy:20-21

### 3.8 LINKAGE
`01 DFHCOMMAREA` = `LK-COMMAREA X(01) OCCURS 1 TO 32767 DEPENDING ON EIBCALEN` — the raw inbound COMMAREA; the program copies `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. // source: COPAUS1C.cbl:151-154,171

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = a method)

**MAIN-PARA** (pseudo-conversational dispatcher) // source: COPAUS1C.cbl:156-206
1. `SET ERR-FLG-OFF`, `SET SEND-ERASE-YES`; `MOVE SPACES TO WS-MESSAGE, ERRMSGO`. // source: COPAUS1C.cbl:159-163
2. **IF `EIBCALEN = 0`** (no COMMAREA — cold start): `INITIALIZE CARDDEMO-COMMAREA`; `MOVE WS-PGM-AUTH-SMRY → CDEMO-TO-PROGRAM`; PERFORM RETURN-TO-PREV-SCREEN (XCTL to COPAUS0C). // source: COPAUS1C.cbl:165-169
3. **ELSE**: `MOVE DFHCOMMAREA(1:EIBCALEN) → CARDDEMO-COMMAREA`; `MOVE SPACES → CDEMO-CPVD-FRAUD-DATA`. // source: COPAUS1C.cbl:171-172
   - **IF NOT `CDEMO-PGM-REENTER`** (first display of this screen): `SET CDEMO-PGM-REENTER`; PERFORM PROCESS-ENTER-KEY; PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:173-177
   - **ELSE** (a key was pressed): PERFORM RECEIVE-AUTHVIEW-SCREEN; **EVALUATE EIBAID**: // source: COPAUS1C.cbl:178-198
     - `DFHENTER` → PROCESS-ENTER-KEY; SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:181-183
     - `DFHPF3` → `MOVE WS-PGM-AUTH-SMRY → CDEMO-TO-PROGRAM`; RETURN-TO-PREV-SCREEN (XCTL back). // source: COPAUS1C.cbl:184-186
     - `DFHPF5` → MARK-AUTH-FRAUD; SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:187-189
     - `DFHPF8` → PROCESS-PF8-KEY; SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:190-192
     - `OTHER` → PROCESS-ENTER-KEY; `MOVE CCDA-MSG-INVALID-KEY → WS-MESSAGE`; SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:193-197
4. `EXEC CICS RETURN TRANSID('CPVD') COMMAREA(CARDDEMO-COMMAREA)`. // source: COPAUS1C.cbl:202-205

**PROCESS-ENTER-KEY** // source: COPAUS1C.cbl:208-228
1. `MOVE LOW-VALUES → COPAU1AO` (clear the output map). // source: COPAUS1C.cbl:210
2. IF `CDEMO-ACCT-ID IS NUMERIC` AND `CDEMO-CPVD-PAU-SELECTED NOT = SPACES AND LOW-VALUES`:
   - `MOVE CDEMO-ACCT-ID → WS-ACCT-ID`; `MOVE CDEMO-CPVD-PAU-SELECTED → WS-AUTH-KEY`; PERFORM READ-AUTH-RECORD. // source: COPAUS1C.cbl:211-216
   - IF `IMS-PSB-SCHD`: `SET IMS-PSB-NOT-SCHD`; PERFORM TAKE-SYNCPOINT. // source: COPAUS1C.cbl:218-221
3. ELSE: `SET ERR-FLG-ON`. // source: COPAUS1C.cbl:223-224
4. PERFORM POPULATE-AUTH-DETAILS. // source: COPAUS1C.cbl:227

**MARK-AUTH-FRAUD** (PF5 — toggle fraud) // source: COPAUS1C.cbl:230-266
1. `MOVE CDEMO-ACCT-ID → WS-ACCT-ID`; `MOVE CDEMO-CPVD-PAU-SELECTED → WS-AUTH-KEY`; PERFORM READ-AUTH-RECORD (re-read the current detail). // source: COPAUS1C.cbl:231-234
2. **Toggle:** IF `PA-FRAUD-CONFIRMED` (currently 'F') → `SET PA-FRAUD-REMOVED` ('R') + `SET WS-REMOVE-FRAUD` ('R'); ELSE → `SET PA-FRAUD-CONFIRMED` ('F') + `SET WS-REPORT-FRAUD` ('F'). // source: COPAUS1C.cbl:236-242
3. `MOVE PENDING-AUTH-DETAILS → WS-FRAUD-AUTH-RECORD`; `MOVE CDEMO-ACCT-ID → WS-FRD-ACCT-ID`; `MOVE CDEMO-CUST-ID → WS-FRD-CUST-ID`. // source: COPAUS1C.cbl:244-246
4. `EXEC CICS LINK PROGRAM('COPAUS2C') COMMAREA(WS-FRAUD-DATA) NOHANDLE`. // source: COPAUS1C.cbl:248-252
5. IF `EIBRESP = DFHRESP(NORMAL)`:
   - IF `WS-FRD-UPDT-SUCCESS` → PERFORM UPDATE-AUTH-DETAILS (REPL the IMS detail). // source: COPAUS1C.cbl:253-255
   - ELSE → `MOVE WS-FRD-ACT-MSG → WS-MESSAGE`; PERFORM ROLL-BACK. // source: COPAUS1C.cbl:256-258
6. ELSE (LINK failed) → PERFORM ROLL-BACK. // source: COPAUS1C.cbl:260-262
7. `MOVE PA-AUTHORIZATION-KEY → CDEMO-CPVD-PAU-SELECTED` (refresh selected key); PERFORM POPULATE-AUTH-DETAILS. // source: COPAUS1C.cbl:264-265

**PROCESS-PF8-KEY** (advance to next auth) // source: COPAUS1C.cbl:268-289
1. `MOVE CDEMO-ACCT-ID → WS-ACCT-ID`; `MOVE CDEMO-CPVD-PAU-SELECTED → WS-AUTH-KEY`. // source: COPAUS1C.cbl:270-271
2. PERFORM READ-AUTH-RECORD (re-reads summary + keyed detail to reposition the cursor at the current key). // source: COPAUS1C.cbl:273
3. PERFORM READ-NEXT-AUTH-RECORD (unqualified GNP → next detail). // source: COPAUS1C.cbl:274
4. IF `IMS-PSB-SCHD`: `SET IMS-PSB-NOT-SCHD`; PERFORM TAKE-SYNCPOINT. // source: COPAUS1C.cbl:276-279
5. IF `AUTHS-EOF`: `SET SEND-ERASE-NO`; `MOVE 'Already at the last Authorization...' → WS-MESSAGE`. // source: COPAUS1C.cbl:281-284
6. ELSE: `MOVE PA-AUTHORIZATION-KEY → CDEMO-CPVD-PAU-SELECTED`; PERFORM POPULATE-AUTH-DETAILS. // source: COPAUS1C.cbl:285-288

**POPULATE-AUTH-DETAILS** (format detail → map output) // source: COPAUS1C.cbl:291-358
Only executes its body **IF `ERR-FLG-OFF`** (otherwise leaves the map cleared). // source: COPAUS1C.cbl:294
1. `MOVE PA-CARD-NUM → CARDNUMO`. // source: COPAUS1C.cbl:295
2. **Auth date** (MM/DD/YY): split `PA-AUTH-ORIG-DATE` (YYMMDD) → `WS-CURDATE-YY` (1:2), `WS-CURDATE-MM` (3:2), `WS-CURDATE-DD` (5:2); `MOVE WS-CURDATE-MM-DD-YY → WS-AUTH-DATE → AUTHDTO`. // source: COPAUS1C.cbl:297-301
3. **Auth time** (HH:MM:SS): `PA-AUTH-ORIG-TIME` (HHMMSS) bytes 1:2/3:2/5:2 → `WS-AUTH-TIME`(1:2)/(4:2)/(7:2) (the ':' separators at positions 3,6 stay from the `'00:00:00'` init); `MOVE WS-AUTH-TIME → AUTHTMO`. // source: COPAUS1C.cbl:303-306
4. **Amount:** `MOVE PA-APPROVED-AMT → WS-AUTH-AMT` (edit into `-zzzzzzz9.99`); `MOVE WS-AUTH-AMT → AUTHAMTO`. // source: COPAUS1C.cbl:308-309
5. **Response indicator:** IF `PA-AUTH-RESP-CODE = '00'` → `MOVE 'A' → AUTHRSPO`, `MOVE DFHGREEN → AUTHRSPC` (green); ELSE → `MOVE 'D' → AUTHRSPO`, `MOVE DFHRED → AUTHRSPC` (red). // source: COPAUS1C.cbl:311-317
6. **Decline-reason text (SEARCH ALL):** binary-search `WS-DECLINE-REASON-TAB` on `DECL-CODE = PA-AUTH-RESP-REASON`:
   - AT END → `MOVE '9999' → AUTHRSNO`, `MOVE '-' → AUTHRSNO(5:1)`, `MOVE 'ERROR' → AUTHRSNO(6:)`. // source: COPAUS1C.cbl:319-323
   - WHEN found → `MOVE PA-AUTH-RESP-REASON → AUTHRSNO`, `MOVE '-' → AUTHRSNO(5:1)`, `MOVE DECL-DESC(idx) → AUTHRSNO(6:)`. // source: COPAUS1C.cbl:324-327
   - Result format: `"<4-char-code>-<16-char-desc>"` in the 20-char `AUTHRSNO`. // source: COPAUS1C.cbl:325-327; cpy-bms/COPAU01.cpy:248
7. `MOVE PA-PROCESSING-CODE → AUTHCDO`; `MOVE PA-POS-ENTRY-MODE → POSEMDO`; `MOVE PA-MESSAGE-SOURCE → AUTHSRCO`; `MOVE PA-MERCHANT-CATAGORY-CODE → MCCCDO`. // source: COPAUS1C.cbl:331-334
8. **Card expiry (MM/YY):** `PA-CARD-EXPIRY-DATE` (assumed YYMM 4-char) → `CRDEXPO(1:2)` = bytes 1:2, `CRDEXPO(3:1)='/'`, `CRDEXPO(4:2)` = bytes 3:2. // source: COPAUS1C.cbl:336-338
9. `MOVE PA-AUTH-TYPE → AUTHTYPO`; `MOVE PA-TRANSACTION-ID → TRNIDO`; `MOVE PA-MATCH-STATUS → AUTHMTCO`. // source: COPAUS1C.cbl:340-342
10. **Fraud status:** IF `PA-FRAUD-CONFIRMED OR PA-FRAUD-REMOVED` → `AUTHFRDO(1:1)=PA-AUTH-FRAUD` ('F'/'R'), `AUTHFRDO(2:1)='-'`, `AUTHFRDO(3:)=PA-FRAUD-RPT-DATE`; ELSE → `MOVE '-' → AUTHFRDO`. // source: COPAUS1C.cbl:344-350
11. `MOVE PA-MERCHANT-NAME → MERNAMEO`; `MOVE PA-MERCHANT-ID → MERIDO`; `MOVE PA-MERCHANT-CITY → MERCITYO`; `MOVE PA-MERCHANT-STATE → MERSTO`; `MOVE PA-MERCHANT-ZIP → MERZIPO`. // source: COPAUS1C.cbl:352-356

**RETURN-TO-PREV-SCREEN** (XCTL out) // source: COPAUS1C.cbl:360-370
1. `MOVE WS-CICS-TRANID → CDEMO-FROM-TRANID`; `MOVE WS-PGM-AUTH-DTL → CDEMO-FROM-PROGRAM`; `MOVE ZEROS → CDEMO-PGM-CONTEXT`; `SET CDEMO-PGM-ENTER`. // source: COPAUS1C.cbl:362-365
2. `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. // source: COPAUS1C.cbl:367-370

**SEND-AUTHVIEW-SCREEN** // source: COPAUS1C.cbl:373-396
1. PERFORM POPULATE-HEADER-INFO. // source: COPAUS1C.cbl:375
2. `MOVE WS-MESSAGE → ERRMSGO`; `MOVE -1 → CARDNUML` (cursor to CARDNUM via −1 length). // source: COPAUS1C.cbl:377-378
3. IF `SEND-ERASE-YES` → `EXEC CICS SEND MAP('COPAU1A') MAPSET('COPAU01') FROM(COPAU1AO) ERASE CURSOR`; ELSE → same SEND **without ERASE** (CURSOR only). // source: COPAUS1C.cbl:380-395

**RECEIVE-AUTHVIEW-SCREEN** // source: COPAUS1C.cbl:398-406
1. `EXEC CICS RECEIVE MAP('COPAU1A') MAPSET('COPAU01') INTO(COPAU1AI) NOHANDLE`. // source: COPAUS1C.cbl:400-405

**POPULATE-HEADER-INFO** // source: COPAUS1C.cbl:409-429
1. `MOVE FUNCTION CURRENT-DATE → WS-CURDATE-DATA`. // source: COPAUS1C.cbl:411
2. `MOVE CCDA-TITLE01 → TITLE01O`; `MOVE CCDA-TITLE02 → TITLE02O`; `MOVE WS-CICS-TRANID → TRNNAMEO`; `MOVE WS-PGM-AUTH-DTL → PGMNAMEO`. // source: COPAUS1C.cbl:413-416
3. Build current date `MM/DD/YY`: `WS-CURDATE-MONTH → WS-CURDATE-MM`, `WS-CURDATE-DAY → WS-CURDATE-DD`, `WS-CURDATE-YEAR(3:2) → WS-CURDATE-YY`; `MOVE WS-CURDATE-MM-DD-YY → CURDATEO`. // source: COPAUS1C.cbl:418-422
4. Build current time `HH:MM:SS`: `WS-CURTIME-HOURS → WS-CURTIME-HH`, `WS-CURTIME-MINUTE → WS-CURTIME-MM`, `WS-CURTIME-SECOND → WS-CURTIME-SS`; `MOVE WS-CURTIME-HH-MM-SS → CURTIMEO`. // source: COPAUS1C.cbl:424-428

**READ-AUTH-RECORD** (GU summary + keyed GNP detail) // source: COPAUS1C.cbl:431-491
1. PERFORM SCHEDULE-PSB. // source: COPAUS1C.cbl:433
2. `MOVE WS-ACCT-ID → PA-ACCT-ID`; `MOVE WS-AUTH-KEY → PA-AUTHORIZATION-KEY`. // source: COPAUS1C.cbl:436-437
3. `EXEC DLI GU SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) WHERE(ACCNTID = PA-ACCT-ID)`. // source: COPAUS1C.cbl:439-443
4. `MOVE DIBSTAT → IMS-RETURN-CODE`; EVALUATE TRUE: `STATUS-OK`→`SET AUTHS-NOT-EOF`; `SEGMENT-NOT-FOUND`/`END-OF-DB`→`SET AUTHS-EOF`; OTHER→`MOVE 'Y' → WS-ERR-FLG`, STRING `' System error while reading Auth Summary: Code:' IMS-RETURN-CODE` → `WS-MESSAGE`, PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:445-462
5. IF `AUTHS-NOT-EOF`: `EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS) WHERE(PAUT9CTS = PA-AUTHORIZATION-KEY)`; `MOVE DIBSTAT → IMS-RETURN-CODE`; EVALUATE: `STATUS-OK`→`SET AUTHS-NOT-EOF`; `SEGMENT-NOT-FOUND`/`END-OF-DB`→`SET AUTHS-EOF`; OTHER→`MOVE 'Y' → WS-ERR-FLG`, STRING `' System error while reading Auth Details: Code:' IMS-RETURN-CODE`, PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:464-489

**READ-NEXT-AUTH-RECORD** (unqualified GNP — advance) // source: COPAUS1C.cbl:493-518
1. `EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS)` (no WHERE — next child). // source: COPAUS1C.cbl:495-498
2. `MOVE DIBSTAT → IMS-RETURN-CODE`; EVALUATE: `STATUS-OK`→`SET AUTHS-NOT-EOF`; `SEGMENT-NOT-FOUND`/`END-OF-DB`→`SET AUTHS-EOF`; OTHER→`MOVE 'Y' → WS-ERR-FLG`, STRING `' System error while reading next Auth: Code:' IMS-RETURN-CODE`, PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:500-517

**UPDATE-AUTH-DETAILS** (REPL detail with fraud flag) // source: COPAUS1C.cbl:520-552
1. `MOVE WS-FRAUD-AUTH-RECORD → PENDING-AUTH-DETAILS` (copy the (possibly fraud-stamped) record back into the io-area). // source: COPAUS1C.cbl:522
2. `DISPLAY 'RPT DT: ' PA-FRAUD-RPT-DATE` (debug DISPLAY — see §7 bug #5). // source: COPAUS1C.cbl:523
3. `EXEC DLI REPL SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS)`. // source: COPAUS1C.cbl:525-528
4. `MOVE DIBSTAT → IMS-RETURN-CODE`; EVALUATE: `STATUS-OK`→PERFORM TAKE-SYNCPOINT; IF `PA-FRAUD-REMOVED` `MOVE 'AUTH FRAUD REMOVED...' → WS-MESSAGE` ELSE `MOVE 'AUTH MARKED FRAUD...' → WS-MESSAGE`; OTHER→PERFORM ROLL-BACK, `MOVE 'Y' → WS-ERR-FLG`, STRING `' System error while FRAUD Tagging, ROLLBACK||' IMS-RETURN-CODE`, PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:530-551

**TAKE-SYNCPOINT** // source: COPAUS1C.cbl:557-560
1. `EXEC CICS SYNCPOINT` → `COMMIT`. // source: COPAUS1C.cbl:558-559

**ROLL-BACK** // source: COPAUS1C.cbl:565-569
1. `EXEC CICS SYNCPOINT ROLLBACK` → `ROLLBACK`. // source: COPAUS1C.cbl:566-568

**SCHEDULE-PSB** // source: COPAUS1C.cbl:574-603
1. `EXEC DLI SCHD PSB((PSB-NAME)) NODHABEND`; `MOVE DIBSTAT → IMS-RETURN-CODE`. // source: COPAUS1C.cbl:575-579
2. IF `PSB-SCHEDULED-MORE-THAN-ONCE` ('TC'): `EXEC DLI TERM`; re-`EXEC DLI SCHD …`; `MOVE DIBSTAT → IMS-RETURN-CODE`. // source: COPAUS1C.cbl:580-589
3. IF `STATUS-OK` → `SET IMS-PSB-SCHD`; ELSE → `MOVE 'Y' → WS-ERR-FLG`, STRING `' System error while scheduling PSB: Code:' IMS-RETURN-CODE` → `WS-MESSAGE`, PERFORM SEND-AUTHVIEW-SCREEN. // source: COPAUS1C.cbl:590-602

### Control-flow summary
`MAIN → (EIBCALEN=0 ? RETURN-TO-PREV-SCREEN[XCTL COPAUS0C]) : copy COMMAREA → (NOT REENTER ? PROCESS-ENTER-KEY + SEND : RECEIVE + EVALUATE EIBAID{ENTER→PROCESS-ENTER-KEY+SEND; PF3→XCTL COPAUS0C; PF5→MARK-AUTH-FRAUD+SEND; PF8→PROCESS-PF8-KEY+SEND; OTHER→PROCESS-ENTER-KEY+invalid-msg+SEND}) → CICS RETURN(TRANSID CPVD, COMMAREA)`.
`PROCESS-ENTER-KEY/PF8/MARK-FRAUD → READ-AUTH-RECORD{SCHEDULE-PSB, GU summary, keyed GNP detail} → (PF8: READ-NEXT-AUTH-RECORD[GNP next]) → syncpoint if scheduled → POPULATE-AUTH-DETAILS`.
`MARK-FRAUD → READ-AUTH-RECORD → toggle PA-AUTH-FRAUD → LINK COPAUS2C → (success ? UPDATE-AUTH-DETAILS[REPL+syncpoint] : ROLLBACK)`. // source: COPAUS1C.cbl:156-206, 208-289, 431-552

---

## 5. ONLINE / BMS specifics

### 5.1 Pseudo-conversational flow
- One screen turn per invocation. State carried entirely in `CARDDEMO-COMMAREA` (incl. CPVD extension). `CDEMO-PGM-CONTEXT` 88 `CDEMO-PGM-REENTER`(=1) distinguishes first display (build + SEND) from a key press (RECEIVE + dispatch). // source: COPAUS1C.cbl:173-179
- `EXEC CICS RETURN TRANSID('CPVD') COMMAREA(CARDDEMO-COMMAREA)` re-arms the same transaction. // source: COPAUS1C.cbl:202-205
- **Cold start** (`EIBCALEN = 0`): immediately XCTL to the summary screen `COPAUS0C` (this detail screen has no standalone entry). // source: COPAUS1C.cbl:165-169

### 5.2 EIBAID / PFKey handling // source: COPAUS1C.cbl:180-198
| AID | Action |
|---|---|
| `DFHENTER` | Re-read & re-display the currently selected auth (PROCESS-ENTER-KEY → SEND). |
| `DFHPF3` | XCTL back to summary screen `COPAUS0C` (set `CDEMO-TO-PROGRAM`, RETURN-TO-PREV-SCREEN). |
| `DFHPF5` | Toggle fraud flag (MARK-AUTH-FRAUD: LINK COPAUS2C, REPL detail) → SEND. |
| `DFHPF8` | Advance to next authorization (PROCESS-PF8-KEY: GNP next) → SEND. |
| OTHER (any other key) | PROCESS-ENTER-KEY (re-read), then show `CCDA-MSG-INVALID-KEY` and SEND. |

### 5.3 BMS map `COPAU1A` / mapset `COPAU01` (`bms/COPAU01.bms`, copybook `cpy-bms/COPAU01.cpy`)
24×80, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `MODE=INOUT`. // source: bms/COPAU01.bms:19-28
**All data fields are `ATTRB=(ASKIP,…)` — the screen is display-only; there is no operator-enterable input field.** The program SENDs the `…O` output fields and never reads any `…I` input field value (RECEIVE is issued but its inbound field contents are unused; only `EIBAID` matters). // source: bms/COPAU01.bms:29-287; COPAUS1C.cbl:400-405

| Map field | Len | Pos | Read/Written by program | Source value |
|---|---|---|---|---|
| `TRNNAMEO` | 4 | 1,7 | written | `WS-CICS-TRANID` ('CPVD'). // source: COPAUS1C.cbl:415 |
| `TITLE01O` | 40 | 1,21 | written | `CCDA-TITLE01`. // source: COPAUS1C.cbl:413 |
| `CURDATEO` | 8 | 1,71 | written | current date MM/DD/YY. // source: COPAUS1C.cbl:422 |
| `PGMNAMEO` | 8 | 2,7 | written | `WS-PGM-AUTH-DTL` ('COPAUS1C'). // source: COPAUS1C.cbl:416 |
| `TITLE02O` | 40 | 2,21 | written | `CCDA-TITLE02`. // source: COPAUS1C.cbl:414 |
| `CURTIMEO` | 8 | 2,71 | written | current time HH:MM:SS. // source: COPAUS1C.cbl:428 |
| `CARDNUMO` | 16 | 7,11 | written | `PA-CARD-NUM`. (`CARDNUML=-1` to position cursor here.) // source: COPAUS1C.cbl:295,378 |
| `AUTHDTO` | 10 | 7,43 | written | auth date MM/DD/YY from `PA-AUTH-ORIG-DATE`. // source: COPAUS1C.cbl:301 |
| `AUTHTMO` | 10 | 7,68 | written | auth time HH:MM:SS from `PA-AUTH-ORIG-TIME`. // source: COPAUS1C.cbl:306 |
| `AUTHRSPO` (+`AUTHRSPC`) | 1 | 9,14 | written | 'A'/green or 'D'/red from `PA-AUTH-RESP-CODE`. // source: COPAUS1C.cbl:312-316 |
| `AUTHRSNO` | 20 | 9,32 | written | `"<code>-<desc>"` from SEARCH ALL (or `'9999-ERROR'`). // source: COPAUS1C.cbl:321-327 |
| `AUTHCDO` | 6 | 9,68 | written | `PA-PROCESSING-CODE`. // source: COPAUS1C.cbl:331 |
| `AUTHAMTO` | 12 | 11,11 | written | `PA-APPROVED-AMT` via `-zzzzzzz9.99`. // source: COPAUS1C.cbl:309 |
| `POSEMDO` | 4 | 11,46 | written | `PA-POS-ENTRY-MODE`. // source: COPAUS1C.cbl:332 |
| `AUTHSRCO` | 10 | 11,68 | written | `PA-MESSAGE-SOURCE`. // source: COPAUS1C.cbl:333 |
| `MCCCDO` | 4 | 13,13 | written | `PA-MERCHANT-CATAGORY-CODE`. // source: COPAUS1C.cbl:334 |
| `CRDEXPO` | 5 | 13,42 | written | `PA-CARD-EXPIRY-DATE` as `bb/bb`. // source: COPAUS1C.cbl:336-338 |
| `AUTHTYPO` | 14 | 13,64 | written | `PA-AUTH-TYPE`. // source: COPAUS1C.cbl:340 |
| `TRNIDO` | 15 | 15,12 | written | `PA-TRANSACTION-ID`. // source: COPAUS1C.cbl:341 |
| `AUTHMTCO` | 1 | 15,46 | written | `PA-MATCH-STATUS`. // source: COPAUS1C.cbl:342 |
| `AUTHFRDO` | 10 | 15,67 | written | `"<F/R>-<rpt-date>"` or `'-'`. // source: COPAUS1C.cbl:344-350 |
| `MERNAMEO` | 25 | 19,9 | written | `PA-MERCHANT-NAME` (22 chars into 25). // source: COPAUS1C.cbl:352 |
| `MERIDO` | 15 | 19,55 | written | `PA-MERCHANT-ID`. // source: COPAUS1C.cbl:353 |
| `MERCITYO` | 25 | 21,9 | written | `PA-MERCHANT-CITY` (13 chars into 25). // source: COPAUS1C.cbl:354 |
| `MERSTO` | 2 | 21,49 | written | `PA-MERCHANT-STATE`. // source: COPAUS1C.cbl:355 |
| `MERZIPO` | 10 | 21,61 | written | `PA-MERCHANT-ZIP` (9 chars into 10). // source: COPAUS1C.cbl:356 |
| `ERRMSGO` | 78 | 23,1 | written | `WS-MESSAGE`. // source: COPAUS1C.cbl:377 |
| (static) | 45 | 24,1 | static literal | `' F3=Back  F5=Mark/Remove Fraud  F8=Next Auth'`. // source: bms/COPAU01.bms:288-292 |

- **Cursor:** `MOVE -1 TO CARDNUML` puts the cursor at `CARDNUM` (the −1-length convention) with `CURSOR` on SEND. // source: COPAUS1C.cbl:378,386,393
- **ERASE:** SEND uses `ERASE` except when `SEND-ERASE-NO` is set (PF8 at end-of-list keeps the prior screen, just updates `ERRMSG`). // source: COPAUS1C.cbl:282,380-395

### 5.4 XCTL / LINK targets
- **XCTL → `COPAUS0C`** (`CDEMO-TO-PROGRAM`): on cold start, PF3, and RETURN-TO-PREV-SCREEN. COMMAREA = `CARDDEMO-COMMAREA` with `CDEMO-FROM-TRANID='CPVD'`, `CDEMO-FROM-PROGRAM='COPAUS1C'`, `CDEMO-PGM-CONTEXT=0`, `CDEMO-PGM-ENTER`. // source: COPAUS1C.cbl:362-370
- **LINK → `COPAUS2C`** (`WS-PGM-AUTH-FRAUD`): synchronous, COMMAREA=`WS-FRAUD-DATA`, `NOHANDLE`; result examined via `EIBRESP` + `WS-FRD-UPDATE-STATUS`. COPAUS2C inserts (`WS-REPORT-FRAUD`) or deletes (`WS-REMOVE-FRAUD`) a row in DB2 `AUTHFRDS` keyed by `(CARD_NUM, AUTH_TS)`, sets `WS-FRD-UPDATE-STATUS='S'/'F'` and `WS-FRD-ACT-MSG`. // source: COPAUS1C.cbl:248-258; COPAUS2C.cbl:73-101; IMS_SCHEMA.md §5

---

## 6. VALIDATION RULES & exact literal messages

**Entry validation (PROCESS-ENTER-KEY):** proceed to read only if `CDEMO-ACCT-ID IS NUMERIC` **AND** `CDEMO-CPVD-PAU-SELECTED NOT = SPACES AND LOW-VALUES`; otherwise `SET ERR-FLG-ON` (no read, map stays cleared, no specific message). // source: COPAUS1C.cbl:211-224

**Response indicator:** `PA-AUTH-RESP-CODE='00'` → 'A' (green); else 'D' (red). // source: COPAUS1C.cbl:311-316

**Decline-reason text (verbatim 16-char descriptions, SEARCH ALL):**
`0000`→`APPROVED`, `3100`→`INVALID CARD`, `4100`→`INSUFFICNT FUND` *(sic)*, `4200`→`CARD NOT ACTIVE`, `4300`→`ACCOUNT CLOSED`, `4400`→`EXCED DAILY LMT` *(sic)*, `5100`→`CARD FRAUD`, `5200`→`MERCHANT FRAUD`, `5300`→`LOST CARD`, `9000`→`UNKNOWN`; not found → reason shown as `9999-ERROR`. // source: COPAUS1C.cbl:57-67,321-327

**Exact literal strings to reproduce verbatim** (preserve embedded/trailing spacing):
- `'Already at the last Authorization...'` (PF8 at end). // source: COPAUS1C.cbl:283
- `'AUTH FRAUD REMOVED...'` (fraud removed OK). // source: COPAUS1C.cbl:535
- `'AUTH MARKED FRAUD...'` (fraud marked OK). // source: COPAUS1C.cbl:537
- `'Invalid key pressed. Please see below...         '` (`CCDA-MSG-INVALID-KEY`). // source: cpy/CSMSG01Y.cpy:20-21; COPAUS1C.cbl:196
- STRING-built error lines (leading space + literal + 2-char `IMS-RETURN-CODE`):
  - `' System error while reading Auth Summary: Code:'` + code. // source: COPAUS1C.cbl:456-457
  - `' System error while reading Auth Details: Code:'` + code. // source: COPAUS1C.cbl:482-483
  - `' System error while reading next Auth: Code:'` + code. // source: COPAUS1C.cbl:511-512
  - `' System error while FRAUD Tagging, ROLLBACK||'` + code. // source: COPAUS1C.cbl:545-546
  - `' System error while scheduling PSB: Code:'` + code. // source: COPAUS1C.cbl:596-597
- `WS-FRD-ACT-MSG` (50-char message returned by COPAUS2C on fraud failure) shown verbatim. // source: COPAUS1C.cbl:257; COPAUS2C.cbl:86

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

**#1 — `WS-AUTH-DATE` separators reused for time, but time keeps date's `'/'`? No — distinct fields; the *real* quirk is the time field reuses the `'00:00:00'` literal init.** `WS-AUTH-TIME` is `VALUE '00:00:00'`; POPULATE only overwrites positions 1:2/4:2/7:2 (the digit pairs), leaving the `':'` at positions 3 and 6 from the initial VALUE. This works only because the field is never reset to spaces between turns. If a prior path left non-colon bytes at 3/6 the separators could be wrong — but in this program the VALUE persists. Reproduce by seeding the formatted-time buffer with `'00:00:00'` and overlaying digit pairs (do NOT rebuild via an edited PIC). // source: COPAUS1C.cbl:54,303-306

**#2 — `IMS-PSB-SCHD` tested without a guaranteed prior SET; `WS-IMS-PSB-SCHD-FLG` has no VALUE clause.** `WS-IMS-PSB-SCHD-FLG` is declared with **no VALUE** (initial bytes undefined/spaces). PROCESS-ENTER-KEY and PROCESS-PF8-KEY do `IF IMS-PSB-SCHD … SET IMS-PSB-NOT-SCHD … TAKE-SYNCPOINT` after READ-AUTH-RECORD; the flag is only ever SET to 'Y' inside SCHEDULE-PSB on success. On the unusual path where SCHEDULE-PSB sends an error screen (and falls through), the flag state and the subsequent syncpoint gating are whatever SCHEDULE-PSB last left. Reproduce the flag's exact lifecycle (init = not-'Y', set 'Y' only on successful SCHD); do not add defensive initialization. // source: COPAUS1C.cbl:89-91,218-221,276-279,590-591

**#3 — Error path sends a screen mid-paragraph but does NOT abort the turn.** On any unexpected `DIBSTAT` in READ-AUTH-RECORD / READ-NEXT-AUTH-RECORD / UPDATE-AUTH-DETAILS / SCHEDULE-PSB, the code sets `WS-ERR-FLG='Y'`, STRINGs a message, and `PERFORM SEND-AUTHVIEW-SCREEN` **inline** — then control returns to the caller, which (e.g. PROCESS-ENTER-KEY) continues to POPULATE-AUTH-DETAILS (guarded by `ERR-FLG-OFF`, so it no-ops) and MAIN then issues **a second** `SEND-AUTHVIEW-SCREEN`. Net effect: the screen is sent twice on an error turn (once inside the read paragraph, once from MAIN's dispatch). Reproduce the double-SEND behavior; do not collapse to a single send. // source: COPAUS1C.cbl:453-461,177,183,294,202

**#4 — Card-expiry display assumes a specific 4-char layout and uses positional slicing.** `CRDEXPO(1:2)=PA-CARD-EXPIRY-DATE(1:2)`, `CRDEXPO(3:1)='/'`, `CRDEXPO(4:2)=PA-CARD-EXPIRY-DATE(3:2)` — produces `xx/yy` from the raw 4 bytes with no validation of which half is month vs year. Whatever the stored 4 bytes are, they are sliced verbatim. Reproduce the byte-slice, not a parsed date. // source: COPAUS1C.cbl:336-338

**#5 — Stray `DISPLAY 'RPT DT: ' PA-FRAUD-RPT-DATE` left in UPDATE-AUTH-DETAILS.** A debug `DISPLAY` to the system log fires on every fraud REPL. It has no screen effect (CICS DISPLAY → region log/console). Carry as a log line (or no-op); do not remove it from the faithful behavior set. // source: COPAUS1C.cbl:523

**#6 — `MARK-AUTH-FRAUD` does not gate on `AUTHS-EOF` / `ERR-FLG`.** After READ-AUTH-RECORD it unconditionally toggles `PA-AUTH-FRAUD` and LINKs COPAUS2C even if the read found no detail (AUTHS-EOF) or errored — operating on a stale/empty io-area. The summary GU's `IMS-PSB-SCHD` syncpoint reset done in PROCESS-ENTER-KEY/PF8 is also **absent** in MARK-AUTH-FRAUD (it relies on UPDATE-AUTH-DETAILS/ROLL-BACK to syncpoint). Reproduce as-is. // source: COPAUS1C.cbl:230-266

**#7 — `WS-RESP-CD`/`WS-REAS-CD` declared but never used (dead).** Inert; carry as unused fields. // source: COPAUS1C.cbl:47-48

**#8 — Truncated/misspelled reason descriptions are intentional data.** `INSUFFICNT FUND` and `EXCED DAILY LMT` are stored exactly that way (to fit X(16)); preserve the spellings. // source: COPAUS1C.cbl:60,63

---

## 8. PORT NOTES (relational-access translation plan + tricky COBOL semantics)

**Target placement & shape.** Implement `CardDemo.Online/COPAUS1C.cs` as a CICS-shim transaction handler for `CPVD` driven by the Online runtime (COMMAREA store + AID dispatch + BMS screen model `COPAU01.COPAU1A`). IMS access goes through the PAUT repositories (`PAUT_SUMMARY`, `PAUT_DETAIL`) per IMS_SCHEMA.md. The fraud LINK target COPAUS2C is an injected service that writes the relational `AUTHFRDS` table and returns success/failure + message.

**IMS position / cursor model (the crux).** This program relies on IMS "current position" across two DL/I calls within one turn:
- READ-AUTH-RECORD does GU(root by acct) then **qualified** GNP(detail `WHERE PAUT9CTS = key`). Model the qualified GNP as `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=@a AND AUTH_KEY >= @key ORDER BY AUTH_KEY ASC LIMIT 1` and **remember the returned `AUTH_KEY`** as the cursor position. (Qualified GNP repositions at-or-after the key; for the detail screen the saved key always exists, so it lands exactly on it.) // IMS_SCHEMA.md §3.3
- PROCESS-PF8-KEY then calls READ-NEXT-AUTH-RECORD = unqualified GNP = `cursor.MoveNext()`: `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=@a AND AUTH_KEY > @current_key ORDER BY AUTH_KEY ASC LIMIT 1`. Exhausted → `AUTHS-EOF` → "Already at the last Authorization...". The position must be the one established by the immediately-preceding READ-AUTH-RECORD in the same turn (PF8 re-reads, then advances). // source: COPAUS1C.cbl:273-274; IMS_SCHEMA.md §3.3
- **Ordering:** `AUTH_KEY` is the 8-byte 9s-complement key; ascending key order = newest-first. Use ordinal string compare on `AUTH_KEY` (or `ORDER BY AUTH_DATE_9C, AUTH_TIME_9C`). Preserve so PF8 walks the same sequence as IMS. // IMS_SCHEMA.md:161-166

**REPL → UPDATE (fraud tag).** UPDATE-AUTH-DETAILS replaces the **whole** detail segment from the io-area. Generate `UPDATE PAUT_DETAIL SET <all non-key columns> WHERE ACCT_ID=@a AND AUTH_KEY=@k`; **never** change `ACCT_ID`/`AUTH_KEY` (IMS forbids key change on REPL). The only fields that actually change vs the read are `PA-AUTH-FRAUD` and `PA-FRAUD-RPT-DATE` (the latter set by COPAUS2C, returned inside `WS-FRAUD-AUTH-RECORD`). Commit on success (TAKE-SYNCPOINT), ROLLBACK on failure. // source: COPAUS1C.cbl:520-552; IMS_SCHEMA.md §3.5,§7

**Fraud toggle + LINK contract.** Pass a `WS-FRAUD-DATA` equivalent: acct id, cust id, the 200-byte detail image (or a typed copy), action 'F'/'R'. COPAUS2C: on 'F' inserts `AUTHFRDS(CARD_NUM, AUTH_TS=PA-FRAUD-RPT-DATE-derived timestamp, …)`, on 'R' deletes it; returns `WS-FRD-UPDATE-STATUS='S'/'F'` + `WS-FRD-ACT-MSG`. The toggle decision is made HERE (line 236-242) based on the current `PA-AUTH-FRAUD`; the report date is stamped by COPAUS2C (`PA-FRAUD-RPT-DATE = WS-CUR-DATE` MMDDYY). // source: COPAUS1C.cbl:236-252; COPAUS2C.cbl:91-101

**Edited numeric (`WS-AUTH-AMT` PIC `-zzzzzzz9.99`).** Format `PA-APPROVED-AMT` (S9(10)V99) into a 12-char field: leading sign-or-space (only `-` shown for negatives), zero-suppressed integer part (7 suppressible + 1 mandatory digit), decimal point, 2 decimals. Use `CobolEditedNumeric` with this picture; truncate toward zero (no rounding) per ARCHITECTURE.md §38. Note the integer part holds only 8 digits while the source has 10 — values ≥ 100,000,000.00 would overflow the edit mask (high-order digits lost / asterisks not specified here → silent truncation by the edit). Pin with a characterization test. // source: COPAUS1C.cbl:52,308-309; ARCHITECTURE.md:38

**Date/time slicing.** Auth date built by reference-modification of `PA-AUTH-ORIG-DATE` (YYMMDD → MM/DD/YY) and `PA-AUTH-ORIG-TIME` (HHMMSS → HH:MM:SS via overlay onto `'00:00:00'`). Reproduce as positional substring moves, NOT date parsing (the program does no validity check). // source: COPAUS1C.cbl:297-306

**SEARCH ALL (binary search).** `WS-DECLINE-REASON-TAB` is `ASCENDING KEY DECL-CODE`; SEARCH ALL is a binary search over the 10 ascending literal codes. Port as a sorted lookup (dictionary or binary search) keyed by the 4-char reason; on miss, emit `9999-ERROR`. The codes are pre-sorted in the source. // source: COPAUS1C.cbl:68-73,319-328

**REDEFINES / reference modification / `LOW-VALUES`.** `MOVE LOW-VALUES TO COPAU1AO` clears the output map to nulls (in BMS this suppresses fields / leaves them at default attrs); model as "clear all output fields to empty + default attribute". The `CDEMO-CPVD-PAU-SELECTED NOT = SPACES AND LOW-VALUES` test means "not all spaces and not all low-values" (abbreviated COBOL combined condition). // source: COPAUS1C.cbl:210,212

**SCHD/TERM/SYNCPOINT.** SCHEDULE-PSB = open unit of work; `TC` → TERM + re-SCHD (close+reopen). After a successful read in PROCESS-ENTER-KEY/PF8, the program TERMs (via syncpoint reset of the flag) and SYNCPOINTs. Model SCHD as "begin/ensure connection", SYNCPOINT as COMMIT, SYNCPOINT ROLLBACK as ROLLBACK. // source: COPAUS1C.cbl:218-221,276-279,557-569,574-603; IMS_SCHEMA.md §3.8

**No QSAM/VSAM file I/O.** All persistence is DL/I (PAUT tables) + the LINKed DB2 fraud table via COPAUS2C. No ACCTDAT/CUSTDAT/CCXREF reads in this program (unlike its summary-screen sibling).

**DISPLAY.** The stray `DISPLAY 'RPT DT: '` goes to the region log; route to a logger sink. // source: COPAUS1C.cbl:523

---

## 9. OPEN QUESTIONS / risks

1. **Qualified GNP semantics (`>=` vs exact).** IMS qualified GNP `WHERE(PAUT9CTS = key)` repositions to the first child whose key satisfies the qualification at-or-after current position; IMS_SCHEMA.md §3.3 models this as `AUTH_KEY >= :key`. For this screen the saved key is always an existing key, so `=` and `>=` coincide. Pin the reposition test so PF8's subsequent unqualified GNP returns the strictly-next key. // source: COPAUS1C.cbl:464-468; IMS_SCHEMA.md §3.3
2. **PF8 within-turn position.** PF8 re-reads (READ-AUTH-RECORD repositions on the current key) then advances (READ-NEXT-AUTH-RECORD). The relational port must thread the same per-turn cursor position from the qualified GNP into the next unqualified GNP; ensure the two SELECTs share `@current_key`. // source: COPAUS1C.cbl:273-274
3. **`AUTHFRDS` timestamp key derivation.** COPAUS2C builds `AUTH_TS` from `WS-AUTH-TIME`/`WS-AUTH-TS` and `PA-FRAUD-RPT-DATE`; the exact mapping of the IMS detail's date/time to the DB2 `(CARD_NUM, AUTH_TS)` PK lives in the COPAUS2C spec. Mark/remove must target the same row — confirm the timestamp derivation matches when re-removing a previously reported fraud. // source: COPAUS1C.cbl:244-264; COPAUS2C.cbl:35-101
4. **Double-SEND on error (bug #3).** Confirm the characterization harness expects two SENDs on an error turn; the screen-parity test must assert the final rendered screen (the second SEND wins) but the behavior log should note the first. // source: COPAUS1C.cbl:453-461,202
5. **Amount edit overflow.** `-zzzzzzz9.99` (8 integer digits) vs `PA-APPROVED-AMT` S9(10)V99 (10 integer digits). For amounts ≥ 1e8 the edit silently drops high-order digits. Confirm test data never exceeds this, or pin the truncation. // source: COPAUS1C.cbl:52,308
