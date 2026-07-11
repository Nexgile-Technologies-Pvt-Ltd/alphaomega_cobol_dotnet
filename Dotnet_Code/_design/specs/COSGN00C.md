# PORT SPEC — COSGN00C (CardDemo Sign-On / Logon)

> Target: C#/.NET 10 over the relational SQLite schema defined in
> `New_Dotnet_Code/_design/ARCHITECTURE.md`. This spec describes a faithful re-implementation.
> All line citations refer to `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COSGN00C.cbl`
> unless another file is named.

---

## 1. Purpose

COSGN00C is the **CICS pseudo-conversational sign-on (logon) screen** for the CardDemo application.
It displays a 24×80 BMS login panel (`COSGN0A` in mapset `COSGN00`), receives a User ID + Password,
upper-cases both, reads the user-security file (`USRSEC`) by User ID, validates the password, and — on
success — transfers control (`XCTL`) to either the **admin menu** (`COADM01C`, for user type `A`) or the
**main menu** (`COMEN01C`, for everyone else), seeding the shared `CARDDEMO-COMMAREA`. On any validation
or lookup failure it re-displays the screen with a red error message. PF3 exits with a thank-you message.
// source: COSGN00C.cbl:2-5, 23, 72-102

### Invocation
- **CICS TRANSID:** `CC00` (the program's own transaction id, `WS-TRANID`). // source: COSGN00C.cbl:37
- **Program id:** `COSGN00C` (`WS-PGMNAME`). // source: COSGN00C.cbl:36
- **Entry style:** pseudo-conversational. First entry has `EIBCALEN = 0` (no commarea → show empty screen).
  Subsequent entries re-enter under the same `CC00` transid carrying `CARDDEMO-COMMAREA`.
  // source: COSGN00C.cbl:80-102
- This is the **top of the application** — it is the program CICS starts for trans `CC00`; it is not
  called as a sub by another CardDemo program. It hands off to `COADM01C` / `COMEN01C` via `XCTL`.
  // source: COSGN00C.cbl:230-240

---

## 2. FILE / TABLE access

| COBOL file | DDNAME / DATASET | Access in this pgm | Relational table (ARCHITECTURE.md) | Operation | SQL mapping |
|---|---|---|---|---|---|
| User security (`SEC-USER-DATA`) | `USRSEC` (`WS-USRSEC-FILE = 'USRSEC  '`) | `EXEC CICS READ ... RIDFLD(WS-USER-ID)` keyed read | **USER_SECURITY** (PK `usr_id` X8) | READ key | `SELECT usr_id, first_name, last_name, pwd, usr_type FROM USER_SECURITY WHERE usr_id = @userId` |

Only one file is touched: a single **keyed READ** by primary key (`usr_id`). No browse, no write.
// source: COSGN00C.cbl:39, 211-219

### Response-code semantics (faithful)
The program switches on `WS-RESP-CD` (CICS `RESP`), **not** on a COBOL FILE STATUS:
- `WS-RESP-CD = 0` (`DFHRESP(NORMAL)`) → record found. // source: COSGN00C.cbl:222
- `WS-RESP-CD = 13` (`DFHRESP(NOTFND)`) → user not found. // source: COSGN00C.cbl:247
- any other value → "Unable to verify the User ...". // source: COSGN00C.cbl:252-256

Port mapping for the repository: a successful `SELECT` (row found) → resp `0`; not-found (FileStatus `'23'`)
→ resp `13`; any infrastructure/exception → "other" branch. The C# `IUserSecurityRepository.ReadByKey`
must return a tri-state outcome (Found / NotFound / Error) so the handler can reproduce these three
branches exactly. The numeric literals `0` and `13` are the CICS `RESP` codes `NORMAL` and `NOTFND`.

`USER_SECURITY` columns used (subset of `SEC-USER-DATA`): `pwd` (`SEC-USR-PWD`, X8) and
`usr_type` (`SEC-USR-TYPE`, X1). The READ pulls the full 80-byte record into `SEC-USER-DATA` but only
those two fields are consumed. // source: CSUSR01Y.cpy:17-23; COSGN00C.cbl:223, 227

---

## 3. Data structures

### 3.1 WORKING-STORAGE (`WS-VARIABLES`) // source: COSGN00C.cbl:35-46
| Field | PIC | C# type | Notes |
|---|---|---|---|
| `WS-PGMNAME` | X(08) `'COSGN00C'` | const string | shown on screen as `PGMNAMEO` |
| `WS-TRANID` | X(04) `'CC00'` | const string | RETURN TRANSID + `CDEMO-FROM-TRANID` |
| `WS-MESSAGE` | X(80) | string(80) | error/info line; also SEND TEXT payload |
| `WS-USRSEC-FILE` | X(08) `'USRSEC  '` | const string | dataset name (2 trailing spaces) |
| `WS-ERR-FLG` | X(01) `'N'` | bool/char | 88 `ERR-FLG-ON`='Y', `ERR-FLG-OFF`='N' |
| `WS-RESP-CD` | S9(09) COMP | int | CICS RESP |
| `WS-REAS-CD` | S9(09) COMP | int | CICS RESP2 (captured, never inspected) |
| `WS-USER-ID` | X(08) | string(8) | upper-cased User ID; READ key |
| `WS-USER-PWD` | X(08) | string(8) | upper-cased Password; compared to `SEC-USR-PWD` |

### 3.2 Copybooks pulled in (logic-relevant)
- `COCOM01Y` → `CARDDEMO-COMMAREA` (shared commarea; see §5.1). // source: COSGN00C.cbl:48, COCOM01Y.cpy:19-44
- `COSGN00` → BMS symbolic map (`COSGN0AI` input / `COSGN0AO` output). // source: COSGN00C.cbl:50, COSGN00.CPY
- `COTTL01Y` → `CCDA-SCREEN-TITLE` (`CCDA-TITLE01`, `CCDA-TITLE02`). // source: COSGN00C.cbl:52, COTTL01Y.cpy:17-22
- `CSDAT01Y` → `WS-DATE-TIME` work area (date/time formatting). // source: COSGN00C.cbl:53, CSDAT01Y.cpy
- `CSMSG01Y` → `CCDA-COMMON-MESSAGES` (`CCDA-MSG-THANK-YOU`, `CCDA-MSG-INVALID-KEY`). // source: COSGN00C.cbl:54, CSMSG01Y.cpy:17-21
- `CSUSR01Y` → `SEC-USER-DATA` (the USRSEC record). // source: COSGN00C.cbl:55, CSUSR01Y.cpy:17-23
- `DFHAID` → AID constants (`DFHENTER`, `DFHPF3`, …). // source: COSGN00C.cbl:57
- `DFHBMSCA` → BMS attribute constants. // source: COSGN00C.cbl:58

### 3.3 LINKAGE / DFHCOMMAREA // source: COSGN00C.cbl:64-67
`DFHCOMMAREA` is declared as a variable-length byte array (`LK-COMMAREA OCCURS 1 TO 32767 DEPENDING ON
EIBCALEN`) but the program never references `LK-COMMAREA`; it uses the typed `CARDDEMO-COMMAREA` from
`COCOM01Y` instead. The only thing read from the runtime here is **`EIBCALEN`** (= 0 vs > 0) to decide
first-entry vs re-entry. Port: a `CommArea` object that is `null`/empty on first entry.

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (one method each)

### MAIN-PARA  (entry point) // source: COSGN00C.cbl:73-102
1. `SET ERR-FLG-OFF TO TRUE`; clear `WS-MESSAGE` and `ERRMSGO`. // 75-78
2. If `EIBCALEN = 0` (first entry): `MOVE LOW-VALUES TO COSGN0AO`, set `USERIDL = -1` (cursor to User ID),
   `PERFORM SEND-SIGNON-SCREEN`. // 80-83
3. Else `EVALUATE EIBAID`:
   - `DFHENTER` → `PERFORM PROCESS-ENTER-KEY`. // 86-87
   - `DFHPF3` → `MOVE CCDA-MSG-THANK-YOU TO WS-MESSAGE`; `PERFORM SEND-PLAIN-TEXT`. // 88-90
   - `OTHER` → set err flag `'Y'`, `MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE`, `PERFORM SEND-SIGNON-SCREEN`. // 91-94
4. `EXEC CICS RETURN TRANSID('CC00') COMMAREA(CARDDEMO-COMMAREA) LENGTH(LENGTH OF CARDDEMO-COMMAREA)`.
   This RETURN is always reached **unless** a paragraph already issued `XCTL` (success) or `RETURN`
   (PF3 path). // 98-102

> Arithmetic note: `MOVE -1 TO USERIDL` (and `PASSWDL`) sets the BMS field-**length** subfield to −1, the
> CICS idiom for "place the cursor in this field". No COMPUTE/rounding anywhere in this program.

### PROCESS-ENTER-KEY // source: COSGN00C.cbl:108-140
1. `EXEC CICS RECEIVE MAP('COSGN0A') MAPSET('COSGN00')` into `COSGN0AI` (RESP/RESP2 captured). // 110-115
2. `EVALUATE TRUE` validation chain (first failing branch wins; `OTHER` = CONTINUE): // 117-130
   - `USERIDI = SPACES OR LOW-VALUES` → err `'Y'`, `WS-MESSAGE = 'Please enter User ID ...'`,
     `USERIDL = -1`, `PERFORM SEND-SIGNON-SCREEN`. // 118-122
   - `PASSWDI = SPACES OR LOW-VALUES` → err `'Y'`, `WS-MESSAGE = 'Please enter Password ...'`,
     `PASSWDL = -1`, `PERFORM SEND-SIGNON-SCREEN`. // 123-127
   - `OTHER` → `CONTINUE`. // 128-129
3. **Unconditionally** (the EVALUATE does not skip these): `MOVE FUNCTION UPPER-CASE(USERIDI)` into
   `WS-USER-ID` **and** `CDEMO-USER-ID`; `MOVE FUNCTION UPPER-CASE(PASSWDI)` into `WS-USER-PWD`. // 132-136
4. `IF NOT ERR-FLG-ON PERFORM READ-USER-SEC-FILE`. // 138-140

> **Important flow subtlety (faithful):** the validation branches call `SEND-SIGNON-SCREEN` but do **not**
> exit the paragraph. After a failed validation, control falls through to the unconditional UPPER-CASE
> MOVEs (step 3). The only thing that stops the file read is the `IF NOT ERR-FLG-ON` gate at step 4 — and
> that gate is only set by the User-ID and "invalid key" branches. See FAITHFUL BUGS §7.

### SEND-SIGNON-SCREEN // source: COSGN00C.cbl:145-157
1. `PERFORM POPULATE-HEADER-INFO`. // 147
2. `MOVE WS-MESSAGE TO ERRMSGO`. // 149
3. `EXEC CICS SEND MAP('COSGN0A') MAPSET('COSGN00') FROM(COSGN0AO) ERASE CURSOR`. // 151-157

### SEND-PLAIN-TEXT // source: COSGN00C.cbl:162-172
1. `EXEC CICS SEND TEXT FROM(WS-MESSAGE) LENGTH(80) ERASE FREEKB`. // 164-169
2. `EXEC CICS RETURN` (no TRANSID → ends the pseudo-conversation; PF3 exit). // 171-172

### POPULATE-HEADER-INFO // source: COSGN00C.cbl:177-204
1. `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`. // 179
2. Set titles/ids: `CCDA-TITLE01→TITLE01O`, `CCDA-TITLE02→TITLE02O`, `WS-TRANID→TRNNAMEO`,
   `WS-PGMNAME→PGMNAMEO`. // 181-184
3. Build `mm/dd/yy`: copy MM, DD, and **YEAR(3:2)** (last 2 digits) into `WS-CURDATE-MM-DD-YY`, then
   `MOVE WS-CURDATE-MM-DD-YY TO CURDATEO`. // 186-190
4. Build `hh:mm:ss`: copy HH/MM/SS into `WS-CURTIME-HH-MM-SS`, then `MOVE … TO CURTIMEO`. // 192-196
5. `EXEC CICS ASSIGN APPLID(APPLIDO)`; `EXEC CICS ASSIGN SYSID(SYSIDO)`. // 198-204

> Date/time formatting uses the `CSDAT01Y` group fields with literal `/` and `:` separators baked into
> `WS-CURDATE-MM-DD-YY` / `WS-CURTIME-HH-MM-SS` (FILLERs hold `'/'` and `':'`). Reproduce by formatting
> `IClock.Now` as `MM/dd/yy` and `HH:mm:ss`. // source: CSDAT01Y.cpy:30-41

### READ-USER-SEC-FILE // source: COSGN00C.cbl:209-257
1. `EXEC CICS READ DATASET('USRSEC') INTO(SEC-USER-DATA) RIDFLD(WS-USER-ID) KEYLENGTH(8) RESP/RESP2`. // 211-219
2. `EVALUATE WS-RESP-CD`: // 221-257
   - **`0` (found):** if `SEC-USR-PWD = WS-USER-PWD`: // 222-223
     - seed commarea: `WS-TRANID→CDEMO-FROM-TRANID`, `WS-PGMNAME→CDEMO-FROM-PROGRAM`,
       `WS-USER-ID→CDEMO-USER-ID`, `SEC-USR-TYPE→CDEMO-USER-TYPE`, `ZEROS→CDEMO-PGM-CONTEXT`. // 224-228
     - if `CDEMO-USRTYP-ADMIN` (type `'A'`): `XCTL PROGRAM('COADM01C') COMMAREA(CARDDEMO-COMMAREA)`;
       else `XCTL PROGRAM('COMEN01C') COMMAREA(CARDDEMO-COMMAREA)`. // 230-240
     - else (password mismatch): `WS-MESSAGE = 'Wrong Password. Try again ...'`, `PASSWDL = -1`,
       `PERFORM SEND-SIGNON-SCREEN`. // 241-245
   - **`13` (NOTFND):** err `'Y'`, `WS-MESSAGE = 'User not found. Try again ...'`, `USERIDL = -1`,
     `PERFORM SEND-SIGNON-SCREEN`. // 247-251
   - **OTHER:** err `'Y'`, `WS-MESSAGE = 'Unable to verify the User ...'`, `USERIDL = -1`,
     `PERFORM SEND-SIGNON-SCREEN`. // 252-256

> Password compare is a **byte-for-byte X(8) equality** on the upper-cased input vs the stored `SEC-USR-PWD`
> (both fixed-width, space-padded). Port: compare the trimmed-to-8 / right-space-padded strings, ordinal,
> case-sensitive (input already upper-cased; stored value compared as-is). // source: COSGN00C.cbl:223

---

## 5. ONLINE specifics

### 5.1 COMMAREA fields used (`CARDDEMO-COMMAREA`, copybook COCOM01Y)
| Field | PIC | Written here | Purpose |
|---|---|---|---|
| `CDEMO-FROM-TRANID` | X(04) | `'CC00'` on success | calling transid for next pgm // 224 |
| `CDEMO-FROM-PROGRAM` | X(08) | `'COSGN00C'` on success | calling pgm name // 225 |
| `CDEMO-USER-ID` | X(08) | upper-cased id (set twice: 134 & 226) | logged-in user |
| `CDEMO-USER-TYPE` | X(01) | `SEC-USR-TYPE` (`'A'`/`'U'`) | drives admin vs user menu // 227 |
| `CDEMO-PGM-CONTEXT` | 9(01) | `ZEROS` (→ `CDEMO-PGM-ENTER`) | fresh-entry flag for next pgm // 228 |

The full commarea is returned on every `EXEC CICS RETURN` (length = `LENGTH OF CARDDEMO-COMMAREA`).
// source: COSGN00C.cbl:98-102. Other commarea groups (`CDEMO-CUSTOMER-INFO`, `CDEMO-ACCOUNT-INFO`,
`CDEMO-CARD-INFO`, `CDEMO-MORE-INFO`) are **not** touched by this program. // source: COCOM01Y.cpy:32-44

### 5.2 Pseudo-conversational flow
- **First show:** `EIBCALEN = 0` → blank map + cursor on User ID → `SEND … ERASE CURSOR` → `RETURN
  TRANSID('CC00')`. // 80-83, 98-102
- **Re-entry (ENTER):** `RECEIVE MAP` → validate → upper-case → conditional file read → either `XCTL`
  (leaves this transaction) or `SEND` + `RETURN TRANSID('CC00')` (loop). // 108-140, 209-257
- **PF3:** `SEND TEXT … ERASE FREEKB` then bare `RETURN` (no transid) → conversation ends. // 162-172
- **Any other AID:** invalid-key message, re-send screen, `RETURN TRANSID('CC00')`. // 91-94

### 5.3 EIBAID / PFKey handling // source: COSGN00C.cbl:85-95
| AID | Handler |
|---|---|
| `DFHENTER` | PROCESS-ENTER-KEY |
| `DFHPF3` | thank-you SEND TEXT + plain RETURN (exit) |
| any other | `CCDA-MSG-INVALID-KEY` + re-send signon screen |

### 5.4 XCTL / LINK targets // source: COSGN00C.cbl:230-240
- `XCTL PROGRAM('COADM01C')` — admin menu (when `CDEMO-USER-TYPE = 'A'`).
- `XCTL PROGRAM('COMEN01C')` — main menu (all other types, including `'U'`).
- No `LINK`, no `START`. Both XCTLs pass `CARDDEMO-COMMAREA`.

### 5.5 BMS map / mapset
- **Mapset:** `COSGN00`  **Map:** `COSGN0A`  Size **24×80**, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`,
  `MODE=INOUT`. // source: COSGN00.bms:19-28
- **Symbolic map copybook:** `cpy-bms/COSGN00.CPY` → input struct `COSGN0AI`, output struct
  `COSGN0AO REDEFINES COSGN0AI`. For each field BMS generates `…L` (length S9(4) COMP), `…F`/`…A`
  (flag/attr X), `…I` (input) on the I side, and `…C/…P/…H/…V` (color/PS/hilite/validation attr) + `…O`
  (output) on the O side. // source: COSGN00.CPY:17-152

#### Fields READ from the screen (input, `…I`) // RECEIVE at COSGN00C.cbl:110-115
| Field | PIC | Used as |
|---|---|---|
| `USERIDI` | X(8) | User ID (validated, upper-cased) // 118, 132 |
| `PASSWDI` | X(8) | Password (validated, upper-cased) // 123, 135 |

(All other input fields exist in the symbolic map but are protected/ASKIP and not consumed.)

#### Fields WRITTEN to the screen (output, `…O`) // SEND at COSGN00C.cbl:151-157
| Field | Source | Line |
|---|---|---|
| `TITLE01O` | `CCDA-TITLE01` = `'      AWS Mainframe Modernization       '` | 181 / COTTL01Y:18-19 |
| `TITLE02O` | `CCDA-TITLE02` = `'              CardDemo                  '` | 182 / COTTL01Y:20-22 |
| `TRNNAMEO` | `WS-TRANID` = `'CC00'` | 183 |
| `PGMNAMEO` | `WS-PGMNAME` = `'COSGN00C'` | 184 |
| `CURDATEO` | `mm/dd/yy` from CURRENT-DATE | 190 |
| `CURTIMEO` | `hh:mm:ss` from CURRENT-DATE | 196 |
| `APPLIDO` | `EXEC CICS ASSIGN APPLID` | 198-200 |
| `SYSIDO` | `EXEC CICS ASSIGN SYSID` | 202-204 |
| `ERRMSGO` | `WS-MESSAGE` (error/info line) | 149 |
| length subfields `USERIDL` / `PASSWDL` | `-1` to position cursor | 82,121,126,244,250,255 |

Static literal/decoration fields (the "ONE DOLLAR" ASCII-art note, prompts, the
`ENTER=Sign-on  F3=Exit` footer) are defined in the BMS with `INITIAL=` and are not driven by the
program — render them verbatim from the map definition. // source: COSGN00.bms:94-205

#### BMS attribute facts the port must honor
- `USERID`: `ATTRB=(FSET,IC,NORM,UNPROT)`, GREEN, **LENGTH=8**, initial cursor (`IC`). // COSGN00.bms:156-160
- `PASSWD`: `ATTRB=(DRK,FSET,UNPROT)`, GREEN, LENGTH=8, `INITIAL='________'` — **DRK = non-display**
  (password is dark/hidden as typed). // COSGN00.bms:175-180
- `ERRMSG`: `ATTRB=(ASKIP,BRT,FSET)`, RED, LENGTH=78 — bright red error line at row 23. // COSGN00.bms:197-200
- Map symbolic `ERRMSGO`/`ERRMSGI` are **X(78)** while `WS-MESSAGE` is **X(80)**; the MOVE on line 149
  truncates the message to 78 chars on send. // COSGN00.CPY:84,152; COSGN00C.cbl:38,149

---

## 6. VALIDATION RULES & exact literal messages

| # | Condition | Field set to err / cursor | Exact message text | Source |
|---|---|---|---|---|
| V1 | User ID is SPACES or LOW-VALUES | err `'Y'`, `USERIDL=-1` | `Please enter User ID ...` | 118-122 |
| V2 | Password is SPACES or LOW-VALUES | err `'Y'`, `PASSWDL=-1` | `Please enter Password ...` | 123-127 |
| V3 | USRSEC read NOTFND (resp 13) | err `'Y'`, `USERIDL=-1` | `User not found. Try again ...` | 247-251 |
| V4 | USRSEC read other error | err `'Y'`, `USERIDL=-1` | `Unable to verify the User ...` | 252-256 |
| V5 | Password mismatch (record found) | (no err flag) `PASSWDL=-1` | `Wrong Password. Try again ...` | 241-245 |
| V6 | Invalid AID/PFkey | err `'Y'` | `Invalid key pressed. Please see below...         ` (X50 `CCDA-MSG-INVALID-KEY`) | 91-94 / CSMSG01Y:20-21 |
| V7 | PF3 pressed (info, not error) | — | `Thank you for using CardDemo application...      ` (X50 `CCDA-MSG-THANK-YOU`) | 88-90 / CSMSG01Y:18-19 |

> Note V5 does **not** set `WS-ERR-FLG` (only sets the message + cursor), unlike V1–V4. Preserve this.
> // source: COSGN00C.cbl:241-245
> Reproduce all literals **verbatim**, including trailing spaces/ellipses. The `...` are three ASCII dots.
> The X50 messages (V6/V7) carry their copybook trailing spaces but only the first 78 chars reach `ERRMSGO`
> (V6 path) or the full 80-char `WS-MESSAGE` reaches `SEND TEXT` (V7 path).

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

**FB-1 — Empty-password input still reaches the file read & password compare.**
In PROCESS-ENTER-KEY the `EVALUATE TRUE` branches `PERFORM SEND-SIGNON-SCREEN` but do **not** return; the
two **unconditional** UPPER-CASE MOVEs run afterwards, and the file read is gated only by `IF NOT
ERR-FLG-ON`. The V1 (User ID blank) and V6 (bad key) branches set `WS-ERR-FLG='Y'`, so they skip the read.
But the **V2 (Password blank) branch never sets `WS-ERR-FLG`** — it only sets the message and cursor —
therefore after the "Please enter Password ..." screen is queued, `ERR-FLG-ON` is still false, so
`READ-USER-SEC-FILE` executes anyway with a blank password. The user sees "Please enter Password ..." only
on the first turn, but the program also performed a USRSEC lookup (and, if the id exists with a blank
stored password, could even match). // source: COSGN00C.cbl:118-127 (V2 sets no err flag), 138-140 (gate),
223 (blank-vs-blank compare). The port MUST keep this: do **not** add an early return or set an error flag
on the blank-password branch.

**FB-2 — Two SENDs can be queued in one turn on the blank-password path.**
As a consequence of FB-1, when password is blank, `SEND-SIGNON-SCREEN` is performed once for the V2
message, then `READ-USER-SEC-FILE` may perform it again (V3/V4/V5) before the single `EXEC CICS RETURN`.
Only the last SEND's map is what the terminal shows, but both execute. Preserve the double execution
(observable via the SEND log / characterization harness). // source: COSGN00C.cbl:127, 245/251/256.

**FB-3 — `CDEMO-USER-ID` populated even on validation failure.**
Line 132-134 moves the upper-cased User ID into `CDEMO-USER-ID` unconditionally (before the read), so the
returned commarea carries the typed id even when sign-on fails and the screen is redisplayed. Keep this.
// source: COSGN00C.cbl:132-134.

> These are behavioral fidelity items. Log each in `_design/faithful-bugs.md` with a pinning test.

---

## 8. PORT NOTES (relational + COBOL semantics)

1. **Single keyed read → SELECT by PK.** `READ-USER-SEC-FILE` becomes
   `repo.ReadByKey(userId)` against `USER_SECURITY` returning `(outcome, row)`.
   Map outcome→resp: Found→`0`, NotFound→`13`, Error→other. Only `pwd` and `usr_type` are needed but the
   repo may return the whole POCO. // ARCHITECTURE.md "VSAM-semantics → SQL"; COSGN00C.cbl:211-257.
2. **Key padding.** `WS-USER-ID` is X(8); the lookup key is the upper-cased input right-padded to 8 with
   spaces. `usr_id` is stored as X(8) (space-padded) per ARCHITECTURE.md. Match on the exact 8-char string.
   Use ordinal comparison. // COSGN00C.cbl:45,132-133; ARCHITECTURE.md USER_SECURITY.
3. **UPPER-CASE semantics.** `FUNCTION UPPER-CASE` upper-cases A–Z only (ASCII/EBCDIC letters); digits and
   punctuation pass through. Implement with invariant-culture ASCII upper-casing (not Turkish-locale). The
   stored password is compared **as-is** (it is not upper-cased on read), so a lower-case stored password
   can never match. // COSGN00C.cbl:132-136, 223.
4. **Password compare width.** Compare full fixed-width X(8): right-pad input to 8 spaces and the stored
   `SEC-USR-PWD` is already 8. Equality is ordinal/byte. // COSGN00C.cbl:223; CSUSR01Y.cpy:21.
5. **`SPACES OR LOW-VALUES` test.** From a BMS RECEIVE, an untyped field arrives as LOW-VALUES (binary
   zeros) or spaces depending on MDT/erase; both mean "empty". In the console/screen port, treat
   null/empty/all-spaces input as the empty case. // COSGN00C.cbl:118,123.
6. **Date/time.** `FUNCTION CURRENT-DATE` → format `MM/dd/yy` (note: 2-digit year via `YEAR(3:2)`) and
   `HH:mm:ss`. Drive from `IClock` so tests can mask/fix it. // COSGN00C.cbl:179-196; CSDAT01Y.cpy:30-41.
7. **`MOVE -1 TO …L`** is the cursor-positioning idiom, not arithmetic. The screen model should expose a
   "cursor field" concept; setting length = −1 = "put cursor here". // COSGN00C.cbl:82,121,126,244,250,255.
8. **`MOVE LOW-VALUES TO COSGN0AO`** zero-fills the entire output map (so all fields blank/default
   attributes) before the first SEND. Port: reset the screen model to defaults on first entry.
   // COSGN00C.cbl:81.
9. **REDEFINES in the symbolic map** (`COSGN0AO REDEFINES COSGN0AI`, and the `…F`/`…A` attribute aliases)
   are storage overlays; in C# model them as a single screen-field object with separate input value,
   output value, length, and attribute properties — no literal byte overlay needed. // COSGN00.CPY:85, 21-22.
10. **`EXEC CICS ASSIGN APPLID/SYSID`.** Provide configurable shim values (e.g. from runtime config /
    `IClock`-like environment provider). Defaults can be a fixed APPLID/SYSID for parity tests.
    // COSGN00C.cbl:198-204.
11. **RETURN TRANSID('CC00')** = re-arm the same transaction. In the console dispatcher this means: on a
    non-terminal turn, keep the next-transaction = `CC00` and re-display. PF3's bare RETURN ends the
    dispatcher loop (logoff). // COSGN00C.cbl:98-102, 171-172.
12. **Message truncation 80→78.** `WS-MESSAGE` is X(80); `ERRMSGO` is X(78). The screen error line shows at
    most 78 chars; `SEND TEXT` (PF3) uses the full 80. Keep both widths. // COSGN00C.cbl:38,149,166; COSGN00.CPY:152.

---

## 9. OPEN QUESTIONS / RISKS

1. **USRSEC seed data.** The relational `USER_SECURITY` table must be seeded from the EBCDIC/CSV master so
   the admin (`usr_type='A'`) and standard (`'U'`) sample users exist; otherwise every sign-on returns V3
   "User not found." Confirm the import covers `USRSEC`. (Out of scope for this program but required to test.)
2. **APPLID/SYSID source.** No real CICS region exists in the port; decide the fixed values the console
   shim returns so screen-parity fixtures are deterministic.
3. **FB-1 testability.** Reproducing the blank-password-still-reads bug requires the repository to be
   invoked with an empty key; the characterization test must assert the repo was called (e.g., via a spy)
   even though the screen shows "Please enter Password ...". Confirm the harness can observe repo calls.
4. **`WS-CURDATE-YEAR(3:2)`** assumes a 4-digit year; for years ≥ 2000 this yields `00`–`99` correctly.
   No Y2K issue for the demo, but the 2-digit display is intentional — do not widen it.
