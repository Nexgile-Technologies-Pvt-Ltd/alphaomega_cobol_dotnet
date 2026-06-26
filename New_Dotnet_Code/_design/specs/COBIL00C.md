# PORT SPEC — COBIL00C (Bill Payment, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COBIL00C.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COBIL00.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COBIL00.CPY`
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.Data` (repositories) per ARCHITECTURE.md.

All line citations use the form `// source: COBIL00C.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COBIL00C is the CICS pseudo-conversational **"Bill Payment"** transaction. It lets a user enter an
11-digit account id, displays that account's current balance, and — on confirmation (`Y`) — pays the balance **in
full**: it writes one new TRANSACTION record for the full current balance (type `02`, category `2`, source `POS
TERM`, description `BILL PAYMENT - ONLINE`, merchant id `999999999` "BILL PAYMENT") and then debits the account so
the new current balance becomes `old balance − amount paid` (i.e. zero). It is the only online program in CardDemo
that both **WRITEs** a transaction and **REWRITEs** the account master. `// source: COBIL00C.cbl:1-7,218-235`

**Invocation.**
- CICS TRANSID **`CB00`** (`WS-TRANID`). `// source: COBIL00C.cbl:38`
- Program id **`COBIL00C`** (`WS-PGMNAME`). `// source: COBIL00C.cbl:37`
- Mapset **`COBIL00`** / map **`COBIL0A`**. `// source: COBIL00C.cbl:296-297,309-310`
- Reached by `EXEC CICS XCTL` from another program that sets `CDEMO-TO-PROGRAM='COBIL00C'` — typically from the
  main menu `COMEN01C`, or from the transaction-list screen which can preselect a transaction via
  `CDEMO-CB00-TRN-SELECTED`. It is pseudo-conversational and re-drives itself via `RETURN TRANSID('CB00')`.
  `// source: COBIL00C.cbl:116-120,146-149`
- It is **not** a called subroutine; all flow control is via COMMAREA + XCTL/RETURN.
- On PF3 it XCTLs back to `CDEMO-FROM-PROGRAM` (or `COMEN01C` if blank). `// source: COBIL00C.cbl:128-135,281-284`
- On empty COMMAREA (cold start, `EIBCALEN = 0`) it XCTLs to `COSGN00C` (sign-on). `// source: COBIL00C.cbl:107-109`

---

## 2. FILE / TABLE ACCESS

Per ARCHITECTURE.md §"VSAM-semantics -> SQL mapping". RESP `NORMAL`→found ('00'); `NOTFND`→'23'; `ENDFILE`→'10';
`DUPKEY`/`DUPREC`→'22'; anything else→error message + screen.

| COBOL DATASET (DDNAME literal) | Logical file | ARCH table | Key / RIDFLD | CICS op | SQL equivalent | Source |
|---|---|---|---|---|---|---|
| `ACCTDAT ` (`WS-ACCTDAT-FILE`) | Account master | **ACCOUNT** | `ACCT-ID` 9(11) | **READ … UPDATE** (keyed read-for-update) | `SELECT * FROM ACCOUNT WHERE acct_id=@acctId` (then row held for update) | `// source: COBIL00C.cbl:41,345-354` |
| `ACCTDAT ` (`WS-ACCTDAT-FILE`) | Account master | **ACCOUNT** | (current held row) | **REWRITE** | `UPDATE ACCOUNT SET curr_bal=@bal,… WHERE acct_id=@acctId` | `// source: COBIL00C.cbl:379-385` |
| `CXACAIX ` (`WS-CXACAIX-FILE`) | Card-xref **alt index by ACCT-ID** | **CARD_XREF** | `XREF-ACCT-ID` 9(11) | **READ** (alt-key) | `SELECT xref_card_num,cust_id,acct_id FROM CARD_XREF WHERE acct_id=@acctId` (first row) | `// source: COBIL00C.cbl:42,410-418` |
| `TRANSACT` (`WS-TRANSACT-FILE`) | Transaction master | **TRANSACTION** | `TRAN-ID` X(16) | **STARTBR** | position cursor by PK = `TRAN-ID` | `// source: COBIL00C.cbl:40,443-449` |
| `TRANSACT` | Transaction master | **TRANSACTION** | `TRAN-ID` X(16) | **READPREV** | `SELECT * FROM TRANSACTION ORDER BY tran_id DESC LIMIT 1` (highest existing key) | `// source: COBIL00C.cbl:474-482` |
| `TRANSACT` | Transaction master | **TRANSACTION** | — | **ENDBR** | close cursor | `// source: COBIL00C.cbl:503-505` |
| `TRANSACT` | Transaction master | **TRANSACTION** | `TRAN-ID` X(16) | **WRITE** | `INSERT INTO TRANSACTION (…) VALUES (…)`; dup→'22' | `// source: COBIL00C.cbl:512-520` |

### Browse-to-get-max-key idiom (important)
The STARTBR/READPREV/ENDBR sequence exists **only to find the highest existing TRAN-ID** so the new transaction id =
max + 1:
1. `MOVE HIGH-VALUES TO TRAN-ID` then `STARTBR TRANSACT RIDFLD(TRAN-ID)` — positions the browse at end-of-file
   (key > all real keys). `// source: COBIL00C.cbl:212-213`
2. `READPREV` returns the **last** (highest-key) record; on `ENDFILE` (empty file) it sets `TRAN-ID` to ZEROS.
   `// source: COBIL00C.cbl:214,484-488`
3. `ENDBR`. `// source: COBIL00C.cbl:215`
4. `MOVE TRAN-ID TO WS-TRAN-ID-NUM`, `ADD 1 TO WS-TRAN-ID-NUM`. `// source: COBIL00C.cbl:216-217`

**Relational port:** replace the browse with `SELECT tran_id FROM TRANSACTION ORDER BY tran_id DESC LIMIT 1`. If no
rows, treat TRAN-ID as `0` (matches the `ENDFILE → MOVE ZEROS TO TRAN-ID` branch). Then numeric-increment as
described in §7 (note the X(16)→9(16) conversion and the ID-format faithful bug in §8).

---

## 3. DATA STRUCTURES USED

- **ACCOUNT-RECORD** (`CVACT01Y`): `ACCT-ID 9(11)`, `ACCT-ACTIVE-STATUS X1`, `ACCT-CURR-BAL S9(10)V99`,
  credit-limit, cash-credit-limit S9(10)V99, open/expiraion/reissue date X10, curr-cyc-credit, curr-cyc-debit
  S9(10)V99, addr-zip X10, group-id X10, FILLER X178. Program reads/updates only `ACCT-ID` (key) and
  `ACCT-CURR-BAL`. `// source: CVACT01Y.cpy:4-17; COBIL00C.cbl:170,193,198,224,234`
- **CARD-XREF-RECORD** (`CVACT03Y`): `XREF-CARD-NUM X16`, `XREF-CUST-ID 9(9)`, `XREF-ACCT-ID 9(11)`, FILLER X14.
  Program sets `XREF-ACCT-ID` (key) and consumes `XREF-CARD-NUM` → `TRAN-CARD-NUM`. `// source: CVACT03Y.cpy:4-8; COBIL00C.cbl:171,225`
- **TRAN-RECORD** (`CVTRA05Y`): `TRAN-ID X16`, `TRAN-TYPE-CD X2`, `TRAN-CAT-CD 9(4)`, `TRAN-SOURCE X10`,
  `TRAN-DESC X100`, `TRAN-AMT S9(9)V99`, `TRAN-MERCHANT-ID 9(9)`, `TRAN-MERCHANT-NAME X50`, `TRAN-MERCHANT-CITY X50`,
  `TRAN-MERCHANT-ZIP X10`, `TRAN-CARD-NUM X16`, `TRAN-ORIG-TS X26`, `TRAN-PROC-TS X26`, FILLER X20.
  `// source: CVTRA05Y.cpy:4-18`
- **WS-DATE-TIME** (`CSDAT01Y`): current-date/time work fields + `WS-TIMESTAMP` (26-byte `YYYY-MM-DD HH:MM:SS.mmmmmm`
  edited group). `// source: CSDAT01Y.cpy:17-55`
- **CCDA-SCREEN-TITLE** (`COTTL01Y`): `CCDA-TITLE01`='      AWS Mainframe Modernization       ',
  `CCDA-TITLE02`='              CardDemo                  '. `// source: COTTL01Y.cpy:18-22`
- **CCDA-COMMON-MESSAGES** (`CSMSG01Y`): `CCDA-MSG-INVALID-KEY`='Invalid key pressed. Please see below...'.
  `// source: CSMSG01Y.cpy:20-21`
- **COMMAREA**: `CARDDEMO-COMMAREA` (COCOM01Y) **plus** an in-line program extension `CDEMO-CB00-INFO`
  appended right after the COPY. `// source: COBIL00C.cbl:63-72`
- `DFHAID` (PF-key AID constants) and `DFHBMSCA` (`DFHGREEN`, etc.) are COPYed. `// source: COBIL00C.cbl:84-85`

---

## 4. COMMAREA FIELDS

`CARDDEMO-COMMAREA` (COCOM01Y) fields actually used:
- `CDEMO-FROM-PROGRAM` X8 — PF3 return target check. `// source: COBIL00C.cbl:129-133; COCOM01Y.cpy:22`
- `CDEMO-TO-PROGRAM` X8 — XCTL target on cold-start / PF3. `// source: COBIL00C.cbl:108,130,133,275-276; COCOM01Y.cpy:24`
- `CDEMO-FROM-TRANID` X4 — set to `CB00` on PF3/return. `// source: COBIL00C.cbl:278; COCOM01Y.cpy:21`
- `CDEMO-FROM-PROGRAM` (set to `COBIL00C` on return). `// source: COBIL00C.cbl:279`
- `CDEMO-PGM-CONTEXT` 9(1): `CDEMO-PGM-ENTER`=0 / `CDEMO-PGM-REENTER`=1 — first-pass vs re-entry dispatch.
  `// source: COBIL00C.cbl:112-113,275-280; COCOM01Y.cpy:29-31`

**Program-private COMMAREA extension `CDEMO-CB00-INFO`** (declared in WORKING-STORAGE immediately after the
COPY COCOM01Y, so it lives in the same `CARDDEMO-COMMAREA` 01 group and is carried across turns):
`// source: COBIL00C.cbl:63-72`
- `CDEMO-CB00-TRNID-FIRST` X16, `CDEMO-CB00-TRNID-LAST` X16, `CDEMO-CB00-PAGE-NUM` 9(8),
  `CDEMO-CB00-NEXT-PAGE-FLG` X1 (88 NEXT-PAGE-YES='Y'), `CDEMO-CB00-TRN-SEL-FLG` X1.
- `CDEMO-CB00-TRN-SELECTED` X16 — when a caller (the transaction-list screen) preselects an account/tran id, the
  first-pass logic copies it into `ACTIDINI` and immediately runs the Enter-key path. `// source: COBIL00C.cbl:116-120`

Only `CDEMO-CB00-TRN-SELECTED` is read by this program; the FIRST/LAST/PAGE-NUM/flags are carried but not used here.

**Port:** model the COMMAREA as a typed object containing CARDDEMO-COMMAREA + the CB00 extension. On `RETURN
TRANSID('CB00')` the whole thing is echoed back (`COMMAREA(CARDDEMO-COMMAREA)`), so persist all CB00 fields verbatim.
`// source: COBIL00C.cbl:146-149`

---

## 5. SCREEN (BMS map COBIL0A / mapset COBIL00)

24×80, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `MODE=INOUT`. `// source: COBIL00.bms:19-28`

### Input fields (read from RECEIVE MAP, `COBIL0AI`)
| Field | PIC (I) | BMS attrs | Purpose |
|---|---|---|---|
| `ACTIDINI` | X(11) | `FSET,IC,NORM,UNPROT`, GREEN, UNDERLINE, LEN 11, POS (6,21) | Account id entered by user (the only initially-cursored field, `IC`). `// source: COBIL00.bms:85-89; COBIL00.CPY:60` |
| `CONFIRMI` | X(1) | `FSET,NORM,UNPROT`, GREEN, UNDERLINE, LEN 1, POS (15,60) | Pay-confirmation flag (Y/N). `// source: COBIL00.bms:115-119; COBIL00.CPY:72` |

`CURBALI` (X14) is part of the symbolic input map but the displayed balance is **written** to it, not read from the
user (the field is `ASKIP` = autoskip/protected on the map). `// source: COBIL00.bms:103-106; COBIL00.CPY:66`

On RECEIVE the program consumes `ACTIDINI`, `CONFIRMI`, plus `EIBAID` (PF key) and `EIBCALEN` (commarea length).
`// source: COBIL00C.cbl:159,170,173,125`

### Length / attribute fields used for cursor placement
The `-L` (length) subfields are set to `-1` to force the cursor onto a field on the next SEND (CICS convention:
`MOVE -1 TO xxxL` + `CURSOR` option). Used: `ACTIDINL`, `CONFIRML`. `// source: COBIL00C.cbl:115,163,189,203,…; COBIL00.CPY:55,67`

### Output fields written (SEND MAP FROM `COBIL0AO`)
Header (set in POPULATE-HEADER-INFO): `TITLE01O`, `TITLE02O`, `TRNNAMEO`(='CB00'), `PGMNAMEO`(='COBIL00C'),
`CURDATEO`(mm/dd/yy), `CURTIMEO`(hh:mm:ss). `// source: COBIL00C.cbl:323-338`
Body: `ACTIDINO`/`ACTIDINI` (account), `CURBALI`/`CURBALO` (current balance, edited), `CONFIRMI`/`CONFIRMO`
(Y/N). `ERRMSGO` = `WS-MESSAGE` (error/info line). `// source: COBIL00C.cbl:194,293,562-565`
`ERRMSGC` (the color attribute byte of ERRMSG) is set to `DFHGREEN` on a successful payment so the success line
shows green; otherwise the field defaults to RED per the map. `// source: COBIL00C.cbl:526; COBIL00.bms:127-130`

**Edited-numeric balance field.** The balance is moved through `WS-CURR-BAL PIC +9999999999.99` (sign + 10 int
digits + 2 dec) and then into `CURBALI`. Reproduce COBOL edited-numeric formatting (leading `+`/`-`, no comma
grouping, fixed 10 integer digits **with leading zeros — this PIC is NOT zero-suppressed**, fixed 2 decimals) via
`CobolEditedNumeric` from CardDemo.Runtime. `// source: COBIL00C.cbl:56,193-194`

Likewise the transaction amount goes through `WS-TRAN-AMT PIC +99999999.99` (declared but, see §7, the success
message uses TRAN-ID not WS-TRAN-AMT; WS-TRAN-AMT is effectively unused). `// source: COBIL00C.cbl:55`

---

## 6. PSEUDO-CONVERSATIONAL FLOW

Standard CardDemo pattern. Each invocation:
1. `RECEIVE` (only on re-entry), 2. dispatch on `EIBAID`, 3. `SEND` with `ERASE CURSOR`, 4. `RETURN TRANSID('CB00')`
carrying the COMMAREA. `// source: COBIL00C.cbl:124-149,295-301`

Dispatch (MAIN-PARA): `// source: COBIL00C.cbl:99-149`
- `EIBCALEN = 0` → cold start → `CDEMO-TO-PROGRAM='COSGN00C'`, RETURN-TO-PREV-SCREEN (XCTL). `// source: 107-109`
- else copy `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. `// source: 111`
  - **First pass** (`NOT CDEMO-PGM-REENTER`): set REENTER, `MOVE LOW-VALUES TO COBIL0AO`, `MOVE -1 TO ACTIDINL`.
    If `CDEMO-CB00-TRN-SELECTED` not spaces/low-values → copy it to `ACTIDINI` and `PERFORM PROCESS-ENTER-KEY`.
    Then `SEND-BILLPAY-SCREEN`. `// source: 112-122`
  - **Re-entry** (`CDEMO-PGM-REENTER`): `RECEIVE-BILLPAY-SCREEN`, then `EVALUATE EIBAID`:
    - `DFHENTER` → PROCESS-ENTER-KEY. `// source: 126-127`
    - `DFHPF3` → set TO-PROGRAM to `CDEMO-FROM-PROGRAM` (or `COMEN01C` if blank) → RETURN-TO-PREV-SCREEN.
      `// source: 128-135`
    - `DFHPF4` → CLEAR-CURRENT-SCREEN. `// source: 136-137`
    - OTHER → err flag, `WS-MESSAGE = CCDA-MSG-INVALID-KEY`, SEND. `// source: 138-141`

**PF-key AID handling:** only ENTER, PF3 (back), PF4 (clear) are recognized; all other AIDs → "Invalid key
pressed." The screen footer advertises exactly these: `ENTER=Continue  F3=Back  F4=Clear`. `// source: COBIL00.bms:131-135`

**XCTL/LINK targets:** `XCTL PROGRAM(CDEMO-TO-PROGRAM)` only (RETURN-TO-PREV-SCREEN). Resolved targets:
`COSGN00C` (cold start), `COMEN01C` (PF3 with blank FROM-PROGRAM), or whatever `CDEMO-FROM-PROGRAM` holds.
`// source: COBIL00C.cbl:108,130,276,281-284`. No `LINK`, no called subprogram.

---

## 7. PARAGRAPH-BY-PARAGRAPH OUTLINE (one method each)

### MAIN-PARA `// source: COBIL00C.cbl:99-149`
1. `SET ERR-FLG-OFF`, `SET USR-MODIFIED-NO`; clear `WS-MESSAGE` and `ERRMSGO`. `// source: 101-105`
2. If `EIBCALEN=0` → cold start to `COSGN00C` via RETURN-TO-PREV-SCREEN. `// source: 107-109`
3. Else move commarea in; first-pass vs reenter dispatch (see §6). `// source: 111-143`
4. `EXEC CICS RETURN TRANSID('CB00') COMMAREA(CARDDEMO-COMMAREA)`. `// source: 146-149`

### PROCESS-ENTER-KEY `// source: COBIL00C.cbl:154-244`
1. `SET CONF-PAY-NO`. `// source: 156`
2. Validate account id: if `ACTIDINI` = SPACES or LOW-VALUES → err, `WS-MESSAGE='Acct ID can NOT be empty...'`,
   `MOVE -1 TO ACTIDINL`, SEND. `// source: 158-167`
3. If no error: `MOVE ACTIDINI TO ACCT-ID, XREF-ACCT-ID`. `// source: 169-171`
4. `EVALUATE CONFIRMI`: `// source: 173-191`
   - `'Y'`/`'y'` → `SET CONF-PAY-YES`, `READ-ACCTDAT-FILE`. `// source: 174-177`
   - `'N'`/`'n'` → `CLEAR-CURRENT-SCREEN`, set err flag (suppresses the rest). `// source: 178-181`
   - SPACES / LOW-VALUES (blank confirm) → `READ-ACCTDAT-FILE` (read but do not pay). `// source: 182-184`
   - OTHER → err, `WS-MESSAGE='Invalid value. Valid values are (Y/N)...'`, `MOVE -1 TO CONFIRML`, SEND. `// source: 185-190`
5. After the EVALUATE (UNCONDITIONALLY — see faithful bug FB-2): `MOVE ACCT-CURR-BAL TO WS-CURR-BAL`,
   `MOVE WS-CURR-BAL TO CURBALI`. `// source: 193-194`
6. If no error and `ACCT-CURR-BAL <= 0` and account-id non-blank → err, `WS-MESSAGE='You have nothing to pay...'`,
   `MOVE -1 TO ACTIDINL`, SEND. `// source: 197-206`
7. If no error: `// source: 208-244`
   - **If CONF-PAY-YES** (the actual payment): `// source: 210-235`
     - `READ-CXACAIX-FILE` (get card num via xref). `// source: 211`
     - `MOVE HIGH-VALUES TO TRAN-ID`; STARTBR / READPREV / ENDBR TRANSACT (max key). `// source: 212-215`
     - `MOVE TRAN-ID TO WS-TRAN-ID-NUM`; `ADD 1 TO WS-TRAN-ID-NUM`. `// source: 216-217`
     - `INITIALIZE TRAN-RECORD`; populate it: `TRAN-ID = WS-TRAN-ID-NUM`, `TRAN-TYPE-CD='02'`, `TRAN-CAT-CD=2`,
       `TRAN-SOURCE='POS TERM'`, `TRAN-DESC='BILL PAYMENT - ONLINE'`, `TRAN-AMT=ACCT-CURR-BAL`,
       `TRAN-CARD-NUM=XREF-CARD-NUM`, `TRAN-MERCHANT-ID=999999999`, `TRAN-MERCHANT-NAME='BILL PAYMENT'`,
       `TRAN-MERCHANT-CITY='N/A'`, `TRAN-MERCHANT-ZIP='N/A'`. `// source: 218-229`
     - `GET-CURRENT-TIMESTAMP`; `MOVE WS-TIMESTAMP TO TRAN-ORIG-TS, TRAN-PROC-TS`. `// source: 230-232`
     - `WRITE-TRANSACT-FILE`. `// source: 233`
     - `COMPUTE ACCT-CURR-BAL = ACCT-CURR-BAL - TRAN-AMT` (S9(10)V99 = S9(10)V99 − S9(9)V99; truncate toward zero,
       no rounding). `// source: 234`
     - `UPDATE-ACCTDAT-FILE` (REWRITE). `// source: 235`
   - **Else** (not confirmed yet): `WS-MESSAGE='Confirm to make a bill payment...'`, `MOVE -1 TO CONFIRML`.
     `// source: 236-240`
   - `SEND-BILLPAY-SCREEN`. `// source: 242`

### GET-CURRENT-TIMESTAMP `// source: COBIL00C.cbl:249-267`
1. `EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME)`. `// source: 251-253`
2. `EXEC CICS FORMATTIME ABSTIME → YYYYMMDD(WS-CUR-DATE-X10, DATESEP '-')`, `TIME(WS-CUR-TIME-X08, TIMESEP ':')`.
   `// source: 255-261`
3. `INITIALIZE WS-TIMESTAMP`; `WS-TIMESTAMP(01:10)=WS-CUR-DATE-X10`; `WS-TIMESTAMP(12:08)=WS-CUR-TIME-X08`;
   `WS-TIMESTAMP-TM-MS6 = ZEROS`. Result: `CCYY-MM-DD HH:MM:SS.000000` (26 chars). `// source: 263-266`
   Port: use `IClock` (CardDemo.Runtime). Microseconds always `000000`.

### RETURN-TO-PREV-SCREEN `// source: COBIL00C.cbl:273-284`
1. If `CDEMO-TO-PROGRAM` blank → `COSGN00C`. `// source: 275-277`
2. `CDEMO-FROM-TRANID='CB00'`, `CDEMO-FROM-PROGRAM='COBIL00C'`, `CDEMO-PGM-CONTEXT=0`. `// source: 278-280`
3. `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: 281-284`

### SEND-BILLPAY-SCREEN `// source: COBIL00C.cbl:289-301`
1. `POPULATE-HEADER-INFO`. 2. `MOVE WS-MESSAGE TO ERRMSGO`. 3. `EXEC CICS SEND MAP('COBIL0A') MAPSET('COBIL00')
FROM(COBIL0AO) ERASE CURSOR`. `// source: 291-301`

### RECEIVE-BILLPAY-SCREEN `// source: COBIL00C.cbl:306-314`
`EXEC CICS RECEIVE MAP('COBIL0A') MAPSET('COBIL00') INTO(COBIL0AI) RESP/RESP2` (RESP not checked). `// source: 308-314`

### POPULATE-HEADER-INFO `// source: COBIL00C.cbl:319-338`
`MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`; set titles, tranid, pgmname; build `mm/dd/yy` (year = positions
3:2 of 4-digit year, i.e. last two digits) → `CURDATEO`; build `hh:mm:ss` → `CURTIMEO`. `// source: 321-338`

### READ-ACCTDAT-FILE `// source: COBIL00C.cbl:343-372`
`READ ACCTDAT INTO(ACCOUNT-RECORD) RIDFLD(ACCT-ID) UPDATE`; EVALUATE RESP: NORMAL→continue; NOTFND→err
`'Account ID NOT found...'`, `-1 TO ACTIDINL`, SEND; OTHER→DISPLAY + err `'Unable to lookup Account...'`, SEND.
`// source: 345-372`

### UPDATE-ACCTDAT-FILE `// source: COBIL00C.cbl:377-403`
`REWRITE ACCTDAT FROM(ACCOUNT-RECORD)`; NORMAL→continue; NOTFND→err `'Account ID NOT found...'`; OTHER→DISPLAY + err
`'Unable to Update Account...'`. `// source: 379-403`

### READ-CXACAIX-FILE `// source: COBIL00C.cbl:408-436`
`READ CXACAIX INTO(CARD-XREF-RECORD) RIDFLD(XREF-ACCT-ID)`; NORMAL→continue; NOTFND→err `'Account ID NOT found...'`;
OTHER→DISPLAY + err `'Unable to lookup XREF AIX file...'`. `// source: 410-436`

### STARTBR-TRANSACT-FILE `// source: COBIL00C.cbl:441-467`
`STARTBR TRANSACT RIDFLD(TRAN-ID)`; NORMAL→continue; NOTFND→err `'Transaction ID NOT found...'`; OTHER→DISPLAY + err
`'Unable to lookup Transaction...'`. `// source: 443-467`

### READPREV-TRANSACT-FILE `// source: COBIL00C.cbl:472-496`
`READPREV TRANSACT INTO(TRAN-RECORD) RIDFLD(TRAN-ID)`; NORMAL→continue; **ENDFILE→`MOVE ZEROS TO TRAN-ID`**;
OTHER→DISPLAY + err `'Unable to lookup Transaction...'`. `// source: 474-496`

### ENDBR-TRANSACT-FILE `// source: COBIL00C.cbl:501-505`
`EXEC CICS ENDBR DATASET(WS-TRANSACT-FILE)`. (No RESP check.) `// source: 503-505`

### WRITE-TRANSACT-FILE `// source: COBIL00C.cbl:510-547`
`WRITE TRANSACT FROM(TRAN-RECORD) RIDFLD(TRAN-ID)`; EVALUATE RESP: `// source: 512-547`
- NORMAL → `INITIALIZE-ALL-FIELDS`, clear WS-MESSAGE, `MOVE DFHGREEN TO ERRMSGC`, build success message via
  STRING (see §9), SEND. `// source: 523-532`
- DUPKEY / DUPREC → err `'Tran ID already exist...'`, `-1 TO ACTIDINL`, SEND. `// source: 533-539`
- OTHER → DISPLAY + err `'Unable to Add Bill pay Transaction...'`, SEND. `// source: 540-546`

### CLEAR-CURRENT-SCREEN `// source: COBIL00C.cbl:552-555`
`INITIALIZE-ALL-FIELDS`; `SEND-BILLPAY-SCREEN`. `// source: 554-555`

### INITIALIZE-ALL-FIELDS `// source: COBIL00C.cbl:560-566`
`MOVE -1 TO ACTIDINL`; `MOVE SPACES TO ACTIDINI, CURBALI, CONFIRMI, WS-MESSAGE`. `// source: 562-565`

---

## 8. FAITHFUL BUGS (reproduce verbatim — DO NOT FIX)

**FB-1 — Transaction-ID generation truncates a 16-digit char key into a 16-digit numeric, losing/garbling
non-numeric or >16-digit content.** `TRAN-ID` is `PIC X(16)` but `WS-TRAN-ID-NUM` is `PIC 9(16)`. The code does
`MOVE TRAN-ID TO WS-TRAN-ID-NUM` then `ADD 1`, then `MOVE WS-TRAN-ID-NUM TO TRAN-ID`. If existing tran ids are
left-justified numeric strings (CardDemo's are), this yields max+1 zero-padded to 16 digits; but the X→9 MOVE
follows COBOL alphanumeric-to-numeric de-editing rules (non-digit bytes can corrupt the value). Reproduce the exact
"parse the X(16) as a 16-digit number, +1, re-store as zero-padded 16-digit string" behavior; do not switch to a
robust id scheme. `// source: COBIL00C.cbl:57,216-219`

**FB-2 — Balance moved to screen even on the invalid-confirm and "N" paths, using whatever ACCT-CURR-BAL currently
holds (possibly stale / unread).** Lines 193-194 (`MOVE ACCT-CURR-BAL TO WS-CURR-BAL` / `MOVE WS-CURR-BAL TO
CURBALI`) execute **unconditionally** inside the `IF NOT ERR-FLG-ON` block at 169, i.e. *after* the CONFIRM
EVALUATE. On the `'N'`/`'n'` branch the screen was already cleared (and err flag set), so 193-194 don't run; but on
the OTHER (invalid Y/N) branch the code at 185-190 sets the err flag and SENDs *inside* the EVALUATE — yet because
185-190 already SENT and returned to the EVALUATE, control still falls through to 193-194 which then re-touches
`CURBALI` with the (uninitialized for a failed read) `ACCT-CURR-BAL`. Faithfully preserve this ordering: the balance
MOVE is unconditional after the CONFIRM EVALUATE whenever the empty-id check passed. `// source: COBIL00C.cbl:169-195`

**FB-3 — "Blank confirm reads the account and shows the balance, but the payment is gated only on CONF-PAY-YES, so a
blank confirm silently falls into the 'Confirm to make a bill payment...' prompt** — i.e. pressing ENTER with a
valid acct and blank confirm does a `READ … UPDATE` (acquiring an update lock on ACCTDAT) but never REWRITEs, leaving
the record read-for-update without completing the update within the same task. In the relational port, a blank-confirm
ENTER must perform the keyed read (to display balance) but must NOT hold a lasting lock and must NOT write — matching
the observable result (balance shown + prompt, no transaction, no balance change). `// source: COBIL00C.cbl:182-184,208-240`

**FB-4 — No account active-status check and no credit/zero-floor guard beyond `<= 0`.** A closed/expired account
whose balance is > 0 will still be paid. Reproduce: the only gate is `ACCT-CURR-BAL <= ZEROS`. `// source: COBIL00C.cbl:197-206`

**FB-5 — `XREF-CARD-NUM` is copied into `TRAN-CARD-NUM` only when CONF-PAY-YES; the xref read happens *inside* the
payment block (line 211), so the card number is always fresh — but note the xref read's NOTFND/error message is
`'Account ID NOT found...'` (wrong noun: it is the *xref* that was not found, message says Account).** Keep the exact
(misleading) message text. `// source: COBIL00C.cbl:423-426`

**FB-6 — `WS-TRAN-DATE PIC X(08) VALUE '00/00/00'` and `WS-TRAN-AMT PIC +99999999.99` are declared but never used**
in PROCEDURE DIVISION; the success message reports `TRAN-ID`, not the amount. Do not "wire them up." `// source: COBIL00C.cbl:55,58`

---

## 9. VALIDATION RULES & EXACT LITERAL MESSAGES

All messages go to `WS-MESSAGE`→`ERRMSGO` (RED by default; GREEN on success). Reproduce **verbatim**, including
trailing `...` and spacing.

| Trigger | Exact message text | Source |
|---|---|---|
| Unrecognized AID (not ENTER/PF3/PF4) | `Invalid key pressed. Please see below...` (from CCDA-MSG-INVALID-KEY) | `// source: 140; CSMSG01Y.cpy:21` |
| Acct id blank/low-values on ENTER | `Acct ID can NOT be empty...` | `// source: 161-162` |
| Confirm value not Y/y/N/n/blank | `Invalid value. Valid values are (Y/N)...` | `// source: 187-188` |
| Balance ≤ 0 with non-blank acct | `You have nothing to pay...` | `// source: 201-202` |
| ACCTDAT read NOTFND | `Account ID NOT found...` | `// source: 361-362` |
| ACCTDAT read other error | `Unable to lookup Account...` | `// source: 368-369` |
| ACCTDAT rewrite NOTFND | `Account ID NOT found...` | `// source: 392-393` |
| ACCTDAT rewrite other error | `Unable to Update Account...` | `// source: 399-400` |
| CXACAIX read NOTFND | `Account ID NOT found...` (see FB-5) | `// source: 425-426` |
| CXACAIX read other error | `Unable to lookup XREF AIX file...` | `// source: 432-433` |
| TRANSACT startbr NOTFND | `Transaction ID NOT found...` | `// source: 456-457` |
| TRANSACT startbr/readprev other | `Unable to lookup Transaction...` | `// source: 463-464,492-493` |
| TRANSACT write DUPKEY/DUPREC | `Tran ID already exist...` | `// source: 536-537` |
| TRANSACT write other error | `Unable to Add Bill pay Transaction...` | `// source: 543-544` |
| Not-yet-confirmed (CONFIRM blank, CONF-PAY-NO) | `Confirm to make a bill payment...` | `// source: 237-238` |
| Payment success | `Payment successful.  Your Transaction ID is <TRAN-ID>.` (see STRING note) | `// source: 527-531` |

**Success message STRING** `// source: COBIL00C.cbl:527-531`:
```
STRING 'Payment successful. '       DELIMITED BY SIZE
       ' Your Transaction ID is '   DELIMITED BY SIZE
       TRAN-ID                      DELIMITED BY SPACE
       '.'                          DELIMITED BY SIZE
  INTO WS-MESSAGE
```
- `'Payment successful. '` includes a trailing space; the next literal begins with a leading space → **two spaces**
  between "successful." and "Your". Preserve.
- `TRAN-ID DELIMITED BY SPACE` copies TRAN-ID up to the first space. Because TRAN-ID is a 16-char field that (after
  the numeric round-trip in FB-1) holds digits with no embedded space, the whole 16-digit string is emitted (e.g.
  `0000000000000123`). Reproduce: take the X(16) tran id, stop at first space.

**Confirm-value semantics** (`EVALUATE CONFIRMI`): `Y`/`y` → pay; `N`/`n` → clear screen (no pay); SPACES/LOW-VALUES
→ read+show balance, then "Confirm…" prompt; anything else → invalid-value error. `// source: 173-191`

---

## 10. PORT NOTES (relational translation + tricky semantics)

- **Field types** (per ARCHITECTURE.md type map): `ACCT-ID`/`XREF-ACCT-ID` 9(11)→`long`; `ACCT-CURR-BAL`,
  `TRAN-AMT` `S9V99`→`decimal` (truncate toward zero, silent overflow, never float); `TRAN-ID`/`XREF-CARD-NUM` X16
  →`string` (full width). `WS-TRAN-ID-NUM` 9(16) is wider than 32-bit; use `long`.
- **Account key passed as char vs numeric.** `MOVE ACTIDINI(X11) TO ACCT-ID(9(11))` is an alphanumeric→numeric MOVE:
  spaces/non-digits de-edit per COBOL rules. The relational repo key is `acct_id` long; convert the entered 11-char
  field by COBOL numeric de-editing (right-justified digits, blanks→problematic). For the common case (user types
  digits, MUSTFILL is **not** set on this map, so partial entry is possible) reproduce COBOL's behavior, not C#
  `long.Parse`. `// source: COBIL00C.cbl:170-171; COBIL00.bms:85-89`
- **READ … UPDATE then REWRITE** maps to: `SELECT` the ACCOUNT row, mutate `curr_bal` in memory, `UPDATE … WHERE
  acct_id=@id`. Per ARCHITECTURE.md the lock semantics collapse to a single transaction; keep the read and rewrite
  in one logical unit so a missing row on rewrite yields '23' (NOTFND message). `// source: 345-354,379-385`
- **Max-key browse → MAX query.** Replace STARTBR(HIGH-VALUES)/READPREV/ENDBR with `SELECT MAX(tran_id)` /
  `ORDER BY tran_id DESC LIMIT 1`. Empty table → treat as `0` (the ENDFILE branch). String tran ids sort ordinally;
  because CardDemo tran ids are zero-padded fixed-width 16-digit strings, ordinal string MAX == numeric MAX. Guard
  with a pinning test per ARCHITECTURE.md collation note. `// source: 212-217,484-488`
- **INITIALIZE TRAN-RECORD** sets numeric subfields to 0 and alphanumerics to spaces before the field MOVEs; in the
  POCO, construct a fresh TRANSACTION entity (default decimals 0, strings padded to width on serialize). `// source: 218`
- **Edited PICs.** `WS-CURR-BAL PIC +9999999999.99` is **not** zero-suppressed (leading zeros shown) and always has
  a sign char — different from the `+ZZZ,ZZZ,ZZZ.99` style elsewhere. Render via `CobolEditedNumeric`. `WS-TRAN-AMT`
  is unused (FB-6). `// source: 55-56`
- **Timestamp** `WS-TIMESTAMP` = `YYYY-MM-DD HH:MM:SS.000000` (CICS ASKTIME/FORMATTIME). Use `IClock`; always 6 zero
  microsecond digits. Stored into both `TRAN-ORIG-TS` and `TRAN-PROC-TS` (X26). `// source: 249-266,231-232`
- **TRAN-CARD-NUM source.** Comes from `XREF-CARD-NUM` (the card-xref row found by acct_id), NOT from any card
  table. `// source: 225; CVACT03Y.cpy:5`
- **Header date** uses `FUNCTION CURRENT-DATE` (wall clock) for `CURDATEO`/`CURTIMEO`, while the transaction
  timestamp uses CICS ASKTIME — two different clock sources; both should resolve to the same `IClock` in the port but
  note the formatting paths differ. `// source: 321-338,251-261`
- **PF/AID and cursor**: model `EIBAID` as an enum (ENTER/PF3/PF4/other); `-1 TO xxxL` + SEND CURSOR = "place cursor
  on this field." Map `ERASE` (clear screen first) and the `ERRMSGC=DFHGREEN` attribute override.
- **DISPLAY statements** in the OTHER error branches are operator-console traces; port as log lines, not screen
  output. `// source: 366,397,430,461,490,541`

---

## 11. OPEN QUESTIONS / RISKS

1. **Confirm = blank (`SPACES`) ENTER path** acquires a `READ UPDATE` lock but never rewrites (FB-3). Confirm the
   intended relational behavior is "display balance + 'Confirm…' prompt, no lock retained, no write." Pinning test
   should assert: no TRANSACTION row added, ACCOUNT.curr_bal unchanged. `// source: 182-184,236-240`
2. **`ACTIDINI` X(11)→`ACCT-ID` 9(11) de-edit** for non-numeric / short input is undefined-ish; CardDemo's BMS map
   here does **not** set `PICIN`/`MUSTFILL` on `ACTIDIN` (unlike COACTVW), so leading/trailing spaces are plausible.
   Decide and pin the exact conversion (recommend: emulate COBOL alphanumeric→numeric: keep digits, treat the field
   as a zoned-decimal-ish move). `// source: COBIL00.bms:85-89`
3. **Concurrent max-key** under multi-user: the browse-then-+1 id scheme is racy on the mainframe too; the relational
   port should serialize the INSERT or rely on PK uniqueness → the DUPKEY/DUPREC branch maps the '22' status back to
   `'Tran ID already exist...'`. Confirm this is the desired faithful behavior rather than auto-retry. `// source: 533-539`
4. **CICS `RESP` not checked on RECEIVE/ENDBR/SEND** — mapfail/length errors are silently ignored; reproduce by not
   surfacing those conditions. `// source: 308-314,503-505,295-301`
