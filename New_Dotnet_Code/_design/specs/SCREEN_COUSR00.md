# SCREEN SPEC: COUSR00 — List Users

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COUSR00.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COUSR00` |
| Map name | `COUSR0A` |
| DFHMSD CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` |
| TYPE | `&&SYSPARM` |
| Map origin | `COLUMN=1, LINE=1` |
| SIZE | `(24,80)` — 24 rows x 80 cols |

Notes on conventions used below:
- POS in BMS is `(row,col)`, 1-based. The byte at POS is the 3270 attribute byte; the field DATA begins at `col+1`. For a text/console renderer that does not reserve an attribute byte, render the literal/data starting at the POS column (the renderer should decide a consistent rule; column numbers here are the BMS POS values, verbatim).
- ATTRB tokens: `ASKIP` = autoskip protected (cannot tab into / type), `PROT` = protected, `UNPROT` = unprotected (input), `NORM` = normal intensity, `BRT` = bright/high intensity, `FSET` = modified-data-tag set on (field returned on read), `IC` = insert cursor.
- No `IC` attribute appears anywhere in this map. No `PICIN`/`PICOUT` clauses are present. No `JUSTIFY` clauses are present.
- HILIGHT=UNDERLINE appears only on the unprotected input fields.
- "Field name" blank = unlabeled literal/filler field (no DFHMDF label, output-only).

---

## Fields in source order

### Header area

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|---------|-------------------|-----|
| 1 | (filler) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | output literal |
| 2 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | output (protected) |
| 3 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | output (protected) |
| 4 | (filler) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | output literal |
| 5 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `mm/dd/yy` | output (protected) |
| 6 | (filler) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | output literal |
| 7 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | — | (none) | output (protected) |
| 8 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | output (protected) |
| 9 | (filler) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | output literal |
| 10 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `hh:mm:ss` | output (protected) |

### Screen title / page

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|---------|-------------------|-----|
| 11 | (filler) | (4,35) | 10 | ASKIP, BRT | NEUTRAL | — | `List Users` | output literal |
| 12 | (filler) | (4,65) | 5 | ASKIP, BRT | TURQUOISE | — | `Page:` | output literal |
| 13 | `PAGENUM` | (4,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | output (protected) |

### Search prompt + input

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|---------|-------------------|-----|
| 14 | (filler) | (6,5) | 15 | ASKIP, NORM | TURQUOISE | — | `Search User ID:` | output literal |
| 15 | `USRIDIN` | (6,21) | 8 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | **INPUT (unprotected)** |
| 16 | (filler) | (6,30) | 0 | ASKIP, NORM | (default) | — | (none) | stopper/attribute field (LEN=0) |

### Column headings (row 8) and rule line (row 9)

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|---------|-------------------|-----|
| 17 | (filler) | (8,5) | 3 | ASKIP, NORM | NEUTRAL | — | `Sel` | output literal |
| 18 | (filler) | (8,12) | 8 | ASKIP, NORM | NEUTRAL | — | `User ID ` (trailing space) | output literal |
| 19 | (filler) | (8,24) | 20 | ASKIP, NORM | NEUTRAL | — | `     First Name     ` (5 spaces + `First Name` + 5 spaces) | output literal |
| 20 | (filler) | (8,48) | 20 | ASKIP, NORM | NEUTRAL | — | `     Last Name      ` (5 spaces + `Last Name` + 6 spaces) | output literal |
| 21 | (filler) | (8,72) | 4 | ASKIP, NORM | NEUTRAL | — | `Type` | output literal |
| 22 | (filler) | (9,5) | 3 | ASKIP, NORM | NEUTRAL | — | `---` | output literal |
| 23 | (filler) | (9,12) | 8 | ASKIP, NORM | NEUTRAL | — | `--------` | output literal |
| 24 | (filler) | (9,24) | 20 | ASKIP, NORM | NEUTRAL | — | `--------------------` | output literal |
| 25 | (filler) | (9,48) | 20 | ASKIP, NORM | NEUTRAL | — | `--------------------` | output literal |
| 26 | (filler) | (9,72) | 4 | ASKIP, NORM | NEUTRAL | — | `----` | output literal |

### Data rows (10 rows: list of users)

Each row repeats the same 5-field pattern + a LEN=0 stopper after the Sel input. Pattern per row N (display lines 10–19):

- `SEL000n` — selection input: `(row,6)`, LEN 1, ATTRB `(FSET,NORM,UNPROT)`, COLOR GREEN, HILIGHT UNDERLINE, INITIAL `' '` → **INPUT (unprotected)**
- (filler stopper) — `(row,8)`, LEN 0, ATTRB `(ASKIP,NORM)`, default color → stopper/attribute field
- `USRIDnn` — `(row,12)`, LEN 8, ATTRB `(ASKIP,FSET,NORM)`, COLOR BLUE, INITIAL `' '` → output (protected)
- `FNAMEnn` — `(row,24)`, LEN 20, ATTRB `(ASKIP,FSET,NORM)`, COLOR BLUE, INITIAL `' '` → output (protected)
- `LNAMEnn` — `(row,48)`, LEN 20, ATTRB `(ASKIP,FSET,NORM)`, COLOR BLUE, INITIAL `' '` → output (protected)
- `UTYPEnn` — `(row,73)`, LEN 1, ATTRB `(ASKIP,FSET,NORM)`, COLOR BLUE, INITIAL `' '` → output (protected)

Explicit per-row table:

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL | I/O |
|---|------|-----------|-----|-------|-------|---------|---------|-----|
| 27 | `SEL0001` | (10,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 28 | (stopper) | (10,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 29 | `USRID01` | (10,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 30 | `FNAME01` | (10,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 31 | `LNAME01` | (10,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 32 | `UTYPE01` | (10,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 33 | `SEL0002` | (11,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 34 | (stopper) | (11,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 35 | `USRID02` | (11,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 36 | `FNAME02` | (11,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 37 | `LNAME02` | (11,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 38 | `UTYPE02` | (11,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 39 | `SEL0003` | (12,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 40 | (stopper) | (12,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 41 | `USRID03` | (12,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 42 | `FNAME03` | (12,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 43 | `LNAME03` | (12,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 44 | `UTYPE03` | (12,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 45 | `SEL0004` | (13,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 46 | (stopper) | (13,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 47 | `USRID04` | (13,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 48 | `FNAME04` | (13,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 49 | `LNAME04` | (13,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 50 | `UTYPE04` | (13,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 51 | `SEL0005` | (14,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 52 | (stopper) | (14,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 53 | `USRID05` | (14,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 54 | `FNAME05` | (14,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 55 | `LNAME05` | (14,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 56 | `UTYPE05` | (14,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 57 | `SEL0006` | (15,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 58 | (stopper) | (15,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 59 | `USRID06` | (15,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 60 | `FNAME06` | (15,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 61 | `LNAME06` | (15,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 62 | `UTYPE06` | (15,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 63 | `SEL0007` | (16,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 64 | (stopper) | (16,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 65 | `USRID07` | (16,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 66 | `FNAME07` | (16,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 67 | `LNAME07` | (16,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 68 | `UTYPE07` | (16,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 69 | `SEL0008` | (17,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 70 | (stopper) | (17,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 71 | `USRID08` | (17,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 72 | `FNAME08` | (17,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 73 | `LNAME08` | (17,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 74 | `UTYPE08` | (17,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 75 | `SEL0009` | (18,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 76 | (stopper) | (18,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 77 | `USRID09` | (18,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 78 | `FNAME09` | (18,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 79 | `LNAME09` | (18,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 80 | `UTYPE09` | (18,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 81 | `SEL0010` | (19,6) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | `' '` | **INPUT** |
| 82 | (stopper) | (19,8) | 0 | ASKIP, NORM | (default) | — | (none) | stopper |
| 83 | `USRID10` | (19,12) | 8 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 84 | `FNAME10` | (19,24) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 85 | `LNAME10` | (19,48) | 20 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |
| 86 | `UTYPE10` | (19,73) | 1 | ASKIP, FSET, NORM | BLUE | — | `' '` | output |

### Footer / messages

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|---------|-------------------|-----|
| 87 | (filler) | (21,12) | 56 | ASKIP, BRT | NEUTRAL | — | `Type 'U' to Update or 'D' to Delete a User from the list` | output literal |
| 88 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | output (error message, protected) |
| 89 | (filler) | (24,1) | 48 | ASKIP, NORM | YELLOW | — | `ENTER=Continue  F3=Back  F7=Backward  F8=Forward` | output literal |

---

## Input vs output summary

**Unprotected (input) fields — 11 total:**
- `USRIDIN` (6,21) LEN 8 — search user id
- `SEL0001`..`SEL0010` (rows 10–19, col 6) LEN 1 each — per-row selection (`U`/`D`)

All 11 input fields are GREEN with HILIGHT=UNDERLINE and FSET on.

**Cursor (IC):** No field carries the `IC` attribute. The application program positions the cursor at runtime (no static insert-cursor in this BMS map). Renderer default: place cursor at the first unprotected field `USRIDIN` (6,21) unless the program overrides.

**Output / protected fields:** all `ASKIP` literals and the `*NAME`, `TITLE0n`, `CURDATE`, `CURTIME`, `PAGENUM`, `USRIDnn`, `FNAMEnn`, `LNAMEnn`, `UTYPEnn`, `ERRMSG` fields.

**Stopper fields (LEN=0):** at (6,30) and at (row,8) for each data row (10–19). These carry only an attribute byte (ASKIP,NORM, default/green color) to terminate the preceding unprotected field so input cannot bleed past it.

## Notes for byte-for-byte rendering

- Multi-line literal #87 is continued across two BMS source lines (447–448): `'Type ''U'' to Update or ''D'' to Delete a User '` + `'from the list'`. Doubled quotes `''` represent a single literal `'`. Final text (56 chars): `Type 'U' to Update or 'D' to Delete a User from the list`.
- Footer literal #89 is continued across lines 457–458: `ENTER=Continue  F3=Back  F7=Backward  F8=Forward` (48 chars; note the double spaces between each option group as written).
- Color tokens map: BLUE, YELLOW, NEUTRAL (white/default), TURQUOISE (cyan), GREEN, RED.
- `NEUTRAL` + `BRT` is the typical "bright white" highlight used for the `List Users` title (4,35) and the action hint (21,12).
- `&&SYSPARM` in TYPE resolves at assembly to DSECT/MAP — not relevant to the rendered screen.
