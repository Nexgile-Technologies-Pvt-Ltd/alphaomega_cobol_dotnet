# PORT SPEC — CORPT00C (Transaction Reports — submit batch report job from online)

Program kind: **online (CICS pseudo-conversational)**
Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CORPT00C.cbl`
BMS map/mapset: `CORPT0A` / `CORPT00` (`app/bms/CORPT00.bms`, symbolic map `app/cpy-bms/CORPT00.CPY`)
Target: `src/CardDemo.Online` (transaction handler `CR00 → CORPT00C`) + `src/CardDemo.ConsoleApp` (screen render), per `_design/ARCHITECTURE.md`.
Called subprogram: `CSUTLDTC` (date validator — see spec `CSUTLDTC.md`).

---

## 1. Purpose & Invocation

CORPT00C is the **Transaction Reports** screen. It lets a user request a transaction report for one of three
time ranges — **Monthly** (current calendar month), **Yearly** (current calendar year), or **Custom** (a
user-entered start/end date range) — and, after a Y/N confirmation, **builds an entire JCL job-stream in
working storage and writes it line-by-line to the CICS extra-partition TDQ `JOBS`**, which is wired to the
internal reader (INTRDR). The actual report is produced asynchronously by the submitted batch job
(`PROC=TRANREPT`, which reads `TRANSACT`); CORPT00C itself does **no transaction-file I/O at run time** — it only
emits JCL. It is pseudo-conversational: each ENTER re-drives transaction `CR00` into this same program; PF3
returns to the previous menu. // source: CORPT00C.cbl:2-6 (header Function), 24 (PROGRAM-ID), 462-535 (JCL build + TDQ write)

- **CICS TRANSID**: `CR00` (`WS-TRANID`, the value RETURNed with). // source: CORPT00C.cbl:38, 199-201
- **Program name (self)**: `CORPT00C` (`WS-PGMNAME`). // source: CORPT00C.cbl:37
- **How reached**: via `XCTL` from the main menu `COMEN01C` (menu option 9 "Transaction Reports" → `CORPT00C`),
  passing the shared `CARDDEMO-COMMAREA`. PF3 here `XCTL`s to `COMEN01C`; a cold start (`EIBCALEN=0`) `XCTL`s to
  `COSGN00C`. No JCL invokes this program; it is purely online. // source: CORPT00C.cbl:172-197
- **Linkage**: receives/returns `DFHCOMMAREA` typed via `CARDDEMO-COMMAREA` (copybook `COCOM01Y`).
  `DFHCOMMAREA` is declared as `LK-COMMAREA OCCURS 1 TO 32767 DEPENDING ON EIBCALEN` and copied with a reference
  modification `DFHCOMMAREA(1:EIBCALEN)`. // source: CORPT00C.cbl:154-157, 176, 201

---

## 2. FILE / TABLE Access

**CORPT00C performs NO VSAM/file READ/WRITE and NO SQL at run time.** It declares
`WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT'` (// source: CORPT00C.cbl:40) and COPYs the transaction record
layout `CVTRA05Y` (`TRAN-RECORD`, // source: CORPT00C.cbl:146), but **neither is referenced in the PROCEDURE
DIVISION** — they are dead working storage carried for documentary purposes. The transaction file is read
later by the *batch job this program submits*, not by this program.

| COBOL file / resource | Relational table (ARCHITECTURE) | Operation(s) used here | SQL / target |
|---|---|---|---|
| `TRANSACT` (declared `WS-TRANSACT-FILE`; `CVTRA05Y` copied) | TRANSACTION | **none — dead declaration**; read only by the submitted batch report job | — (handled by the TRANREPT batch program, not here) |
| TDQ `JOBS` (extra-partition transient data queue → INTRDR) | *not a relational table* | `EXEC CICS WRITEQ TD QUEUE('JOBS')` per JCL line | maps to the .NET "internal reader / job-submit" sink (see §9) |

> **Port note:** the .NET handler needs **no DbContext/repository dependency**. Its only external effects are:
> (1) write JCL records to a `JOBS` job-submit queue (an `IInternalReader`/`IJobSubmitQueue` shim), and
> (2) call the date validator `CSUTLDTC`. The transaction report data path lives entirely in the batch
> `TRANREPT` program/job, which is a separate port artifact.

---

## 3. Working storage & embedded data (logic-affecting)

`WS-VARIABLES` (// source: CORPT00C.cbl:36-79):
- `WS-PGMNAME PIC X(08) VALUE 'CORPT00C'`. // :37
- `WS-TRANID  PIC X(04) VALUE 'CR00'`. // :38
- `WS-MESSAGE PIC X(80) VALUE SPACES` — error/info line buffer (note 80 wide, but map `ERRMSGO` is X(78) → 2-char clamp, see FB-5). // :39
- `WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT'` — dead. // :40
- `WS-ERR-FLG PIC X(01)` with 88s `ERR-FLG-ON='Y'` / `ERR-FLG-OFF='N'`. // :41-43
- `WS-TRANSACT-EOF PIC X(01)` 88s `TRANSACT-EOF='Y'`/`TRANSACT-NOT-EOF='N'` — **dead** (set at :166 but never tested). // :44-46
- `WS-SEND-ERASE-FLG PIC X(01) VALUE 'Y'` 88s `SEND-ERASE-YES='Y'`/`SEND-ERASE-NO='N'` — controls ERASE on SEND; set to Y once at :167 and never changed → SEND always ERASEs (the `ELSE` no-ERASE branch is dead). // :47-49
- `WS-END-LOOP PIC X(01)` 88s `END-LOOP-YES='Y'`/`END-LOOP-NO='N'` — JCL-line loop terminator. // :50-52
- `WS-RESP-CD`, `WS-REAS-CD PIC S9(09) COMP` — RESP/RESP2 from RECEIVE and WRITEQ. // :54-55
- `WS-REC-COUNT PIC S9(04) COMP` — declared, **dead** (never used). // :56
- `WS-IDX PIC S9(04) COMP` — JCL-line loop subscript. // :57
- `WS-REPORT-NAME PIC X(10)` — `'Monthly'`/`'Yearly'`/`'Custom'`. // :58, 214/240/433
- `WS-START-DATE` group = `YYYY` `'-'` `MM` `'-'` `DD` (10 chars, ISO `YYYY-MM-DD`). // :60-65
- `WS-END-DATE` group = `YYYY` `'-'` `MM` `'-'` `DD` (10 chars). // :66-71
- `WS-DATE-FORMAT PIC X(10) VALUE 'YYYY-MM-DD'` — mask passed to CSUTLDTC. // :72
- `WS-NUM-99 PIC 99 VALUE 0`, `WS-NUM-9999 PIC 9999 VALUE 0` — NUMVAL-C scratch for MM/DD and YYYY. // :74-75
- `WS-TRAN-AMT PIC +99999999.99`, `WS-TRAN-DATE PIC X(08) VALUE '00/00/00'` — declared, **dead**. // :77-78
- `JCL-RECORD PIC X(80) VALUE ' '` — one 80-byte JCL line buffer written to TDQ. // :79

`JOB-DATA` — the embedded JCL job-stream template (// source: CORPT00C.cbl:81-127):
- `JOB-DATA-1` is a sequence of `FILLER PIC X(80)` literals = the JCL lines, plus three sub-groups that
  redefine in two date placeholders each:
  - `FILLER-1` (X18 `"PARM-START-DATE,C'"` + `PARM-START-DATE-1 X(10)` + X52 `"'"`). // :103-107
  - `FILLER-2` (X16 `"PARM-END-DATE,C'"` + `PARM-END-DATE-1 X(10)` + X54 `"'"`). // :108-112
  - `FILLER-3` (`PARM-START-DATE-2 X(10)` + X1 space + `PARM-END-DATE-2 X(10)` + X59 spaces). // :117-121
- `JOB-DATA-2 REDEFINES JOB-DATA-1` exposes the same bytes as `JOB-LINES OCCURS 1000 TIMES PIC X(80)` — i.e.
  the JCL template is iterated as an array of 80-byte lines. // source: CORPT00C.cbl:126-127
- The literal JCL lines (in order, // source: CORPT00C.cbl:83-125):
  1. `//TRNRPT00 JOB 'TRAN REPORT',CLASS=A,MSGCLASS=0,`
  2. `// NOTIFY=&SYSUID`
  3. `//*`
  4. `//JOBLIB JCLLIB ORDER=('AWS.M2.CARDDEMO.PROC')`
  5. `//*`
  6. `//STEP10 EXEC PROC=TRANREPT`
  7. `//*`
  8. `//STEP05R.SYMNAMES DD *`
  9. `TRAN-CARD-NUM,263,16,ZD`
  10. `TRAN-PROC-DT,305,10,CH`
  11. `PARM-START-DATE,C'<start>'` (start date injected via `PARM-START-DATE-1`)
  12. `PARM-END-DATE,C'<end>'` (end date injected via `PARM-END-DATE-1`)
  13. `/*`
  14. `//STEP10R.DATEPARM DD *`
  15. `<start> <end>` (the DATEPARM data line via `PARM-START-DATE-2`/`PARM-END-DATE-2`)
  16. `/*`
  17. `/*EOF`

`CSUTLDTC-PARM` — call-area for the date validator (// source: CORPT00C.cbl:129-136):
- `CSUTLDTC-DATE X(10)`, `CSUTLDTC-DATE-FORMAT X(10)`, then `CSUTLDTC-RESULT` redefined as
  `CSUTLDTC-RESULT-SEV-CD X(04)` + FILLER X(11) + `CSUTLDTC-RESULT-MSG-NUM X(04)` + `CSUTLDTC-RESULT-MSG X(61)`.
  (Caller reads bytes 1-4 = severity, 16-19 = msg number — see CSUTLDTC.md §1.)

Copybook constants/structures used:
- `CARDDEMO-COMMAREA` (`COCOM01Y`): `CDEMO-FROM-TRANID`, `CDEMO-FROM-PROGRAM`, `CDEMO-TO-PROGRAM`,
  `CDEMO-PGM-CONTEXT` (88s `CDEMO-PGM-ENTER=0`, `CDEMO-PGM-REENTER=1`). // COCOM01Y.cpy:19-44
- `WS-DATE-TIME` (`CSDAT01Y`): `WS-CURDATE-DATA`/`WS-CURDATE-YEAR/MONTH/DAY`, `WS-CURDATE-N` (REDEFINES as
  PIC 9(08)), `WS-CURTIME-*`, and the formatted `WS-CURDATE-MM-DD-YY` / `WS-CURTIME-HH-MM-SS` groups. // CSDAT01Y.cpy:17-55
- `CCDA-TITLE01/02` (`COTTL01Y`) for header titles. // COTTL01Y.cpy:18-22
- `CCDA-MSG-INVALID-KEY = 'Invalid key pressed. Please see below...         '` (X50) (`CSMSG01Y`). // CSMSG01Y.cpy:20-21
- `CVTRA05Y` `TRAN-RECORD` — copied but unused. // CVTRA05Y.cpy:4-18
- `DFHAID` (EIBAID constants: `DFHENTER`, `DFHPF3`), `DFHBMSCA` (attribute/color: `DFHGREEN`, `DFHRED`,
  `-1` cursor sentinel). // CORPT00C.cbl:148-149

---

## 4. BMS Map CORPT0A (mapset CORPT00)

Screen size 24×80, `DFHMSD CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `MODE=INOUT`, `TIOAPFX=YES`. // source: CORPT00.bms:19-28

Named fields (label / field, position, length, attributes; I=input field in CORPT0AI, O=output in CORPT0AO):
- Static `Tran:` (1,1) L5; **TRNNAME** (1,7) L4 ASKIP — written `TRNNAMEO=CR00`. // bms:29-37; cbl:615
- **TITLE01** (1,21) L40 YELLOW — written. // bms:38-41; cbl:613
- Static `Date:` (1,65) L5; **CURDATE** (1,71) L8 init `mm/dd/yy` — written. // bms:42-51; cbl:622
- Static `Prog:` (2,1) L5; **PGMNAME** (2,7) L8 — written `CORPT00C`. // bms:52-60; cbl:616
- **TITLE02** (2,21) L40 — written. // bms:61-64; cbl:614
- Static `Time:` (2,65) L5; **CURTIME** (2,71) L8 init `hh:mm:ss` — written. // bms:65-74; cbl:628
- Static `Transaction Reports` (4,30) L19 BRT NEUTRAL. // bms:75-79
- **MONTHLY** (7,10) L1 `(FSET,IC,NORM,UNPROT)` GREEN UNDERLINE init `' '` — **input** (radio-style select; any non-space marks Monthly). Has `IC` (initial cursor). // bms:80-85
- Static `Monthly (Current Month)` (7,15) L23 BRT TURQUOISE. // bms:89-93
- **YEARLY** (9,10) L1 `(FSET,NORM,UNPROT)` GREEN init `' '` — **input**. // bms:94-99
- Static `Yearly (Current Year)` (9,15) L23. // bms:103-107
- **CUSTOM** (11,10) L1 `(FSET,NORM,UNPROT)` GREEN init `' '` — **input**. // bms:108-113
- Static `Custom (Date Range)` (11,15) L23. // bms:117-121
- Static `Start Date :` (13,15) L12. // bms:122-126
- **SDTMM** (13,29) L2 `(FSET,NORM,NUM,UNPROT)` GREEN init `'  '` — **input** start-month. // bms:127-132
- Static `/` (13,32). **SDTDD** (13,34) L2 NUM — **input** start-day. Static `/` (13,37). **SDTYYYY** (13,39) L4 NUM — **input** start-year. // bms:133-154
- Static `(MM/DD/YYYY)` (13,46) L12 BLUE. // bms:157-160
- Static `  End Date :` (14,15) L12. // bms:161-165
- **EDTMM** (14,29) L2 NUM — **input** end-month. Static `/`. **EDTDD** (14,34) L2 NUM — **input** end-day. Static `/`. **EDTYYYY** (14,39) L4 NUM — **input** end-year. // bms:166-193
- Static `(MM/DD/YYYY)` (14,46) L12. // bms:196-199
- Static `The Report will be submitted for printing. Please confirm: ` (19,6) L59 TURQUOISE. // bms:200-205
- **CONFIRM** (19,66) L1 `(FSET,NORM,UNPROT)` GREEN — **input** Y/N. // bms:206-210
- Static `(Y/N)` (19,69) L5 NEUTRAL. // bms:213-217
- **ERRMSG** (23,1) L78 `(ASKIP,BRT,FSET)` RED — written `WS-MESSAGE` (clamped to 78). // bms:218-221; cbl:560
- Static footer `ENTER=Continue  F3=Back` (24,1) L23 YELLOW. // bms:222-226

**Read from screen (input):** `MONTHLYI, YEARLYI, CUSTOMI, SDTMMI, SDTDDI, SDTYYYYI, EDTMMI, EDTDDI,
EDTYYYYI, CONFIRMI`. // source: CORPT00C.cbl:213,239,256,259-300,464,478-487
**Written to screen (output):** `TRNNAMEO, TITLE01O, CURDATEO, PGMNAMEO, TITLE02O, CURTIMEO, ERRMSGO` (+
`ERRMSGC=DFHGREEN` color override on the success message). The program also re-echoes parsed numeric values
back into the **input** fields (`MOVE WS-NUM-99 TO SDTMMI ...`), see §6 PROCESS-ENTER-KEY. // source: CORPT00C.cbl:307-327,448,560,613-628

**Cursor control:** `MOVE -1 TO <field>L` sets `-1` into the symbolic length of the field to position the
cursor there on the next SEND (`CURSOR` option). Used on `MONTHLYL` (default), and on the specific failed
field (`SDTMML`, `SDTDDL`, `SDTYYYYL`, `EDTMML`, `EDTDDL`, `EDTYYYYL`, `CONFIRML`). // source: CORPT00C.cbl:180,192,264,...,472

---

## 5. Pseudo-conversational flow (RECEIVE / SEND / RETURN)

Every invocation ends with `EXEC CICS RETURN TRANSID('CR00') COMMAREA(CARDDEMO-COMMAREA)` — either the single
RETURN in `MAIN-PARA` (// :199-202) or the one in `RETURN-TO-CICS` reached by `GO TO` from `SEND-TRNRPT-SCREEN`
(// :580, 587-591). So the next terminal AID re-drives `CR00` into this same program.

`MAIN-PARA` dispatch logic: // source: CORPT00C.cbl:163-202
1. `SET ERR-FLG-OFF`, `SET TRANSACT-NOT-EOF`, `SET SEND-ERASE-YES`; clear `WS-MESSAGE` and `ERRMSGO`. // :165-170
2. **If `EIBCALEN = 0`** (cold start): `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`
   (→ XCTL to sign-on). // :172-174
3. **Else** copy `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. // :176
   - **If NOT `CDEMO-PGM-REENTER`** (first display): `SET CDEMO-PGM-REENTER`, `MOVE LOW-VALUES TO CORPT0AO`,
     `MOVE -1 TO MONTHLYL` (cursor on Monthly), `PERFORM SEND-TRNRPT-SCREEN`. // :177-181
   - **Else** (returning from a prior SEND): `PERFORM RECEIVE-TRNRPT-SCREEN`, then `EVALUATE EIBAID`: // :182-196
     - `DFHENTER` → `PERFORM PROCESS-ENTER-KEY`. // :185-186
     - `DFHPF3` → `MOVE 'COMEN01C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. // :187-189
     - `WHEN OTHER` → `WS-ERR-FLG='Y'`, `MOVE -1 TO MONTHLYL`, `WS-MESSAGE = CCDA-MSG-INVALID-KEY`,
       `PERFORM SEND-TRNRPT-SCREEN`. // :190-194

**AID/PFKey handling summary:** ENTER → process the form; PF3 → back to `COMEN01C`; everything else
(PF1/PF2/PF4..PF24, PA1/PA2, CLEAR, etc.) → "Invalid key pressed. Please see below..." and redisplay. // source: CORPT00C.cbl:185-194

**XCTL/LINK targets:**
- `RETURN-TO-PREV-SCREEN`: `XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)` — targets `COSGN00C`
  (cold start) or `COMEN01C` (PF3). Defaults blank/low-values target to `COSGN00C`. // source: CORPT00C.cbl:540-551
- No `LINK`. The only sub-call is `CALL 'CSUTLDTC'` (static COBOL CALL, not CICS) for date validation. // source: CORPT00C.cbl:392-394, 412-414

---

## 6. PARAGRAPH-BY-PARAGRAPH outline (every paragraph = one method)

### MAIN-PARA  // source: CORPT00C.cbl:163-202
Entry. Reset flags (err off / not-eof / send-erase yes); clear message + `ERRMSGO`. If `EIBCALEN=0` → go to
sign-on (`COSGN00C`). Else load COMMAREA; first pass → init output map to LOW-VALUES, cursor on Monthly, send
fresh screen; subsequent pass → RECEIVE then dispatch on `EIBAID` (ENTER / PF3 / other). Always ends with the
`EXEC CICS RETURN TRANSID('CR00') COMMAREA(...)`. Pseudo-conversational. // :165-202

### PROCESS-ENTER-KEY  // source: CORPT00C.cbl:208-456
`DISPLAY 'PROCESS ENTER KEY'` (trace). Then `EVALUATE TRUE` over which report-type field is non-blank: // :210-443
1. **WHEN `MONTHLYI NOT = SPACES AND LOW-VALUES`** (Monthly selected): // :213-238
   - `WS-REPORT-NAME = 'Monthly'`; `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`.
   - Start date = first of current month: `WS-START-DATE-YYYY = WS-CURDATE-YEAR`, `-MM = WS-CURDATE-MONTH`,
     `-DD = '01'`; move `WS-START-DATE` to `PARM-START-DATE-1` and `PARM-START-DATE-2`. // :217-221
   - End date = last day of current month, computed as (first day of next month) minus 1 day:
     `MOVE 1 TO WS-CURDATE-DAY`; `ADD 1 TO WS-CURDATE-MONTH`; if `WS-CURDATE-MONTH > 12` then
     `ADD 1 TO WS-CURDATE-YEAR` and `MOVE 1 TO WS-CURDATE-MONTH`;
     `COMPUTE WS-CURDATE-N = FUNCTION DATE-OF-INTEGER(FUNCTION INTEGER-OF-DATE(WS-CURDATE-N) - 1)`. // :223-230
     (Arithmetic: integer day math; `WS-CURDATE-N` REDEFINES `WS-CURDATE` as `9(08)` = YYYYMMDD; subtract one
     integer day, convert back — no truncation/sign hazard, pure integer Lillian/Gregorian day math.)
   - `WS-END-DATE-YYYY/MM/DD = WS-CURDATE-YEAR/MONTH/DAY`; move `WS-END-DATE` to `PARM-END-DATE-1`/`-2`. // :232-236
   - `PERFORM SUBMIT-JOB-TO-INTRDR`. // :238
2. **WHEN `YEARLYI NOT = SPACES AND LOW-VALUES`** (Yearly selected): // :239-255
   - `WS-REPORT-NAME = 'Yearly'`; `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`.
   - Start = Jan 1 of current year: `WS-START-DATE-YYYY = WS-END-DATE-YYYY = WS-CURDATE-YEAR`;
     `WS-START-DATE-MM = WS-START-DATE-DD = '01'`; move `WS-START-DATE` to `PARM-START-DATE-1`/`-2`. // :243-248
   - End = Dec 31 of current year: `WS-END-DATE-MM='12'`, `-DD='31'`; move `WS-END-DATE` to `PARM-END-DATE-1`/`-2`. // :250-253
   - `PERFORM SUBMIT-JOB-TO-INTRDR`. // :255
3. **WHEN `CUSTOMI NOT = SPACES AND LOW-VALUES`** (Custom selected): // :256-436
   - **Empty-field guard** (`EVALUATE TRUE`, first matching wins): if `SDTMMI` blank → "Start Date - Month can
     NOT be empty..."; else `SDTDDI` blank → "Start Date - Day can NOT be empty..."; else `SDTYYYYI` blank →
     "Start Date - Year can NOT be empty..."; else `EDTMMI` blank → "End Date - Month can NOT be empty...";
     else `EDTDDI` blank → "End Date - Day can NOT be empty..."; else `EDTYYYYI` blank → "End Date - Year can
     NOT be empty...". Each sets `WS-ERR-FLG='Y'`, cursor on that field's `L`, `PERFORM SEND-TRNRPT-SCREEN`;
     `WHEN OTHER → CONTINUE`. // :258-303
   - **NUMVAL-C normalization** of all six date parts (whether or not the guard flagged an error — no exit):
     `COMPUTE WS-NUM-99 = FUNCTION NUMVAL-C(SDTMMI)` then `MOVE WS-NUM-99 TO SDTMMI` (re-echo, zero-padded);
     repeat for `SDTDDI` (WS-NUM-99), `SDTYYYYI` (WS-NUM-9999), `EDTMMI`, `EDTDDI` (WS-NUM-99), `EDTYYYYI`
     (WS-NUM-9999). // :305-327 (Arithmetic: NUMVAL-C parses numeric value from a possibly punctuated string;
     result MOVEd into a `PIC 99`/`9999` then back into the `X(2)`/`X(4)` map field → numeric→display, zero
     padded; truncation possible if value > 2/4 digits — faithful.)
   - **Range/numeric validations** (each independent `IF`, no `ELSE`, no early exit):
     - `IF SDTMMI NOT NUMERIC OR SDTMMI > '12'` → "Start Date - Not a valid Month..."; flag; cursor SDTMML; SEND. // :329-336
     - `IF SDTDDI NOT NUMERIC OR SDTDDI > '31'` → "Start Date - Not a valid Day..."; flag; cursor SDTDDL; SEND. // :338-345
     - `IF SDTYYYYI NOT NUMERIC` → "Start Date - Not a valid Year..."; flag; cursor SDTYYYYL; SEND. // :347-353
     - `IF EDTMMI NOT NUMERIC OR EDTMMI > '12'` → "End Date - Not a valid Month..."; flag; cursor EDTMML; SEND. // :355-362
     - `IF EDTDDI NOT NUMERIC OR EDTDDI > '31'` → "End Date - Not a valid Day..."; flag; cursor EDTDDL; SEND. // :364-371
     - `IF EDTYYYYI NOT NUMERIC` → "End Date - Not a valid Year..."; flag; cursor EDTYYYYL; SEND. // :373-379
     (String comparisons `> '12'` / `> '31'` are alphanumeric comparisons on the 2-char field.)
   - **Build dates from inputs**: `WS-START-DATE-YYYY/MM/DD = SDTYYYYI/SDTMMI/SDTDDI`;
     `WS-END-DATE-YYYY/MM/DD = EDTYYYYI/EDTMMI/EDTDDI`. // :381-386
   - **CSUTLDTC start-date validation**: `MOVE WS-START-DATE TO CSUTLDTC-DATE`, `WS-DATE-FORMAT TO
     CSUTLDTC-DATE-FORMAT`, `SPACES TO CSUTLDTC-RESULT`; `CALL 'CSUTLDTC' USING CSUTLDTC-DATE,
     CSUTLDTC-DATE-FORMAT, CSUTLDTC-RESULT`. If `CSUTLDTC-RESULT-SEV-CD = '0000'` → OK; else if
     `CSUTLDTC-RESULT-MSG-NUM NOT = '2513'` → "Start Date - Not a valid date..."; flag; cursor SDTMML; SEND.
     (Msg `'2513'` = "date out of supported LE range" is tolerated as valid.) // :388-406
   - **CSUTLDTC end-date validation**: same pattern for `WS-END-DATE`; on failure (sev≠'0000' and msg≠'2513')
     → "End Date - Not a valid date..."; flag; cursor EDTMML; SEND. // :408-426
   - Move `WS-START-DATE`→`PARM-START-DATE-1`/`-2`, `WS-END-DATE`→`PARM-END-DATE-1`/`-2`;
     `WS-REPORT-NAME='Custom'`; `IF NOT ERR-FLG-ON PERFORM SUBMIT-JOB-TO-INTRDR`. // :429-436
4. **WHEN OTHER** (no report type selected): "Select a report type to print report..."; flag; cursor MONTHLYL;
   SEND. // :437-442
- **Success tail**: `IF NOT ERR-FLG-ON`: `PERFORM INITIALIZE-ALL-FIELDS`; `MOVE DFHGREEN TO ERRMSGC`;
  STRING `WS-REPORT-NAME (DELIMITED BY SPACE) + ' report submitted for printing ...' (DELIMITED BY SIZE)` INTO
  `WS-MESSAGE`; `MOVE -1 TO MONTHLYL`; `PERFORM SEND-TRNRPT-SCREEN`. // :445-456

### SUBMIT-JOB-TO-INTRDR  // source: CORPT00C.cbl:462-510
1. **Confirmation guard**: `IF CONFIRMI = SPACES OR LOW-VALUES` → STRING `'Please confirm to print the ' +
   WS-REPORT-NAME (DELIMITED BY SPACE) + ' report...'` INTO `WS-MESSAGE`; `WS-ERR-FLG='Y'`; cursor CONFIRML;
   `PERFORM SEND-TRNRPT-SCREEN`. // :464-474
2. `IF NOT ERR-FLG-ON`: `EVALUATE TRUE` on `CONFIRMI`: // :476-494
   - `'Y' OR 'y'` → CONTINUE (proceed to submit).
   - `'N' OR 'n'` → `PERFORM INITIALIZE-ALL-FIELDS`; `WS-ERR-FLG='Y'`; `PERFORM SEND-TRNRPT-SCREEN`
     (cancels; clears form; redisplays with blank message). // :480-483
   - `WHEN OTHER` → STRING `'"' + CONFIRMI (DELIMITED BY SPACE) + '" is not a valid value to confirm...'` INTO
     `WS-MESSAGE`; `WS-ERR-FLG='Y'`; cursor CONFIRML; `PERFORM SEND-TRNRPT-SCREEN`. // :484-493
3. `SET END-LOOP-NO`. Then `PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > 1000 OR END-LOOP-YES OR
   ERR-FLG-ON`: `MOVE JOB-LINES(WS-IDX) TO JCL-RECORD`; if `JCL-RECORD = '/*EOF' OR SPACES OR LOW-VALUES`
   set `END-LOOP-YES`; `PERFORM WIRTE-JOBSUB-TDQ`. (Note: the loop writes the line FIRST regardless, then on
   the next iteration check stops — actually it sets END-LOOP-YES BEFORE the WRITE, so `/*EOF`/blank line is
   still written then the loop ends — see FB-3.) // :496-508

### WIRTE-JOBSUB-TDQ  *(paragraph name misspelled in source — keep)*  // source: CORPT00C.cbl:515-535
`EXEC CICS WRITEQ TD QUEUE('JOBS') FROM(JCL-RECORD) LENGTH(LENGTH OF JCL-RECORD=80) RESP(WS-RESP-CD)
RESP2(WS-REAS-CD)`. Then `EVALUATE WS-RESP-CD`: `DFHRESP(NORMAL)` → CONTINUE; `WHEN OTHER` →
`DISPLAY 'RESP:' ... 'REAS:' ...`; `WS-ERR-FLG='Y'`; `WS-MESSAGE='Unable to Write TDQ (JOBS)...'`; cursor
MONTHLYL; `PERFORM SEND-TRNRPT-SCREEN`. // :517-535

### RETURN-TO-PREV-SCREEN  // source: CORPT00C.cbl:540-551
If `CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES` set it to `'COSGN00C'`; `MOVE WS-TRANID TO CDEMO-FROM-TRANID`;
`MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM`; `MOVE ZEROS TO CDEMO-PGM-CONTEXT`;
`EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. // :542-551

### SEND-TRNRPT-SCREEN  // source: CORPT00C.cbl:556-580
`PERFORM POPULATE-HEADER-INFO`; `MOVE WS-MESSAGE TO ERRMSGO`; if `SEND-ERASE-YES` → `EXEC CICS SEND
MAP('CORPT0A') MAPSET('CORPT00') FROM(CORPT0AO) ERASE CURSOR`; else (dead branch) the same without ERASE.
Then `GO TO RETURN-TO-CICS`. // :558-580 (Note: SEND-ERASE is always Y → always ERASE; the no-ERASE arm and
`CURSOR` honors the `-1` length sentinel for cursor placement.)

### RETURN-TO-CICS  // source: CORPT00C.cbl:585-591
`EXEC CICS RETURN TRANSID('CR00') COMMAREA(CARDDEMO-COMMAREA)`. (Reached only via `GO TO` from
SEND-TRNRPT-SCREEN; this is the exit for every send path — i.e. once a SEND happens, control leaves the
program; later statements after a `PERFORM SEND-TRNRPT-SCREEN` in the same paragraph never run after a SEND.
See FB-1.) // :587-591

### RECEIVE-TRNRPT-SCREEN  // source: CORPT00C.cbl:596-604
`EXEC CICS RECEIVE MAP('CORPT0A') MAPSET('CORPT00') INTO(CORPT0AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. RESP
captured but **never inspected**. // :598-604

### POPULATE-HEADER-INFO  // source: CORPT00C.cbl:609-628
`MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`; titles → `TITLE01O`/`TITLE02O`; `WS-TRANID`→`TRNNAMEO`;
`WS-PGMNAME`→`PGMNAMEO`; build `mm/dd/yy` (`WS-CURDATE-MM/DD`, `WS-CURDATE-YY = WS-CURDATE-YEAR(3:2)`) →
`CURDATEO`; build `hh:mm:ss` (`WS-CURTIME-HH/MM/SS`) → `CURTIMEO`. // :611-628

### INITIALIZE-ALL-FIELDS  // source: CORPT00C.cbl:633-646
`MOVE -1 TO MONTHLYL` (cursor Monthly); `INITIALIZE` the ten input fields `MONTHLYI, YEARLYI, CUSTOMI,
SDTMMI, SDTDDI, SDTYYYYI, EDTMMI, EDTDDI, EDTYYYYI, CONFIRMI` and `WS-MESSAGE` (INITIALIZE sets alphanumeric
to spaces). // :635-646

---

## 7. Validation rules & exact literal messages

| # | Trigger | Exact message text (verbatim) | Source |
|---|---|---|---|
| 1 | Non-ENTER, non-PF3 AID | `Invalid key pressed. Please see below...         ` (X50, `CCDA-MSG-INVALID-KEY`) | cbl:193; CSMSG01Y.cpy:20-21 |
| 2 | Custom: start month blank | `Start Date - Month can NOT be empty...` | cbl:261 |
| 3 | Custom: start day blank | `Start Date - Day can NOT be empty...` | cbl:268 |
| 4 | Custom: start year blank | `Start Date - Year can NOT be empty...` | cbl:275 |
| 5 | Custom: end month blank | `End Date - Month can NOT be empty...` | cbl:282 |
| 6 | Custom: end day blank | `End Date - Day can NOT be empty...` | cbl:289 |
| 7 | Custom: end year blank | `End Date - Year can NOT be empty...` | cbl:296 |
| 8 | Start month non-numeric or > '12' | `Start Date - Not a valid Month...` | cbl:331 |
| 9 | Start day non-numeric or > '31' | `Start Date - Not a valid Day...` | cbl:340 |
| 10 | Start year non-numeric | `Start Date - Not a valid Year...` | cbl:348 |
| 11 | End month non-numeric or > '12' | `End Date - Not a valid Month...` | cbl:357 |
| 12 | End day non-numeric or > '31' | `End Date - Not a valid Day...` | cbl:366 |
| 13 | End year non-numeric | `End Date - Not a valid Year...` | cbl:374 |
| 14 | CSUTLDTC start date invalid (sev≠'0000' and msg≠'2513') | `Start Date - Not a valid date...` | cbl:400 |
| 15 | CSUTLDTC end date invalid (sev≠'0000' and msg≠'2513') | `End Date - Not a valid date...` | cbl:420 |
| 16 | No report type selected | `Select a report type to print report...` | cbl:438 |
| 17 | Confirm blank | STRING: `Please confirm to print the ` + `<report name>` (delim SPACE) + ` report...` | cbl:465-470 |
| 18 | Confirm value not Y/y/N/n | STRING: `"` + `<confirm char>` (delim SPACE) + `" is not a valid value to confirm...` | cbl:485-490 |
| 19 | TDQ WRITEQ failed | `Unable to Write TDQ (JOBS)...` | cbl:531 |
| 20 | Success (no error) | STRING: `<report name>` (delim SPACE) + ` report submitted for printing ...` ; color GREEN (`DFHGREEN`) | cbl:449-451 |

Report-type selection rule: a report-type radio is "selected" when its 1-char field is `NOT = SPACES AND
LOW-VALUES` (i.e. neither space nor binary low-value). The `EVALUATE TRUE` picks the **first** matching of
Monthly → Yearly → Custom, so if multiple are filled, Monthly wins, then Yearly. // source: CORPT00C.cbl:212-256

Confirm rule: accept only `'Y'`/`'y'` (proceed) or `'N'`/`'n'` (cancel); blank → "please confirm"; anything
else → "not a valid value". // source: CORPT00C.cbl:464-494

---

## 8. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

- **FB-1 — No early return after a validation `PERFORM SEND-TRNRPT-SCREEN`; but SEND-TRNRPT-SCREEN exits the
  program via `GO TO RETURN-TO-CICS`.** `SEND-TRNRPT-SCREEN` ends with `GO TO RETURN-TO-CICS` which issues
  `EXEC CICS RETURN` — so the **first** `PERFORM SEND-TRNRPT-SCREEN` in any paragraph effectively terminates
  the program (the PERFORM never returns to its caller; the rest of `PROCESS-ENTER-KEY` / the validation chain
  after it does NOT execute). This means: in the Custom path, the empty-field guard `EVALUATE` and each
  subsequent `IF` validation, if it fires, **does NOT continue** to the later validations or to NUMVAL-C
  normalization — the program just returns to CICS on the first failure. The `IF NOT ERR-FLG-ON` guards are
  therefore largely redundant for the failure paths but MUST be reproduced exactly, because the port's screen
  shim must mimic "PERFORM SEND-TRNRPT-SCREEN == return from the handler". In the .NET port model
  `SEND-TRNRPT-SCREEN` should SEND the map and then return control all the way out of the transaction (queue
  the RETURN), not fall through. // source: CORPT00C.cbl:556-591 (GO TO RETURN-TO-CICS), 258-456

- **FB-2 — Date-part validations re-echo NUMVAL-C results into INPUT fields, then compare them as text.**
  `MOVE WS-NUM-99 TO SDTMMI` writes a `PIC 99` numeric (zero-padded "00".."99") back into the 2-char input
  field; the next checks compare `SDTMMI > '12'` etc. as **alphanumeric** strings. Because the value is now
  zero-padded numeric text, comparisons like `'13' > '12'` work, but e.g. month `'00'` passes `NOT > '12'`
  and is `NUMERIC`, so month/day = `00` are accepted as valid (not caught). Day `'00'` likewise passes. The
  CSUTLDTC call is the only thing that may reject `00`. Preserve this: do not add a `>= 1` lower-bound check. // source: CORPT00C.cbl:305-371

- **FB-3 — `/*EOF` and trailing blank JCL lines are written to the TDQ before the loop stops.** In
  `SUBMIT-JOB-TO-INTRDR` the loop sets `END-LOOP-YES` when `JCL-RECORD = '/*EOF' OR SPACES OR LOW-VALUES`
  **before** calling `WIRTE-JOBSUB-TDQ`, but the `PERFORM WIRTE-JOBSUB-TDQ` still executes in that same
  iteration — so the terminating sentinel line (`/*EOF` or the first blank/low-value line) IS written to the
  `JOBS` queue. Reproduce: write the line that triggered the stop, then end. // source: CORPT00C.cbl:498-508

- **FB-4 — Confirm error message uses `DELIMITED BY SPACE` on `CONFIRMI` (1 char), and the success/confirm
  messages also delimit `WS-REPORT-NAME` by SPACE.** For a 1-char confirm value the SPACE delimiter is moot,
  but if `CONFIRMI` is a space it yields an empty insert producing `"" is not a valid value to confirm...` —
  however this branch is only reached when CONFIRMI is NOT space (the blank case is handled earlier), so it
  is effectively `"<char>"`. Keep the SPACE-delimited STRING semantics. // source: CORPT00C.cbl:484-490

- **FB-5 — `WS-MESSAGE` (X80) wider than `ERRMSGO` (X78) → 2-char truncation.** `MOVE WS-MESSAGE TO ERRMSGO`
  silently drops the last 2 chars. All current messages fit, but reproduce the 78-char clamp on the error
  line. // source: CORPT00C.cbl:39, 560; CORPT00.CPY:224

- **FB-6 — Dead working storage / dead branch.** `WS-TRANSACT-EOF` (set, never tested), `WS-REC-COUNT`,
  `WS-TRAN-AMT`, `WS-TRAN-DATE`, the copied `TRAN-RECORD` (CVTRA05Y), and the no-ERASE arm of
  `SEND-TRNRPT-SCREEN` (`SEND-ERASE-NO` never set) are all dead. Carry them as inert state (don't repurpose). // source: CORPT00C.cbl:44-46,56,77-78,146,562-578

- **FB-7 — `DISPLAY 'PROCESS ENTER KEY'` and the WRITEQ-failure `DISPLAY 'RESP:'...` are debug traces.** They
  emit to the CICS region log/SYSOUT, not the screen. Treat as logging side effects (or no-ops) but document. // source: CORPT00C.cbl:210, 529

- **FB-8 — Monthly end-of-month math uses `WS-CURDATE-N` AFTER mutating month/day in place.** It sets
  `WS-CURDATE-DAY=01`, bumps month (and possibly year) for the next month, then `INTEGER-OF-DATE(WS-CURDATE-N)`
  where `WS-CURDATE-N` redefines the just-mutated `WS-CURDATE` (now = first of NEXT month), subtracts 1 day →
  last day of the ORIGINAL month. Reproduce exactly; the intermediate `WS-CURDATE` is the next-month date, the
  result is the original month's last day. // source: CORPT00C.cbl:223-234; CSDAT01Y.cpy:19-23

---

## 9. PORT NOTES (relational + COBOL semantics)

- **No data layer needed.** Implement as CICS-shim handler `CR00 → CORPT00C` in `CardDemo.Online` with a
  screen model for `CORPT0A`. Dependencies: the CICS shim (COMMAREA store, XCTL/RETURN, AID), an `IClock`
  (header + report date math), the `CSUTLDTC` date-validator service, and an **`IJobSubmitQueue`/internal-
  reader** sink for the `JOBS` TDQ writes. No DbContext/repository.

- **TDQ `JOBS` → internal reader.** `EXEC CICS WRITEQ TD QUEUE('JOBS')` appends one 80-byte fixed record per
  call. Model `JOBS` as a job-submit queue that accumulates 80-char lines; "submitting" the assembled stream
  triggers the `TRANREPT` batch job (port artifact). The success/failure of each WRITEQ maps to a result code:
  NORMAL → continue, anything else → the "Unable to Write TDQ (JOBS)..." error path. Preserve fixed 80-byte
  line width (right-pad with spaces). // source: CORPT00C.cbl:517-523

- **JCL template assembly (REDEFINES + OCCURS).** `JOB-DATA-1` is a flat 80×N byte block of literal JCL lines
  with date placeholders embedded; `JOB-DATA-2 REDEFINES` it as `JOB-LINES OCCURS 1000 PIC X(80)`. Port as a
  list of 80-char line templates; inject the start/end dates into the 4 placeholder slots
  (`PARM-START-DATE-1/2`, `PARM-END-DATE-1/2`) before iterating. The loop reads `JOB-LINES(1..)` until `/*EOF`
  / blank / low-values OR index>1000 OR error (writing the sentinel line — FB-3). The placeholder MOVEs write
  the SAME 10-char date into both the SYMNAMES `C'...'` form and the DATEPARM data line. // source: CORPT00C.cbl:103-127, 220-236, 247-253, 429-432, 496-508

- **`FUNCTION CURRENT-DATE`** → `IClock.Now`. `WS-CURDATE-DATA` is the 21-char CURRENT-DATE register
  (YYYYMMDDHHMMSSmm…); header uses `WS-CURDATE-YEAR(3:2)` (last 2 digits) for `mm/dd/yy`. Report date math
  (Monthly/Yearly) uses `WS-CURDATE-YEAR/MONTH/DAY`. Mask header date/time in screen-parity diffs. // source: CORPT00C.cbl:215, 611, 620

- **`INTEGER-OF-DATE` / `DATE-OF-INTEGER`** — Gregorian day-number conversions for the last-day-of-month calc;
  port via .NET `DateOnly`/day arithmetic. `WS-CURDATE-N` REDEFINES `WS-CURDATE` as `PIC 9(08)` (YYYYMMDD);
  represent the mutated next-month date then subtract one day (FB-8). // source: CORPT00C.cbl:229-230; CSDAT01Y.cpy:23

- **`FUNCTION NUMVAL-C`** — parses a numeric value out of a (possibly punctuated) string; here applied to the
  2/4-char date-part input fields, result MOVEd to `PIC 99`/`9999` then back to the X(2)/X(4) field (zero-padded
  numeric text). Port: parse leniently to int (ignoring non-numeric noise per NUMVAL-C rules), clamp to field
  width (2 or 4 digits, silent overflow truncation), re-render zero-padded. // source: CORPT00C.cbl:305-327

- **`EVALUATE TRUE` first-match radio selection** — Monthly > Yearly > Custom; `NOT = SPACES AND LOW-VALUES`
  means "field is neither all-spaces nor binary-low-values". The console input model must distinguish a
  never-touched field (LOW-VALUES / null) from a space-typed field — both count as "not selected" here, so
  treat empty/space/low-values uniformly as unselected. // source: CORPT00C.cbl:212-256

- **`INITIALIZE` of input fields** sets them to spaces (alphanumeric). On success/cancel the form is cleared
  and cursor returned to Monthly. // source: CORPT00C.cbl:633-646

- **Cursor sentinel `-1`** in the `*L` length field → CICS `CURSOR` option places the cursor at that field on
  the next SEND. Model per-field "cursor here" flag honored by the renderer (only one is -1 at a time per
  path). // source: CORPT00C.cbl:180,264,562-577

- **`SEND ... ERASE CURSOR`** clears the 24×80 buffer then draws. `SEND-ERASE-YES` is always true here. The
  output map is initialized with `MOVE LOW-VALUES TO CORPT0AO` on first display so unset fields transmit as
  null (no-data) rather than spaces. // source: CORPT00C.cbl:179, 562-569

- **COMMAREA persistence / re-entry** — `CDEMO-PGM-CONTEXT` 88 `CDEMO-PGM-REENTER` is the first-pass flag;
  store COMMAREA on RETURN, rehydrate on next AID. PF3 sets `CDEMO-TO-PROGRAM='COMEN01C'`; cold start (no
  commarea) → `COSGN00C`. // source: CORPT00C.cbl:172-197, 540-551

- **Edited PIC** — `WS-TRAN-AMT PIC +99999999.99` is dead. The live edited fields are `WS-NUM-99 PIC 99` /
  `WS-NUM-9999 PIC 9999` (zero-padded numeric) used for the re-echo; date groups use embedded `'-'`/`'/'`
  FILLERs (`YYYY-MM-DD`, `MM/DD/YY`, `HH:MM:SS`). // source: CORPT00C.cbl:60-78; CSDAT01Y.cpy:30-41

---

## 10. OPEN QUESTIONS / RISKS

- **TDQ → job execution wiring.** This spec ports CORPT00C's behavior up to *emitting* the JCL stream to the
  `JOBS` queue. The actual report generation belongs to the `TRANREPT` PROC / batch program (`STEP10
  EXEC PROC=TRANREPT`, reading `TRANSACT` with start/end date params). Confirm whether the .NET port should
  (a) merely capture the emitted JCL lines for characterization (no execution), or (b) actually trigger the
  ported batch report when the queue is "submitted". Recommend (a) for parity tests + (b) behind the
  job-runner once `TRANREPT` is ported. // source: CORPT00C.cbl:90-94

- **FB-1 control-flow model.** The `PERFORM SEND-TRNRPT-SCREEN` → `GO TO RETURN-TO-CICS` means a SEND ends the
  transaction; the port's screen shim must treat "send" as a terminating return, not a fall-through. Validate
  with a characterization test that an early validation failure does NOT reach later validations or the JCL
  loop.

- **Month/day `00` acceptance (FB-2).** With NUMVAL-C re-echo, `00` for month or day is accepted by the
  range checks and only possibly rejected by CSUTLDTC. Confirm whether the ported `CSUTLDTC`/date service
  rejects `YYYY-00-DD` / `YYYY-MM-00` under the `YYYY-MM-DD` mask (LE `CEEDAYS` would; msg≠'2513' → error).
  Pin the exact accept/reject outcome with a date-corpus test.

- **NUMVAL-C edge behavior.** NUMVAL-C on non-numeric junk (e.g. `'/3'`) returns 0; whether the .NET parser
  matches NUMVAL-C exactly for all 3270-typeable inputs must be pinned (it feeds the re-echo and the
  subsequent text comparisons). // source: CORPT00C.cbl:305-327

- **`/*EOF`/blank sentinel written to queue (FB-3).** Confirm the downstream internal-reader tolerates the
  trailing `/*EOF` (and any blank line) being present — it is part of the emitted stream and must be in the
  captured golden.
