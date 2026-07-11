# PORT SPEC — COPAUS0C (Pending Authorization Summary / List, CICS + IMS DL/I, BMS)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/cbl/COPAUS0C.cbl`
> BMS map: `.../app-authorization-ims-db2-mq/bms/COPAU00.bms` (mapset `COPAU00`, map `COPAU0A`)
> IMS DBD: `DBPAUTP0` (HIDAM, root `PAUTSUM0`, child `PAUTDTL1`) + `DBPAUTX0` (secondary index `PAUTINDX`/`INDXSEQ`); PSB `PSBPAUTB` (PCB #1).
> Re-host target tables: `PAUT_SUMMARY`, `PAUT_DETAIL` (see `_design/specs/optional/IMS_SCHEMA.md`); base tables `CARD_XREF`, `ACCOUNT`, `CUSTOMER` (see `ARCHITECTURE.md`).
> Type hint: **ims** (online CICS BMS pseudo-conversational program that also drives an IMS DL/I database).

---

## 1. Purpose & Invocation

COPAUS0C is the **online "Pending Authorization Summary" list** transaction. Given a credit-card
**account id** the user types into the screen, it looks the account up through the card cross-reference,
account, and customer files to paint the header (customer name, address, phone, credit/cash limit), then
reads the IMS **pending-authorization summary** root segment for that account (approval/decline counts,
balances, amounts) and pages through that account's **pending-authorization detail** child segments,
showing up to **5 authorizations per screen** with PF7 (backward) / PF8 (forward) paging. The user may
type `S` next to one row to drill into the detail program. // source: COPAUS0C.cbl:1-6, 176-257

**Invocation**
- **CICS TRANSID `CPVS`** (`WS-CICS-TRANID PIC X(04) VALUE 'CPVS'`). Pseudo-conversational: every path ends
  with `EXEC CICS RETURN TRANSID('CPVS') COMMAREA(CARDDEMO-COMMAREA)`. // source: COPAUS0C.cbl:36, 254-257
- Reached by **XCTL** from the menu (`COMEN01C`) or returning from the detail program (`COPAUS1C`); on
  PF3 it XCTLs to the menu `COMEN01C`. Program id constant `WS-PGM-AUTH-SMRY = 'COPAUS0C'`. // source: COPAUS0C.cbl:33-35, 236-238
- Drills down via **XCTL to `COPAUS1C`** (`WS-PGM-AUTH-DTL`) when a row is selected with `S`. // source: COPAUS0C.cbl:34, 316-325
- Not a called subprogram; it is a top-level CICS transaction program. COMMAREA is `CARDDEMO-COMMAREA`
  (copybook `COCOM01Y`) extended with a program-private `CDEMO-CPVS-INFO` block. // source: COPAUS0C.cbl:116-126, 172-174

---

## 2. FILE / TABLE access

### 2a. VSAM (CICS file control) → relational base tables

| COBOL DATASET (DDname) | Access | CICS op | Key | Relational table (ARCHITECTURE.md) | SQL equivalent |
|---|---|---|---|---|---|
| `CXACAIX` (`WS-CARDXREFNAME-ACCT-PATH`) — card-xref **alternate index by acct id** | READ alt key | `EXEC CICS READ DATASET(CXACAIX) RIDFLD(WS-CARD-RID-ACCT-ID-X) KEYLENGTH(11) INTO(CARD-XREF-RECORD)` | acct_id X(11) | **CARD_XREF** (idx `acct_id`) | `SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF WHERE acct_id = :acctId` (first match). NOTFND → message + return. // source: COPAUS0C.cbl:41, 812-862 |
| `ACCTDAT` (`WS-ACCTFILENAME`) — account master | READ key | `EXEC CICS READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X) KEYLENGTH(11) INTO(ACCOUNT-RECORD)` | acct_id X(11) | **ACCOUNT** (PK `acct_id`) | `SELECT * FROM ACCOUNT WHERE acct_id = :acctId`. // source: COPAUS0C.cbl:38, 864-912 |
| `CUSTDAT` (`WS-CUSTFILENAME`) — customer master | READ key | `EXEC CICS READ DATASET(CUSTDAT) RIDFLD(WS-CARD-RID-CUST-ID-X) KEYLENGTH(9) INTO(CUSTOMER-RECORD)` | cust_id X(9) | **CUSTOMER** (PK `cust_id`) | `SELECT * FROM CUSTOMER WHERE cust_id = :custId`. // source: COPAUS0C.cbl:39, 914-963 |

`WS-CARDFILENAME 'CARDDAT'` and `WS-CCXREF-FILE 'CCXREF'` are declared but **never used** in PROCEDURE
DIVISION. // source: COPAUS0C.cbl:40, 42

> Note on `GETACCTDATA-BYACCT` RIDFLD: it `MOVE XREF-ACCT-ID TO WS-CARD-RID-ACCT-ID` (the numeric 9(11)
> redefine) but the READ uses `RIDFLD(WS-CARD-RID-ACCT-ID-X)` (the X(11) redefine over the same bytes).
> Same storage, so the key value is the cross-ref acct id. Port: read ACCOUNT by `xref.acct_id`.
> // source: COPAUS0C.cbl:868-872

### 2b. IMS DL/I segments → relational PAUT_* tables

| IMS segment (PCB #1, `PSBPAUTB`) | Role | Seq field | DL/I ops used | Re-host table (IMS_SCHEMA.md) | SQL equivalent |
|---|---|---|---|---|---|
| `PAUTSUM0` (`PENDING-AUTH-SUMMARY`, cpy CIPAUSMY) | ROOT | `ACCNTID` = `PA-ACCT-ID` S9(11) COMP-3 (6-byte packed) | **GU … WHERE (ACCNTID = PA-ACCT-ID)** (keyed get-unique) | **PAUT_SUMMARY** (PK `ACCT_ID`) | `SELECT * FROM PAUT_SUMMARY WHERE ACCT_ID = :acctId`. `GE`→not-found; else error. // source: COPAUS0C.cbl:966-997, IMS_SCHEMA.md:66-97 |
| `PAUTDTL1` (`PENDING-AUTH-DETAILS`, cpy CIPAUDTY) | CHILD of PAUTSUM0 | `PAUT9CTS` = `PA-AUTHORIZATION-KEY` (8-byte char = date-9C + time-9C) | **GNP** (get-next-within-parent forward scan) and **GNP … WHERE (PAUT9CTS = PA-AUTHORIZATION-KEY)** (reposition) | **PAUT_DETAIL** (PK `ACCT_ID,AUTH_KEY`; FK→summary) | Forward cursor `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=:acctId ORDER BY AUTH_KEY ASC` (== `AUTH_DATE_9C, AUTH_TIME_9C` ASC == newest-first). Reposition = cursor seek to first row with `AUTH_KEY >= :savedKey`. `GE`/`GB`→EOF. // source: COPAUS0C.cbl:457-519, IMS_SCHEMA.md:99-165 |

DL/I housekeeping ops:
- **SCHD** schedule PSB `PSBPAUTB` (with `NODHABEND`); if `TC` (scheduled-more-than-once) → `TERM` then
  re-`SCHD`. // source: COPAUS0C.cbl:1001-1031
- **TERM** / **SYNCPOINT** on each SEND when a PSB is currently scheduled (`IMS-PSB-SCHD`). // source: COPAUS0C.cbl:684-688, 1007-1009

**Critical ordering rule (IMS_SCHEMA.md:161-165):** `PAUT9CTS` is a 9s-complement key, so ascending key
order = **most-recent authorization first**. The relational forward cursor MUST `ORDER BY AUTH_KEY ASC`
(equiv. `AUTH_DATE_9C, AUTH_TIME_9C ASC`) to preserve screen paging order.

---

## 3. DATA STRUCTURES used by logic

### 3.1 COMMAREA — base `CARDDEMO-COMMAREA` (COCOM01Y) fields used
// source: COCOM01Y.cpy:19-44, COPAUS0C.cbl:116
- `CDEMO-FROM-TRANID` X(4), `CDEMO-FROM-PROGRAM` X(8), `CDEMO-TO-PROGRAM` X(8) — set on XCTL hops. // source: COPAUS0C.cbl:236, 317-318, 671-672
- `CDEMO-PGM-CONTEXT` 9(1) with 88s `CDEMO-PGM-ENTER`(0)/`CDEMO-PGM-REENTER`(1) — pseudo-conversational reentry flag. // source: COCOM01Y.cpy:29-31; COPAUS0C.cbl:194, 202-203, 319-320, 673
- `CDEMO-ACCT-ID` 9(11) — the searched account; round-tripped through the COMMAREA. // source: COPAUS0C.cbl:207-208, 283-284, 971
- `CDEMO-CUST-ID` 9(9), `CDEMO-CARD-NUM` 9(16) — populated from the XREF read. // source: COPAUS0C.cbl:830-831

### 3.2 COMMAREA — program-private extension `CDEMO-CPVS-INFO`
This 01-level block is appended **immediately after** `COPY COCOM01Y` (so it physically extends
`CARDDEMO-COMMAREA` in the COMMAREA byte stream — port must persist these fields across turns too).
// source: COPAUS0C.cbl:116-126
| Field | PIC | Purpose |
|---|---|---|
| `CDEMO-CPVS-PAU-SEL-FLG` | X(01) | selection char typed (`S`/`s`) // source: 118, 287, 313-315 |
| `CDEMO-CPVS-PAU-SELECTED` | X(08) | auth key of the selected row // source: 119, 288-289, 312 |
| `CDEMO-CPVS-PAUKEY-PREV-PG` | X(08) OCCURS 20 | first auth-key of each previous page (page-top anchors) // source: 120, 368, 439-440 |
| `CDEMO-CPVS-PAUKEY-LAST` | X(08) | last auth-key shown on current page (forward anchor) // source: 121, 391-394, 422, 434-435 |
| `CDEMO-CPVS-PAGE-NUM` | S9(04) COMP | current page number // source: 122, 347, 365-366, 437-438 |
| `CDEMO-CPVS-NEXT-PAGE-FLG` | X(01)=`N`, 88s NEXT-PAGE-YES/NO | "more pages exist" flag // source: 123-125, 374, 404, 448-450 |
| `CDEMO-CPVS-AUTH-KEYS` | X(08) OCCURS 5 | auth key of each of the 5 displayed rows (for selection lookup) // source: 126, 288, 544-545, etc. |

### 3.3 IMS summary segment `PENDING-AUTH-SUMMARY` (CIPAUSMY) — fields read
`PA-ACCT-ID` S9(11) COMP-3 (key), `PA-CREDIT-LIMIT`/`PA-CASH-LIMIT` (not displayed here; ACCOUNT file used
instead), `PA-CREDIT-BALANCE`/`PA-CASH-BALANCE` S9(09)V99 COMP-3, `PA-APPROVED-AUTH-CNT`/`PA-DECLINED-AUTH-CNT`
S9(04) COMP, `PA-APPROVED-AUTH-AMT`/`PA-DECLINED-AUTH-AMT` S9(09)V99 COMP-3. // source: CIPAUSMY.cpy:19-31; COPAUS0C.cbl:787-799

### 3.4 IMS detail segment `PENDING-AUTH-DETAILS` (CIPAUDTY) — fields read
`PA-AUTHORIZATION-KEY` (X8 = `PA-AUTH-DATE-9C` S9(05) COMP-3 + `PA-AUTH-TIME-9C` S9(09) COMP-3),
`PA-AUTH-ORIG-DATE` X(06) (YYMMDD), `PA-AUTH-ORIG-TIME` X(06) (HHMMSS), `PA-AUTH-TYPE` X(04),
`PA-AUTH-RESP-CODE` X(02) (`'00'`=approved), `PA-MATCH-STATUS` X(01), `PA-APPROVED-AMT` S9(10)V99 COMP-3,
`PA-TRANSACTION-ID` X(15). // source: CIPAUDTY.cpy:19-49; COPAUS0C.cbl:525-605

### 3.5 Numeric edit / work fields
- `WS-AUTH-AMT PIC -zzzzzzz9.99` — edited amount for the row Amount column; sign-leading float, zero-suppress.
  Note PIC width is 11 (`-` + 8 digits + `.` + 2) but the **screen field `PAMTnnn` is 12 chars**, so the
  edited value is left-justified into 12 with a trailing space when MOVEd. // source: COPAUS0C.cbl:55, 525, 553; COPAU00.bms:316-320
- `WS-DISPLAY-AMT12 PIC -zzzzzzz9.99` (credit limit / credit balance), `WS-DISPLAY-AMT9 PIC -zzzz9.99`
  (cash limit / cash balance / appr amt / decl amt). // source: COPAUS0C.cbl:56-57, 780-799
- `WS-DISPLAY-COUNT PIC 9(03)` (approval/decline counts). // source: COPAUS0C.cbl:58, 788-791
- Date/time work via CSDAT01Y: `WS-CURDATE-MM-DD-YY` (mm/dd/yy), `WS-CURTIME-HH-MM-SS` (hh:mm:ss). // source: CSDAT01Y.cpy:30-41

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (statement order preserved)

### MAIN-PARA  // source: COPAUS0C.cbl:178-257
1. Init switches: `ERR-FLG-OFF`, `AUTHS-NOT-EOF`, `NEXT-PAGE-NO`, `SEND-ERASE-YES` TRUE. // 181-184
2. Clear `WS-MESSAGE` and screen `ERRMSGO`; set `ACCTIDL = -1` (cursor to acct-id field). // 186-188
3. **If `EIBCALEN = 0`** (first entry, no commarea): `INITIALIZE CARDDEMO-COMMAREA`; `CDEMO-TO-PROGRAM='COPAUS0C'`;
   set `CDEMO-PGM-REENTER`; `MOVE LOW-VALUES TO COPAU0AO`; `ACCTIDL=-1`; `PERFORM SEND-PAULST-SCREEN`. // 190-198
4. **Else** copy `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. // 200
   - **If NOT `CDEMO-PGM-REENTER`** (arriving from another program, e.g. menu/back from detail): set REENTER;
     `MOVE LOW-VALUES TO COPAU0AO`; if `CDEMO-ACCT-ID` numeric → move it to `WS-ACCT-ID` and `ACCTIDO`, else
     blank `ACCTIDO` and `WS-ACCT-ID=LOW-VALUES`; `PERFORM GATHER-DETAILS`; `SEND-ERASE-YES`; `PERFORM SEND-PAULST-SCREEN`. // 202-219
   - **Else** (true reentry after our own RETURN): `PERFORM RECEIVE-PAULST-SCREEN`, then `EVALUATE EIBAID`:
     - `DFHENTER` → `PROCESS-ENTER-KEY`; if `WS-ACCT-ID = LOW-VALUES` blank `ACCTIDO` else show it; `SEND-PAULST-SCREEN`. // 225-234
     - `DFHPF3` → `CDEMO-TO-PROGRAM = 'COMEN01C'`; `RETURN-TO-PREV-SCREEN` (XCTL); `SEND-PAULST-SCREEN` (unreached after XCTL — see faithful-bug #1). // 235-238
     - `DFHPF7` → `PROCESS-PF7-KEY`; `SEND-PAULST-SCREEN`. // 239-241
     - `DFHPF8` → `PROCESS-PF8-KEY`; `SEND-PAULST-SCREEN`. // 242-244
     - `WHEN OTHER` → set err flag, `ACCTIDL=-1`, `WS-MESSAGE = CCDA-MSG-INVALID-KEY`, `SEND-PAULST-SCREEN`. // 245-249
5. `EXEC CICS RETURN TRANSID('CPVS') COMMAREA(CARDDEMO-COMMAREA)`. // 254-257

### PROCESS-ENTER-KEY  // source: COPAUS0C.cbl:261-338
1. **If `ACCTIDI = SPACES OR LOW-VALUES`** → `WS-ACCT-ID=LOW-VALUES`, err flag, `WS-MESSAGE='Please enter Acct Id...'`, `ACCTIDL=-1`. // 264-271
2. **Else if `ACCTIDI` NOT NUMERIC** → `WS-ACCT-ID=LOW-VALUES`, err flag, `WS-MESSAGE='Acct Id must be Numeric ...'`, `ACCTIDL=-1`. // 273-281
3. **Else** valid: move `ACCTIDI` to `WS-ACCT-ID` and `CDEMO-ACCT-ID`; then `EVALUATE TRUE` over the 5 row
   selection inputs `SEL0001I..SEL0005I` — first non-blank/non-low row sets `CDEMO-CPVS-PAU-SEL-FLG` = that
   char and `CDEMO-CPVS-PAU-SELECTED` = `CDEMO-CPVS-AUTH-KEYS(n)`; `WHEN OTHER` clears both. // 282-309
4. If both sel-flag and selected-key are non-blank → `EVALUATE CDEMO-CPVS-PAU-SEL-FLG`:
   - `'S'`/`'s'` → set XCTL target `COPAUS1C`, `CDEMO-FROM-TRANID='CPVS'`, `CDEMO-FROM-PROGRAM='COPAUS0C'`,
     `CDEMO-PGM-CONTEXT=0`, `CDEMO-PGM-ENTER`; `EXEC CICS XCTL PROGRAM(COPAUS1C) COMMAREA(CARDDEMO-COMMAREA)`. // 313-325
   - `WHEN OTHER` → `WS-MESSAGE='Invalid selection. Valid value is S'`, `ACCTIDL=-1`. // 326-331
5. **Always** (fallthrough) `PERFORM GATHER-DETAILS`. // 337

> Note: only the **first** selected row wins (EVALUATE TRUE first-true). If user types `S` next to row 3
> and garbage next to row 1, row 1 wins → "Invalid selection" (faithful behaviour, not a bug to fix).

### GATHER-DETAILS  // source: COPAUS0C.cbl:342-358
1. `ACCTIDL=-1`; `CDEMO-CPVS-PAGE-NUM = 0`. // 345-347
2. **If `WS-ACCT-ID NOT = LOW-VALUES`**: `PERFORM GATHER-ACCOUNT-DETAILS`; `PERFORM INITIALIZE-AUTH-DATA`;
   if `FOUND-PAUT-SMRY-SEG` → `PERFORM PROCESS-PAGE-FORWARD`. // 349-356

### PROCESS-PF7-KEY (page backward)  // source: COPAUS0C.cbl:362-385
1. **If `CDEMO-CPVS-PAGE-NUM > 1`**: `CDEMO-CPVS-PAGE-NUM = CDEMO-CPVS-PAGE-NUM - 1` (COMPUTE, integer);
   `WS-AUTH-KEY-SAVE = CDEMO-CPVS-PAUKEY-PREV-PG(CDEMO-CPVS-PAGE-NUM)`; `GET-AUTH-SUMMARY`;
   `SEND-ERASE-NO`; `NEXT-PAGE-YES`; `ACCTIDL=-1`; `INITIALIZE-AUTH-DATA`; `PROCESS-PAGE-FORWARD`. // 365-379
2. **Else** `WS-MESSAGE='You are already at the top of the page...'`; `SEND-ERASE-NO`. // 380-384

### PROCESS-PF8-KEY (page forward)  // source: COPAUS0C.cbl:388-412
1. **If `CDEMO-CPVS-PAUKEY-LAST = SPACES OR LOW-VALUES`** → `WS-AUTH-KEY-SAVE = LOW-VALUES`. // 391-392
2. **Else** `WS-AUTH-KEY-SAVE = CDEMO-CPVS-PAUKEY-LAST`; `GET-AUTH-SUMMARY`; `REPOSITION-AUTHORIZATIONS`. // 393-398
3. `ACCTIDL=-1`; `SEND-ERASE-NO`. // 400-402
4. **If `NEXT-PAGE-YES`** → `INITIALIZE-AUTH-DATA`; `PROCESS-PAGE-FORWARD`. // 404-407
5. **Else** `WS-MESSAGE='You are already at the bottom of the page...'`. // 408-410

### PROCESS-PAGE-FORWARD  // source: COPAUS0C.cbl:415-454
1. **If `ERR-FLG-OFF`**: // 418
2. `WS-IDX = 1`; `CDEMO-CPVS-PAUKEY-LAST = LOW-VALUES`. // 420-422
3. `PERFORM UNTIL WS-IDX > 5 OR AUTHS-EOF OR ERR-FLG-ON`: // 424
   - If `EIBAID = DFHPF7 AND WS-IDX = 1` → `REPOSITION-AUTHORIZATIONS`, else `GET-AUTHORIZATIONS`. // 425-429
   - If `AUTHS-NOT-EOF AND ERR-FLG-OFF`: `POPULATE-AUTH-LIST`; `WS-IDX = WS-IDX + 1` (COMPUTE);
     `CDEMO-CPVS-PAUKEY-LAST = PA-AUTHORIZATION-KEY`; if `WS-IDX = 2` → `CDEMO-CPVS-PAGE-NUM = CDEMO-CPVS-PAGE-NUM + 1`
     and store this key as the page-top anchor `CDEMO-CPVS-PAUKEY-PREV-PG(page-num)`. // 430-442
4. After loop, if still `AUTHS-NOT-EOF AND ERR-FLG-OFF`: do one extra `GET-AUTHORIZATIONS` (lookahead); if
   that one is NOT-EOF&OK → `NEXT-PAGE-YES`, else `NEXT-PAGE-NO`. // 445-452

> Page logic semantics: PF7 first-iteration uses `REPOSITION-AUTHORIZATIONS` to re-seek the saved page-top
> key (which was consumed by `GATHER-...`/lookahead), then GNP forward 5 rows. The lookahead read (step 4)
> peeks one extra row to set the "more pages" flag, then is discarded — meaning on the next forward page the
> first GNP returns the row AFTER the peeked one... (see faithful-bug #2).

### GET-AUTHORIZATIONS  // source: COPAUS0C.cbl:457-486
`EXEC DLI GNP USING PCB(1) SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS)`; `IMS-RETURN-CODE = DIBSTAT`;
`STATUS-OK`→`AUTHS-NOT-EOF`; `SEGMENT-NOT-FOUND`/`END-OF-DB`→`AUTHS-EOF`; OTHER→err flag, build
`' System error while reading AUTH Details: Code:' + code` into `WS-MESSAGE`, `ACCTIDL=-1`, `SEND-PAULST-SCREEN`.
→ relational: forward child cursor `MoveNext()`. // (see §2b)

### REPOSITION-AUTHORIZATIONS  // source: COPAUS0C.cbl:488-519
`MOVE WS-AUTH-KEY-SAVE TO PA-AUTHORIZATION-KEY`; `EXEC DLI GNP … SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS)
WHERE (PAUT9CTS = PA-AUTHORIZATION-KEY)`; same status handling; error text `' System error while repos.
AUTH Details: Code:'`. → relational: seek child cursor to the row whose `AUTH_KEY = :savedKey` (and read it).

### POPULATE-AUTH-LIST  // source: COPAUS0C.cbl:522-605
1. `WS-AUTH-AMT = PA-APPROVED-AMT` (edited). // 525
2. Build `WS-AUTH-TIME` `hh:mm:ss` from `PA-AUTH-ORIG-TIME(1:2)(3:2)(5:2)` (ref-mod splice into positions 1,4,7;
   the `:` separators are the field's literal value bytes). // 527-529
3. Build date: move `PA-AUTH-ORIG-DATE(1:2/3:2/5:2)` into `WS-CURDATE-YY/-MM/-DD`, then `WS-AUTH-DATE =
   WS-CURDATE-MM-DD-YY` (mm/dd/yy). // 531-534  **Reuses the shared CSDAT01Y date work-fields — see PORT NOTES.**
4. `WS-AUTH-APRV-STAT = 'A'` if `PA-AUTH-RESP-CODE = '00'`, else `'D'`. // 536-540
5. `EVALUATE WS-IDX` (1..5): write the row's fields into the matching screen fields and save the auth key:
   - `CDEMO-CPVS-AUTH-KEYS(n) = PA-AUTHORIZATION-KEY`
   - `TRNIDnnI = PA-TRANSACTION-ID`, `PDATEnnI = WS-AUTH-DATE`, `PTIMEnnI = WS-AUTH-TIME`,
     `PTYPEnnI = PA-AUTH-TYPE`, `PAPRVnnI = WS-AUTH-APRV-STAT`, `PSTATnnI = PA-MATCH-STATUS`,
     `PAMT00nI = WS-AUTH-AMT`, `SELnnnnA = DFHBMUNP` (unprotect the selection field). // 542-605

### INITIALIZE-AUTH-DATA  // source: COPAUS0C.cbl:608-662
`PERFORM VARYING WS-IDX 1..5`: for each of the 5 rows protect the selection field (`SELnnnnA = DFHBMPRO`) and
blank the row output fields (`TRNID`, `PDATE`, `PTIME`, `PTYPE`, `PAPRV`, `PSTAT`, `PAMT`). Clears the list grid.

### RETURN-TO-PREV-SCREEN  // source: COPAUS0C.cbl:665-677
If `CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES` default to `'COSGN00C'`; set `CDEMO-FROM-TRANID='CPVS'`,
`CDEMO-FROM-PROGRAM='COPAUS0C'`, `CDEMO-PGM-CONTEXT=0`; `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM)
COMMAREA(CARDDEMO-COMMAREA)`. (Called from PF3 with `CDEMO-TO-PROGRAM='COMEN01C'`.)

### SEND-PAULST-SCREEN  // source: COPAUS0C.cbl:681-709
1. **If `IMS-PSB-SCHD`** → set `IMS-PSB-NOT-SCHD`; `EXEC CICS SYNCPOINT`. (commit/unschedule IMS before terminal I/O). // 684-688
2. `PERFORM POPULATE-HEADER-INFO`; `ERRMSGO = WS-MESSAGE`. // 690-692
3. If `SEND-ERASE-YES` → `SEND MAP('COPAU0A') MAPSET('COPAU00') FROM(COPAU0AO) ERASE CURSOR`;
   else same SEND without ERASE. // 694-708

### RECEIVE-PAULST-SCREEN  // source: COPAUS0C.cbl:712-722
`EXEC CICS RECEIVE MAP('COPAU0A') MAPSET('COPAU00') INTO(COPAU0AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`.

### POPULATE-HEADER-INFO  // source: COPAUS0C.cbl:726-747
`WS-CURDATE-DATA = FUNCTION CURRENT-DATE`; set `TITLE01O=CCDA-TITLE01`, `TITLE02O=CCDA-TITLE02`,
`TRNNAMEO='CPVS'`, `PGMNAMEO='COPAUS0C'`; build `CURDATEO` (mm/dd/yy from current date) and `CURTIMEO`
(hh:mm:ss from current time).

### GATHER-ACCOUNT-DETAILS  // source: COPAUS0C.cbl:750-808
1. `GETCARDXREF-BYACCT`; `GETACCTDATA-BYACCT`; `GETCUSTDATA-BYCUST`. // 753-755
2. `CUSTIDO = CUST-ID`; `CNAMEO = STRING(first-name DELIM SPACES, ' ', middle-name(1:1), ' ', last-name DELIM SPACES)`. // 757-764
3. `ADDR001O = STRING(addr-line-1 DELIM '  ', ',', addr-line-2 DELIM '  ')`. // 766-770
4. `ADDR002O = STRING(addr-line-3 DELIM '  ', ',', state-cd, ',', zip(1:5))`. // 771-777
5. `PHONE1O = CUST-PHONE-NUM-1`; `CREDLIMO = edit(ACCT-CREDIT-LIMIT)` via WS-DISPLAY-AMT12;
   `CASHLIMO = edit(ACCT-CASH-CREDIT-LIMIT)` via WS-DISPLAY-AMT9. // 779-783
6. `PERFORM GET-AUTH-SUMMARY`. // 785
7. If `FOUND-PAUT-SMRY-SEG`: paint `APPRCNTO`/`DECLCNTO` (counts via WS-DISPLAY-COUNT),
   `CREDBALO`/`CASHBALO` (balances), `APPRAMTO`/`DECLAMTO` (amounts). Else `MOVE ZERO` to all six. // 787-807

### GETCARDXREF-BYACCT  // source: COPAUS0C.cbl:812-862
`MOVE WS-ACCT-ID TO WS-CARD-RID-ACCT-ID-X`; `READ DATASET(CXACAIX) RIDFLD(WS-CARD-RID-ACCT-ID-X)
INTO(CARD-XREF-RECORD)`. `NORMAL`→`CDEMO-CUST-ID=XREF-CUST-ID`, `CDEMO-CARD-NUM=XREF-CARD-NUM`.
`NOTFND`→ message `'Account:' WS-ACCT-ID ' not found in XREF file. Resp:'…' Reas:'…`, `ACCTIDL=-1`,
`SEND-PAULST-SCREEN` (**no `WS-ERR-FLG`** set — see faithful-bug #3). `OTHER`→ err flag + system-error msg +
`SEND-PAULST-SCREEN`.

### GETACCTDATA-BYACCT  // source: COPAUS0C.cbl:865-912
`MOVE XREF-ACCT-ID TO WS-CARD-RID-ACCT-ID`; `READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X)
INTO(ACCOUNT-RECORD)`. `NORMAL`→continue. `NOTFND`→ `'Account:'…' not found in ACCT file…'`, `ACCTIDL=-1`,
`SEND` (no err flag). `OTHER`→ err flag + system-error msg + `SEND`.

### GETCUSTDATA-BYCUST  // source: COPAUS0C.cbl:915-963
`MOVE XREF-CUST-ID TO WS-CARD-RID-CUST-ID`; `READ DATASET(CUSTDAT) RIDFLD(WS-CARD-RID-CUST-ID-X)
INTO(CUSTOMER-RECORD)`. `NORMAL`→continue. `NOTFND`→ `'Customer:'…' not found in CUST file…'` (no err flag).
`OTHER`→ err flag + system-error msg. Both error paths `SEND`.

### GET-AUTH-SUMMARY  // source: COPAUS0C.cbl:966-997
1. `PERFORM SCHEDULE-PSB`. // 969
2. `PA-ACCT-ID = CDEMO-ACCT-ID` (commented-out alt: from `XREF-ACCT-ID`). // 971-972
3. `EXEC DLI GU USING PCB(1) SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) WHERE (ACCNTID = PA-ACCT-ID)`. // 973-977
4. `IMS-RETURN-CODE = DIBSTAT`; `STATUS-OK`→`FOUND-PAUT-SMRY-SEG`; `SEGMENT-NOT-FOUND`→`NFOUND-PAUT-SMRY-SEG`;
   OTHER→ err flag + `' System error while reading AUTH Summary: Code:'` + code + `SEND`. // 979-996

### SCHEDULE-PSB  // source: COPAUS0C.cbl:1001-1031
`EXEC DLI SCHD PSB('PSBPAUTB') NODHABEND`; `IMS-RETURN-CODE=DIBSTAT`; if `PSB-SCHEDULED-MORE-THAN-ONCE`('TC')
→ `EXEC DLI TERM` then re-`SCHD` + recapture status. If `STATUS-OK`→`IMS-PSB-SCHD` TRUE; else err flag +
`' System error while scheduling PSB: Code:'` + code + `SEND`.

---

## 5. ONLINE: pseudo-conversational flow & BMS

### Mapset / map
- **Mapset `COPAU00`, map `COPAU0A`**, size 24×80, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, MODE=INOUT, TIOAPFX=YES.
  Symbolic input struct `COPAU0AI` / output `COPAU0AO`. // source: COPAU00.bms:19-28; COPAUS0C.cbl:696-697, 715-718

### Fields READ from screen (in `COPAU0AI`)
- `ACCTIDI` X(11) — searched account id (UNPROT, FSET). // bms:84-88; cbl:264-283
- `SEL0001I..SEL0005I` X(1) — per-row selection char (UNPROT). // bms:277,321,365,409,488; cbl:286-303
- AID via `EIBAID` (ENTER/PF3/PF7/PF8). // cbl:224-250

### Fields WRITTEN to screen (in `COPAU0AO` / `…I` for grid fields)
- Header: `TITLE01O`, `TITLE02O`, `TRNNAMEO`, `PGMNAMEO`, `CURDATEO`, `CURTIMEO`. // cbl:731-746
- Account header block: `CUSTIDO`, `CNAMEO`, `ADDR001O`, `ADDR002O`, `PHONE1O`, `CREDLIMO`, `CASHLIMO`,
  `APPRCNTO`, `DECLCNTO`, `CREDBALO`, `CASHBALO`, `APPRAMTO`, `DECLAMTO`. // cbl:757-807
  - Note: `ACCSTAT` (Acct Status) field exists in the map but is **never populated** by this program. // bms:114-117 (no MOVE in cbl)
- Grid rows 1..5: `TRNIDnnI`, `PDATEnnI`, `PTIMEnnI`, `PTYPEnnI`, `PAPRVnnI`, `PSTATnnI`, `PAMT00nI` plus
  attribute byte `SELnnnnA` (DFHBMUNP when populated / DFHBMPRO when cleared). // cbl:547-605, 614-657
- `ERRMSGO` X(78) — message line. // bms:503-506; cbl:692
- `ACCTIDL` (length attribute of ACCTID) set to `-1` repeatedly to force the cursor to the acct-id field. // cbl:188,247,…

### Pseudo-conversational state machine
```
TERM ──CPVS──▶ EIBCALEN=0 ──▶ SEND COPAU0A (ERASE, empty) ──▶ RETURN TRANSID CPVS
                                                                     │
   ┌─────────────────────────────────────────────────────────────── ┘
   ▼ (commarea present)
   NOT PGM-REENTER  ──▶ paint from CDEMO-ACCT-ID (GATHER-DETAILS) ─▶ SEND (ERASE) ─▶ RETURN
   PGM-REENTER      ──▶ RECEIVE ─▶ EVALUATE EIBAID:
        ENTER ─▶ PROCESS-ENTER-KEY (validate, maybe XCTL COPAUS1C) ─▶ SEND ─▶ RETURN
        PF3   ─▶ XCTL COMEN01C
        PF7   ─▶ page back  ─▶ SEND (no ERASE) ─▶ RETURN
        PF8   ─▶ page fwd   ─▶ SEND (no ERASE) ─▶ RETURN
        other ─▶ "invalid key" ─▶ SEND ─▶ RETURN
```
// source: COPAUS0C.cbl:190-257

### XCTL / LINK targets
- `XCTL COPAUS1C` (row selected with `S`). // cbl:316-325
- `XCTL COMEN01C` (PF3, via RETURN-TO-PREV-SCREEN; default `COSGN00C` if to-program blank). // cbl:236-238, 665-677
- No `LINK`, no called subprograms.

---

## 6. VALIDATION RULES & EXACT LITERAL MESSAGES

| Condition | Exact message text | source |
|---|---|---|
| Acct id blank/low | `Please enter Acct Id...` | cbl:268-269 |
| Acct id non-numeric | `Acct Id must be Numeric ...` | cbl:277-278 |
| Selection flag set but not `S`/`s` | `Invalid selection. Valid value is S` | cbl:327-328 |
| PF7 at first page | `You are already at the top of the page...` | cbl:381 |
| PF8 with no further pages | `You are already at the bottom of the page...` | cbl:409 |
| Unsupported AID key | `CCDA-MSG-INVALID-KEY` (common-message copybook CSMSG01Y literal) | cbl:248 |
| XREF acct not found | `Account:` + WS-ACCT-ID + ` not found in XREF file. Resp:` + resp + ` Reas:` + reas | cbl:836-843 |
| XREF system error | `Account:` + key + ` System error while reading XREF file. Resp:` … ` Reas:` … | cbl:851-858 |
| ACCT not found | `Account:` + key + ` not found in ACCT file. Resp:` … ` Reas:` … | cbl:886-893 |
| ACCT system error | `Account:` + key + ` System error while reading ACCT file. Resp:` … ` Reas:` … | cbl:901-908 |
| CUST not found | `Customer:` + key + ` not found in CUST file. Resp:` … ` Reas:` … | cbl:937-944 |
| CUST system error | `Customer:` + key + ` System error while reading CUST file. Resp:` … ` Reas:` … | cbl:952-959 |
| IMS GU summary error | ` System error while reading AUTH Summary: Code:` + DIBSTAT | cbl:988-993 |
| IMS GNP detail error | ` System error while reading AUTH Details: Code:` + DIBSTAT | cbl:476-481 |
| IMS reposition error | ` System error while repos. AUTH Details: Code:` + DIBSTAT | cbl:509-514 |
| IMS SCHD error | ` System error while scheduling PSB: Code:` + DIBSTAT | cbl:1022-1027 |

> Reproduce all messages byte-for-byte including leading spaces, the literal embedded label text, and the
> trailing `...` / spacing. `WS-RESP-CD-DIS` / `WS-REAS-CD-DIS` are `9(09)` so the codes appear zero-padded
> to 9 digits. `IMS-RETURN-CODE` is 2 chars.

---

## 7. FAITHFUL BUGS (reproduce verbatim — DO NOT FIX)

1. **Dead `SEND-PAULST-SCREEN` after `RETURN-TO-PREV-SCREEN` (PF3).** MAIN-PARA PF3 branch performs
   `RETURN-TO-PREV-SCREEN` (which does an unconditional `EXEC CICS XCTL`) and then `PERFORM SEND-PAULST-SCREEN`.
   The SEND can never execute because XCTL transfers control away. Keep the unreachable call in the port's
   control flow model. // source: COPAUS0C.cbl:235-238, 674-677

2. **PF8 forward-paging skips the look-ahead row.** `PROCESS-PAGE-FORWARD` peeks one extra `GET-AUTHORIZATIONS`
   purely to set `NEXT-PAGE-YES/NO` (lines 445-452) and discards that record. On the subsequent PF8,
   `PROCESS-PF8-KEY` saves `CDEMO-CPVS-PAUKEY-LAST` (the LAST *displayed* key, not the peeked one) and
   repositions to it via `REPOSITION-AUTHORIZATIONS`, then `PROCESS-PAGE-FORWARD` re-reads from that point.
   Because the IMS reposition `WHERE (PAUT9CTS = key)` re-reads the saved row and the loop's first GNP after
   reposition moves to the *next* row, the peeked-but-discarded row IS shown again on the next page (it is the
   first row of the new page). Net effect: the look-ahead row is **re-read**, so no row is actually skipped —
   BUT the forward path on the *first* page (entry via ENTER/GATHER-DETAILS, where `EIBAID≠DFHPF7`) consumes
   the look-ahead row WITHOUT saving an anchor for it, so the boundary row handling differs between the
   first page and PF8 pages. Port the exact reposition+lookahead sequence; do not "optimise" the cursor.
   // source: COPAUS0C.cbl:424-452, 488-519, 391-407

3. **NOTFND on XREF/ACCT/CUST does not set `WS-ERR-FLG`.** The three `GET…` paragraphs set the error flag
   only on `WHEN OTHER`, NOT on `DFHRESP(NOTFND)`. They emit the "not found" message and `SEND` the screen,
   but because `ERR-FLG` stays OFF, after returning up the call stack `GATHER-ACCOUNT-DETAILS` keeps going
   (e.g. on XREF NOTFND it still performs `GETACCTDATA-BYACCT` with a stale `XREF-ACCT-ID`, then
   `GETCUSTDATA-BYCUST`, then `GET-AUTH-SUMMARY`), and `GATHER-DETAILS` then performs `INITIALIZE-AUTH-DATA`
   and `PROCESS-PAGE-FORWARD` on garbage. So a single "not found" screen is sent multiple times and the final
   screen reflects whichever SEND ran last. Reproduce: NOTFND must NOT abort the gather sequence. // source: COPAUS0C.cbl:832-845 (XREF), 882-895 (ACCT), 933-946 (CUST), 349-356, 750-755

4. **Multiple `SEND-PAULST-SCREEN` per transaction turn.** Because error paragraphs SEND inline and the
   caller also SENDs at the end of MAIN-PARA, an error path can SEND the map two (or more) times before the
   single `EXEC CICS RETURN`. Only the last physical SEND matters at the terminal, but the extra SENDs each
   also do `SYNCPOINT`/unschedule logic (SEND-PAULST-SCREEN lines 684-688). Preserve the repeated SEND calls.
   // source: COPAUS0C.cbl:681-709 + every inline `PERFORM SEND-PAULST-SCREEN`

5. **Shared date work-fields clobbered between header and grid.** `POPULATE-AUTH-LIST` reuses the global
   `WS-CURDATE-YY/-MM/-DD` (CSDAT01Y) to format each row's authorization date (lines 531-534), overwriting the
   values set by `POPULATE-HEADER-INFO`. Because `POPULATE-HEADER-INFO` runs again inside `SEND-PAULST-SCREEN`
   (line 690) right before the SEND, the header date is recomputed and is correct at SEND time — but any logic
   that relied on those fields between the grid build and the SEND would see row values. Faithfully share one
   set of date work-fields; recompute header date inside the send path. // source: COPAUS0C.cbl:531-534, 690, 729-740

6. **Spelling `repos.` and embedded leading spaces in messages.** IMS error strings begin with a leading
   space and the reposition error literally reads `repos.`. Keep verbatim. // source: COPAUS0C.cbl:510, 477, 989, 1023

---

## 8. PORT NOTES (relational translation plan + tricky semantics)

### IMS → relational
- **PSB scheduling / SYNCPOINT / TERM**: model as a logical unit-of-work on the PAUT repositories. The
  `SCHD`→`IMS-PSB-SCHD`, `SEND`→`SYNCPOINT`+unschedule, `TC`→`TERM`+re-`SCHD` dance has no relational analogue
  beyond opening/closing a read transaction; preserve the `WS-IMS-PSB-SCHD-FLG` state machine so the
  characterization tests see the same flag transitions and so no extra commits leak. // source: cbl:684-688, 1001-1031
- **GU summary** → `SELECT … FROM PAUT_SUMMARY WHERE ACCT_ID=:acctId`; map `STATUS-OK`→found,
  `GE`('GE')→not-found, anything else→system-error message path. `DIBSTAT` blank ('  ') and 'FW' both count as
  OK (88 `STATUS-OK VALUE '  ','FW'`). // source: cbl:79, 980-996
- **GNP forward** → a per-account ordered cursor `SELECT … FROM PAUT_DETAIL WHERE ACCT_ID=:acctId ORDER BY
  AUTH_KEY ASC`; `MoveNext()`; end-of-rows → `AUTHS-EOF`. **AUTH_KEY ascending = newest first** (9s-complement);
  do NOT reorder. // source: IMS_SCHEMA.md:161-165
- **GNP reposition (`WHERE PAUT9CTS = key`)** → re-open the same ordered cursor positioned at the first row
  with `AUTH_KEY = :savedKey` (and return that row). Implement as seek-then-read so the subsequent forward
  GNPs continue from there. // source: cbl:488-519
- **Key comparison/collation**: `AUTH_KEY` is an 8-byte value (packed date+time, 9s-complement). Store as
  `CHAR(8)`/`BINARY(8)` and compare ordinally; ASCII vs EBCDIC collation is irrelevant for the binary key but
  ensure ordinal compare to mirror IMS twin order. // source: IMS_SCHEMA.md:104-106, 155-159

### VSAM → relational
- `CXACAIX` is the card-xref **alternate index keyed by acct id**; one acct may have multiple cards, but the
  program takes the first hit (CICS keyed READ returns the first record on the path). Port:
  `SELECT … FROM CARD_XREF WHERE acct_id=:a ORDER BY xref_card_num LIMIT 1` (faithful "first" semantics).
  Map RESP NORMAL/NOTFND/other to the three branches. // source: cbl:812-862
- `ACCTDAT`/`CUSTDAT` are simple PK reads → `WHERE acct_id=` / `WHERE cust_id=`.
- The RIDFLD redefine trick (`WS-CARD-RID-ACCT-ID` numeric vs `…-X` char over the same 11 bytes; cust id 9
  bytes) is just type punning — port passes the id as a string key. // source: cbl:65-72, 817, 868, 918

### COBOL semantics to preserve
- **REDEFINES** `WS-CARD-RID-*` (numeric vs char) and CSDAT01Y `WS-CURDATE-N`/`WS-CURTIME-N` — same bytes,
  pick the char form for keys. // source: cbl:65-72; CSDAT01Y.cpy:23,29
- **OCCURS**: `CDEMO-CPVS-PAUKEY-PREV-PG(20)`, `CDEMO-CPVS-AUTH-KEYS(5)`, `PA-ACCOUNT-STATUS(5)` → fixed-size
  arrays / 5 flattened columns (per IMS_SCHEMA.md). Page-anchor array indexed by `CDEMO-CPVS-PAGE-NUM`
  (1-based); guard the 20-element bound (program never checks it — paging past page 20 would overflow; faithful
  but a latent bug — note it). // source: cbl:120,126; CIPAUSMY.cpy:22; cbl:368,440
- **Edited PIC** (`-zzzzzzz9.99`, `-zzzz9.99`, `9(03)`): use the Runtime `CobolEditedNumeric` formatter; sign
  is leading floating `-`, zero-suppression with `z`, blank for positive sign position. Width mismatch between
  `WS-AUTH-AMT`(11) and screen `PAMTnnn`(12) yields a trailing blank on MOVE (left-justified into the larger
  alphanumeric receiver). // source: cbl:55-58
- **STRING with `DELIMITED BY SPACES` / `'  '`** in name/address building: replicate exactly — name uses
  `DELIMITED BY SPACES` (stops at first space) for first/last and `(1:1)` for middle initial; address uses
  `DELIMITED BY '  '` (two-space delimiter) which truncates each address line at the first run of 2 spaces.
  Reproduce the delimiter semantics precisely (these are easy to get subtly wrong). // source: cbl:758-777
- **Ref-mod splice** building `WS-AUTH-TIME`/date from `(1:2)/(3:2)/(5:2)` substrings — copy character ranges,
  keep the literal `:` / `/` separators that live in the receiving field's VALUE. // source: cbl:527-534
- **INITIALIZE CARDDEMO-COMMAREA** on cold start (EIBCALEN=0): zero/space the whole commarea incl. the
  CPVS extension. // source: cbl:191
- **`MOVE LOW-VALUES TO COPAU0AO`**: blanks the entire output map symbolic (LOW-VALUES = x'00' → BMS treats as
  "no data / default attribute"); port should treat as "clear all output fields to unset". // source: cbl:195,205
- **`ACCTIDL = -1`** is the BMS cursor-positioning idiom (negative length attr forces cursor). Model as
  "cursor → ACCTID field". // source: cbl:188 and throughout
- **`SEL000nA = DFHBMUNP / DFHBMPRO`**: per-row selection field attribute toggled between unprotect (row has
  data, selectable) and protect (empty row). Model as a per-row "selectable" attribute. // source: cbl:554,614 etc.

### Counts / arithmetic
- `WS-IDX = WS-IDX + 1`, `CDEMO-CPVS-PAGE-NUM ± 1` are plain S9(04) COMP integer COMPUTEs — no rounding, no
  fractional part; overflow not possible at these magnitudes. // source: cbl:366, 432, 437-438
- Counts moved to `WS-DISPLAY-COUNT PIC 9(03)`: values > 999 truncate to low-order 3 digits (faithful). // source: cbl:58, 788-791

---

## 9. OPEN QUESTIONS / RISKS

1. **`PAUTSUM0` re-read on every page turn.** `PROCESS-PF7/PF8` call `GET-AUTH-SUMMARY` (a GU on the summary)
   before paging details (cbl:370, 396). This re-schedules the PSB and re-reads the summary each turn. The
   relational port should mirror the re-read (cheap SELECT) for behavioural parity but it has no functional
   effect on the detail paging. Confirm with owners that re-reading the summary on PF7/PF8 is intended.
2. **First-page vs PF8-page look-ahead asymmetry** (faithful-bug #2): the exact set of rows shown on page
   boundaries depends on subtle GNP/reposition interplay. Characterization fixtures must be captured from a
   trusted run (no COBOL oracle available per ARCHITECTURE.md §Verification(4)); pin the observed paging with
   a scripted online-parity test over seeded PAUT_DETAIL data.
3. **Page-anchor array bound (20).** `CDEMO-CPVS-PAUKEY-PREV-PG OCCURS 20` with no bounds check; >20 pages
   would corrupt adjacent commarea storage. Decide whether to faithfully allow the overflow (matches source)
   or guard it; recommend faithful (allow) with a pinning note, since the screen only ever pages 5 at a time
   and realistic data is small.
4. **`CDEMO-CPVS-AUTH-KEYS` selection mapping.** ENTER selection uses `CDEMO-CPVS-AUTH-KEYS(n)` which were set
   during the PREVIOUS turn's `POPULATE-AUTH-LIST`. The port must persist these 5 keys in the COMMAREA across
   the pseudo-conversational turn so selection resolves to the right detail key. // source: cbl:288-305, 544-593
5. **`ACCSTAT` map field unused.** The map has an Acct-Status field that this program never fills (the detail
   program may). Leave it blank in the port to match. // source: bms:114-117
