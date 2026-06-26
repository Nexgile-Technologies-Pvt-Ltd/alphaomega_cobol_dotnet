# PORT SPEC — COUSR02C (Update User)

Online CICS pseudo-conversational program that updates an existing user record in the
`USRSEC` (USER_SECURITY) file. Source: `Old_Cobol_Code/.../app/cbl/COUSR02C.cbl`.

---

## 1. Purpose & Invocation

**Purpose.** COUSR02C is the admin-side *Update User* screen. The operator types an 8-char User ID,
presses Enter to **fetch** the existing security record (first name, last name, password, user type)
into the editable fields, edits one or more of those fields, and presses **PF5** to save (REWRITE) the
record back to USRSEC, or **PF3** to save-and-exit. It performs field-level validation, reads the
keyed user record with an UPDATE intent, applies only the fields that actually changed, and rewrites
the record. // source: COUSR02C.cbl:2-6,36,143-390

**How invoked.**
- **CICS TRANSID:** `CU02` (`WS-TRANID`). // source: COUSR02C.cbl:37
- **Program id:** `COUSR02C` (`WS-PGMNAME`). // source: COUSR02C.cbl:36
- Pseudo-conversational: ends with `EXEC CICS RETURN TRANSID('CU02') COMMAREA(CARDDEMO-COMMAREA)`. // source: COUSR02C.cbl:135-138
- Reached via **XCTL** from the User-List screen **COUSR00C** (which sets `CDEMO-CU02-USR-SELECTED` and
  transfers control to this program), or from the Admin menu **COADM01C**. On entry with a pre-selected
  user, it auto-fetches. // source: COUSR02C.cbl:99-104
- On `EIBCALEN = 0` (no commarea, i.e. cold start) it transfers to the sign-on program `COSGN00C`.
  // source: COUSR02C.cbl:90-92
- **Exit targets (XCTL):** `COADM01C` (PF3 default / PF12 cancel), or `CDEMO-FROM-PROGRAM` if set (PF3),
  or `COSGN00C` (cold start / fallback). // source: COUSR02C.cbl:113-126,251-261

---

## 2. FILE / TABLE Access

| COBOL file | DDNAME / DATASET | Relational table (ARCHITECTURE.md) | Operation(s) | SQL mapping |
|---|---|---|---|---|
| `USRSEC` (`WS-USRSEC-FILE = 'USRSEC  '`) | USRSEC VSAM KSDS, key = `SEC-USR-ID` X(8) | **USER_SECURITY** (PK `usr_id` X8) | `EXEC CICS READ ... UPDATE` (keyed read for update); `EXEC CICS REWRITE` | READ → `SELECT * FROM USER_SECURITY WHERE usr_id = @id` (RESP NORMAL=found / NOTFND=not found); REWRITE → `UPDATE USER_SECURITY SET first_name=@f, last_name=@l, pwd=@p, usr_type=@t WHERE usr_id=@id` |

// source: COUSR02C.cbl:39,320-331,358-366; record layout CSUSR01Y.cpy:17-23

**Record layout `SEC-USER-DATA` (CSUSR01Y, 80 bytes):**
`SEC-USR-ID X(8)`, `SEC-USR-FNAME X(20)`, `SEC-USR-LNAME X(20)`, `SEC-USR-PWD X(8)`,
`SEC-USR-TYPE X(1)`, `SEC-USR-FILLER X(23)`. // source: CSUSR01Y.cpy:17-23

**Repository contract notes (per ARCHITECTURE.md §VSAM->SQL):**
- The CICS `READ ... UPDATE` takes a record lock in CICS. In the relational port there is **no held lock
  across the pseudo-conversational turn** — the READ happens inside `UPDATE-USER-INFO` immediately before
  the REWRITE within the same invocation, so a simple read-then-update in one method/transaction is faithful.
- `DFHRESP(NORMAL)` → FileStatus `'00'` (row found). `DFHRESP(NOTFND)` → FileStatus `'23'` (no row).
  REWRITE NOTFND likewise maps to `'23'`. Any other RESP → the program's "Unable to ..." branch.
  // source: COUSR02C.cbl:333-353,368-390

---

## 3. Working storage & COMMAREA fields used

**WS-VARIABLES** (// source: COUSR02C.cbl:35-47):
- `WS-PGMNAME X(8) = 'COUSR02C'`, `WS-TRANID X(4) = 'CU02'`, `WS-MESSAGE X(80)`,
  `WS-USRSEC-FILE X(8) = 'USRSEC  '`.
- `WS-ERR-FLG X(1)` with 88s `ERR-FLG-ON ('Y')` / `ERR-FLG-OFF ('N')`.
- `WS-RESP-CD`, `WS-REAS-CD` `S9(9) COMP` — CICS RESP/RESP2.
- `WS-USR-MODIFIED X(1)` with 88s `USR-MODIFIED-YES ('Y')` / `USR-MODIFIED-NO ('N')`.

**CARDDEMO-COMMAREA** (COCOM01Y) fields referenced:
- `CDEMO-TO-PROGRAM`, `CDEMO-FROM-PROGRAM`, `CDEMO-FROM-TRANID` — XCTL routing. // source: COUSR02C.cbl:91,113-117,125,253-256
- `CDEMO-PGM-CONTEXT` 9(1) with 88s `CDEMO-PGM-ENTER (0)` / `CDEMO-PGM-REENTER (1)` — first-time vs re-entry. // source: COCOM01Y.cpy:29-31; COUSR02C.cbl:95-96,257
- `CDEMO-CU02-INFO` group (declared *inline after* the COPY COCOM01Y at the working-storage level, **redefining/extending** the commarea area conceptually — note these are program-local extensions appended under the commarea copy):
  - `CDEMO-CU02-USRID-FIRST X(8)`, `CDEMO-CU02-USRID-LAST X(8)`, `CDEMO-CU02-PAGE-NUM 9(8)`,
    `CDEMO-CU02-NEXT-PAGE-FLG X(1)` (88 NEXT-PAGE-YES/NO), `CDEMO-CU02-USR-SEL-FLG X(1)`,
    `CDEMO-CU02-USR-SELECTED X(8)`. // source: COUSR02C.cbl:50-58
  - Only `CDEMO-CU02-USR-SELECTED` is read in this program (pre-selected user from the list screen). // source: COUSR02C.cbl:99-102

> **NOTE (layout subtlety):** the `05 CDEMO-CU02-INFO` lines at COUSR02C.cbl:50-58 are coded *immediately
> after* `COPY COCOM01Y` (line 49) at the same 05 level. In the source program these become **additional
> fields appended to the `CARDDEMO-COMMAREA` 01 group** (the copybook's last group is `CDEMO-MORE-INFO`),
> so the commarea passed via RETURN/XCTL carries the CU02 selection sub-block. Port: model the commarea as
> the union (general info + customer/account/card/more info + CU02-INFO block) so the selected user survives
> the round trip.

---

## 4. BMS Map

**Mapset `COUSR02`, map `COUSR2A`**, 24×80, `MODE=INOUT`, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`.
// source: bms/COUSR02.bms:19-28; symbolic map COUSR02.CPY (COUSR2AI / COUSR2AO)

| Field | Len | Pos | Kind | Read (in) | Written (out) | Notes |
|---|---|---|---|---|---|---|
| TRNNAME | 4 | (1,7) | ASKIP | — | yes | "CU02" |
| TITLE01 | 40 | (1,21) | ASKIP | — | yes | header title 1 |
| CURDATE | 8 | (1,71) | ASKIP | — | yes | mm/dd/yy |
| PGMNAME | 8 | (2,7) | ASKIP | — | yes | "COUSR02C" |
| TITLE02 | 40 | (2,21) | ASKIP | — | yes | header title 2 |
| CURTIME | 8 | (2,71) | ASKIP | — | yes | hh:mm:ss |
| USRIDIN | 8 | (6,21) | UNPROT, **IC** (initial cursor), FSET | **yes** | yes (echo) | User ID key |
| FNAME | 20 | (11,18) | UNPROT, FSET | **yes** | yes | first name |
| LNAME | 20 | (11,56) | UNPROT, FSET | **yes** | yes | last name |
| PASSWD | 8 | (13,16) | UNPROT, **DRK** (non-display), FSET | **yes** | yes | password (dark) |
| USRTYPE | 1 | (15,17) | UNPROT, FSET | **yes** | yes | A=Admin / U=User |
| ERRMSG | 78 | (23,1) | ASKIP, BRT, FSET | — | yes | error/status line (RED via attr override) |

// source: bms/COUSR02.bms:34-164

**Symbolic-map field naming convention** (COUSR02.CPY): for each named field there is an input subfield
`...I`, a length subfield `...L COMP S9(4)`, a flag/attr subfield `...F`/`...A` (redefine), and on the
output side `...O`, plus colour/attr bytes `...C/...P/...H/...V`. The program writes attribute byte
`ERRMSGC` to recolour the message line. // source: COUSR02.CPY:17-164

**Cursor positioning idiom:** `MOVE -1 TO <field>L` forces the cursor (via `CURSOR` option on SEND) to that
field. Port: set a "cursor field = X" flag when length subfield is set to -1. // source: COUSR02C.cbl:98,150,184,190,196,202,208,211,344,351,381,388,405; SEND uses CURSOR at COUSR02C.cbl:277

---

## 5. Paragraph-by-Paragraph Outline (every paragraph → one method)

### MAIN-PARA  // source: COUSR02C.cbl:82-138
1. `SET ERR-FLG-OFF`, `SET USR-MODIFIED-NO`; clear `WS-MESSAGE` and `ERRMSGO`. // source: 84-88
2. **If `EIBCALEN = 0`** → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. // source: 90-92
3. **Else** copy `DFHCOMMAREA(1:EIBCALEN)` into `CARDDEMO-COMMAREA`. // source: 94
   - **If NOT `CDEMO-PGM-REENTER` (first entry):** set REENTER; `MOVE LOW-VALUES TO COUSR2AO`;
     `MOVE -1 TO USRIDINL` (cursor to user-id). If `CDEMO-CU02-USR-SELECTED` is **not** spaces/low-values,
     move it to `USRIDINI` and `PERFORM PROCESS-ENTER-KEY` (auto-fetch). Then `PERFORM SEND-USRUPD-SCREEN`.
     // source: 95-105
   - **Else (re-entry):** `PERFORM RECEIVE-USRUPD-SCREEN`, then `EVALUATE EIBAID` (PFKey dispatch below). // source: 106-131
4. `EXEC CICS RETURN TRANSID('CU02') COMMAREA(CARDDEMO-COMMAREA)`. // source: 135-138

**EIBAID dispatch** (re-entry path) // source: COUSR02C.cbl:108-131:
| AID | Action |
|---|---|
| `DFHENTER` | `PERFORM PROCESS-ENTER-KEY` (fetch user). // source:109-110 |
| `DFHPF3` | `PERFORM UPDATE-USER-INFO`; then set `CDEMO-TO-PROGRAM` = `'COADM01C'` if `CDEMO-FROM-PROGRAM` empty else `CDEMO-FROM-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. // source:111-119 |
| `DFHPF4` | `PERFORM CLEAR-CURRENT-SCREEN`. // source:120-121 |
| `DFHPF5` | `PERFORM UPDATE-USER-INFO`. // source:122-123 |
| `DFHPF12` | `MOVE 'COADM01C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. // source:124-126 |
| OTHER | err-flg='Y'; `WS-MESSAGE = CCDA-MSG-INVALID-KEY` ("Invalid key pressed. Please see below..."); `SEND-USRUPD-SCREEN`. // source:127-130 |

### PROCESS-ENTER-KEY  // source: COUSR02C.cbl:143-172
1. `EVALUATE TRUE`: if `USRIDINI` = spaces/low-values → err-flg='Y', msg `'User ID can NOT be empty...'`,
   `MOVE -1 TO USRIDINL`, `SEND-USRUPD-SCREEN`; OTHER → `MOVE -1 TO USRIDINL`, CONTINUE. // source:145-155
2. **If not err:** clear `FNAMEI/LNAMEI/PASSWDI/USRTYPEI`; `MOVE USRIDINI TO SEC-USR-ID`; `PERFORM READ-USER-SEC-FILE`. // source:157-164
3. **If not err** (after read): move `SEC-USR-FNAME/LNAME/PWD/TYPE` into the corresponding screen input
   fields; `PERFORM SEND-USRUPD-SCREEN`. // source:166-172

### UPDATE-USER-INFO  // source: COUSR02C.cbl:177-245
1. `EVALUATE TRUE` sequential field-empty validation (first failing branch wins; each sets err-flg, message,
   cursor field, sends, and stops further evaluation):
   - USRIDINI empty → `'User ID can NOT be empty...'`, cursor USRIDINL. // source:180-185
   - FNAMEI empty → `'First Name can NOT be empty...'`, cursor FNAMEL. // source:186-191
   - LNAMEI empty → `'Last Name can NOT be empty...'`, cursor LNAMEL. // source:192-197
   - PASSWDI empty → `'Password can NOT be empty...'`, cursor PASSWDL. // source:198-203
   - USRTYPEI empty → `'User Type can NOT be empty...'`, cursor USRTYPEL. // source:204-209
   - OTHER → `MOVE -1 TO FNAMEL`, CONTINUE. // source:210-212
2. **If not err:** `MOVE USRIDINI TO SEC-USR-ID`; `PERFORM READ-USER-SEC-FILE`. // source:215-217
3. Field-by-field change detection (each compares screen value vs record; if differs, copies into record
   and `SET USR-MODIFIED-YES`):
   - FNAMEI ≠ SEC-USR-FNAME → copy, modified. // source:219-222
   - LNAMEI ≠ SEC-USR-LNAME → copy, modified. // source:223-226
   - PASSWDI ≠ SEC-USR-PWD → copy, modified. // source:227-230
   - USRTYPEI ≠ SEC-USR-TYPE → copy, modified. // source:231-234
4. **If `USR-MODIFIED-YES`** → `PERFORM UPDATE-USER-SEC-FILE`; **else** msg `'Please modify to update ...'`,
   `MOVE DFHRED TO ERRMSGC`, `SEND-USRUPD-SCREEN`. // source:236-243

### RETURN-TO-PREV-SCREEN  // source: COUSR02C.cbl:250-261
1. If `CDEMO-TO-PROGRAM` empty → `'COSGN00C'`. // source:252-254
2. `CDEMO-FROM-TRANID = WS-TRANID`; `CDEMO-FROM-PROGRAM = WS-PGMNAME`; `CDEMO-PGM-CONTEXT = ZEROS`. // source:255-257
3. `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. // source:258-261

### SEND-USRUPD-SCREEN  // source: COUSR02C.cbl:266-278
1. `PERFORM POPULATE-HEADER-INFO`; `MOVE WS-MESSAGE TO ERRMSGO`. // source:268-270
2. `EXEC CICS SEND MAP('COUSR2A') MAPSET('COUSR02') FROM(COUSR2AO) ERASE CURSOR`. // source:272-278

### RECEIVE-USRUPD-SCREEN  // source: COUSR02C.cbl:283-291
1. `EXEC CICS RECEIVE MAP('COUSR2A') MAPSET('COUSR02') INTO(COUSR2AI) RESP/RESP2`. // source:285-291
   (RESP/RESP2 captured but not inspected in this paragraph.)

### POPULATE-HEADER-INFO  // source: COUSR02C.cbl:296-315
1. `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`. // source:298
2. Set TITLE01O=CCDA-TITLE01, TITLE02O=CCDA-TITLE02, TRNNAMEO=WS-TRANID, PGMNAMEO=WS-PGMNAME. // source:300-303
3. Build `mm/dd/yy` from month/day/year(3:2) into CURDATEO. // source:305-309
4. Build `hh:mm:ss` from hours/minute/second into CURTIMEO. // source:311-315

### READ-USER-SEC-FILE  // source: COUSR02C.cbl:320-353
1. `EXEC CICS READ DATASET('USRSEC') INTO(SEC-USER-DATA) RIDFLD(SEC-USR-ID) KEYLENGTH UPDATE RESP/RESP2`. // source:322-331
2. `EVALUATE WS-RESP-CD`:
   - NORMAL → `CONTINUE` then msg `'Press PF5 key to save your updates ...'`, `ERRMSGC = DFHNEUTR`,
     `SEND-USRUPD-SCREEN`. **(See Faithful Bug #1.)** // source:334-339
   - NOTFND → err-flg='Y', msg `'User ID NOT found...'`, cursor USRIDINL, `SEND-USRUPD-SCREEN`. // source:340-345
   - OTHER → `DISPLAY 'RESP:'...'REAS:'...`; err-flg='Y'; msg `'Unable to lookup User...'`; cursor FNAMEL;
     `SEND-USRUPD-SCREEN`. // source:346-352

### UPDATE-USER-SEC-FILE  // source: COUSR02C.cbl:358-390
1. `EXEC CICS REWRITE DATASET('USRSEC') FROM(SEC-USER-DATA) LENGTH RESP/RESP2`. // source:360-366
2. `EVALUATE WS-RESP-CD`:
   - NORMAL → clear WS-MESSAGE, `ERRMSGC = DFHGREEN`, `STRING 'User ' || SEC-USR-ID(trim space) || ' has been updated ...' INTO WS-MESSAGE`, `SEND-USRUPD-SCREEN`. // source:369-376
   - NOTFND → err-flg='Y', msg `'User ID NOT found...'`, cursor USRIDINL, `SEND-USRUPD-SCREEN`. // source:377-382
   - OTHER → DISPLAY RESP/REAS; err-flg='Y'; msg `'Unable to Update User...'`; cursor FNAMEL; `SEND-USRUPD-SCREEN`. // source:383-389

### CLEAR-CURRENT-SCREEN  // source: COUSR02C.cbl:395-398
1. `PERFORM INITIALIZE-ALL-FIELDS`; `PERFORM SEND-USRUPD-SCREEN`.

### INITIALIZE-ALL-FIELDS  // source: COUSR02C.cbl:403-411
1. `MOVE -1 TO USRIDINL` (cursor); `MOVE SPACES` to USRIDINI, FNAMEI, LNAMEI, PASSWDI, USRTYPEI, WS-MESSAGE.

---

## 6. Pseudo-conversational Flow

```
ENTER (first time, EIBCALEN>0, NOT REENTER):
  set REENTER, blank output map, cursor->USRID
  if USR-SELECTED present -> PROCESS-ENTER-KEY (auto-fetch)
  SEND map (ERASE) ; RETURN TRANSID CU02

RE-ENTRY (REENTER):
  RECEIVE map
  ENTER -> fetch record into edit fields
  PF3   -> validate+save (UPDATE-USER-INFO) then XCTL to FROM-PROGRAM/COADM01C
  PF4   -> clear all fields, redisplay
  PF5   -> validate+save (UPDATE-USER-INFO), stay on screen
  PF12  -> XCTL COADM01C (cancel, no save)
  other -> "Invalid key pressed..." , SEND
  RETURN TRANSID CU02

EIBCALEN = 0 -> XCTL COSGN00C
```
// source: COUSR02C.cbl:90-138,250-261

**EIBAID constants** come from `COPY DFHAID`; attribute/colour constants (`DFHRED`, `DFHGREEN`,
`DFHNEUTR`, `DFHBMASB` etc.) from `COPY DFHBMSCA`. // source: COUSR02C.cbl:67-68

---

## 7. Validation Rules & Exact Literal Messages

All messages are 80-char `WS-MESSAGE` (display-trimmed). Reproduce **verbatim**, including trailing dots/spaces.

| Trigger | Exact message text | Colour attr | Source |
|---|---|---|---|
| Unknown PFKey | `Invalid key pressed. Please see below...         ` (CCDA-MSG-INVALID-KEY, X(50)) | (default red ASKIP/BRT field) | 129; CSMSG01Y.cpy:20-21 |
| User ID empty (Enter) | `User ID can NOT be empty...` | red | 148-149 |
| User ID empty (Update) | `User ID can NOT be empty...` | red | 182-183 |
| First Name empty | `First Name can NOT be empty...` | red | 188-189 |
| Last Name empty | `Last Name can NOT be empty...` | red | 194-195 |
| Password empty | `Password can NOT be empty...` | red | 200-201 |
| User Type empty | `User Type can NOT be empty...` | red | 206-207 |
| Read OK (record fetched) | `Press PF5 key to save your updates ...` | DFHNEUTR (neutral) | 336,338 |
| User ID not found (read) | `User ID NOT found...` | red | 342-343 |
| Read other error | `Unable to lookup User...` | red | 349-350 |
| No field changed | `Please modify to update ...` | DFHRED | 239,241 |
| Update OK | `User <id> has been updated ...` (STRING-built) | DFHGREEN | 372-375 |
| User ID not found (rewrite) | `User ID NOT found...` | red | 379-380 |
| Update other error | `Unable to Update User...` | red | 386-387 |

**Empty test semantics:** `= SPACES OR LOW-VALUES`. Port: treat a field as empty if all chars are space
**or** all binary zero/null (uninitialized map field). // source: COUSR02C.cbl:146,180,186,192,198,204

**Change detection:** plain `NOT =` byte comparison of the fixed-width screen field against the fixed-width
record field (trailing-space padded). Preserve exact-width comparison semantics so trailing-space
differences do NOT register as a change. // source: COUSR02C.cbl:219-234

**No content validation** beyond non-empty: USRTYPE is *not* validated to be exactly A/U; any non-blank
single char is accepted and written. // source: COUSR02C.cbl:204-209,231-234

---

## 8. FAITHFUL BUGS (reproduce verbatim — DO NOT FIX)

1. **Successful READ shows "Press PF5..." but the user record fields are NOT yet displayed on that SEND.**
   In `READ-USER-SEC-FILE`, the NORMAL branch does `CONTINUE` then immediately sets the "Press PF5 key to
   save your updates ..." message and `PERFORM SEND-USRUPD-SCREEN` — sending the screen **before** control
   returns to PROCESS-ENTER-KEY, which only afterwards moves the fetched FNAME/LNAME/PWD/TYPE into the map
   and sends again. The net effect is the map is sent twice in one turn (the second SEND with data wins,
   but only ENTER path performs the second SEND). On the PF5/PF3 (UPDATE-USER-INFO) path, READ-USER-SEC-FILE
   on a NORMAL read **also** fires this extra SEND with "Press PF5..." even though the caller is mid-update —
   producing an unexpected screen send during the update flow. Reproduce the double-SEND behaviour exactly.
   // source: COUSR02C.cbl:334-339, 166-172, 215-217

2. **Dead `CONTINUE` before the message move.** The NORMAL branch begins with `CONTINUE` immediately
   followed by the message MOVE — the CONTINUE is a no-op left in place; keep it (harmless, but preserve
   the structure). // source: COUSR02C.cbl:334-336

3. **PASSWD is FSET + DRK (non-display).** The password field is sent dark; on RECEIVE the FSET means the
   field is always returned even when the user didn't retype it, but because it's DRK the operator cannot
   see the fetched value. Editing other fields while leaving password blank-looking can cause confusion;
   the change-detection compares whatever the terminal returns. Reproduce attribute behaviour as-is.
   // source: bms/COUSR02.bms:130-134; COUSR02C.cbl:227-230

4. **No re-validation of USRTYPE value.** Any single non-space character is accepted as user type and
   written to USRSEC (e.g. 'Z'), with no A/U enforcement. Do not add validation. // source: COUSR02C.cbl:204-209,231-234

5. **`DISPLAY 'RESP:'...'REAS:'...` on error branches** writes to the CICS region SYSOUT/log. Port: route
   to a diagnostic log sink, do not surface to user. // source: COUSR02C.cbl:347,384

---

## 9. PORT NOTES

- **Relational access translation.**
  - READ→ `userSecurityRepo.TryGet(secUsrId, out row)`; RESP NORMAL ⇒ found, NOTFND ⇒ not found.
  - REWRITE→ `userSecurityRepo.Update(row)`; missing PK ⇒ NOTFND/'23' branch. Map ANY other exception to
    the "OTHER" branch ("Unable to lookup/Update User...").
  - The READ uses `UPDATE` intent (lock). In the relational shim no cross-turn lock is needed because the
    READ in `UPDATE-USER-INFO` is immediately followed by the REWRITE in the same handler invocation; wrap
    READ+REWRITE in a single SQL transaction for atomicity. // source: COUSR02C.cbl:322-331,360-366

- **Fixed-width fields.** SEC-USR-* and the map I-fields are space-padded fixed width. Per ARCHITECTURE.md,
  store EXACT n chars incl. trailing spaces; the screen↔record copies (`MOVE FNAMEI TO SEC-USR-FNAME`)
  are width-truncating/padding MOVEs: FNAMEI is X(20)→SEC-USR-FNAME X(20) (same), LNAMEI X(20)→X(20),
  PASSWDI X(8)→X(8), USRTYPEI X(1)→X(1), USRIDINI X(8)→SEC-USR-ID X(8). All equal-width — straight copy.
  // source: COUSR02.CPY:60-84; CSUSR01Y.cpy:18-22

- **STRING builder (success message).** `STRING 'User ' DELIMITED BY SIZE, SEC-USR-ID DELIMITED BY SPACE,
  ' has been updated ...' DELIMITED BY SIZE INTO WS-MESSAGE`. SEC-USR-ID is delimited by the first space,
  i.e. the trimmed user id is inserted. Port: `$"User {secUsrId.TrimEnd()} has been updated ..."`, then
  left-justify into the 80-char message buffer (remaining chars are whatever was already there — but
  WS-MESSAGE was set to SPACES at 370 first, so result is space-padded). // source: COUSR02C.cbl:370-375

- **Cursor / attribute model.** `MOVE -1 TO <field>L` ⇒ cursor to that field on next SEND (CURSOR option).
  Attribute overrides write a colour byte to `ERRMSGC` (`DFHNEUTR` neutral, `DFHRED` red, `DFHGREEN` green).
  Represent as a per-field {value, attrByte, cursorHere} model. // source: COUSR02C.cbl:241,338,371; SEND CURSOR 277

- **`MOVE LOW-VALUES TO COUSR2AO`** on first entry clears the entire output symbolic map to nulls so unset
  fields are not transmitted; model as "reset all output fields to null/unset". // source: COUSR02C.cbl:97

- **Header date/time** via `FUNCTION CURRENT-DATE` (CSDAT01Y layout). Use `IClock` from Runtime; format
  `mm/dd/yy` and `hh:mm:ss` exactly (year(3:2) = last two digits). // source: COUSR02C.cbl:298-315; CSDAT01Y.cpy:17-41

- **COMMAREA persistence.** On RETURN, the full `CARDDEMO-COMMAREA` (incl. CU02-INFO selection block) is
  passed back under TRANSID CU02; on XCTL it is passed to the target. Port: serialize the same struct.
  // source: COUSR02C.cbl:135-138,258-261

- **EIBCALEN slice.** `MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA` copies only the bytes actually
  passed; in .NET deserialize the incoming commarea length-safely (pad/truncate to struct size). // source: COUSR02C.cbl:94

---

## 10. Open Questions / Risks

1. **CU02-INFO commarea overlay.** Confirm the intended in-memory layout: source appends `05 CDEMO-CU02-INFO`
   right after `COPY COCOM01Y`, so it extends the commarea 01. The base commarea (COCOM01Y) does not declare
   this block, so other programs writing CU02-USR-SELECTED (COUSR00C) must use a compatible offset. The port
   should model a single shared commarea record containing the CU02 block. // source: COUSR02C.cbl:49-58
2. **Empty-on-LOW-VALUES** vs the .NET screen model: ensure unset (never-typed) fields deserialize to nulls
   so the `SPACES OR LOW-VALUES` test reproduces (otherwise a never-touched FNAME could falsely pass as
   non-empty). // source: COUSR02C.cbl:146,180-208
3. **Double-SEND on NORMAL read (Faithful Bug #1)** — verify against captured screen-parity fixtures from
   COUSR00C→COUSR02C flow; the extra SEND ordering must match the characterization golden.
4. **No held lock semantics** — acceptable for single-user characterization; flag if concurrency tests are
   later required.
