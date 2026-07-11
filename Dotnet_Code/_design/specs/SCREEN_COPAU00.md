# SCREEN SPEC — COPAU00 (Pending Authorization / View Authorizations)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/bms/COPAU00.bms`

Application: AWS CardDemo — Authorization (IMS / DB2 / MQ variant)
Purpose: "View Authorizations" — list of pending/processed authorizations for a searched account, with selectable rows.

---

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COPAU00` |
| Macro | `DFHMSD` |
| CTRL | `(ALARM,FREEKB)` — sound alarm on send; keyboard freed (unlock requires operator) |
| EXTATT | `YES` (extended attributes — color/highlight supported) |
| LANG | `COBOL` |
| MODE | `INOUT` (map used for both input and output) |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` (12-byte TIOA prefix on symbolic map — generated fields have `FILLER` prefix) |
| TYPE | `&&SYSPARM` (DSECT vs MAP chosen at assembly time) |

Default justification: LEFT (no `JUSTIFY` specified on any field). No PICIN/PICOUT clauses appear anywhere in this map (all data fields are plain alphanumeric DFHMDF without PICIN/PICOUT).

---

## Map

| Property | Value |
|----------|-------|
| Map name | `COPAU0A` |
| Macro | `DFHMDI` |
| SIZE | `(24,80)` — 24 rows × 80 columns |
| COLUMN | `1` |
| LINE | `1` |

Rows are 1-based (1..24), columns are 1-based (1..80). In each field, the screen attribute byte occupies the position one column BEFORE `POS` is not used here — BMS reserves the attribute byte AT the field start, so the visible text begins effectively at the same `POS` column for rendering purposes; each field also consumes one trailing attribute position. For byte-for-byte text rendering, place the literal/value starting at the given `(row,col)` for `LENGTH` characters.

---

## Field Catalog (in BMS source order)

Legend:
- **Type**: `LITERAL` = unlabeled static text (no field name, has INITIAL), `OUTPUT` = named protected/ASKIP data field (display only), `INPUT` = named unprotected field (operator can type), `STOP` = zero-length ASKIP stopper field (field separator, no data).
- ATTRB abbreviations: `ASKIP` = autoskip (protected, cursor skips over), `PROT` = protected, `UNPROT` = unprotected (input), `NORM` = normal intensity, `BRT` = bright/high intensity, `FSET` = modified-data-tag preset on (field returned on read), `IC` = insert cursor.
- No field in this map sets `NUM` (numeric), `DRK` (dark/non-display), or `IC`. **There is NO explicit `IC` cursor field** (see Cursor note below).

### Header area (rows 1–2)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (1,1) | 5 | ASKIP,NORM | BLUE | — | `Tran:` |
| 1 | `TRNNAME` | OUTPUT | (1,7) | 4 | ASKIP,FSET,NORM | BLUE | — | (none) |
| 2 | `TITLE01` | OUTPUT | (1,21) | 40 | ASKIP,FSET,NORM | YELLOW | — | (none) |
| — | (unnamed) | LITERAL | (1,65) | 5 | ASKIP,NORM | BLUE | — | `Date:` |
| 3 | `CURDATE` | OUTPUT | (1,71) | 8 | ASKIP,FSET,NORM | BLUE | — | `mm/dd/yy` |
| — | (unnamed) | LITERAL | (2,1) | 5 | ASKIP,NORM | BLUE | — | `Prog:` |
| 4 | `PGMNAME` | OUTPUT | (2,7) | 8 | ASKIP,FSET,NORM | BLUE | — | (none) |
| 5 | `TITLE02` | OUTPUT | (2,21) | 40 | ASKIP,FSET,NORM | YELLOW | — | (none) |
| — | (unnamed) | LITERAL | (2,65) | 5 | ASKIP,NORM | BLUE | — | `Time:` |
| 6 | `CURTIME` | OUTPUT | (2,71) | 8 | ASKIP,FSET,NORM | BLUE | — | `hh:mm:ss` |

### Screen sub-title (row 3)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (3,30) | 19 | (none → defaults: PROT,NORM) | NEUTRAL | — | `View Authorizations` |

> Note: this field has no ATTRB clause. With no ATTRB, the default is unprotected... but because no `UNPROT` is given and it carries an INITIAL literal acting as a heading, it behaves as static text. For the renderer treat as protected display literal, NEUTRAL color.

### Account search line (row 5)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (5,3) | 15 | ASKIP,NORM | TURQUOISE | — | `Search Acct Id:` |
| 7 | `ACCTID` | **INPUT** | (5,19) | 11 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | (none) |
| — | (unnamed) | STOP | (5,31) | 0 | ASKIP,NORM | (default) | — | (none) — stopper terminating ACCTID input field |

### Customer / account detail block (rows 6–9)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (6,3) | 6 | (none → PROT,NORM) | DEFAULT | — | `Name: ` (trailing space) |
| 8 | `CNAME` | OUTPUT | (6,10) | 25 | ASKIP,NORM | BLUE | — | (none) |
| — | (unnamed) | LITERAL | (6,44) | 13 | (none → PROT,NORM) | (default) | — | `Customer Id: ` (trailing space) |
| 9 | `CUSTID` | OUTPUT | (6,58) | 9 | ASKIP,NORM | BLUE | — | (none) |
| 10 | `ADDR001` | OUTPUT | (7,10) | 25 | ASKIP,NORM | BLUE | — | (none) |
| — | (unnamed) | LITERAL | (7,44) | 13 | (none → PROT,NORM) | (default) | — | `Acct Status: ` (trailing space) |
| 11 | `ACCSTAT` | OUTPUT | (7,58) | 1 | ASKIP,NORM | BLUE | — | (none) |
| 12 | `ADDR002` | OUTPUT | (8,10) | 25 | ASKIP,NORM | BLUE | — | (none) |
| — | (unnamed) | LITERAL | (9,10) | 3 | (none → PROT,NORM) | (default) | — | `PH:` |
| 13 | `PHONE1` | OUTPUT | (9,15) | 13 | ASKIP,NORM | BLUE | — | (none) |
| — | (unnamed) | LITERAL | (9,44) | 13 | (none → PROT,NORM) | (default) | — | `Approval # : ` (trailing space) |
| 14 | `APPRCNT` | OUTPUT | (9,58) | 3 | ASKIP,NORM | BLUE | — | (none) |
| — | (unnamed) | LITERAL | (9,64) | 10 | (none → PROT,NORM) | (default) | — | `Decline #:` |
| 15 | `DECLCNT` | OUTPUT | (9,76) | 3 | ASKIP,NORM | BLUE | — | (none) |

### Limits / balances block (rows 11–12)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (11,6) | 11 | (none → PROT,NORM) | DEFAULT | — | `Credit Lim:` |
| 16 | `CREDLIM` | OUTPUT | (11,19) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |
| — | (unnamed) | LITERAL | (11,35) | 9 | (none → PROT,NORM) | DEFAULT | — | `Cash Lim:` |
| 17 | `CASHLIM` | OUTPUT | (11,46) | 9 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |
| — | (unnamed) | LITERAL | (11,58) | 9 | (none → PROT,NORM) | DEFAULT | — | `Appr Amt:` |
| 18 | `APPRAMT` | OUTPUT | (11,69) | 10 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |
| — | (unnamed) | LITERAL | (12,6) | 11 | (none → PROT,NORM) | DEFAULT | — | `Credit Bal:` |
| 19 | `CREDBAL` | OUTPUT | (12,19) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |
| — | (unnamed) | LITERAL | (12,35) | 9 | (none → PROT,NORM) | DEFAULT | — | `Cash Bal:` |
| 20 | `CASHBAL` | OUTPUT | (12,46) | 9 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |
| — | (unnamed) | LITERAL | (12,58) | 9 | (none → PROT,NORM) | DEFAULT | — | `Decl Amt:` |
| 21 | `DECLAMT` | OUTPUT | (12,69) | 10 | ASKIP,FSET,NORM | BLUE | — | `' '` (single blank) |

### List column headers (row 14) and separator rules (row 15)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (14,2) | 3 | ASKIP,NORM | NEUTRAL | — | `Sel` |
| — | (unnamed) | LITERAL | (14,8) | 16 | ASKIP,NORM | NEUTRAL | — | ` Transaction ID ` (leading+trailing space) |
| — | (unnamed) | LITERAL | (14,27) | 8 | ASKIP,NORM | NEUTRAL | — | `  Date  ` |
| — | (unnamed) | LITERAL | (14,38) | 8 | ASKIP,NORM | NEUTRAL | — | `  Time  ` |
| — | (unnamed) | LITERAL | (14,49) | 5 | ASKIP,NORM | NEUTRAL | — | `Type ` (trailing space) |
| — | (unnamed) | LITERAL | (14,56) | 3 | ASKIP,NORM | NEUTRAL | — | `A/D` |
| — | (unnamed) | LITERAL | (14,61) | 3 | ASKIP,NORM | NEUTRAL | — | `STS` |
| — | (unnamed) | LITERAL | (14,67) | 12 | ASKIP,NORM | NEUTRAL | — | `   Amount   ` |
| — | (unnamed) | LITERAL | (15,2) | 3 | ASKIP,NORM | NEUTRAL | — | `---` |
| — | (unnamed) | LITERAL | (15,8) | 16 | ASKIP,NORM | NEUTRAL | — | `----------------` |
| — | (unnamed) | LITERAL | (15,27) | 8 | ASKIP,NORM | NEUTRAL | — | `--------` |
| — | (unnamed) | LITERAL | (15,37) | 8 | ASKIP,NORM | NEUTRAL | — | `--------` |
| — | (unnamed) | LITERAL | (15,49) | 4 | ASKIP,NORM | NEUTRAL | — | `----` |
| — | (unnamed) | LITERAL | (15,56) | 3 | ASKIP,NORM | NEUTRAL | — | `---` |
| — | (unnamed) | LITERAL | (15,61) | 3 | ASKIP,NORM | NEUTRAL | — | `---` |
| — | (unnamed) | LITERAL | (15,67) | 12 | ASKIP,NORM | NEUTRAL | — | `------------` |

> Note the slight misalignment between header (14,27)/(14,38) and rule (15,27)/(15,37): the Time rule starts at col 37 while the Time header starts at col 38. This is reproduced exactly as in the BMS.

### Detail rows 1–5 (rows 16–20)

Each detail row has: a `SELnnnn` INPUT selection field (col 3, len 1, GREEN, UNDERLINE), an ASKIP zero-length STOP at col 5, then OUTPUT data fields. All data fields are `ASKIP,FSET,NORM`, COLOR BLUE, INITIAL `' '` (single blank). All `SELnnnn` are `FSET,NORM,UNPROT`, COLOR GREEN, HILIGHT UNDERLINE, INITIAL `' '`.

**Row 1 (screen row 16):**

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL |
|---|------|------|-----------|-----|-------|-------|---------|---------|
| 22 | `SEL0001` | **INPUT** | (16,3) | 1 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | `' '` |
| — | (unnamed) | STOP | (16,5) | 0 | ASKIP,NORM | (default) | — | (none) |
| 23 | `TRNID01` | OUTPUT | (16,8) | 16 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 24 | `PDATE01` | OUTPUT | (16,27) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 25 | `PTIME01` | OUTPUT | (16,38) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 26 | `PTYPE01` | OUTPUT | (16,49) | 4 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 27 | `PAPRV01` | OUTPUT | (16,58) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 28 | `PSTAT01` | OUTPUT | (16,63) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 29 | `PAMT001` | OUTPUT | (16,67) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` |

**Row 2 (screen row 17):**

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL |
|---|------|------|-----------|-----|-------|-------|---------|---------|
| 30 | `SEL0002` | **INPUT** | (17,3) | 1 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | `' '` |
| — | (unnamed) | STOP | (17,5) | 0 | ASKIP,NORM | (default) | — | (none) |
| 31 | `TRNID02` | OUTPUT | (17,8) | 16 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 32 | `PDATE02` | OUTPUT | (17,27) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 33 | `PTIME02` | OUTPUT | (17,38) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 34 | `PTYPE02` | OUTPUT | (17,49) | 4 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 35 | `PAPRV02` | OUTPUT | (17,58) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 36 | `PSTAT02` | OUTPUT | (17,63) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 37 | `PAMT002` | OUTPUT | (17,67) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` |

**Row 3 (screen row 18):**

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL |
|---|------|------|-----------|-----|-------|-------|---------|---------|
| 38 | `SEL0003` | **INPUT** | (18,3) | 1 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | `' '` |
| — | (unnamed) | STOP | (18,5) | 0 | ASKIP,NORM | (default) | — | (none) |
| 39 | `TRNID03` | OUTPUT | (18,8) | 16 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 40 | `PDATE03` | OUTPUT | (18,27) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 41 | `PTIME03` | OUTPUT | (18,38) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 42 | `PTYPE03` | OUTPUT | (18,49) | 4 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 43 | `PAPRV03` | OUTPUT | (18,58) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 44 | `PSTAT03` | OUTPUT | (18,63) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 45 | `PAMT003` | OUTPUT | (18,67) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` |

**Row 4 (screen row 19):**

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL |
|---|------|------|-----------|-----|-------|-------|---------|---------|
| 46 | `SEL0004` | **INPUT** | (19,3) | 1 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | `' '` |
| — | (unnamed) | STOP | (19,5) | 0 | ASKIP,NORM | (default) | — | (none) |
| 47 | `TRNID04` | OUTPUT | (19,8) | 16 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 48 | `PDATE04` | OUTPUT | (19,27) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 49 | `PTIME04` | OUTPUT | (19,38) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 50 | `PTYPE04` | OUTPUT | (19,49) | 4 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 51 | `PAPRV04` | OUTPUT | (19,58) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 52 | `PSTAT04` | OUTPUT | (19,63) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 53 | `PAMT004` | OUTPUT | (19,67) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` |

**Row 5 (screen row 20):**

> SOURCE ORDER QUIRK: In the BMS, the row-5 OUTPUT data fields (`TRNID05`..`PAMT005`) are defined BEFORE the row-5 selection field (`SEL0005`). `SEL0005` and its stopper appear at the very end of the detail block. Catalog reflects BMS source order.

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL |
|---|------|------|-----------|-----|-------|-------|---------|---------|
| 54 | `TRNID05` | OUTPUT | (20,8) | 16 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 55 | `PDATE05` | OUTPUT | (20,27) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 56 | `PTIME05` | OUTPUT | (20,38) | 8 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 57 | `PTYPE05` | OUTPUT | (20,49) | 4 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 58 | `PAPRV05` | OUTPUT | (20,58) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 59 | `PSTAT05` | OUTPUT | (20,63) | 1 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 60 | `PAMT005` | OUTPUT | (20,67) | 12 | ASKIP,FSET,NORM | BLUE | — | `' '` |
| 61 | `SEL0005` | **INPUT** | (20,3) | 1 | FSET,NORM,**UNPROT** | GREEN | **UNDERLINE** | `' '` |
| — | (unnamed) | STOP | (20,5) | 0 | ASKIP,NORM | (default) | — | (none) |

### Instruction line, error message, function-key footer (rows 22–24)

| # | Name | Type | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal |
|---|------|------|-----------|-----|-------|-------|---------|-------------------|
| — | (unnamed) | LITERAL | (22,12) | 52 | ASKIP,**BRT** | NEUTRAL | — | `Type 'S' to View Authorization details from the list` |
| 62 | `ERRMSG` | OUTPUT | (23,1) | 78 | ASKIP,**BRT**,FSET | RED | — | (none) |
| — | (unnamed) | LITERAL | (24,1) | 48 | ASKIP,NORM | YELLOW | — | `ENTER=Continue  F3=Back  F7=Backward  F8=Forward` |

> The (22,12) instruction literal continues across two BMS lines; the assembled value is exactly: `Type 'S' to View Authorization details from the list` (the doubled `''` in BMS = one literal apostrophe).

---

## Input vs Output summary

### INPUT fields (UNPROT — operator can type)
All are `FSET,NORM,UNPROT`, COLOR GREEN, HILIGHT UNDERLINE:

| Name | POS | LEN | Purpose |
|------|-----|-----|---------|
| `ACCTID` | (5,19) | 11 | Account ID to search |
| `SEL0001` | (16,3) | 1 | Row-1 selection (type `S`) |
| `SEL0002` | (17,3) | 1 | Row-2 selection |
| `SEL0003` | (18,3) | 1 | Row-3 selection |
| `SEL0004` | (19,3) | 1 | Row-4 selection |
| `SEL0005` | (20,3) | 1 | Row-5 selection |

(6 unprotected input fields total.)

### OUTPUT / display fields (named, ASKIP — protected)
`TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `CNAME`, `CUSTID`, `ADDR001`, `ACCSTAT`, `ADDR002`, `PHONE1`, `APPRCNT`, `DECLCNT`, `CREDLIM`, `CASHLIM`, `APPRAMT`, `CREDBAL`, `CASHBAL`, `DECLAMT`, `ERRMSG`, and the 5×7 detail-row grid (`TRNIDnn`, `PDATEnn`, `PTIMEnn`, `PTYPEnn`, `PAPRVnn`, `PSTATnn`, `PAMTnn`, nn=01..05).
(56 named protected/display fields.)

### Static literals (unnamed, no field name)
~38 unnamed `DFHMDF` entries carrying `INITIAL` heading/label/rule text, plus zero-length ASKIP stopper fields after each input field. These are NOT counted in `fieldCount` (only named fields are counted).

---

## Cursor (IC)

**No field specifies `IC`.** No `CURSOR=` clause on `DFHMDI` either. The initial cursor position is therefore not set by the map; the COBOL program is responsible for positioning the cursor at runtime (typically via `EXEC CICS SEND ... CURSOR(...)` or by setting `-1` in the symbolic-map length field). For the renderer's default behavior, place the cursor at the first unprotected field in display order: **`ACCTID` at (5,19)**.

---

## Color / highlight / intensity notes

- **Colors used:** BLUE (most labels & output data), YELLOW (`TITLE01`, `TITLE02`, footer line), TURQUOISE (`Search Acct Id:` label), GREEN (all input fields), NEUTRAL (sub-title, list column headers/rules, instruction line), RED (`ERRMSG`), DEFAULT (several detail labels — render in the terminal's default foreground).
- **HILIGHT=UNDERLINE:** only on the 6 input fields (`ACCTID`, `SEL0001`..`SEL0005`). No `BLINK` or `REVERSE` anywhere.
- **Intensity:** `NORM` (normal) everywhere except `BRT` (bright/high) on the (22,12) instruction literal and on `ERRMSG` (23,1).
- **Justify:** none specified — all fields default to LEFT justify.
- **NUM / DRK:** none — no numeric-only or dark (non-display) fields.
- **PICIN / PICOUT:** none present in this map.

---

## Rendering hints for 24×80 console

1. Clear screen to spaces (blanks), 24 rows × 80 cols.
2. Paint static LITERAL text at each `(row,col)` for its `LENGTH`, in its COLOR.
3. Render output data fields as blanks initially (program fills at runtime); `CURDATE`=`mm/dd/yy`, `CURTIME`=`hh:mm:ss` show as placeholder initial text.
4. Input fields render with UNDERLINE styling, GREEN, length-wide; one trailing attribute byte (the next position) terminates the field — keep at least one blank/stopper column after each.
5. Watch the deliberate offsets: header Time at col 38 vs rule at col 37; row-5 fields defined out of order vs other rows.
6. `ERRMSG` (row 23) is the dynamic error line — RED, BRIGHT, full width 78.
