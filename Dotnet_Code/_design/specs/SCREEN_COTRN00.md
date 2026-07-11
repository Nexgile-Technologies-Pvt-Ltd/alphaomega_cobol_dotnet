# SCREEN SPEC: COTRN00 (Transaction List)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COTRN00.bms`
Purpose: CardDemo - List Transactions screen.

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COTRN00` (DFHMSD) |
| Map name | `COTRN0A` (DFHMDI) |
| Map origin | COLUMN=1, LINE=1 |
| SIZE | (24 rows, 80 cols) |
| CTRL | ALARM, FREEKB |
| EXTATT | YES (extended attributes / color enabled) |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES (12-byte fill prefix on symbolic map; no functional screen effect) |
| TYPE | &&SYSPARM |

Notes on conventions used below:
- POS=(row,col) are 1-based BMS coordinates. The attribute byte occupies the position immediately preceding the field's first data column; visible data begins at the stated (row,col). For a 24x80 text renderer, render the literal/data starting at that (row,col).
- ATTRB ASKIP = autoskip + protected (output/label, cursor skips over it). UNPROT = unprotected input field. NORM = normal intensity. BRT = bright/high intensity. FSET = modified-data-tag set (field returned on transmit). No `IC` (insert cursor) attribute is coded anywhere in this map; CICS places the cursor on the first unprotected field by default = `TRNIDIN` at (6,21).
- No NUM, no DRK (dark), no JUSTIFY, no PICIN/PICOUT clauses appear in this map.
- HILIGHT=UNDERLINE is present on all input fields.

## Fields (in source order)

Legend: I/O column — `OUT` = protected output/label (ASKIP), `IN` = unprotected input (UNPROT).

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | I/O |
|---|------|---------------|-----|-------|-------|---------|-------------------|-----|
| - | (unnamed) | (1,1) | 5 | ASKIP, NORM | BLUE | - | `Tran:` | OUT (label) |
| 1 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | - | (none) | OUT |
| 2 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | - | (none) | OUT |
| - | (unnamed) | (1,65) | 5 | ASKIP, NORM | BLUE | - | `Date:` | OUT (label) |
| 3 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | - | `mm/dd/yy` | OUT |
| - | (unnamed) | (2,1) | 5 | ASKIP, NORM | BLUE | - | `Prog:` | OUT (label) |
| 4 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | - | (none) | OUT |
| 5 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | - | (none) | OUT |
| - | (unnamed) | (2,65) | 5 | ASKIP, NORM | BLUE | - | `Time:` | OUT (label) |
| 6 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | - | `hh:mm:ss` | OUT |
| - | (unnamed) | (4,30) | 17 | ASKIP, BRT | NEUTRAL | - | `List Transactions` | OUT (label) |
| - | (unnamed) | (4,65) | 5 | ASKIP, BRT | TURQUOISE | - | `Page:` | OUT (label) |
| 7 | `PAGENUM` | (4,71) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` (single space) | OUT |
| - | (unnamed) | (6,5) | 15 | ASKIP, NORM | TURQUOISE | - | `Search Tran ID:` | OUT (label) |
| 8 | `TRNIDIN` | (6,21) | 16 | UNPROT, FSET, NORM | GREEN | UNDERLINE | (none) | **IN** |
| - | (unnamed) | (6,38) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| - | (unnamed) | (8,2) | 3 | ASKIP, NORM | NEUTRAL | - | `Sel` | OUT (header) |
| - | (unnamed) | (8,8) | 16 | ASKIP, NORM | NEUTRAL | - | `' Transaction ID '` | OUT (header) |
| - | (unnamed) | (8,27) | 8 | ASKIP, NORM | NEUTRAL | - | `'  Date  '` | OUT (header) |
| - | (unnamed) | (8,38) | 26 | ASKIP, NORM | NEUTRAL | - | `'     Description          '` | OUT (header) |
| - | (unnamed) | (8,67) | 12 | ASKIP, NORM | NEUTRAL | - | `'   Amount   '` | OUT (header) |
| - | (unnamed) | (9,2) | 3 | ASKIP, NORM | NEUTRAL | - | `---` | OUT (rule) |
| - | (unnamed) | (9,8) | 16 | ASKIP, NORM | NEUTRAL | - | `----------------` | OUT (rule) |
| - | (unnamed) | (9,27) | 8 | ASKIP, NORM | NEUTRAL | - | `--------` | OUT (rule) |
| - | (unnamed) | (9,38) | 26 | ASKIP, NORM | NEUTRAL | - | `--------------------------` | OUT (rule) |
| - | (unnamed) | (9,67) | 12 | ASKIP, NORM | NEUTRAL | - | `------------` | OUT (rule) |
| 9 | `SEL0001` | (10,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (10,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 10 | `TRNID01` | (10,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 11 | `TDATE01` | (10,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 12 | `TDESC01` | (10,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 13 | `TAMT001` | (10,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 14 | `SEL0002` | (11,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (11,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 15 | `TRNID02` | (11,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 16 | `TDATE02` | (11,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 17 | `TDESC02` | (11,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 18 | `TAMT002` | (11,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 19 | `SEL0003` | (12,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (12,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 20 | `TRNID03` | (12,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 21 | `TDATE03` | (12,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 22 | `TDESC03` | (12,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 23 | `TAMT003` | (12,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 24 | `SEL0004` | (13,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (13,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 25 | `TRNID04` | (13,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 26 | `TDATE04` | (13,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 27 | `TDESC04` | (13,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 28 | `TAMT004` | (13,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 29 | `SEL0005` | (14,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (14,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 30 | `TRNID05` | (14,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 31 | `TDATE05` | (14,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 32 | `TDESC05` | (14,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 33 | `TAMT005` | (14,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 34 | `SEL0006` | (15,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (15,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 35 | `TRNID06` | (15,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 36 | `TDATE06` | (15,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 37 | `TDESC06` | (15,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 38 | `TAMT006` | (15,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 39 | `SEL0007` | (16,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (16,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 40 | `TRNID07` | (16,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 41 | `TDATE07` | (16,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 42 | `TDESC07` | (16,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 43 | `TAMT007` | (16,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 44 | `SEL0008` | (17,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (17,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 45 | `TRNID08` | (17,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 46 | `TDATE08` | (17,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 47 | `TDESC08` | (17,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 48 | `TAMT008` | (17,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 49 | `SEL0009` | (18,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (18,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 50 | `TRNID09` | (18,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 51 | `TDATE09` | (18,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 52 | `TDESC09` | (18,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 53 | `TAMT009` | (18,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 54 | `SEL0010` | (19,3) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` | **IN** |
| - | (unnamed) | (19,5) | 0 | ASKIP, NORM | (default) | - | (stopper/attr only) | OUT |
| 55 | `TRNID10` | (19,8) | 16 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 56 | `TDATE10` | (19,27) | 8 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 57 | `TDESC10` | (19,38) | 26 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| 58 | `TAMT010` | (19,67) | 12 | ASKIP, FSET, NORM | BLUE | - | `' '` | OUT |
| - | (unnamed) | (21,12) | 50 | ASKIP, BRT | NEUTRAL | - | `Type 'S' to View Transaction details from the list` | OUT (instruction) |
| 59 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | - | (none) | OUT (error msg) |
| - | (unnamed) | (24,1) | 48 | ASKIP, NORM | YELLOW | - | `ENTER=Continue  F3=Back  F7=Backward  F8=Forward` | OUT (PF-key footer) |

## Input vs Output summary

Input (UNPROT) fields — these are the only fields the user can type into:
- `TRNIDIN` (6,21) LEN 16 — search transaction ID.
- `SEL0001`..`SEL0010` (rows 10-19, col 3) LEN 1 each — per-row selection markers (type `S`).

All other named fields (`TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `PAGENUM`, the `TRNIDnn`/`TDATEnn`/`TDESCnn`/`TAMT0nn` row data fields, and `ERRMSG`) are protected (ASKIP) output fields populated by the program. Unnamed `DFHMDF` entries are static literal labels/headers/rules/footer or zero-length attribute stoppers.

## Cursor (IC)

- No `IC` attribute is specified on any field.
- CICS default: cursor is placed on the first unprotected field in the map = `TRNIDIN` at (6,21).

## HILIGHT / Justify / PIC

- HILIGHT=UNDERLINE on all 11 input fields: `TRNIDIN`, `SEL0001`..`SEL0010`.
- No JUSTIFY (LEFT/RIGHT) clauses present.
- No PICIN / PICOUT clauses present.
- No NUM (numeric), no DRK (dark/non-display) attributes present.

## Multi-line literal note

- The instruction literal at (21,12) is written across two BMS source lines (continuation): `'Type ''S'' to View Transaction details from the' + ' list'`, with the doubled `''` representing a single literal apostrophe. Effective text: `Type 'S' to View Transaction details from the list` (LEN 50).
- The footer literal at (24,1) is continued as `'ENTER=Continue  F3=Back  F7=Backward  F8=Forwar' + 'd'`. Effective text: `ENTER=Continue  F3=Back  F7=Backward  F8=Forward` (LEN 48).

## Row layout reference (the 10 detail rows)

Each detail row N (rows 10-19) consists of 5 fields at fixed columns:

| Sub-field | Col | LEN | Type |
|-----------|-----|-----|------|
| `SEL000N` (selection) | 3 | 1 | IN (UNPROT, GREEN, UNDERLINE) |
| `TRNIDN`  (tran id)   | 8 | 16 | OUT (BLUE) |
| `TDATEN`  (date)      | 27 | 8 | OUT (BLUE) |
| `TDESCN`  (desc)      | 38 | 26 | OUT (BLUE) |
| `TAMT00N` (amount)    | 67 | 12 | OUT (BLUE) |

(Field-name digit suffixes: SEL uses `0001`..`0010`; TRNID/TDATE/TDESC use `01`..`10`; TAMT uses `001`..`010`.)
