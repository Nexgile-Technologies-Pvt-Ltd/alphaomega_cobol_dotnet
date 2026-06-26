# PORT SPEC — COACTUPC (Account Update, ONLINE / CICS)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COACTUPC.cbl`
> BMS map/mapset: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COACTUP.bms` (mapset `COACTUP`, map `CACTUPA`)
> Transaction id: **`CAUP`**   Program: **`COACTUPC`**
> Target: `src/CardDemo.Online` + `src/CardDemo.ConsoleApp` over the relational SQLite schema in `_design/ARCHITECTURE.md`.
> All line citations refer to `COACTUPC.cbl` unless otherwise stated.

---

## 1. Purpose & Invocation

**Purpose.** COACTUPC is the online (pseudo-conversational CICS) **Account Update** transaction. It lets a user (a) enter an 11-digit account number, (b) fetch and display the joined Account + Customer detail for that account, (c) overtype any of the displayed account/customer fields, (d) have every field validated with field-level edits and a battery of cross-field edits (state code, ZIP-vs-state, area code, leap-year date checks, SSN structure, FICO range, date-of-birth-not-in-future), (e) confirm via F5, and (f) commit the changes to the Account master and Customer master under optimistic locking — re-reading both records for update, detecting concurrent modification ("record changed by someone else"), and rewriting both. It is a single-screen, multi-state pseudo-conversational program whose state is carried in a 2000-byte COMMAREA. // source: 1-5, 858-1020

**Invocation.**
- CICS transaction **`CAUP`** (`LIT-THISTRANID`, line 535-536). The program XCTLs to/from the menu (`COMEN01C`/`CM00`) and other programs (`COCRDUPC`, `COCRDLIC`, `COCRDSLC`) via the COMMAREA `CDEMO-TO-PROGRAM`. // source: 535-572, 956-959
- Entered fresh from the menu (`CDEMO-FROM-PROGRAM = LIT-MENUPGM`), or re-entered to itself each pseudo-conversational turn via `EXEC CICS RETURN TRANSID('CAUP') COMMAREA(WS-COMMAREA)`. // source: 880-893, 1015-1019
- No JCL; this is a terminal-driven CICS program. It is **not** a callable subprogram (it owns `DFHCOMMAREA` of variable length and issues XCTL/RETURN). // source: 853-857, 956-959, 1015-1019

---

## 2. FILE / TABLE ACCESS TABLE

Logical files (VSAM datasets named via literals, lines 573-582) and their relational targets per ARCHITECTURE.md §48-72:

| COBOL DATASET literal | VSAM access | Key field (RIDFLD) | Relational table | Ops used | SQL mapping |
|---|---|---|---|---|---|
| `CXACAIX` (`LIT-CARDXREFNAME-ACCT-PATH`) | alternate-index path on CARD-XREF by acct id | `WS-CARD-RID-ACCT-ID-X` X(11) | `CARD_XREF` (idx acct_id) | `EXEC CICS READ` (random, by alt key) | `SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF WHERE acct_id=@acct LIMIT 1`; NORMAL→take cust_id+card_num; NOTFND→error. // source: 581-582, 3650-3697 |
| `ACCTDAT` (`LIT-ACCTFILENAME`) | KSDS by acct id | `WS-CARD-RID-ACCT-ID-X` X(11) | `ACCOUNT` (PK acct_id) | `READ` (random); `READ … UPDATE`; `REWRITE` | READ: `SELECT * FROM ACCOUNT WHERE acct_id=@acct`; READ UPDATE: same select + begin row lock/snapshot; REWRITE: `UPDATE ACCOUNT SET … WHERE acct_id=@acct`. // source: 573-574, 3701-3748, 3894-3915, 4065-4071 |
| `CUSTDAT` (`LIT-CUSTFILENAME`) | KSDS by cust id | `WS-CARD-RID-CUST-ID-X` X(9) | `CUSTOMER` (PK cust_id) | `READ` (random); `READ … UPDATE`; `REWRITE` | READ: `SELECT * FROM CUSTOMER WHERE cust_id=@cust`; READ UPDATE: same + lock/snapshot; REWRITE: `UPDATE CUSTOMER SET … WHERE cust_id=@cust`. // source: 575-576, 3752-3799, 3921-3942, 4085-4091 |

Notes:
- `CARDDAT`/`CARDAIX` literals (577-580) are declared but **not used** by any EXEC CICS in this program — ignore. // source: 577-580 (no READ/WRITE references them)
- This program never STARTBR/READNEXT/READPREV/WRITE/DELETE. Only random keyed READ, READ-for-UPDATE, and REWRITE. // source: 3654, 3703, 3753, 3894, 3921, 4065, 4085
- `EXEC CICS SYNCPOINT` is issued on the F3-exit path (line 952-954) and `EXEC CICS SYNCPOINT ROLLBACK` if the customer REWRITE fails after the account REWRITE succeeded (line 4099-4101). In the .NET port these map to commit / rollback of a single transaction spanning both UPDATEs.

**Repository contract (ARCHITECTURE.md §80-89):** READ key → SELECT by PK → FileStatus/RESP `NORMAL`/`NOTFND`. READ UPDATE → SELECT + take a "before image" for the optimistic concurrency check. REWRITE → UPDATE (missing → not-NORMAL). The "could not lock" branches here are really "READ UPDATE returned non-NORMAL". // source: 3907-3915, 3934-3942, 4076-4103

---

## 3. WORKING-STORAGE / record layouts (typed)

Record copybooks (one column per elementary field, per ARCHITECTURE.md type map):

- `ACCOUNT-RECORD` (COPY CVACT01Y, line 640): `ACCT-ID 9(11)`, `ACCT-ACTIVE-STATUS X(1)`, `ACCT-CURR-BAL S9(10)V99`, `ACCT-CREDIT-LIMIT S9(10)V99`, `ACCT-CASH-CREDIT-LIMIT S9(10)V99`, `ACCT-OPEN-DATE X(10)`, `ACCT-EXPIRAION-DATE X(10)`, `ACCT-REISSUE-DATE X(10)`, `ACCT-CURR-CYC-CREDIT S9(10)V99`, `ACCT-CURR-CYC-DEBIT S9(10)V99`, `ACCT-ADDR-ZIP X(10)`, `ACCT-GROUP-ID X(10)`. → `ACCOUNT`.
- `CARD-XREF-RECORD` (COPY CVACT03Y, line 643): `XREF-CARD-NUM X(16)`, `XREF-CUST-ID 9(9)`, `XREF-ACCT-ID 9(11)`. → `CARD_XREF`.
- `CUSTOMER-RECORD` (COPY CVCUS01Y, line 646): `CUST-ID 9(9)`, names X(25)×3, addr lines X(50)×3, `CUST-ADDR-STATE-CD X(2)`, `CUST-ADDR-COUNTRY-CD X(3)`, `CUST-ADDR-ZIP X(10)`, `CUST-PHONE-NUM-1/2 X(15)`, `CUST-SSN 9(9)`, `CUST-GOVT-ISSUED-ID X(20)`, `CUST-DOB-YYYY-MM-DD X(10)`, `CUST-EFT-ACCOUNT-ID X(10)`, `CUST-PRI-CARD-HOLDER-IND X(1)`, `CUST-FICO-CREDIT-SCORE 9(3)`. → `CUSTOMER`.
- `ACCT-UPDATE-RECORD` (lines 418-433, RECLN 300): the rewrite image for ACCOUNT (note `ACCT-UPDATE-OPEN-DATE` etc. are **X(10)** with embedded `-`). Maps to the ACCOUNT row written by REWRITE.
- `CUST-UPDATE-RECORD` (lines 434-456, RECLN 300): the rewrite image for CUSTOMER. Maps to CUSTOMER row written by REWRITE.

**COMMAREA layouts** (carried across pseudo-conversational turns; total ≤ 2000 bytes, `WS-COMMAREA PIC X(2000)`, line 850):
- `CARDDEMO-COMMAREA` (COPY COCOM01Y, line 650): general nav info `CDEMO-FROM-TRANID X(4)`, `CDEMO-FROM-PROGRAM X(8)`, `CDEMO-TO-TRANID X(4)`, `CDEMO-TO-PROGRAM X(8)`, `CDEMO-USER-ID X(8)`, `CDEMO-USER-TYPE X(1)` (88 ADMIN 'A'/USER 'U'), `CDEMO-PGM-CONTEXT 9(1)` (88 `CDEMO-PGM-ENTER` 0 / `CDEMO-PGM-REENTER` 1); customer info `CDEMO-CUST-ID 9(9)` + 3 names; account info `CDEMO-ACCT-ID 9(11)`, `CDEMO-ACCT-STATUS X(1)`; `CDEMO-CARD-NUM 9(16)`; `CDEMO-LAST-MAP X(7)`, `CDEMO-LAST-MAPSET X(7)`. // source: COCOM01Y.cpy:19-44
- `WS-THIS-PROGCOMMAREA` (lines 652-849): program-private state appended **after** `CARDDEMO-COMMAREA` in the 2000-byte buffer (see §6 split). Contains:
  - `ACUP-CHANGE-ACTION X(1)` (line 654) — the master state flag with 88-levels: `ACUP-DETAILS-NOT-FETCHED` (LOW-VALUES/SPACES), `ACUP-SHOW-DETAILS` ('S'), `ACUP-CHANGES-MADE` ('E','N','C','L','F'), `ACUP-CHANGES-NOT-OK` ('E'), `ACUP-CHANGES-OK-NOT-CONFIRMED` ('N'), `ACUP-CHANGES-OKAYED-AND-DONE` ('C'), `ACUP-CHANGES-FAILED` ('L','F'), `ACUP-CHANGES-OKAYED-LOCK-ERROR` ('L'), `ACUP-CHANGES-OKAYED-BUT-FAILED` ('F'). // source: 654-668
  - `ACUP-OLD-DETAILS` (669-756): the "before image" of all account+customer fields, with money fields as **X(12) overlaid by S9(10)V99** and dates split into Y/M/D parts. This is the snapshot used both for the screen "original values" and for the changed-since-fetch comparison.
  - `ACUP-NEW-DETAILS` (757-849): the user-entered "after image", same shape, plus `ACUP-NEW-CUST-SSN-X` split into 1/2/3 parts (830-835) and `FICO-RANGE-IS-VALID` 88 = 300 THRU 850 (848-849).

**Generic edit work fields** (`WS-GENERIC-EDITS`, 52-146) and program-specific flag groups (`WS-NON-KEY-FLAGS`, 191-352) — every editable field has a 1-byte flag with 88-levels `…-ISVALID` (LOW-VALUES), `…-NOT-OK` ('0'), `…-BLANK` ('B'). Date edit work area comes from COPY CSUTLDWY (line 166). These flags drive cursor positioning and red-highlight attributes in §7.4/§7.5 — port them as a per-field validity enum {Valid, NotOk, Blank}.

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (each = one method)

### 4.1 Driver / pseudo-conversational shell

**`0000-MAIN`** (859-1005). (1) `HANDLE ABEND LABEL(ABEND-ROUTINE)` (862-864). (2) INITIALIZE work areas; MOVE 'CAUP' to WS-TRANID; clear return message (866-876). (3) If `EIBCALEN=0` OR (came from menu and not re-enter): INITIALIZE both COMMAREAs, set `CDEMO-PGM-ENTER` and `ACUP-DETAILS-NOT-FETCHED`; else split the inbound `DFHCOMMAREA` into `CARDDEMO-COMMAREA` (first `LENGTH OF CARDDEMO-COMMAREA` bytes) and `WS-THIS-PROGCOMMAREA` (next bytes) (880-893). (4) PERFORM `YYYY-STORE-PFKEY` (898-899). (5) Re-map AID validity: only ENTER, PF03, (PF05 when `ACUP-CHANGES-OK-NOT-CONFIRMED`), (PF12 when not `ACUP-DETAILS-NOT-FETCHED`) are valid; any invalid key is forced to ENTER (905-916). (6) `EVALUATE TRUE`:
  - WHEN `CCARD-AID-PFK03` → exit path: pick `CDEMO-TO-TRANID`/`CDEMO-TO-PROGRAM` from FROM fields (default menu CM00/COMEN01C), stamp FROM=this, set USRTYP-USER + PGM-ENTER + LAST-MAPSET/MAP, `SYNCPOINT`, `XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. // source: 927-959
  - WHEN (`ACUP-DETAILS-NOT-FETCHED` AND `CDEMO-PGM-ENTER`) OR (from menu AND not re-enter) → fresh entry: INITIALIZE `WS-THIS-PROGCOMMAREA`, send map, set PGM-REENTER + DETAILS-NOT-FETCHED, GO TO COMMON-RETURN. // source: 964-973
  - WHEN `ACUP-CHANGES-OKAYED-AND-DONE` OR `ACUP-CHANGES-FAILED` → reset: INITIALIZE program commarea + misc + `CDEMO-ACCT-ID`, set PGM-ENTER, send map, set PGM-REENTER + DETAILS-NOT-FETCHED, GO TO COMMON-RETURN. // source: 979-989
  - WHEN OTHER → PERFORM `1000-PROCESS-INPUTS`, `2000-DECIDE-ACTION`, `3000-SEND-MAP`, GO TO COMMON-RETURN. // source: 996-1003

**`COMMON-RETURN`** (1007-1020). MOVE `WS-RETURN-MSG` to `CCARD-ERROR-MSG`; reassemble `WS-COMMAREA` = `CARDDEMO-COMMAREA` ++ `WS-THIS-PROGCOMMAREA`; `EXEC CICS RETURN TRANSID('CAUP') COMMAREA(WS-COMMAREA) LENGTH(2000)`. (Pseudo-conversational turn boundary.) // source: 1007-1020

### 4.2 Input processing (1000-series)

**`1000-PROCESS-INPUTS`** (1025-1034). PERFORM `1100-RECEIVE-MAP`, `1200-EDIT-MAP-INPUTS`; copy return msg to `CCARD-ERROR-MSG`; set `CCARD-NEXT-PROG/MAPSET/MAP` to this program/map. // source: 1025-1034

**`1100-RECEIVE-MAP`** (1039-1428). `EXEC CICS RECEIVE MAP('CACTUPA') MAPSET('COACTUP') INTO(CACTUPAI)` (1040-1045). INITIALIZE `ACUP-NEW-DETAILS`. Then for **every** input field: if map field = '*' or SPACES → MOVE LOW-VALUES to the NEW field; else MOVE the value in. The account id is always processed (1051-1058); if `ACUP-DETAILS-NOT-FETCHED`, GO TO exit (only the account number matters on the search screen) (1060-1062). For the money fields (Credit Limit 1073-1084, Cash Limit 1087-1098, Curr Bal 1101-1112, Curr Cyc Credit 1115-1126, Curr Cyc Debit 1129-1140): if non-blank, MOVE the raw X into `…-X`, and **if `FUNCTION TEST-NUMVAL-C(...) = 0`** (i.e. the string is a parseable signed/edited number) `COMPUTE …-N = FUNCTION NUMVAL-C(...)` (parse currency → packed numeric); else CONTINUE (leave the raw chars). Date parts (open/expiry/reissue Y/M/D 1144-1209), group id, customer id, SSN parts 1/2/3, DOB Y/M/D, FICO, names, address lines, state, country, zip, phone1 A/B/C, phone2 A/B/C, govt id, EFT, primary-holder are all moved the same `'*'/SPACES→LOW-VALUES else value` way. // source: 1039-1425

**`1200-EDIT-MAP-INPUTS`** (1429-1680). SET `INPUT-OK`. If `ACUP-DETAILS-NOT-FETCHED`: PERFORM `1210-EDIT-ACCOUNT`, MOVE LOW-VALUES to `ACUP-OLD-ACCT-DATA`, if `FLG-ACCTFILTER-BLANK` set `NO-SEARCH-CRITERIA-RECEIVED`, GO TO exit (search screen — only the key is edited) (1433-1449). Otherwise: SET FOUND/VALID flags for account+customer (1452-1457); PERFORM `1205-COMPARE-OLD-NEW` (1460-1461); if NO-CHANGES-FOUND OR already-confirmed-or-done → MOVE LOW-VALUES to flags, GO TO exit (1463-1468); SET `ACUP-CHANGES-NOT-OK` (1470); then run the full edit battery in this order: Account Status (yes/no), Open Date, Credit Limit, Expiry Date, Cash Credit Limit, Reissue Date, Current Balance, Curr Cyc Credit, Curr Cyc Debit, SSN, Date of Birth (+ DOB future check if date valid), FICO (+ range check if num valid), First Name (alpha req), Middle Name (alpha opt), Last Name (alpha req), Address Line 1 (mandatory), State (alpha req + state-code lookup if alpha valid), Zip (num req), City (alpha req), Country (alpha req), Phone 1, Phone 2, EFT Account Id (num req), Primary Card Holder (yes/no) (1472-1662). Cross-field: if state valid AND zip valid → PERFORM `1280-EDIT-US-STATE-ZIP-CD` (1665-1669). Finally if not INPUT-ERROR → SET `ACUP-CHANGES-OK-NOT-CONFIRMED` (1671-1675). // source: 1429-1676

**`1205-COMPARE-OLD-NEW`** (1681-1779). SET `NO-CHANGES-FOUND`. Big `IF`: if NEW account fields all equal OLD (status compared via UPPER-CASE; group id via UPPER-CASE+TRIM) → continue; else SET `CHANGE-HAS-OCCURRED` and GO TO exit (1684-1705). Then a second `IF` comparing all NEW customer fields to OLD (names/addr/state/country/zip/govt-id/pri-holder via UPPER-CASE+TRIM; phone parts, SSN, DOB, EFT, FICO direct) → if all equal SET `NO-CHANGES-DETECTED`; else SET `CHANGE-HAS-OCCURRED`, GO TO exit (1708-1773). // source: 1681-1779

**`1210-EDIT-ACCOUNT`** (1783-1822). SET `FLG-ACCTFILTER-NOT-OK`. If `CC-ACCT-ID` is LOW-VALUES/SPACES → INPUT-ERROR, `FLG-ACCTFILTER-BLANK`, if msg free set `WS-PROMPT-FOR-ACCT` ("Account number not provided" message via the 88; **NB** see §5 — the literal moved here vs the 88-value differ), MOVE ZEROES to `CDEMO-ACCT-ID`/`ACUP-NEW-ACCT-ID`, GO TO exit (1787-1797). Else MOVE `CC-ACCT-ID` to `ACUP-NEW-ACCT-ID`; if `CC-ACCT-ID` NOT NUMERIC OR `CC-ACCT-ID-N = 0` → INPUT-ERROR, build message `'Account Number if supplied must be a 11 digit Non-Zero Number'` via STRING, MOVE ZEROES to `CDEMO-ACCT-ID`, GO TO exit (1801-1813); else MOVE to `CDEMO-ACCT-ID`, SET `FLG-ACCTFILTER-ISVALID` (1814-1817). // source: 1783-1822

**Generic field editors** (each sets one flag group, builds an exact error message into `WS-RETURN-MSG` only if `WS-RETURN-MSG-OFF`, i.e. only the **first** error wins):
- **`1215-EDIT-MANDATORY`** (1824-1854): blank → `"<name> must be supplied."`. // source: 1824-1851
- **`1220-EDIT-YESNO`** (1856-1896): blank → `"<name> must be supplied."`; not Y/N → `"<name> must be Y or N."`. (88 `FLG-YES-NO-ISVALID` = 'Y','N'.) // source: 1856-1893
- **`1225-EDIT-ALPHA-REQD`** (1898-1953): blank → `"<name> must be supplied."`; non-alpha (after INSPECT CONVERTING letters→spaces, residue non-empty) → `"<name> can have alphabets only."`. // source: 1898-1950
- **`1230-EDIT-ALPHANUM-REQD`** (1955-2011): blank → must be supplied; residue → `"<name> can have numbers or alphabets only."`. (Not invoked in main flow but present.) // source: 1955-2008
- **`1235-EDIT-ALPHA-OPT`** (2012-2059): blank → valid (optional); else same alpha-only check. // source: 2012-2056
- **`1240-EDIT-ALPHANUM-OPT`** (2061-2107): blank → valid; else alphanum check. (Not invoked.) // source: 2061-2104
- **`1245-EDIT-NUM-REQD`** (2109-2178): blank → must be supplied; not NUMERIC → `"<name> must be all numeric."`; NUMVAL = 0 → `"<name> must not be zero."`. // source: 2109-2175
- **`1250-EDIT-SIGNED-9V2`** (2180-2223): blank → must be supplied; `TEST-NUMVAL-C ≠ 0` → `"<name> is not valid"`. // source: 2180-2221

**`1260-EDIT-US-PHONE-NUM`** + fall-through paras (2225-2429). If all three parts blank → phone is optional, valid, exit (2232-2245). **`EDIT-AREA-CODE`** (2246-2315): blank→supplied msg; not numeric→`": Area code must be A 3 digit number."`; = 0 →`": Area code cannot be zero"`; not a valid NANP general-purpose area code (lookup `VALID-GENERAL-PURP-CODE`) →`": Not valid North America general purpose area code"`. **`EDIT-US-PHONE-PREFIX`** (2316-2368): blank/not-numeric/zero with `": Prefix code …"` msgs. **`EDIT-US-PHONE-LINENUM`** (2370-2422): blank/not-numeric/zero with `": Line number code …"` msgs. Note GO TOs chain area→prefix→linenum so all three are checked even after an error in an earlier part. // source: 2225-2429

**`1265-EDIT-US-SSN`** (2431-2491). Part1: num-required (3 chars) then if valid and `INVALID-SSN-PART1` (0, 666, or 900-999) → `": should not be 000, 666, or between 900 and 999"` (2439-2464). Part2: num-required (2 chars) (2469-2475). Part3: num-required (4 chars) (2481-2487). // source: 2431-2489

**`1270-EDIT-US-STATE-CD`** (2493-2513). MOVE state to `US-STATE-CODE-TO-EDIT`; if not `VALID-US-STATE-CODE` (lookup list of 59 USPS codes incl. territories) → `": is not a valid state code"`. // source: 2493-2511, CSLKPCDY.cpy:1012-1071

**`1275-EDIT-FICO-SCORE`** (2514-2533). If not `FICO-RANGE-IS-VALID` (300-850) → `": should be between 300 and 850"`. // source: 2514-2531

**`1280-EDIT-US-STATE-ZIP-CD`** (2536-2560). STRING state ++ first 2 chars of zip into `US-STATE-AND-FIRST-ZIP2`; if not `VALID-US-STATE-ZIP-CD2-COMBO` → message `"Invalid zip code for state"`; also SET both `FLG-STATE-NOT-OK` and `FLG-ZIPCODE-NOT-OK`. // source: 2536-2558

**Date edits (COPY CSUTLDPY, line 4232):** `EDIT-DATE-CCYYMMDD` orchestrates `EDIT-YEAR-CCYY` (year must be numeric 4-digit, century 19 or 20 only — see faithful note), `EDIT-MONTH` (1-12), `EDIT-DAY` (1-31), `EDIT-DAY-MONTH-YEAR` (31-day-month check, Feb-30, Feb-29 leap-year via DIVIDE remainder where `WS-DIV-BY=400` if YY=00 else 4), `EDIT-DATE-LE` (calls `CSUTLDTC` LE date validator). Messages e.g. `"<name> : Year must be supplied."`, `"<name> : Century is not valid."`, `": Month must be a number between 1 and 12."`, `":Cannot have 31 days in this month."`, `":Cannot have 30 days in this month."`, `":Not a leap year.Cannot have 29 days in this month."`. // source: CSUTLDPY.cpy:18-331, CSUTLDWY.cpy:4-59
**`EDIT-DATE-OF-BIRTH`** (CSUTLDPY 341-372): compares integer-of-date of DOB to current date; future DOB → `":cannot be in the future "`. // source: CSUTLDPY.cpy:341-372

### 4.3 Decision (2000-series)

**`2000-DECIDE-ACTION`** (2562-2645). `EVALUATE TRUE`:
- WHEN `ACUP-DETAILS-NOT-FETCHED` / WHEN `CCARD-AID-PFK12` (cancel): if `FLG-ACCTFILTER-ISVALID` → clear return msg, PERFORM `9000-READ-ACCT`; if `FOUND-CUST-IN-MASTER` SET `ACUP-SHOW-DETAILS` (2568-2580). (PF12 collapses back to "show original".)
- WHEN `ACUP-SHOW-DETAILS`: if INPUT-ERROR OR NO-CHANGES-DETECTED → continue; else SET `ACUP-CHANGES-OK-NOT-CONFIRMED` (2585-2591).
- WHEN `ACUP-CHANGES-NOT-OK` → continue (re-show with errors) (2596-2597).
- WHEN `ACUP-CHANGES-OK-NOT-CONFIRMED AND CCARD-AID-PFK05` → PERFORM `9600-WRITE-PROCESSING`; then EVALUATE: lock-error→`ACUP-CHANGES-OKAYED-LOCK-ERROR`; update-failed→`ACUP-CHANGES-OKAYED-BUT-FAILED`; data-changed→`ACUP-SHOW-DETAILS`; OTHER→`ACUP-CHANGES-OKAYED-AND-DONE` (2602-2615).
- WHEN `ACUP-CHANGES-OK-NOT-CONFIRMED` (no F5) → continue (2620-2621).
- WHEN `ACUP-CHANGES-OKAYED-AND-DONE` → SET `ACUP-SHOW-DETAILS`; if FROM-TRANID blank zero out account/card context (2625-2632).
- WHEN OTHER → ABEND `'0001'` `"UNEXPECTED DATA SCENARIO"` (2633-2640). // source: 2562-2645

### 4.4 Screen output (3000-series)

**`3000-SEND-MAP`** (2649-2666). PERFORM `3100-SCREEN-INIT`, `3200-SETUP-SCREEN-VARS`, `3250-SETUP-INFOMSG`, `3300-SETUP-SCREEN-ATTRS`, `3390-SETUP-INFOMSG-ATTRS`, `3400-SEND-SCREEN`. // source: 2649-2662

**`3100-SCREEN-INIT`** (2668-2696). MOVE LOW-VALUES to map output `CACTUPAO`; fill title1/2 from `CCDA-TITLE01/02`, tran name 'CAUP', program name; format current date `mm/dd/yy` into `CURDATEO`, time `hh:mm:ss` into `CURTIMEO`. // source: 2668-2692

**`3200-SETUP-SCREEN-VARS`** (2698-2729). If `CDEMO-PGM-ENTER` → leave blank. Else set `ACCTSIDO` from `CC-ACCT-ID` (LOW-VALUES if zero+valid filter); then EVALUATE: details-not-fetched OR acct=0 → `3201-SHOW-INITIAL-VALUES`; show-details → `3202-SHOW-ORIGINAL-VALUES`; changes-made → `3203-SHOW-UPDATED-VALUES`; OTHER → `3202`. // source: 2698-2726

**`3201-SHOW-INITIAL-VALUES`** (2731-2785). MOVE LOW-VALUES to every data output field (blank the detail area on the search screen). // source: 2731-2781

**`3202-SHOW-ORIGINAL-VALUES`** (2787-2869). MOVE LOW-VALUES to non-key flags; SET `PROMPT-FOR-CHANGES`; if found-acct/cust, format each OLD account money field via `WS-EDIT-CURRENCY-9-2-F` (PIC `+ZZZ,ZZZ,ZZZ.99`) into the output, move OLD date parts and group id; if found-cust, move all OLD customer fields, slicing SSN into 3/2/4, phone into A/B/C via reference-modification (`(2:3)`,`(6:3)`,`(10:4)`). // source: 2787-2864

**`3203-SHOW-UPDATED-VALUES`** (2870-2953). For each money field: if its `…-ISVALID` flag set, format the numeric via edited PIC; else echo the raw NEW X chars back (so the user sees their bad input). Move all NEW account+customer fields (dates, SSN parts, phone parts, names, address, etc.) to the output map. // source: 2870-2949

**`3250-SETUP-INFOMSG`** (2955-2985). EVALUATE state → set one `WS-INFO-MSG` 88: PGM-ENTER/details-not-fetched → `PROMPT-FOR-SEARCH-KEYS` ("Enter or update id of account to update"); show-details/changes-not-ok → `PROMPT-FOR-CHANGES` ("Update account details presented above."); ok-not-confirmed → `PROMPT-FOR-CONFIRMATION` ("Changes validated.Press F5 to save"); okayed-and-done → `CONFIRM-UPDATE-SUCCESS` ("Changes committed to database"); lock-error/but-failed → `INFORM-FAILURE` ("Changes unsuccessful. Please try again"). MOVE info msg → `INFOMSGO`, return msg → `ERRMSGO`. // source: 2955-2982, 462-528

**`3300-SETUP-SCREEN-ATTRS`** (2986-3439). PERFORM `3310-PROTECT-ALL-ATTRS`; then EVALUATE state to unprotect: details-not-fetched → make `ACCTSID` editable (`DFHBMFSE`); show-details/changes-not-ok → `3320-UNPROTECT-FEW-ATTRS`; confirmed/done → leave protected; OTHER → make `ACCTSID` editable. Then a big EVALUATE positions the cursor (`MOVE -1` to the `…L` length field) at the **first** field in screen order whose flag is NOT-OK/BLANK (3009-3167). Then color: if last mapset was card-list use default color; if acct filter not-ok → red `ACCTSID`; if acct filter blank AND re-enter → put '*' + red. If details-not-fetched/blank/not-ok → exit early (3186-3192). Otherwise COPY CSSETATY for each field sets red+'*' when that field is NOT-OK/BLANK and re-enter. // source: 2986-3436, CSSETATY.cpy:17-27

**`3310-PROTECT-ALL-ATTRS`** (3441-3498): MOVE `DFHBMPRF` (protect) to every field attribute. **`3320-UNPROTECT-FEW-ATTRS`** (3500-3564): MOVE `DFHBMFSE` (unprotect+FSET) to the editable account/customer fields, but keep `ACSTNUM` (customer id), `ACSCTRY` (country), and `INFOMSG` protected (`DFHBMPRF`). // source: 3441-3564

**`3390-SETUP-INFOMSG-ATTRS`** (3566-3586): info msg dark if empty else bright; show F12 when changes-made-and-not-done; show F05+F12 when prompting for confirmation. // source: 3566-3583

**`3400-SEND-SCREEN`** (3589-3605): set NEXT-MAPSET/MAP; `EXEC CICS SEND MAP('CACTUPA') MAPSET('COACTUP') FROM(CACTUPAO) CURSOR ERASE FREEKB`. // source: 3589-3602

### 4.5 File access (9000-series)

**`9000-READ-ACCT`** (3608-3649). INITIALIZE `ACUP-OLD-DETAILS`; set no-info; MOVE `CC-ACCT-ID` to `ACUP-OLD-ACCT-ID`/`WS-CARD-RID-ACCT-ID`; PERFORM `9200-GETCARDXREF-BYACCT`; if filter not-ok exit; PERFORM `9300-GETACCTDATA-BYACCT`; if acct not found exit; MOVE `CDEMO-CUST-ID` to RID; PERFORM `9400-GETCUSTDATA-BYCUST`; if cust not found exit; PERFORM `9500-STORE-FETCHED-DATA`. // source: 3608-3644

**`9200-GETCARDXREF-BYACCT`** (3650-3700). READ `CXACAIX` (alt index) by acct id into `CARD-XREF-RECORD`. NORMAL → MOVE `XREF-CUST-ID`→`CDEMO-CUST-ID`, `XREF-CARD-NUM`→`CDEMO-CARD-NUM`. NOTFND → INPUT-ERROR, filter not-ok, message `"Account:<id> not found in Cross ref file.  Resp:<resp> Reas:<resp2>"`. OTHER → file-error message. // source: 3650-3697

**`9300-GETACCTDATA-BYACCT`** (3701-3750). READ `ACCTDAT` by acct id into `ACCOUNT-RECORD`. NORMAL→`FOUND-ACCT-IN-MASTER`. NOTFND→error `"Account:<id> not found in Acct Master file.Resp:…"`. OTHER→file error. // source: 3701-3748

**`9400-GETCUSTDATA-BYCUST`** (3752-3799). READ `CUSTDAT` by cust id into `CUSTOMER-RECORD`. NORMAL→`FOUND-CUST-IN-MASTER`. NOTFND→`FLG-CUSTFILTER-NOT-OK`, message `"CustId:<id> not found in customer master.Resp: …"`. OTHER→file error. // source: 3752-3797

**`9500-STORE-FETCHED-DATA`** (3801-3887). Store nav context into COMMAREA (`CDEMO-*`); INITIALIZE `ACUP-OLD-DETAILS`; copy ACCOUNT fields into `ACUP-OLD-*` (money fields direct to the S9(10)V99 redefine, dates **sliced** `(1:4)/(6:2)/(9:2)` into Y/M/D — note the source dates are X(10) `CCYY-MM-DD`); copy CUSTOMER fields into `ACUP-OLD-CUST-*` (SSN, DOB sliced, FICO, names, address, phones, govt id, EFT, pri-holder). // source: 3801-3884

**`9600-WRITE-PROCESSING`** (3888-4107). (1) READ `ACCTDAT` UPDATE by acct id; non-NORMAL → INPUT-ERROR + `COULD-NOT-LOCK-ACCT-FOR-UPDATE`, exit (3894-3915). (2) READ `CUSTDAT` UPDATE by cust id; non-NORMAL → `COULD-NOT-LOCK-CUST-FOR-UPDATE`, exit (3921-3942). (3) PERFORM `9700-CHECK-CHANGE-IN-REC`; if `DATA-WAS-CHANGED-BEFORE-UPDATE` exit (3947-3952). (4) Build `ACCT-UPDATE-RECORD` from NEW fields: money fields direct, dates **rebuilt** via STRING `YYYY '-' MM '-' DD` into the X(10) update field; **reissue date** does `MOVE ACCT-REISSUE-DATE TO ACCT-UPDATE-REISSUE-DATE` first then overwrites it with the STRING (see faithful note) (3956-4002). (5) Build `CUST-UPDATE-RECORD`: names/addr direct, phones rebuilt via STRING `'(' A ')' B '-' C` into X(15), SSN, govt id, DOB rebuilt, EFT, pri-holder, FICO (4007-4059). (6) `REWRITE ACCTDAT FROM ACCT-UPDATE-RECORD`; non-NORMAL → `LOCKED-BUT-UPDATE-FAILED`, exit (4065-4081). (7) `REWRITE CUSTDAT FROM CUST-UPDATE-RECORD`; non-NORMAL → `LOCKED-BUT-UPDATE-FAILED` + `SYNCPOINT ROLLBACK`, exit (4085-4103). // source: 3888-4104

**`9700-CHECK-CHANGE-IN-REC`** (4109-4195). Optimistic-lock check: compare the just-(re)read `ACCOUNT-RECORD`/`CUSTOMER-RECORD` to the `ACUP-OLD-*` before-image. Account: status/balances/cycle direct, dates sliced, group id via LOWER-CASE (4115-4140). Customer: names/addr/state/country/govt-id via UPPER-CASE, zip/phones/ssn/eft/pri/fico direct, DOB sliced (4152-4186). Any mismatch → SET `DATA-WAS-CHANGED-BEFORE-UPDATE` and `GO TO 9600-WRITE-PROCESSING-EXIT` (note: GO TO targets the **caller's** exit; see faithful note). // source: 4109-4195

### 4.6 Shared paragraphs

**`YYYY-STORE-PFKEY`** (COPY CSSTRPFY, line 4199): map `EIBAID` → `CCARD-AID-*` (ENTER, CLEAR, PA1/2, PF1-12; PF13-24 fold onto PF1-12). // source: CSSTRPFY.cpy:17-82
**`ABEND-ROUTINE`** (4203-4228): default message; SEND `ABEND-DATA`; `HANDLE ABEND CANCEL`; `ABEND ABCODE('9999')`. // source: 4203-4225
**Date routines** COPY CSUTLDPY (4232). // source: 4232

---

## 5. VALIDATION RULES & EXACT LITERAL MESSAGES

Error messages land in `WS-RETURN-MSG` (X(75)) and are shown in `ERRMSG`. Only the **first** error is kept (every STRING is guarded by `IF WS-RETURN-MSG-OFF`). `<name>` = `WS-EDIT-VARIABLE-NAME` (TRIM-med). Info messages land in `INFOMSG`.

**Account-key (search) edits** — `1210-EDIT-ACCOUNT`:
- Blank account → sets 88 `WS-PROMPT-FOR-ACCT` whose value text is **`"Account number not provided"`**. // source: 483-484, 1791-1792
- Non-numeric or zero account → STRING literal **`"Account Number if supplied must be a 11 digit Non-Zero Number"`**. // source: 1806-1810

**Per-field generic messages** (built by STRING `<name>` + suffix):
- `" must be supplied."` (mandatory/yesno/alpha/num blank). // source: 1841, 1869, 1915, 1972, 2126, 2191
- `" must be Y or N."`. // source: 1886
- `" can have alphabets only."`. // source: 1941, 2047
- `" can have numbers or alphabets only."`. // source: 1999, 2095
- `" must be all numeric."`. // source: 2146
- `" must not be zero."`. // source: 2163
- `" is not valid"` (signed 9V2). // source: 2209

**Phone** (`1260` group): `": Area code must be supplied."`, `": Area code must be A 3 digit number."`, `": Area code cannot be zero"`, `": Not valid North America general purpose area code"`; prefix: `": Prefix code must be supplied."`, `": Prefix code must be A 3 digit number."`, `": Prefix code cannot be zero"`; line: `": Line number code must be supplied."`, `": Line number code must be A 4 digit number."`, `": Line number code cannot be zero"`. // source: 2254, 2272, 2286, 2306, 2325, 2343, 2357, 2378, 2396, 2410

**SSN** (`1265`): part1 invalid → `": should not be 000, 666, or between 900 and 999"`. (Part1 invalid set = 0, 666, 900-999 from 88 `INVALID-SSN-PART1`.) // source: 121-123, 2457

**State / Zip / FICO**: state→`": is not a valid state code"`; state+zip combo→`"Invalid zip code for state"`; FICO→`": should be between 300 and 850"`. // source: 2503, 2550, 2523

**Date** (CSUTLDPY): `" : Year must be supplied."`, `" must be 4 digit number."`, `" : Century is not valid."`, `" : Month must be supplied."`, `": Month must be a number between 1 and 12."`, `" : Day must be supplied."`, `":day must be a number between 1 and 31."`, `":Cannot have 31 days in this month."`, `":Cannot have 30 days in this month."`, `":Not a leap year.Cannot have 29 days in this month."`, `" validation error Sev code: <sev> Message code: <msg>"`, `":cannot be in the future "`. // source: CSUTLDPY.cpy:37,54,79,101,119/136,161,180/195,221,236,266,308-311,363

**File-not-found / file-error messages** (`9200`-`9400`): dynamic STRING messages embedding the key and RESP/RESP2; the generic file-error template is `"File Error: <op> on <file> returned RESP <resp> ,RESP2 <resp2>"`. // source: 389-408, 3674-3684, 3723-3733, 3773-3783

**Info messages** (88 of `WS-INFO-MSG`): `"Details of selected account shown above"`, `"Enter or update id of account to update"`, `"Update account details presented above."`, `"Changes validated.Press F5 to save"`, `"Changes committed to database"`, `"Changes unsuccessful. Please try again"`. // source: 466-477

**Return-message 88s of note** (some defined but superseded — see faithful bugs): `"PF03 pressed.Exiting              "`, `"No change detected with respect to values fetched."`, `"Did not find this account in account card xref file"`, etc. // source: 479-528

---

## 6. ONLINE SPECIFICS

**BMS map.** Mapset **`COACTUP`**, map **`CACTUPA`**, SIZE 24×80, CTRL=FREEKB, DSATTS/MAPATTS include COLOR/HILIGHT/PS/VALIDN. // source: COACTUP.bms:20-28

**Fields read from the map (input `CACTUPAI`, the `…I` suffix):** `ACCTSIDI` (acct no), `ACSTTUSI` (active Y/N), `ACRDLIMI`, `ACSHLIMI`, `ACURBALI`, `ACRCYCRI`, `ACRCYDBI` (money), `OPNYEARI/OPNMONI/OPNDAYI`, `EXPYEARI/EXPMONI/EXPDAYI`, `RISYEARI/RISMONI/RISDAYI` (dates), `AADDGRPI` (group), `ACSTNUMI` (cust id, display-only but received), `ACTSSN1I/2I/3I`, `DOBYEARI/MONI/DAYI`, `ACSTFCOI` (FICO), `ACSFNAMI/ACSMNAMI/ACSLNAMI` (names), `ACSADL1I/ACSADL2I` (address 1/2), `ACSCITYI` (city), `ACSSTTEI` (state), `ACSZIPCI` (zip), `ACSCTRYI` (country), `ACSPH1AI/1BI/1CI`, `ACSPH2AI/2BI/2CI` (phones), `ACSGOVTI` (govt id), `ACSEFTCI` (EFT), `ACSPFLGI` (primary holder). // source: 1051-1424
**Convention:** a received value of `'*'` or all-SPACES means "field unchanged/empty" and is normalized to LOW-VALUES (no input). // source: 1051-1058 (and each field block)

**Fields written to the map (output `CACTUPAO`, the `…O` suffix):** all of the above data fields, plus headers `TRNNAMEO`, `PGMNAMEO`, `TITLE01O`, `TITLE02O`, `CURDATEO`, `CURTIMEO`, `INFOMSGO`, `ERRMSGO`. Length fields (`…L`) are set to -1 for cursor placement; attribute fields (`…A`) for protect/unprotect/color. // source: 2668-2949, 3009-3167
**Map literals/initials of note:** SSN parts pre-init `'999'/'99'/'9999'`; FKEYS line `'ENTER=Process F3=Exit'`; `F5=Save`/`F12=Cancel` initially `DRK` (hidden) and turned bright contextually. // source: COACTUP.bms:268,276,284,497,502,507; COACTUPC.cbl:3573-3581

**Pseudo-conversational flow.** `RECEIVE MAP` (1040) → process/edit/decide → `SEND MAP … ERASE FREEKB` (3594) → `RETURN TRANSID('CAUP') COMMAREA(WS-COMMAREA) LENGTH(2000)` (1015-1019). All state lives in the COMMAREA. On first entry (EIBCALEN 0 or from-menu) the program just sends the empty search screen. // source: 880-893, 1015-1019, 3594-3602

**COMMAREA split.** Inbound `DFHCOMMAREA` is partitioned: bytes `1 : LENGTH OF CARDDEMO-COMMAREA` → `CARDDEMO-COMMAREA`; the following `LENGTH OF WS-THIS-PROGCOMMAREA` bytes → `WS-THIS-PROGCOMMAREA`. Outbound, `WS-COMMAREA` is rebuilt by concatenation. The port must preserve this exact layout split (the nav area first, then the program-private `ACUP-*` state). // source: 888-892, 1010-1013

**EIBAID / PFKey handling.** `YYYY-STORE-PFKEY` (CSSTRPFY) maps EIBAID to `CCARD-AID-*`; PF13-24 fold onto PF1-12. Valid keys at this screen: **ENTER** (process), **PF03** (exit/XCTL back), **PF05** (save — only when `ACUP-CHANGES-OK-NOT-CONFIRMED`), **PF12** (cancel — only when details fetched). Any other AID is coerced to ENTER. // source: 905-916, CSSTRPFY.cpy:17-82

**XCTL / LINK targets.** On PF03 exit: `XCTL PROGRAM(CDEMO-TO-PROGRAM)` where `CDEMO-TO-PROGRAM` = the calling program or default `COMEN01C` (menu). No `LINK`. Related program literals: `COCRDUPC`/`CCUP`, `COCRDLIC`/`CCLI`, `COCRDSLC`/`CCDL`, `COMEN01C`/`CM00`. // source: 537-572, 930-959

**Transaction commit boundary.** Account+Customer REWRITEs occur within the same CICS LUW; the F3 exit issues `SYNCPOINT`, and a failed customer REWRITE issues `SYNCPOINT ROLLBACK`. Port = one DB transaction wrapping both UPDATEs, committed on success, rolled back on the second-update failure. // source: 952-954, 4065-4103

---

## 7. PORT NOTES (relational translation + tricky COBOL semantics)

1. **Three keyed reads, two keyed updates.** `9000-READ-ACCT` = `CARD_XREF` (by acct alt key) → `ACCOUNT` (PK) → `CUSTOMER` (PK). `9600-WRITE-PROCESSING` = re-`SELECT … FOR UPDATE`-equivalent on `ACCOUNT` and `CUSTOMER`, the optimistic check, then two `UPDATE`s. There is no browse/range scan, no insert/delete. // source: 3608-3644, 3888-4104
2. **Optimistic concurrency, not pessimistic locks.** CICS READ UPDATE holds a lock for the LUW, but the program *also* re-compares the freshly read record to the `ACUP-OLD-*` before-image captured at fetch time (`9700-CHECK-CHANGE-IN-REC`). In the .NET port (SQLite, short transactions), reproduce the **compare-to-before-image** logic faithfully and surface `DATA-WAS-CHANGED-BEFORE-UPDATE` → re-show screen with the original values. The "lock" messages are really "the re-read returned non-NORMAL". // source: 3907-3915, 4109-4195
3. **`TEST-NUMVAL-C` / `NUMVAL-C` for money fields.** Inputs like `1,234.56` or `+1234.56` are parsed only when `TEST-NUMVAL-C = 0` (valid). Implement a COBOL-compatible currency parser (commas, sign, decimal). On invalid input the raw chars are echoed back unparsed (3203). Money columns are `decimal` (S9(10)V99) — truncate-toward-zero, never float (ARCHITECTURE.md §38). // source: 1078-1083, 1106-1111, 2201, 2890-2894
4. **Edited numeric output** `WS-EDIT-CURRENCY-9-2-F PIC +ZZZ,ZZZ,ZZZ.99` (line 371) — use `CobolEditedNumeric` (Runtime) to format old/new money for the screen. // source: 371, 2797-2811, 2874-2906
5. **Dates are X(10) `CCYY-MM-DD` on disk but split into Y/M/D X-parts in the COMMAREA.** Reads slice `(1:4)/(6:2)/(9:2)`; writes rebuild via `STRING YYYY '-' MM '-' DD`. Validation uses the 8-char `CCYYMMDD` (no dashes) form `WS-EDIT-DATE-CCYYMMDD`. Watch the reference-modification offsets: the stored form has dashes at positions 5 and 8, hence `(6:2)` and `(9:2)`. // source: 3832-3845, 3976-4000, CSUTLDWY.cpy:4-36
6. **Phones stored X(15) as `(AAA)BBB-CCCC`.** Reads slice `(2:3)/(6:3)/(10:4)`; writes rebuild via `STRING '(' A ')' B '-' C`. // source: 2846-2857, 4027-4041
7. **SSN stored 9(9); displayed/edited in 3 parts (3/2/4).** Display slices `(1:3)/(4:2)/(6:4)`; the NEW SSN is reassembled from the 3 input parts via the group `ACUP-NEW-CUST-SSN-X`. // source: 830-835, 2829-2831
8. **INITIALIZE semantics.** `INITIALIZE` on the COMMAREA structures sets numerics to 0 and alphanumerics to SPACES (not LOW-VALUES). Several paths instead explicitly `MOVE LOW-VALUES`. Preserve the distinction: LOW-VALUES (binary zero) is the "no input / not fetched" sentinel; SPACES is the BMS "empty field". Many `IF x = '*' OR SPACES` tests treat both as empty. // source: 866-868, 883-884, 968, 1047, 1053
9. **REDEFINES everywhere.** `…-X` (X) vs `…-N` (numeric) overlays for acct id, money, SSN, FICO, phone parts. In C# model each as one typed property plus parsing/formatting helpers; the X-form is the canonical fixed-width persisted value per ARCHITECTURE.md §44. // source: 359-369, 671-756, 759-847
10. **INSPECT … CONVERTING** for alpha checks: converts letters+space to spaces, then `LENGTH(TRIM(...)) = 0` means "only letters/spaces were present". Reproduce as a regex/char-class test. // source: 1925-1933, 1984-1991
11. **Cursor & attribute model.** The big EVALUATE in `3300` chooses the first errored field (screen order) and sets its `…L = -1`; `CSSETATY` sets red color + `'*'` placeholder for each errored field on re-entry. The console renderer must model field protect (`DFHBMPRF`), unprotect+FSET (`DFHBMFSE`), colors (`DFHRED`/`DFHDFCOL`), and bright/dark (`DFHBMASB`/`DFHBMDAR`) attributes. // source: 3009-3167, 3208-3435, CSSETATY.cpy:17-27
12. **State machine.** The single COMMAREA flag `ACUP-CHANGE-ACTION` plus AID drives all flow. Implement as an explicit enum state machine: NotFetched → ShowDetails → (ChangesNotOk | ChangesOkNotConfirmed) → (OkayedAndDone | LockError | Failed) → reset. // source: 654-668, 2562-2645

---

## 8. FAITHFUL BUGS (reproduce verbatim — DO NOT FIX)

1. **`9700-CHECK-CHANGE-IN-REC` GO TOs the wrong exit label.** On a detected change it does `GO TO 9600-WRITE-PROCESSING-EXIT` (the *caller's* exit), not `9700-CHECK-CHANGE-IN-REC-EXIT`. Because `9700` is PERFORMed THRU its own exit from `9600` (3947-3948), this GO TO jumps out of the PERFORM range. Reproduce the control flow exactly: when data changed, abort `9600` immediately (skip the REWRITEs) with `DATA-WAS-CHANGED-BEFORE-UPDATE` set. // source: 4143-4144, 4189-4190, 3947-3948
2. **DOB before-image reference-modification offset mismatch in `9700`.** Account/cust dates are stored `CCYY-MM-DD` (X with dashes), but the customer DOB comparison uses `ACUP-OLD-CUST-DOB-YYYY-MM-DD (5:2)` and `(7:2)` for month/day against `CUST-DOB-YYYY-MM-DD (6:2)/(9:2)`. The OLD copy is an **8-char dashless** field (`ACUP-OLD-CUST-DOB-YYYY-MM-DD PIC X(08)`, line 746) while the live record is 10-char dashed — so the offsets `(5:2)/(7:2)` vs `(6:2)/(9:2)` line up only by coincidence of the dashless OLD layout. Reproduce the exact offsets; do not "correct" them. // source: 746, 4174-4179
3. **Reissue date double-write in `9600`.** `MOVE ACCT-REISSUE-DATE TO ACCT-UPDATE-REISSUE-DATE` (3993) is immediately overwritten by the `STRING ACUP-NEW-REISSUE-YEAR '-' … INTO ACCT-UPDATE-REISSUE-DATE` (3994-4000). The first MOVE is dead. Keep both statements (the STRING result wins). // source: 3993-4000
4. **Duplicate / overshadowed 88 message values.** `WS-RETURN-MSG` defines `DID-NOT-FIND-ACCT-IN-CARDXREF` twice with different texts (`"Did not find this account in account card xref file"` at 497-498 and `"Did not find this account in cards database"` at 513-514); `SEARCHED-ACCT-ZEROES` and `SEARCHED-ACCT-NOT-NUMERIC` share the same text. The first definition wins in COBOL; preserve as-authored. (In practice the dynamic STRING messages in 9200/9300 are what the user sees, not these 88s.) // source: 493-528
5. **`1210-EDIT-ACCOUNT` blank-account message divergence.** The blank branch sets 88 `WS-PROMPT-FOR-ACCT` = `"Account number not provided"` (1791-1792 → value 483-484), but the non-numeric/zero branch STRINGs a *different* literal `"Account Number if supplied must be a 11 digit Non-Zero Number"` (1806-1810). The two "bad account" paths emit different text; keep both. // source: 483-484, 1791-1792, 1806-1810
6. **`CSSETATY` for EFT vs Primary-Holder are swapped.** At 3427-3430 the comment says "EFT Account Id" but the COPY uses `PRI-CARDHOLDER`/`ACSPFLG`; at 3432-3435 the comment says "Primary Card Holder" but the COPY uses `EFT-ACCOUNT-ID`/`ACSEFTC`. The generated attribute-set code is therefore cross-labeled (functionally each field still gets set, just under the other's comment). Reproduce the code as written. // source: 3426-3435
7. **`1260-EDIT-US-PHONE-NUM` optional-phone test uses NUMA twice.** The "all blank → optional" guard checks `WS-EDIT-US-PHONE-NUMA` in the third clause where it almost certainly meant `WS-EDIT-US-PHONE-NUMC` (`AND (WS-EDIT-US-PHONE-NUMA EQUAL SPACES OR WS-EDIT-US-PHONE-NUMC EQUAL LOW-VALUES)`). Reproduce the exact (buggy) condition. // source: 2234-2240
8. **Century hard-limited to 19xx/20xx.** Date year edit only accepts century 19 or 20 (`THIS-CENTURY`/`LAST-CENTURY`); any 21xx+ date is rejected with `": Century is not valid."`. Keep this restriction. // source: CSUTLDWY.cpy:9-10, CSUTLDPY.cpy:70-84

---

## 9. OPEN QUESTIONS / RISKS

1. **`CSUTLDTC` (LE date validator) call** in `EDIT-DATE-LE` — a separate utility program (its own spec exists, `CSUTLDTC.md`). The port must route the in-program date edit through the same .NET date-validation helper to keep the "validation error Sev code…" message reachable. // source: CSUTLDPY.cpy:284-325
2. **Alt-index uniqueness on `CARD_XREF` by acct id.** `9200` does a single keyed READ expecting one xref row per account. If the relational data has multiple cards per account, choose the first deterministically (e.g. `ORDER BY xref_card_num LIMIT 1`); pin with a test. // source: 3654-3667; ARCHITECTURE.md:53-56
3. **RESP/RESP2 numeric formatting in messages.** The dynamic file-error STRINGs embed `WS-RESP-CD`/`WS-REAS-CD` (S9(9) COMP) moved into X(10) display fields. The exact textual rendering of these codes is environment-specific; for screen-parity tests, mask or pin them. // source: 3672-3683, 389-408
4. **`WS-CURDATE-DATA` / time fields** come from COPY CSDAT01Y (line 626) — confirm the `WS-CURDATE-*` / `WS-CURTIME-*` subfield layout there matches the `mm/dd/yy` and `hh:mm:ss` slicing used in `3100`. // source: 626, 2678-2690
5. **`ACUP-CHANGES-OKAYED-AND-DONE` post-state** zeroes account/card context only when `CDEMO-FROM-TRANID` is blank (2627-2632); when invoked from another transaction it keeps context. Verify the intended re-entry behavior with a scripted flow. // source: 2625-2632
