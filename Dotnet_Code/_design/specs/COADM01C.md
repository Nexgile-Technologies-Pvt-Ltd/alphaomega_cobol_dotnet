# PORT SPEC — COADM01C (Admin Menu, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COADM01C.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COADM01.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COADM01.CPY`
Admin-options copybook: `Old_Cobol_Code/.../app/cpy/COADM02Y.cpy`
COMMAREA copybook: `Old_Cobol_Code/.../app/cpy/COCOM01Y.cpy`
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.ConsoleApp` (24×80 renderer/dispatch) per ARCHITECTURE.md §"Target solution layout".

All line citations use the form `// source: COADM01C.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COADM01C is the CICS pseudo-conversational **Admin Menu** transaction for administrator users. It renders a 24×80 BMS menu listing the admin functions (User List/Add/Update/Delete in Security, plus two Db2 transaction-type maintenance options), reads a single-character/two-digit option number the operator types, validates it, and on a valid in-range option `XCTL`s to the program registered for that option in the static menu table. It owns no business data and performs **no file/table I/O at runtime** — its only "data" is the hard-coded menu option array compiled into the COADM02Y copybook. `// source: COADM01C.cbl:1-5, 4-6`

**Invocation.**
- CICS TRANSID **`CA00`** (`WS-TRANID`). `// source: COADM01C.cbl:37`
- Program id **`COADM01C`** (`WS-PGMNAME`). `// source: COADM01C.cbl:36`
- Mapset **`COADM01`** / map **`COADM1A`**. `// source: COADM01C.cbl:183-184, 195-196`
- Reached by `EXEC CICS XCTL` from the sign-on program (`COSGN00C`) after an admin (`CDEMO-USRTYP-ADMIN`='A') signs on; it is pseudo-conversational and re-drives itself via `RETURN TRANSID('CA00')`. `// source: COADM01C.cbl:111-114`
- It is **not** a called subroutine; all flow control is via COMMAREA + XCTL/RETURN.

---

## 2. FILE / TABLE ACCESS

**None.** COADM01C executes **no** file/VSAM/DB2 I/O. It does not `COPY` any file-status/select machinery and issues no `EXEC CICS READ/WRITE/STARTBR/READNEXT`. The `WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC'` literal `// source: COADM01C.cbl:39` is **declared but never referenced** in PROCEDURE DIVISION — dead literal copied from sibling programs; do **not** wire up any USER_SECURITY access for this program.

The "menu" is a compile-time constant table (`CARDDEMO-ADMIN-MENU-OPTIONS` in COADM02Y), not a table read. See §6.

| Logical access | ARCH table | Operation | SQL equivalent |
|---|---|---|---|
| (none) | — | — | — |

Port note: the .NET handler needs **no repository dependency**. The menu option list is a static, in-code array (see §6) — port it as a `readonly` table, not a DB query.

---

## 3. DATA STRUCTURES USED

- **WS-VARIABLES** (working storage). `// source: COADM01C.cbl:35-48`
  - `WS-PGMNAME X(08)='COADM01C'`, `WS-TRANID X(04)='CA00'`. `// source: COADM01C.cbl:36-37`
  - `WS-MESSAGE X(80)` — error/info text staged into the screen ERRMSG field. `// source: COADM01C.cbl:38`
  - `WS-ERR-FLG X(01)` with 88s `ERR-FLG-ON='Y'` / `ERR-FLG-OFF='N'`. `// source: COADM01C.cbl:40-42`
  - `WS-RESP-CD S9(09) COMP`, `WS-REAS-CD S9(09) COMP` — RESP/RESP2 from RECEIVE. `// source: COADM01C.cbl:43-44`
  - `WS-OPTION-X PIC X(02) JUST RIGHT` — the 2-char option string, right-justified. `// source: COADM01C.cbl:45`
  - `WS-OPTION PIC 9(02) VALUE 0` — numeric option (0–99). `// source: COADM01C.cbl:46`
  - `WS-IDX PIC S9(04) COMP` — loop index. `// source: COADM01C.cbl:47`
  - `WS-ADMIN-OPT-TXT PIC X(40)` — formatted "NN. name" menu line. `// source: COADM01C.cbl:48`
- **CARDDEMO-COMMAREA** (COCOM01Y) — see §5. `// source: COADM01C.cbl:50`
- **CARDDEMO-ADMIN-MENU-OPTIONS** (COADM02Y) — static menu table — see §6. `// source: COADM01C.cbl:51`
- **COADM1AI / COADM1AO** (COADM01 symbolic map, copybook COADM01.CPY) — input/output map structures (REDEFINES) — see §7. `// source: COADM01C.cbl:53`
- **CCDA-SCREEN-TITLE** (COTTL01Y): `CCDA-TITLE01='      AWS Mainframe Modernization       '`, `CCDA-TITLE02='              CardDemo                  '`. `// source: COADM01C.cbl:55; COTTL01Y.cpy:18-22`
- **WS-DATE-TIME** (CSDAT01Y): current date/time fields incl. `WS-CURDATE-MM-DD-YY` and `WS-CURTIME-HH-MM-SS` edited groups. `// source: COADM01C.cbl:56; CSDAT01Y.cpy:17-41`
- **CCDA-COMMON-MESSAGES** (CSMSG01Y): `CCDA-MSG-INVALID-KEY='Invalid key pressed. Please see below...         '` (X(50)). `// source: COADM01C.cbl:57; CSMSG01Y.cpy:20-21`
- **SEC-USER-DATA** (CSUSR01Y) — COPYed `// source: COADM01C.cbl:58` but **never referenced** in PROCEDURE DIVISION — dead copybook (no USRSEC I/O). Do not port.
- **DFHAID / DFHBMSCA** — CICS AID keys (DFHENTER, DFHPF3) and BMS attribute constants (DFHGREEN). `// source: COADM01C.cbl:60-61`

---

## 4. SCREEN / BMS MAP (mapset COADM01, map COADM1A)

24×80 map, `SIZE=(24,80)`, `CTRL=(ALARM,FREEKB)`, `MODE=INOUT`. `// source: COADM01.bms:19-28`

Fields (POS = row,col):
| Field | Type | Len | POS | Read? | Written? | Source |
|---|---|---|---|---|---|---|
| (label) `'Tran:'` | ASKIP | 5 | 1,1 | no | static | `// source: COADM01.bms:29-33` |
| **TRNNAME** | ASKIP,FSET | 4 | 1,7 | no | yes (=`WS-TRANID`) | `// source: COADM01.bms:34-37` |
| **TITLE01** | ASKIP,FSET | 40 | 1,21 | no | yes (=`CCDA-TITLE01`) | `// source: COADM01.bms:38-41` |
| (label) `'Date:'` | ASKIP | 5 | 1,65 | no | static | `// source: COADM01.bms:42-46` |
| **CURDATE** | ASKIP,FSET | 8 | 1,71 | no | yes (mm/dd/yy) | `// source: COADM01.bms:47-51` |
| (label) `'Prog:'` | ASKIP | 5 | 2,1 | no | static | `// source: COADM01.bms:52-56` |
| **PGMNAME** | ASKIP,FSET | 8 | 2,7 | no | yes (=`WS-PGMNAME`) | `// source: COADM01.bms:57-60` |
| **TITLE02** | ASKIP,FSET | 40 | 2,21 | no | yes (=`CCDA-TITLE02`) | `// source: COADM01.bms:61-64` |
| (label) `'Time:'` | ASKIP | 5 | 2,65 | no | static | `// source: COADM01.bms:65-69` |
| **CURTIME** | ASKIP,FSET | 8 | 2,71 | no | yes (hh:mm:ss) | `// source: COADM01.bms:70-74` |
| (label) `'Admin Menu'` | ASKIP,BRT | 10 | 4,35 | no | static | `// source: COADM01.bms:75-79` |
| **OPTN001..OPTN012** | ASKIP,FSET | 40 | 6,20 .. 17,20 | no | yes (menu lines; only 001–010 ever set by code) | `// source: COADM01.bms:80-139` |
| (label) `'Please select an option :'` | ASKIP,BRT | 25 | 20,15 | no | static | `// source: COADM01.bms:140-144` |
| **OPTION** | FSET,IC,NORM,**NUM**,UNPROT, JUSTIFY=(RIGHT,ZERO) | 2 | 20,41 | **yes (only input field)** | yes (echoes `WS-OPTION`) | `// source: COADM01.bms:145-149` |
| **ERRMSG** | ASKIP,BRT,FSET | 78 | 23,1 | no | yes (=`WS-MESSAGE`) | `// source: COADM01.bms:154-157` |
| (label) `'ENTER=Continue  F3=Exit'` | ASKIP | 23 | 24,1 | no | static | `// source: COADM01.bms:158-162` |

Key BMS behaviors to preserve:
- **OPTION is the single unprotected input field**; it is `NUM` (numeric-only) and `JUSTIFY=(RIGHT,ZERO)` (right-justified, zero-filled) with `IC` (insert cursor). The symbolic input field is `OPTIONI PIC X(2)`. `// source: COADM01.bms:145-149; COADM01.CPY:132`
- All other fields are `ASKIP` (auto-skip, protected) — operator cannot tab into them.
- Symbolic map: input struct **COADM1AI**, output struct **COADM1AO REDEFINES COADM1AI**. Output field names end `…O` (e.g. `OPTIONO`, `ERRMSGO`, `TITLE01O`); each field also has length (`…L`), flag (`…F`), attribute (`…A`/`…C`), etc. `// source: COADM01.CPY:17-261`
- `ERRMSGC OF COADM1AO` is the **color/attribute byte** for ERRMSG; the program forces it to `DFHGREEN` in two places (see §8 PROCESS-ENTER-KEY and PGMIDERR-ERR-PARA) — port as: when emitting the "not installed" info message, render ERRMSG in green instead of the default red. `// source: COADM01C.cbl:151, 272`

---

## 5. COMMAREA FIELDS (CARDDEMO-COMMAREA, COCOM01Y)

The full COMMAREA is `CARDDEMO-COMMAREA`. On RETURN the program passes back the **entire** `CARDDEMO-COMMAREA` (no appended trailer). `// source: COADM01C.cbl:90, 113`

Fields actually read/written here:
- `CDEMO-FROM-PROGRAM` X8 — set to `'COSGN00C'` when called with empty commarea, and to `WS-PGMNAME`='COADM01C' before an option XCTL. `// source: COADM01C.cbl:87, 143; COCOM01Y.cpy:22`
- `CDEMO-FROM-TRANID` X4 — set to `WS-TRANID`='CA00' before an option XCTL. `// source: COADM01C.cbl:142; COCOM01Y.cpy:21`
- `CDEMO-TO-PROGRAM` X8 — set to `'COSGN00C'` on the PF3 path; checked/defaulted in RETURN-TO-SIGNON-SCREEN. `// source: COADM01C.cbl:101, 165-167; COCOM01Y.cpy:24`
- `CDEMO-PGM-CONTEXT` 9(1) with 88s `CDEMO-PGM-ENTER`=0 / `CDEMO-PGM-REENTER`=1 — drives the first-entry vs re-entry dispatch; set to ZEROS before an option XCTL. `// source: COADM01C.cbl:91-92, 144; COCOM01Y.cpy:29-31`

Notes:
- `CDEMO-PGM-REENTER` (88 on `CDEMO-PGM-CONTEXT`=1) is **set on the first SEND** so the next turn takes the RECEIVE branch. `// source: COADM01C.cbl:91-94`
- Other COCOM01Y fields (USER-ID, USER-TYPE, CUST/ACCT/CARD info, LAST-MAP/LAST-MAPSET) are **not** touched by this program; pass them through unchanged. `// source: COCOM01Y.cpy:25-44`
- `EIBCALEN` governs the entry decision (0 ⇒ no commarea ⇒ came in cold, return to signon). `// source: COADM01C.cbl:86-90`
- COMMAREA is copied in via `MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA`. Port: model COMMAREA as the typed CARDDEMO-COMMAREA object; preserve its declared length on RETURN. `// source: COADM01C.cbl:90`

---

## 6. STATIC MENU TABLE (COADM02Y) — authoritative option list

`CARDDEMO-ADMIN-MENU-OPTIONS` is compiled-in constant data; `CDEMO-ADMIN-OPTIONS REDEFINES CDEMO-ADMIN-OPTIONS-DATA` as `CDEMO-ADMIN-OPT OCCURS 9 TIMES`, each entry = `CDEMO-ADMIN-OPT-NUM 9(02)` + `CDEMO-ADMIN-OPT-NAME X(35)` + `CDEMO-ADMIN-OPT-PGMNAME X(08)`. `// source: COADM02Y.cpy:55-59`

- `CDEMO-ADMIN-OPT-COUNT PIC 9(02) VALUE 6` — only **6** options are populated (loop/validation bound). `// source: COADM02Y.cpy:22`

| # (NUM) | NAME (X35, trailing spaces kept) | PGMNAME (X8) | Source |
|---|---|---|---|
| 1 | `User List (Security)               ` | `COUSR00C` | `// source: COADM02Y.cpy:26-29` |
| 2 | `User Add (Security)                ` | `COUSR01C` | `// source: COADM02Y.cpy:31-34` |
| 3 | `User Update (Security)             ` | `COUSR02C` | `// source: COADM02Y.cpy:36-39` |
| 4 | `User Delete (Security)             ` | `COUSR03C` | `// source: COADM02Y.cpy:41-44` |
| 5 | `Transaction Type List/Update (Db2) ` | `COTRTLIC` | `// source: COADM02Y.cpy:46-49` |
| 6 | `Transaction Type Maintenance (Db2) ` | `COTRTUPC` | `// source: COADM02Y.cpy:50-53` |

Notes / faithful semantics:
- The OCCURS is declared `9 TIMES` but the redefined raw data only contains **6** populated entries (NUM 1..6). The `CDEMO-ADMIN-OPT-COUNT=6` is what bounds every loop and the range check, so entries 7–9 are never read as valid options. Reproduce: model a 9-slot array but treat slots 7–9 as undefined; never select them. `// source: COADM02Y.cpy:22, 55-59`
- Because each populated entry is exactly 45 bytes (2+35+8) and the OCCURS stride is 45 bytes, the redefinition lines up cleanly over the 6×45 = 270 bytes of populated data. Port the menu as a fixed array of `{ Num, Name(35-char, space-padded), PgmName(8-char) }`. `// source: COADM02Y.cpy:26-53, 56-59`

---

## 7. PSEUDO-CONVERSATIONAL FLOW (overview)

CICS pattern: each invocation either **cold-starts** (no commarea), **first-displays** the menu (commarea present but not yet re-enter), or **processes** the operator's keystroke (re-enter). Every path ends with `EXEC CICS RETURN TRANSID('CA00') COMMAREA(CARDDEMO-COMMAREA)` (except XCTL paths, which transfer control away). `// source: COADM01C.cbl:111-114`

1. `EIBCALEN = 0` (cold): set `CDEMO-FROM-PROGRAM='COSGN00C'`, `PERFORM RETURN-TO-SIGNON-SCREEN` → XCTL to signon. `// source: COADM01C.cbl:86-88`
2. commarea present, **not** `CDEMO-PGM-REENTER`: set re-enter, `MOVE LOW-VALUES TO COADM1AO`, `PERFORM SEND-MENU-SCREEN`, RETURN. (First display.) `// source: COADM01C.cbl:91-94`
3. commarea present, `CDEMO-PGM-REENTER`: `PERFORM RECEIVE-MENU-SCREEN`, then dispatch on EIBAID. `// source: COADM01C.cbl:95-107`

EIBAID dispatch (`EVALUATE EIBAID`): `// source: COADM01C.cbl:97-107`
- `DFHENTER` → `PERFORM PROCESS-ENTER-KEY`. `// source: COADM01C.cbl:98-99`
- `DFHPF3` → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`, `PERFORM RETURN-TO-SIGNON-SCREEN` (XCTL). `// source: COADM01C.cbl:100-102`
- `WHEN OTHER` (any other AID/PFKey) → `MOVE 'Y' TO WS-ERR-FLG`, `MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE`, `PERFORM SEND-MENU-SCREEN`. `// source: COADM01C.cbl:103-106`

---

## 8. PARAGRAPH-BY-PARAGRAPH OUTLINE (each = one method)

### MAIN-PARA `// source: COADM01C.cbl:75-114`
1. `EXEC CICS HANDLE CONDITION PGMIDERR(PGMIDERR-ERR-PARA)` — register handler so a failed XCTL to a missing program branches to PGMIDERR-ERR-PARA. `// source: COADM01C.cbl:77-79`
2. `SET ERR-FLG-OFF TO TRUE`; clear `WS-MESSAGE` and `ERRMSGO OF COADM1AO`. `// source: COADM01C.cbl:81-84`
3. `IF EIBCALEN = 0`: cold start → from-program='COSGN00C', `PERFORM RETURN-TO-SIGNON-SCREEN`. `// source: COADM01C.cbl:86-88`
4. ELSE copy commarea (`MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA`); if `NOT CDEMO-PGM-REENTER` → set re-enter, `MOVE LOW-VALUES TO COADM1AO`, `PERFORM SEND-MENU-SCREEN`; else `PERFORM RECEIVE-MENU-SCREEN` + `EVALUATE EIBAID` (see §7). `// source: COADM01C.cbl:89-108`
5. `EXEC CICS RETURN TRANSID('CA00') COMMAREA(CARDDEMO-COMMAREA)`. `// source: COADM01C.cbl:111-114`

Port note: in .NET, `HANDLE CONDITION PGMIDERR` becomes a try/catch around the XCTL-dispatch in PROCESS-ENTER-KEY: if the target handler is not registered/installed, run the PGMIDERR-ERR-PARA body (green "not installed" message + re-send + RETURN). See §12 Faithful Bugs for the control-flow quirk this interacts with.

### PROCESS-ENTER-KEY `// source: COADM01C.cbl:119-158`
1. `PERFORM VARYING WS-IDX FROM LENGTH OF OPTIONI BY -1 UNTIL OPTIONI(WS-IDX:1) NOT = SPACES OR WS-IDX = 1` — scan the 2-char input from the right to find the last non-space position (effectively a right-trim length probe). `// source: COADM01C.cbl:121-125`
2. `MOVE OPTIONI(1:WS-IDX) TO WS-OPTION-X` — copy the leading `WS-IDX` chars into the right-justified 2-char field. `// source: COADM01C.cbl:126`
3. `INSPECT WS-OPTION-X REPLACING ALL ' ' BY '0'` — turn spaces into '0' (so blank → '00'). `// source: COADM01C.cbl:127`
4. `MOVE WS-OPTION-X TO WS-OPTION` (X(2)→9(2)); `MOVE WS-OPTION TO OPTIONO` (echo to screen). `// source: COADM01C.cbl:128-129`
5. Validation: `IF WS-OPTION IS NOT NUMERIC OR WS-OPTION > CDEMO-ADMIN-OPT-COUNT OR WS-OPTION = ZEROS` → set ERR flag, `MOVE 'Please enter a valid option number...' TO WS-MESSAGE`, `PERFORM SEND-MENU-SCREEN`. `// source: COADM01C.cbl:131-138`
6. `IF NOT ERR-FLG-ON`: if `CDEMO-ADMIN-OPT-PGMNAME(WS-OPTION)(1:5) NOT = 'DUMMY'` → stage from-tranid/from-program/context, then `EXEC CICS XCTL PROGRAM(CDEMO-ADMIN-OPT-PGMNAME(WS-OPTION)) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COADM01C.cbl:140-149`
7. (Fall-through after the IF, still inside `IF NOT ERR-FLG-ON`): clear WS-MESSAGE, set `ERRMSGC=DFHGREEN`, `STRING 'This option ' … 'is not installed ...' INTO WS-MESSAGE`, `PERFORM SEND-MENU-SCREEN`. `// source: COADM01C.cbl:150-158`

Arithmetic/conversion notes:
- Step 1's `PERFORM VARYING … BY -1` decrements `WS-IDX` from 2 (LENGTH OF OPTIONI) toward 1; stops at first non-space from the right or at WS-IDX=1. If input is `' 5'`, WS-IDX lands on 2; if `'5 '` it lands on 1 (then step 2 copies only `'5'`). `// source: COADM01C.cbl:121-126`
- Step 4 `MOVE WS-OPTION-X TO WS-OPTION` is alphanumeric→numeric. After step 3 every char is a digit (originally numeric NUM field + spaces→'0'), so it parses cleanly; `WS-OPTION IS NOT NUMERIC` in step 5 is therefore effectively always false post-INSPECT (defensive). `// source: COADM01C.cbl:127-131`
- `WS-OPTION-X` is `JUST RIGHT`: `MOVE OPTIONI(1:WS-IDX)` right-justifies the substring into the 2-byte field (left-padding with space, which step 3 then turns to '0'). Reproduce right-justification + zero-fill exactly. `// source: COADM01C.cbl:45, 126-127`

### RETURN-TO-SIGNON-SCREEN `// source: COADM01C.cbl:163-170`
1. `IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES` → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`. `// source: COADM01C.cbl:165-167`
2. `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM)` (no commarea passed). `// source: COADM01C.cbl:168-170`

Note: the cold-start path (MAIN-PARA step 3) reaches here having only set `CDEMO-FROM-PROGRAM` (not `CDEMO-TO-PROGRAM`); with a zero-length commarea `CDEMO-TO-PROGRAM` is whatever the (absent) commarea held — the LOW-VALUES/SPACES guard then forces 'COSGN00C'. Reproduce the guard. `// source: COADM01C.cbl:86-88, 165-167`

### SEND-MENU-SCREEN `// source: COADM01C.cbl:175-187`
1. `PERFORM POPULATE-HEADER-INFO`; `PERFORM BUILD-MENU-OPTIONS`. `// source: COADM01C.cbl:177-178`
2. `MOVE WS-MESSAGE TO ERRMSGO OF COADM1AO`. `// source: COADM01C.cbl:180`
3. `EXEC CICS SEND MAP('COADM1A') MAPSET('COADM01') FROM(COADM1AO) ERASE`. `// source: COADM01C.cbl:182-187`

Note: `ERASE` clears the 24×80 screen before painting. WS-MESSAGE is X(80) but ERRMSGO is X(78); the move truncates to 78 chars (right two dropped). `// source: COADM01C.cbl:38, 180; COADM01.CPY:260`

### RECEIVE-MENU-SCREEN `// source: COADM01C.cbl:192-200`
1. `EXEC CICS RECEIVE MAP('COADM1A') MAPSET('COADM01') INTO(COADM1AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. `// source: COADM01C.cbl:194-200`

Note: RESP/RESP2 are captured but **never inspected** — the program does not branch on a RECEIVE error (e.g. MAPFAIL). Port: read the input map; on MAPFAIL treat OPTIONI as spaces (consistent with COBOL's behavior of leaving the field at its prior/low-value state). `// source: COADM01C.cbl:198-199`

### POPULATE-HEADER-INFO `// source: COADM01C.cbl:205-224`
1. `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`. `// source: COADM01C.cbl:207`
2. Move titles/tranid/pgmname into header output fields: `CCDA-TITLE01→TITLE01O`, `CCDA-TITLE02→TITLE02O`, `WS-TRANID→TRNNAMEO`, `WS-PGMNAME→PGMNAMEO`. `// source: COADM01C.cbl:209-212`
3. Build `mm/dd/yy`: `WS-CURDATE-MONTH→WS-CURDATE-MM`, `WS-CURDATE-DAY→WS-CURDATE-DD`, `WS-CURDATE-YEAR(3:2)→WS-CURDATE-YY`; `WS-CURDATE-MM-DD-YY→CURDATEO`. `// source: COADM01C.cbl:214-218`
4. Build `hh:mm:ss`: `WS-CURTIME-HOURS→WS-CURTIME-HH`, `WS-CURTIME-MINUTE→WS-CURTIME-MM`, `WS-CURTIME-SECOND→WS-CURTIME-SS`; `WS-CURTIME-HH-MM-SS→CURTIMEO`. `// source: COADM01C.cbl:220-224`

Note: `WS-CURDATE-YEAR(3:2)` takes the last 2 digits of the 4-digit year (e.g. 2026→'26'). The edited groups `WS-CURDATE-MM-DD-YY` and `WS-CURTIME-HH-MM-SS` embed literal '/' and ':' separators (from CSDAT01Y). `// source: CSDAT01Y.cpy:30-41`. Port using the runtime IClock so tests can pin the timestamp.

### BUILD-MENU-OPTIONS `// source: COADM01C.cbl:229-266`
1. `PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > CDEMO-ADMIN-OPT-COUNT` (1..6). `// source: COADM01C.cbl:231-232`
2. Inside: `MOVE SPACES TO WS-ADMIN-OPT-TXT`; `STRING CDEMO-ADMIN-OPT-NUM(WS-IDX) DELIMITED BY SIZE, '. ' DELIMITED BY SIZE, CDEMO-ADMIN-OPT-NAME(WS-IDX) DELIMITED BY SIZE INTO WS-ADMIN-OPT-TXT` → e.g. `"01. User List (Security)               "`. `// source: COADM01C.cbl:234-239`
3. `EVALUATE WS-IDX` WHEN 1..10 → move WS-ADMIN-OPT-TXT into OPTN001O..OPTN010O respectively; WHEN OTHER → CONTINUE. `// source: COADM01C.cbl:241-264`

Notes:
- The STRING uses `CDEMO-ADMIN-OPT-NUM` PIC 9(02) — emits 2 zero-padded digits, so option 1 renders as `"01. "`. `// source: COADM02Y.cpy:57; COADM01C.cbl:236`
- `WS-ADMIN-OPT-TXT` is X(40); NUM(2)+'. '(2)+NAME(35) = 39 chars, fits with one trailing space. `// source: COADM01C.cbl:48, 236-238`
- The EVALUATE handles up to 10 slots, but the loop only runs to 6 (COUNT). Slots 7–10 (OPTN007O..OPTN010O) are never populated by data; OPTN011O/OPTN012O exist in the map but have no EVALUATE branch (dead map fields). Reproduce: only OPTN001O..OPTN006O get text; the rest are left at their LOW-VALUES/cleared state. `// source: COADM01C.cbl:241-264; COADM01.CPY:238-248`

### PGMIDERR-ERR-PARA `// source: COADM01C.cbl:270-284`
1. `MOVE SPACES TO WS-MESSAGE`; `MOVE DFHGREEN TO ERRMSGC OF COADM1AO`. `// source: COADM01C.cbl:271-272`
2. `STRING 'This option ' DELIMITED BY SIZE, 'is not installed ...' DELIMITED BY SIZE INTO WS-MESSAGE` → `"This option is not installed ..."`. `// source: COADM01C.cbl:273-277`
3. `PERFORM SEND-MENU-SCREEN`. `// source: COADM01C.cbl:279`
4. `EXEC CICS RETURN TRANSID('CA00') COMMAREA(CARDDEMO-COMMAREA)`. `// source: COADM01C.cbl:280-283`

Note: this is the `HANDLE CONDITION PGMIDERR` target — entered when an XCTL targets a program that is not installed in the CICS region. It issues its **own** RETURN (does not fall back into MAIN-PARA's RETURN). `// source: COADM01C.cbl:77-78, 280-283`

---

## 9. VALIDATION RULES & EXACT LITERAL MESSAGES

| Condition | Message text (exact) | Where staged | Source |
|---|---|---|---|
| AID key other than ENTER/PF3 | `Invalid key pressed. Please see below...         ` (X(50) literal `CCDA-MSG-INVALID-KEY`) | WS-MESSAGE (truncated to 78 into ERRMSGO) | `// source: COADM01C.cbl:104-105; CSMSG01Y.cpy:20-21` |
| Option non-numeric, OR > COUNT(6), OR = 0 | `Please enter a valid option number...` | WS-MESSAGE | `// source: COADM01C.cbl:131-136` |
| Valid in-range option whose PGMNAME(1:5) = 'DUMMY' (XCTL skipped) → info | `This option is not installed ...` (ERRMSG rendered green) | WS-MESSAGE via STRING | `// source: COADM01C.cbl:141, 150-156` |
| XCTL to a real but not-installed program (PGMIDERR) → info | `This option is not installed ...` (ERRMSG rendered green) | WS-MESSAGE via STRING | `// source: COADM01C.cbl:270-277` |

Exact-text reproduction requirements:
- `'Please enter a valid option number...'` — three trailing dots, no trailing space in the literal. `// source: COADM01C.cbl:135`
- The "not installed" message is assembled by `STRING 'This option ' + 'is not installed ...'` → `"This option is not installed ..."` (single space between "option" and "is" from the trailing space of the first literal). The commented-out `CDEMO-ADMIN-OPT-NAME(WS-OPTION)` between them is **not** included. `// source: COADM01C.cbl:152-156, 273-277`
- `CCDA-MSG-INVALID-KEY` is X(50) and includes its own trailing spaces; only the first 50 chars are defined, the move to X(80) WS-MESSAGE space-fills the rest, then truncates to 78 into ERRMSGO. `// source: CSMSG01Y.cpy:20-21`

Range/validation semantics:
- Valid option ⟺ `WS-OPTION` numeric AND `1 <= WS-OPTION <= 6` (CDEMO-ADMIN-OPT-COUNT). `// source: COADM01C.cbl:131-133`
- "DUMMY" guard: an option whose registered PGMNAME starts with `'DUMMY'` is treated as a placeholder — XCTL is skipped and the "not installed" green message shown. None of the 6 live entries are 'DUMMY' in COADM02Y, so this branch is currently unreachable with shipped data, but port the guard faithfully. `// source: COADM01C.cbl:141; COADM02Y.cpy:26-53`

---

## 10. EIBAID / PFKey HANDLING

- `DFHENTER` → PROCESS-ENTER-KEY (option dispatch). `// source: COADM01C.cbl:98-99`
- `DFHPF3` → return to signon (XCTL to `COSGN00C` via CDEMO-TO-PROGRAM). `// source: COADM01C.cbl:100-102`
- Any other AID (PF1/2/4..24, PA keys, CLEAR, etc.) → "Invalid key" message + re-SEND. `// source: COADM01C.cbl:103-106`
- AID copybook `DFHAID` provides DFHENTER/DFHPF3; map handler in .NET maps the console key/AID to these tokens. `// source: COADM01C.cbl:60`

---

## 11. XCTL / LINK TARGETS

| Trigger | XCTL target | COMMAREA passed | Source |
|---|---|---|---|
| Valid option 1, PGMNAME≠DUMMY | `COUSR00C` (CDEMO-ADMIN-OPT-PGMNAME(1)) | CARDDEMO-COMMAREA | `// source: COADM01C.cbl:145-148; COADM02Y.cpy:29` |
| Valid option 2 | `COUSR01C` | CARDDEMO-COMMAREA | `// source: COADM02Y.cpy:34` |
| Valid option 3 | `COUSR02C` | CARDDEMO-COMMAREA | `// source: COADM02Y.cpy:39` |
| Valid option 4 | `COUSR03C` | CARDDEMO-COMMAREA | `// source: COADM02Y.cpy:44` |
| Valid option 5 | `COTRTLIC` | CARDDEMO-COMMAREA | `// source: COADM02Y.cpy:49` |
| Valid option 6 | `COTRTUPC` | CARDDEMO-COMMAREA | `// source: COADM02Y.cpy:53` |
| PF3, or cold start (EIBCALEN=0) | `COSGN00C` (CDEMO-TO-PROGRAM) | **none** (XCTL without COMMAREA) | `// source: COADM01C.cbl:100-102, 168-170` |

Before an option XCTL the program sets `CDEMO-FROM-TRANID='CA00'`, `CDEMO-FROM-PROGRAM='COADM01C'`, `CDEMO-PGM-CONTEXT=0` so the target sees a fresh-entry context originating from the admin menu. `// source: COADM01C.cbl:142-144`

Port: the dispatcher maps each PGMNAME to its registered transaction handler. If a target handler is not implemented yet, treat as PGMIDERR (green "not installed" message), matching the mainframe's behavior when a program is uncataloged. `// source: COADM01C.cbl:77-78, 270-283`

---

## 12. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **Fall-through "not installed" after a successful in-region XCTL is dead, but after a same-region skip it always fires.** In PROCESS-ENTER-KEY, after the `IF CDEMO-ADMIN-OPT-PGMNAME(WS-OPTION)(1:5) NOT = 'DUMMY'` block performs the XCTL, control normally leaves the program. But the code unconditionally falls through to build and SEND the green `"This option is not installed ..."` message inside the same `IF NOT ERR-FLG-ON`. For a real installed program the XCTL never returns, so the message is dead code; for a 'DUMMY' (skipped) option the message is the intended output. The structure means: any valid, non-DUMMY option that for any reason does **not** transfer (e.g. PGMIDERR path is separate) would still try to SEND "not installed". Reproduce this exact control flow — do not restructure into an else. `// source: COADM01C.cbl:140-158`

2. **Commented-out option name in both "not installed" messages.** The STRING that builds the "not installed" text has `CDEMO-ADMIN-OPT-NAME(WS-OPTION)` commented out in both PROCESS-ENTER-KEY and PGMIDERR-ERR-PARA, so the message omits the option name and reads `"This option is not installed ..."` (with a double space artifact? No — single space). Keep the name out. `// source: COADM01C.cbl:152-156, 273-277`

3. **`ERRMSGC=DFHGREEN` overrides the map's red ERRMSG color for an informational message.** The BMS defines ERRMSG `COLOR=RED`, but for the "not installed" info path the program forces green. So a non-error informational message appears green while genuine errors (invalid key / invalid option) appear red. Reproduce the green override only on the two "not installed" paths. `// source: COADM01.bms:154-155; COADM01C.cbl:151, 272`

4. **WS-MESSAGE is X(80) but ERRMSGO is X(78) — silent 2-char truncation.** Any message ≥79 chars loses its tail. None of the shipped literals exceed 78, so no visible effect today, but the truncation is real; do not widen ERRMSG. `// source: COADM01C.cbl:38; COADM01.CPY:260`

5. **OCCURS 9 but only 6 entries populated / COUNT=6.** `CDEMO-ADMIN-OPT OCCURS 9 TIMES` over data that only fills 6 entries; indexing options 7–9 would read past the defined data (subscript valid per OCCURS, but data is whatever follows). The COUNT=6 guard prevents this at runtime, but the mismatch is structural. Do not change the OCCURS to 6 or trim the array — preserve the 9-slot declaration with 6 valid rows. `// source: COADM02Y.cpy:22, 56` 

6. **RESP/RESP2 from RECEIVE are captured but never checked.** A MAPFAIL or other RECEIVE non-zero RESP is not handled; the program proceeds as if input were received. Reproduce: do not add RECEIVE error handling. `// source: COADM01C.cbl:198-199`

7. **Dead literals/copybooks compiled but unused:** `WS-USRSEC-FILE='USRSEC'` `// source: COADM01C.cbl:39`; COPY `CSUSR01Y` (SEC-USER-DATA) `// source: COADM01C.cbl:58`. No USRSEC/USER_SECURITY I/O occurs. Do not implement security-file access for this program.

---

## 13. PORT NOTES (relational-access + tricky COBOL semantics)

- **No repository/DB work.** Per §2 this program touches no tables. The .NET handler is pure screen+dispatch logic. Do not inject any `IVsamFile`/EF repository.
- **Static menu table** → a `readonly` array `{ byte Num; string Name(35); string PgmName(8) }` with 6 entries and `Count=6`; preserve trailing spaces in `Name` so the rendered `"NN. name"` line is byte-identical (BUILD-MENU-OPTIONS STRING). `// source: COADM02Y.cpy:26-59; COADM01C.cbl:236-238`
- **OPTION input parsing** (PROCESS-ENTER-KEY): emulate exactly — right-trim probe via reverse scan, `JUST RIGHT` copy of `OPTIONI(1:WS-IDX)` into a 2-char field, then `INSPECT REPLACING ALL ' ' BY '0'` (spaces→'0'), then parse to 0..99. Blank input → '00' → fails the `=ZEROS` check → "Please enter a valid option number..." `// source: COADM01C.cbl:121-138`
- **`WS-OPTION` PIC 9(02)** → C# `int` (small code, n<2≤9 per ARCH type map). Compare against `Count` and against 0; the `NOT NUMERIC` test is effectively unreachable after the INSPECT but keep it for fidelity. `// source: ARCHITECTURE.md type map; COADM01C.cbl:46, 131`
- **Date/time** via `FUNCTION CURRENT-DATE` → use Runtime `IClock`; format header as `mm/dd/yy` and `hh:mm:ss` using the literal-separator edited groups; year uses last 2 digits `WS-CURDATE-YEAR(3:2)`. `// source: COADM01C.cbl:207, 214-224; CSDAT01Y.cpy:30-41`
- **REDEFINES** in COADM02Y (`CDEMO-ADMIN-OPTIONS REDEFINES CDEMO-ADMIN-OPTIONS-DATA`) and in the BMS symbolic map (`COADM1AO REDEFINES COADM1AI`) are layout devices; model the menu as a typed array and the map as a single screen object with input + output views (don't literally overlay bytes). `// source: COADM02Y.cpy:55; COADM01.CPY:139`
- **`MOVE LOW-VALUES TO COADM1AO`** before first SEND clears all output fields to nulls (BMS treats null as "no data / leave as map default"). Port: reset the screen model to defaults before first paint. `// source: COADM01C.cbl:93`
- **STRING DELIMITED BY SIZE** concatenates full field widths (no trimming). Reproduce fixed-width concatenation for menu lines and messages. `// source: COADM01C.cbl:152-156, 236-238`
- **`HANDLE CONDITION PGMIDERR`** → try/catch around dispatch; on "target not installed" run PGMIDERR-ERR-PARA (green message + SEND + RETURN). `// source: COADM01C.cbl:77-78, 270-283`
- **RETURN TRANSID('CA00')** keeps the pseudo-conversational loop on the admin menu; model as "next transaction = CA00, persist COMMAREA". `// source: COADM01C.cbl:111-114`

---

## 14. OPEN QUESTIONS / RISKS

1. **XCTL with no COMMAREA on the signon path.** `RETURN-TO-SIGNON-SCREEN` issues `XCTL PROGRAM(CDEMO-TO-PROGRAM)` with **no** COMMAREA `// source: COADM01C.cbl:168-170`, while the option-dispatch XCTL passes CARDDEMO-COMMAREA. Confirm the .NET signon handler tolerates a zero-length commarea (it should, mirroring `EIBCALEN=0` cold-start handling). Low risk.
2. **'DUMMY' guard unreachable with shipped data.** No COADM02Y entry begins with 'DUMMY' `// source: COADM02Y.cpy:26-53`, so that branch and its green message are currently dead. Keep for fidelity; flag in coverage matrix as a faithful-but-untriggered path. Low risk.
3. **Db2 options 5/6 (`COTRTLIC`/`COTRTUPC`)** target the optional Db2 module programs. If those handlers are not yet ported, selecting option 5/6 must hit the PGMIDERR "not installed" path (green message) rather than crash. Ensure the dispatcher routes unimplemented targets through PGMIDERR. `// source: COADM02Y.cpy:46-53; COADM01C.cbl:270-283`
4. **OPTION field NUM attribute vs INSPECT.** BMS `NUM` should prevent non-digits, but the COBOL still defensively handles non-numeric via INSPECT+NOT NUMERIC. The console renderer must enforce numeric-only entry consistent with `NUM` to match real behavior; the validation must still run regardless. `// source: COADM01.bms:145-149; COADM01C.cbl:127-131`
