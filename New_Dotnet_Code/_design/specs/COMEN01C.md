# PORT SPEC — COMEN01C (Main Menu for Regular Users)

Program kind: **online (CICS pseudo-conversational)**
Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COMEN01C.cbl`
BMS map/mapset: `COMEN1A` / `COMEN01` (`app/bms/COMEN01.bms`, symbolic map `app/cpy-bms/COMEN01.CPY`)
Target: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.ConsoleApp` (screen render), per `_design/ARCHITECTURE.md`.

---

## 1. Purpose & Invocation

COMEN01C is the **main menu for "regular" (non-admin) users** of CardDemo. It renders a 12-slot menu populated from a hard-coded option table (11 active options), accepts a 2-digit option number, validates it, and `XCTL`s to the program backing the chosen option. It is a pseudo-conversational CICS program: each press of ENTER re-enters the same transaction. PF3 returns to the sign-on program. // source: COMEN01C.cbl:4-5 (Function: "Main Menu for the Regular users"), 22-23 (PROGRAM-ID COMEN01C)

- **CICS TRANSID**: `CM00` (the value RETURNed with, `WS-TRANID`). // source: COMEN01C.cbl:37, 107-110
- **Program name (self)**: `COMEN01C` (`WS-PGMNAME`). // source: COMEN01C.cbl:36
- **How reached**: normally via `XCTL` from `COSGN00C` (sign-on) after a regular user logs in, or via `XCTL` back from any child menu program passing the shared `CARDDEMO-COMMAREA`. There is no JCL; it is purely online. The transaction is also driven by re-entry on ENTER.
- **Linkage**: receives/returns `DFHCOMMAREA` typed as `CARDDEMO-COMMAREA` (copybook `COCOM01Y`). // source: COMEN01C.cbl:50, 66-69, 86, 109

---

## 2. FILE / TABLE Access

**This program performs NO VSAM/file I/O and NO SQL.** It has `WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC'` declared (// source: COMEN01C.cbl:39) but it is **never opened, read, or referenced** in the PROCEDURE DIVISION — it is dead working storage carried over from a sign-on template. There are no `EXEC CICS READ/WRITE/STARTBR/READNEXT` statements anywhere in this program.

| COBOL file | Relational table (ARCHITECTURE) | Operation(s) | SQL |
|---|---|---|---|
| `USRSEC` (declared only) | USER_SECURITY | **none — dead declaration** | — |

The only CICS resource interaction is `EXEC CICS INQUIRE PROGRAM` (a catalog probe, not data I/O — see §5/§6). // source: COMEN01C.cbl:148-151

> **Port note:** the .NET handler needs no repository/DbContext dependency for data. It depends only on the CICS shim (COMMAREA store, XCTL/RETURN, AID), the screen model for `COMEN1A`, an `IClock` for the date/time header, and a way to test "is target program installed" (the INQUIRE PROGRAM analog — a registry of known transaction handlers).

---

## 3. Working storage & embedded tables (logic-affecting)

`WS-VARIABLES` (// source: COMEN01C.cbl:35-48):
- `WS-PGMNAME PIC X(08) VALUE 'COMEN01C'` // :36
- `WS-TRANID PIC X(04) VALUE 'CM00'` // :37
- `WS-MESSAGE PIC X(80)` — error/info line buffer (note: 80 wide, but the map field `ERRMSGO` is only X(78)). // :38, COMEN01.CPY:260
- `WS-ERR-FLG PIC X(01)` with 88s `ERR-FLG-ON='Y'` / `ERR-FLG-OFF='N'`. // :40-42
- `WS-RESP-CD`, `WS-REAS-CD` PIC S9(09) COMP — RESP/RESP2 from RECEIVE. // :43-44
- `WS-OPTION-X PIC X(02) JUST RIGHT` — text form of the option (right-justified). // :45
- `WS-OPTION PIC 9(02) VALUE 0` — numeric form of the option. // :46
- `WS-IDX PIC S9(04) COMP` — loop index. // :47
- `WS-MENU-OPT-TXT PIC X(40)` — formatted menu line "NN. name". // :48

`CARDDEMO-MAIN-MENU-OPTIONS` (copybook `COMEN02Y`, // source: COMEN02Y.cpy:19-99): a count + a 12-element `OCCURS` table (`CDEMO-MENU-OPT`) over hard-coded data:
- `CDEMO-MENU-OPT-COUNT PIC 9(02) VALUE 11`. // COMEN02Y.cpy:21
- Each entry: `CDEMO-MENU-OPT-NUM 9(02)`, `CDEMO-MENU-OPT-NAME X(35)`, `CDEMO-MENU-OPT-PGMNAME X(08)`, `CDEMO-MENU-OPT-USRTYPE X(01)`. // COMEN02Y.cpy:94-98
- 11 populated rows; the 12th `OCCURS` slot is **unpopulated FILLER beyond the data** (the REDEFINES gives 12 entries but only 11 sets of literals exist — slot 12 reads whatever follows in storage, effectively low/space binary garbage). This is relevant to the bounds check (see Faithful Bugs). // COMEN02Y.cpy:23-91 vs 93-98

The 11 options (num → name → program → usrtype): // source: COMEN02Y.cpy:25-90
1. `Account View` → `COACTVWC` → U
2. `Account Update` → `COACTUPC` → U
3. `Credit Card List` → `COCRDLIC` → U
4. `Credit Card View` → `COCRDSLC` → U
5. `Credit Card Update` → `COCRDUPC` → U
6. `Transaction List` → `COTRN00C` → U
7. `Transaction View` → `COTRN01C` → U
8. `Transaction Add` → `COTRN02C` → U   (NOTE: name literal had "(Admin Only)" commented out; effective usrtype is **U**, see Faithful Bug FB-4) // COMEN02Y.cpy:67-72
9. `Transaction Reports` → `CORPT00C` → U
10. `Bill Payment` → `COBIL00C` → U
11. `Pending Authorization View` → `COPAUS0C` → U

Constants from copybooks:
- `CCDA-TITLE01 = '      AWS Mainframe Modernization       '` (X40), `CCDA-TITLE02 = '              CardDemo                  '` (X40). // COTTL01Y.cpy:18-22
- `CCDA-MSG-INVALID-KEY = 'Invalid key pressed. Please see below...         '` (X50). // CSMSG01Y.cpy:20-21
- `WS-DATE-TIME` group for header date/time formatting (`CSDAT01Y`). // CSDAT01Y.cpy:17-55

`CARDDEMO-COMMAREA` (copybook `COCOM01Y`, // source: COCOM01Y.cpy:19-44) — fields this program uses:
- `CDEMO-FROM-TRANID X(04)`, `CDEMO-FROM-PROGRAM X(08)`, `CDEMO-TO-PROGRAM X(08)`. // COCOM01Y.cpy:21-24
- `CDEMO-USER-TYPE X(01)` with 88s `CDEMO-USRTYP-ADMIN='A'`, `CDEMO-USRTYP-USER='U'`. // COCOM01Y.cpy:26-28
- `CDEMO-PGM-CONTEXT 9(01)` with 88s `CDEMO-PGM-ENTER=0`, `CDEMO-PGM-REENTER=1`. // COCOM01Y.cpy:29-31

---

## 4. BMS Map COMEN1A (mapset COMEN01)

Screen size 24×80, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`. // source: COMEN01.bms:19-28

Fields (label / named field, position, length, attr):
- Static `Tran:` (1,1); **TRNNAME** (1,7) L4 ASKIP — written `TRNNAMEO` = `CM00`. // COMEN01.bms:29-37; COMEN01C.cbl:244
- **TITLE01** (1,21) L40 ASKIP YELLOW — written. // COMEN01.bms:38-41; cbl:242
- Static `Date:` (1,65); **CURDATE** (1,71) L8 — written `mm/dd/yy`. // COMEN01.bms:47-51; cbl:251
- Static `Prog:` (2,1); **PGMNAME** (2,7) L8 — written `COMEN01C`. // COMEN01.bms:57-60; cbl:245
- **TITLE02** (2,21) L40 — written. // COMEN01.bms:61-64; cbl:243
- Static `Time:` (2,65); **CURTIME** (2,71) L8 — written `hh:mm:ss`. // COMEN01.bms:70-74; cbl:257
- Static `Main Menu` (4,35) BRT NEUTRAL. // COMEN01.bms:75-79
- **OPTN001..OPTN012** (rows 6..17, col 20) L40 each ASKIP — written by BUILD-MENU-OPTIONS. // COMEN01.bms:80-139; cbl:276-298
- Static `Please select an option :` (20,15) BRT TURQUOISE. // COMEN01.bms:140-144
- **OPTION** (20,41) L2 — the ONLY input field. ATTRB `(FSET,IC,NORM,NUM,UNPROT)`, `HILIGHT=UNDERLINE`, `JUSTIFY=(RIGHT,ZERO)`. // COMEN01.bms:145-149
- **ERRMSG** (23,1) L78 BRT RED — written `WS-MESSAGE`. // COMEN01.bms:154-157; cbl:213, 260
- Static footer `ENTER=Continue  F3=Exit` (24,1). // COMEN01.bms:158-162

**Read from screen (input):** only `OPTIONI` (the option number). All other named fields are output-only on this program's flow. // source: COMEN01C.cbl:118-122
**Written to screen (output):** `TRNNAMEO, TITLE01O, CURDATEO, PGMNAMEO, TITLE02O, CURTIMEO, OPTN001O..OPTN012O, OPTIONO, ERRMSGO` (+ `ERRMSGC` color override in some branches). // source: COMEN01C.cbl:242-257, 276-298, 125, 162, 171, 213

`OPTION` field map semantics worth porting faithfully: `JUSTIFY=(RIGHT,ZERO)` + `NUM` means the 3270 right-justifies and zero-fills the keyed digits. The program then re-derives the number from `OPTIONI` itself (see PROCESS-ENTER-KEY), so the .NET console input model should deliver `OPTIONI` as the raw 2-char field (right-justified, spaces where not typed).

---

## 5. Pseudo-conversational flow (RECEIVE / SEND / RETURN)

Every invocation ends with: // source: COMEN01C.cbl:107-110
```
EXEC CICS RETURN TRANSID(WS-TRANID='CM00') COMMAREA(CARDDEMO-COMMAREA)
```
so the next terminal AID re-drives transaction `CM00` into this same program.

`MAIN-PARA` dispatch logic: // source: COMEN01C.cbl:75-110
1. `SET ERR-FLG-OFF` ; clear `WS-MESSAGE` and `ERRMSGO`. // :77-80
2. **If `EIBCALEN = 0`** (cold start, no commarea): set `CDEMO-FROM-PROGRAM='COSGN00C'` then `PERFORM RETURN-TO-SIGNON-SCREEN` (XCTL to sign-on). // :82-84
3. **Else** copy `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. // :86
   - **If NOT `CDEMO-PGM-REENTER`** (first display): set reenter flag, `MOVE LOW-VALUES TO COMEN1AO`, `PERFORM SEND-MENU-SCREEN`. // :87-90
   - **Else** (returning from a prior SEND): `PERFORM RECEIVE-MENU-SCREEN`, then `EVALUATE EIBAID`: // :91-104
     - `DFHENTER` → `PERFORM PROCESS-ENTER-KEY`. // :94-95
     - `DFHPF3` → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-SIGNON-SCREEN`. // :96-98
     - `WHEN OTHER` (any other AID/PFKey) → set `WS-ERR-FLG='Y'`, `WS-MESSAGE = CCDA-MSG-INVALID-KEY`, `PERFORM SEND-MENU-SCREEN`. // :99-102

**AID/PFKey handling summary:** ENTER → process option; PF3 → exit to sign-on; everything else (PF1,PF2,PF4..PF24,PA1,PA2,CLEAR, etc.) → "Invalid key pressed..." and redisplay. // source: COMEN01C.cbl:93-103

**XCTL/LINK targets:**
- `RETURN-TO-SIGNON-SCREEN`: `XCTL PROGRAM(CDEMO-TO-PROGRAM)` (defaults to `COSGN00C`). No COMMAREA passed. // source: COMEN01C.cbl:201-203
- `PROCESS-ENTER-KEY`: `XCTL PROGRAM(CDEMO-MENU-OPT-PGMNAME(WS-OPTION)) COMMAREA(CARDDEMO-COMMAREA)` to the chosen option's program (two XCTL sites: the COPAUS0C branch and the generic OTHER branch). // source: COMEN01C.cbl:156-159, 184-187
- `INQUIRE PROGRAM(...) NOHANDLE`: probes whether the option's program is installed (used only for option 11 / COPAUS0C). // source: COMEN01C.cbl:148-151

---

## 6. PARAGRAPH-BY-PARAGRAPH outline (every paragraph = one method)

### MAIN-PARA  // source: COMEN01C.cbl:75-110
Entry. Reset error flag and message buffers; branch on `EIBCALEN`. Cold start → go to sign-on. Otherwise load COMMAREA; first pass → send fresh menu; subsequent pass → RECEIVE then dispatch on `EIBAID` (ENTER/PF3/other). Always ends with `EXEC CICS RETURN TRANSID('CM00') COMMAREA(...)`. Pseudo-conversational; the RETURN is the single exit. // :77-110

### PROCESS-ENTER-KEY  // source: COMEN01C.cbl:115-191
1. **Right-trim scan**: `PERFORM VARYING WS-IDX FROM LENGTH OF OPTIONI BY -1 UNTIL OPTIONI(WS-IDX:1) NOT = SPACES OR WS-IDX = 1` — finds the index of the last non-space char (or 1). `LENGTH OF OPTIONI` = 2. // :117-121
2. `MOVE OPTIONI(1:WS-IDX) TO WS-OPTION-X` — copies the leading `WS-IDX` chars into the right-justified field. `INSPECT WS-OPTION-X REPLACING ALL ' ' BY '0'` (spaces → '0'). `MOVE WS-OPTION-X TO WS-OPTION` (text→numeric). `MOVE WS-OPTION TO OPTIONO` (echo back to screen, zero-padded). // :122-125
3. **Range validation** `IF WS-OPTION IS NOT NUMERIC OR WS-OPTION > CDEMO-MENU-OPT-COUNT OR WS-OPTION = ZEROS`: set err flag, `WS-MESSAGE = 'Please enter a valid option number...'`, `PERFORM SEND-MENU-SCREEN`. **(No GO BACK / no fall-through guard — see FB-1.)** // :127-134
4. **Admin-only guard** `IF CDEMO-USRTYP-USER AND CDEMO-MENU-OPT-USRTYPE(WS-OPTION) = 'A'`: set `ERR-FLG-ON`, `WS-MESSAGE = 'No access - Admin Only option... '`, `PERFORM SEND-MENU-SCREEN`. (With the shipped table all usrtypes are 'U', so this branch never fires — FB-4.) // :136-143
5. **Dispatch** `IF NOT ERR-FLG-ON` → `EVALUATE TRUE`: // :145-188
   - **WHEN `CDEMO-MENU-OPT-PGMNAME(WS-OPTION) = 'COPAUS0C'`** (option 11): `EXEC CICS INQUIRE PROGRAM(...) NOHANDLE`. If `EIBRESP = DFHRESP(NORMAL)` (installed): set `CDEMO-FROM-TRANID='CM00'`, `CDEMO-FROM-PROGRAM='COMEN01C'`, `CDEMO-PGM-CONTEXT=0`, then `XCTL PROGRAM(COPAUS0C) COMMAREA(...)`. Else (not installed): `MOVE SPACES TO WS-MESSAGE`, `MOVE DFHRED TO ERRMSGC`, STRING `'This option ' + name(delim '  ') + ' is not installed...'` INTO `WS-MESSAGE`. // :147-168
   - **WHEN `CDEMO-MENU-OPT-PGMNAME(WS-OPTION)(1:5) = 'DUMMY'`**: `MOVE SPACES TO WS-MESSAGE`, `MOVE DFHGREEN TO ERRMSGC`, STRING `'This option ' + name(delim SPACE) + 'is coming soon ...'` INTO `WS-MESSAGE`. (No shipped option starts with DUMMY, so unreachable with the current table.) // :169-176
   - **WHEN OTHER** (all other valid programs, options 1-10): set `CDEMO-FROM-TRANID='CM00'`, `CDEMO-FROM-PROGRAM='COMEN01C'` (set twice — FB-2), `CDEMO-PGM-CONTEXT=0`, then `XCTL PROGRAM(CDEMO-MENU-OPT-PGMNAME(WS-OPTION)) COMMAREA(...)`. // :177-187
   - After EVALUATE, `PERFORM SEND-MENU-SCREEN` (re-displays when no XCTL happened, i.e. COPAUS0C-not-installed or DUMMY branches). // :190
   Note: arithmetic here is only `MOVE WS-OPTION-X TO WS-OPTION` (alphanumeric→numeric implicit conversion of a zero-filled 2-char string into PIC 9(02)); no COMPUTE, no truncation/sign concern beyond the 2-digit width.

### RETURN-TO-SIGNON-SCREEN  // source: COMEN01C.cbl:196-203
If `CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES` set it to `'COSGN00C'`; then `XCTL PROGRAM(CDEMO-TO-PROGRAM)` (no COMMAREA). The `OR SPACES` is an abbreviated comparison: `(CDEMO-TO-PROGRAM = LOW-VALUES) OR (CDEMO-TO-PROGRAM = SPACES)`. // :198-203

### SEND-MENU-SCREEN  // source: COMEN01C.cbl:208-220
`PERFORM POPULATE-HEADER-INFO`; `PERFORM BUILD-MENU-OPTIONS`; `MOVE WS-MESSAGE TO ERRMSGO`; `EXEC CICS SEND MAP('COMEN1A') MAPSET('COMEN01') FROM(COMEN1AO) ERASE`. (No `CURSOR`, no `DATAONLY`; `ERASE` clears the screen first.) // :210-220

### RECEIVE-MENU-SCREEN  // source: COMEN01C.cbl:225-233
`EXEC CICS RECEIVE MAP('COMEN1A') MAPSET('COMEN01') INTO(COMEN1AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. RESP codes captured but **never inspected** afterward (no error handling on the RECEIVE). // :227-233

### POPULATE-HEADER-INFO  // source: COMEN01C.cbl:238-257
`MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`. Move titles (`CCDA-TITLE01/02`), `WS-TRANID`→`TRNNAMEO`, `WS-PGMNAME`→`PGMNAMEO`. Build `mm/dd/yy` from `WS-CURDATE-MONTH/DAY/YEAR(3:2)` into `CURDATEO`. Build `hh:mm:ss` from `WS-CURTIME-HOURS/MINUTE/SECOND` into `CURTIMEO`. // :240-257
> Date/time come from `FUNCTION CURRENT-DATE` (21-char string YYYYMMDDHHMMSSmm…); year is taken as `WS-CURDATE-YEAR(3:2)` = last 2 digits. Port via `IClock.Now`.

### BUILD-MENU-OPTIONS  // source: COMEN01C.cbl:262-303
`PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > CDEMO-MENU-OPT-COUNT` (1..11): clear `WS-MENU-OPT-TXT`; STRING `CDEMO-MENU-OPT-NUM(WS-IDX) + '. ' + CDEMO-MENU-OPT-NAME(WS-IDX)` INTO it (all DELIMITED BY SIZE); then `EVALUATE WS-IDX` to move the text into the matching `OPTN001O..OPTN012O` (WHEN OTHER → CONTINUE). Produces lines like `01. Account View`. // :264-303
> `CDEMO-MENU-OPT-NUM` is PIC 9(02) so it renders zero-padded ("01", "02", …, "11"). `WS-MENU-OPT-TXT` is X(40); option name is X(35); "NN. " is 4 chars → 39 chars, fits.

---

## 7. Validation rules & exact literal messages

| Trigger | Exact message text (verbatim) | Source |
|---|---|---|
| Non-ENTER, non-PF3 AID pressed | `Invalid key pressed. Please see below...         ` (X50, `CCDA-MSG-INVALID-KEY`) | cbl:101; CSMSG01Y.cpy:20-21 |
| Option not numeric, > count (11), or zero | `Please enter a valid option number...` | cbl:131 |
| Regular user selecting an admin-only ('A') option | `No access - Admin Only option... ` (note trailing space; preceded by `MOVE SPACES`) | cbl:140-141 |
| Option = COPAUS0C but program not installed | STRING: `This option ` + `<opt name>` (delim by two spaces `'  '`) + ` is not installed...` ; color RED (`DFHRED`) | cbl:163-167 |
| Option program name starts with `DUMMY` | STRING: `This option ` + `<opt name>` (delim by SPACE) + `is coming soon ...` ; color GREEN (`DFHGREEN`) | cbl:172-176 |

Validation order (faithful): right-trim → spaces-to-zeros → numeric/range check → admin check → dispatch. Note all error paths set the message and `PERFORM SEND-MENU-SCREEN`, then **continue executing** the next checks (no early return) — see Faithful Bugs.

---

## 8. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

- **FB-1 — No early exit after a validation SEND; fall-through past the range check.** In `PROCESS-ENTER-KEY` the invalid-option block does `PERFORM SEND-MENU-SCREEN` but does NOT `GO BACK`/exit; control falls to the admin-only `IF` and then to `IF NOT ERR-FLG-ON`. Because the range-error block sets `WS-ERR-FLG='Y'` (via `MOVE 'Y' TO WS-ERR-FLG`, which satisfies `ERR-FLG-ON`), the subsequent `IF NOT ERR-FLG-ON` is false, so dispatch is skipped — but `SEND-MENU-SCREEN` runs **twice** for an invalid number (once in the error block, once at the end is guarded by `NOT ERR-FLG-ON` so it does NOT run again). The double-evaluation of the admin guard against an out-of-range/zero `WS-OPTION` subscript (`CDEMO-MENU-OPT-USRTYPE(WS-OPTION)`) can index with `WS-OPTION` = 0 or > 11. With `WS-OPTION=0`, `CDEMO-MENU-OPT-USRTYPE(0)` is a subscript-zero reference (undefined / storage before the table). Preserve this exact ordering and the unguarded subscript. // source: COMEN01C.cbl:127-143

- **FB-2 — `MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM` issued twice** in the OTHER dispatch branch (two identical consecutive statements). Harmless but must be reproduced. // source: COMEN01C.cbl:179-180

- **FB-3 — Subscript can reach the 12th (unpopulated) OCCURS slot / subscript 0.** `CDEMO-MENU-OPT` OCCURS 12 but only 11 entries are initialized in `CDEMO-MENU-OPTIONS-DATA`. The range check rejects `WS-OPTION > CDEMO-MENU-OPT-COUNT(=11)` and `WS-OPTION = ZEROS`, but the admin-only check at :136-143 runs **even when the range check already flagged an error** (no exit), so `CDEMO-MENU-OPT-USRTYPE(WS-OPTION)` may be evaluated with `WS-OPTION`=0 or out of range. In the .NET port, replicate by indexing the same flat option array without re-guarding (define slot 12 as default/empty, slot access with 0 → element before slot 1). // source: COMEN02Y.cpy:93-98; COMEN01C.cbl:136-137

- **FB-4 — Admin-only check is effectively dead for the shipped table.** Every option's `CDEMO-MENU-OPT-USRTYPE` is `'U'` (option 8 "Transaction Add" had its `(Admin Only)` label commented out and is usrtype 'U'), so `CDEMO-MENU-OPT-USRTYPE(WS-OPTION) = 'A'` is never true and the "No access - Admin Only option..." path is unreachable with current data. Keep the message and the dead branch. // source: COMEN02Y.cpy:67-72, 29/35/41/47/53/59/65/72/78/84/90; COMEN01C.cbl:136-143

- **FB-5 — `WS-MESSAGE` (X80) wider than `ERRMSGO` (X78).** Assigning the 80-char message to the 78-char map field silently truncates the last 2 chars. Messages here are short, but reproduce the 78-char clamp on the error line. // source: COMEN01C.cbl:38, 213; COMEN01.CPY:260

- **FB-6 — `IS NOT NUMERIC` test on a `PIC 9(02)` after space→zero replacement is effectively always true (numeric).** After `INSPECT ... REPLACING ALL ' ' BY '0'` and `MOVE` into `WS-OPTION PIC 9(02)`, the value is always numeric, so the `WS-OPTION IS NOT NUMERIC` disjunct can never independently fail. Keep the redundant test. // source: COMEN01C.cbl:123-124, 127

- **FB-7 — `INQUIRE PROGRAM` "not installed" branch never XCTLs and shows an error, but only option 11 (COPAUS0C) is gated this way**; options 1-10 XCTL unconditionally with no installed-check, so selecting an uninstalled option 1-10 would AID/abend at XCTL rather than show a friendly message. Reproduce the asymmetry. // source: COMEN01C.cbl:147-168 vs 177-187

---

## 9. PORT NOTES (relational + COBOL semantics)

- **No data layer needed.** Implement as a CICS-shim transaction handler `CM00 → COMEN01C` in `CardDemo.Online`, with a screen-model class for `COMEN1A`. The "INQUIRE PROGRAM" check maps to a lookup in the handler/program registry (is `COPAUS0C` registered?). The XCTL targets map to dispatching the registry-resolved handler with the same in-memory `CardDemoCommarea`.

- **COMMAREA persistence.** `CARDDEMO-COMMAREA` is the shared cross-program state. On RETURN store it keyed by terminal/session; on re-entry rehydrate. `CDEMO-PGM-CONTEXT` 88-level (`PGM-REENTER`) is the first-pass-vs-subsequent flag — model as the same byte (0/1).

- **`EIBCALEN = 0` cold start.** Model "no commarea / length 0" → XCTL to `COSGN00C`. Faithfully set `CDEMO-FROM-PROGRAM='COSGN00C'` first (note it sets FROM, not TO, here), then `RETURN-TO-SIGNON-SCREEN` which independently defaults `CDEMO-TO-PROGRAM` to `COSGN00C` when blank/low-values. // source: COMEN01C.cbl:82-84, 198-203

- **Option parse semantics (right-justify + zero-fill).** Reproduce: scan `OPTIONI` (2 chars) from the right for the last non-blank; copy `OPTIONI[0..idx]` into a right-justified 2-char field (`WS-OPTION-X JUST RIGHT`); replace spaces with '0'; parse to int. With `JUST RIGHT`, e.g. input " 5" or "5 " or "5" should normalize to "05" → 5. Mirror the COBOL `JUST RIGHT` semantics exactly (the source-field move into a JUST-RIGHT field right-aligns). // source: COMEN01C.cbl:45, 117-124

- **`INITIALIZE`/`MOVE LOW-VALUES TO COMEN1AO`** on first display zeroes the symbolic output map (binary low-values) so unset fields send as nulls (no data) to the 3270. In the .NET screen model, represent as "field not set / send nothing" rather than spaces, to preserve which attribute bytes are transmitted. // source: COMEN01C.cbl:89

- **`OCCURS` table** `CDEMO-MENU-OPT(12)` over `CDEMO-MENU-OPTIONS-DATA` (REDEFINES). Port as a fixed array of 12 records; populate 11 from the literal table; leave slot 12 as an empty/default record. 1-based subscript in COBOL → use index `WS-OPTION-1` carefully, and preserve the unguarded out-of-range access for FB-1/FB-3 characterization (clamp or sentinel, documented in the pinning test).

- **`STRING ... DELIMITED BY`** message building: the COPAUS0C-not-installed message delimits the option name by `'  '` (two spaces) — i.e. it copies up to the first run of two spaces in the X(35) name; the DUMMY message delimits by a single SPACE. Implement with the same delimiter semantics (DELIMITED BY a literal stops at the first occurrence of that literal). Note both leave the rest of the name out. // source: COMEN01C.cbl:163-167, 172-176

- **Edited/formatted output.** `CDEMO-MENU-OPT-NUM` PIC 9(02) → zero-padded 2-digit. `WS-CURDATE-MM-DD-YY` and `WS-CURTIME-HH-MM-SS` are group fields with embedded '/' and ':' FILLERs → format date as `MM/DD/YY` and time as `HH:MM:SS`. `OPTIONO` echoes `WS-OPTION` (PIC 9(02)) zero-padded. // source: CSDAT01Y.cpy:30-41; COMEN01C.cbl:125, 269-272

- **Color attribute override.** `MOVE DFHRED TO ERRMSGC` / `MOVE DFHGREEN TO ERRMSGC` set the error-line color byte for the not-installed / coming-soon messages; the screen model should carry a per-field color attribute and honor these overrides (default error color is RED from the BMS). // source: COMEN01C.cbl:162, 171; COMEN01.bms:154-156

- **No `HANDLE CONDITION`/`HANDLE AID`.** AID dispatch is explicit via `EVALUATE EIBAID`. RECEIVE uses RESP but ignores it. Replicate: no implicit error handlers.

---

## 10. OPEN QUESTIONS / RISKS

- **Subscript-0 / slot-12 behavior (FB-1/FB-3):** native COBOL accessing `CDEMO-MENU-OPT-USRTYPE(0)` is undefined; the original likely "worked" only because the value before the table didn't equal 'A'. The .NET port must pick a deterministic, documented behavior (e.g. treat index 0 / >count as a non-'A' empty slot) and pin it with a characterization test. Confirm with stakeholders that exact undefined-storage reproduction is NOT required (it cannot be portably reproduced).
- **`COPAUS0C` installed-check (FB-7):** whether the .NET registry should mark `COPAUS0C` installed by default (it is shipped) — assume installed so option 11 XCTLs; keep the "not installed" path reachable via a test that unregisters it.
- **Date source:** `FUNCTION CURRENT-DATE` uses local time on the mainframe; port via `IClock` so tests can pin the header. Header fields are display-only and excluded from screen-parity diffs (mask like timestamps).
- **`ERASE` on SEND:** confirm the console renderer clears the 24×80 buffer before drawing (ERASE present, no DATAONLY). 
```
