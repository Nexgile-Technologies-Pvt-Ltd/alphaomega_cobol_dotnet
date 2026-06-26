# SCREEN SPEC: COADM01 (CardDemo Admin Menu)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COADM01.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COADM01` |
| DFHMSD attributes | `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `LANG=COBOL`, `MODE=INOUT`, `STORAGE=AUTO`, `TIOAPFX=YES`, `TYPE=&&SYSPARM` |
| Notes | `FREEKB` = keyboard unlocked on display. `ALARM` = sound terminal alarm on display. `EXTATT=YES` enables extended attributes (color, highlight). |

## Map

| Property | Value |
|----------|-------|
| Map name | `COADM1A` |
| Position | `LINE=1`, `COLUMN=1` |
| SIZE (rows, cols) | `(24, 80)` |

Screen geometry: 24 rows x 80 columns.

---

## Field list (in BMS source order)

Notes on conventions:
- Row/Col are 1-based from `POS=(row,col)`.
- An unnamed field (blank label column in BMS) is a literal/decoration field; it has no DFHMDF name and is referenced here as `(literal #n)`.
- All fields default justify LEFT unless `JUSTIFY` is specified.
- "Input" = `UNPROT` (operator can type). "Output/Literal" = `ASKIP`/`PROT` (cannot type into it).
- `NORM` = normal intensity, `BRT` = bright/high intensity.
- `FSET` = modified-data-tag set on send (field returned on next read even if unchanged).

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT / JUSTIFY | INITIAL / literal | I/O |
|---|------|-----------|-----|-------|-------|-------------------|-------------------|-----|
| 1 | (literal #1) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | Output/Literal |
| 2 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (program-filled: transaction id) |
| 3 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (program-filled: title line 1) |
| 4 | (literal #2) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | Output/Literal |
| 5 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `mm/dd/yy` | Output (program-filled: current date) |
| 6 | (literal #3) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | Output/Literal |
| 7 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (program-filled: program name) |
| 8 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (program-filled: title line 2) |
| 9 | (literal #4) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | Output/Literal |
| 10 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `hh:mm:ss` | Output (program-filled: current time) |
| 11 | (literal #5) | (4,35) | 10 | ASKIP, BRT | NEUTRAL | — | `Admin Menu` | Output/Literal |
| 12 | `OPTN001` | (6,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 1) |
| 13 | `OPTN002` | (7,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 2) |
| 14 | `OPTN003` | (8,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 3) |
| 15 | `OPTN004` | (9,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 4) |
| 16 | `OPTN005` | (10,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 5) |
| 17 | `OPTN006` | (11,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 6) |
| 18 | `OPTN007` | (12,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 7) |
| 19 | `OPTN008` | (13,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 8) |
| 20 | `OPTN009` | (14,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 9) |
| 21 | `OPTN010` | (15,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 10) |
| 22 | `OPTN011` | (16,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 11) |
| 23 | `OPTN012` | (17,20) | 40 | ASKIP, FSET, NORM | BLUE | — | `' '` (single space) | Output (program-filled: menu option 12) |
| 24 | (literal #6) | (20,15) | 25 | ASKIP, BRT | TURQUOISE | — | `Please select an option :` | Output/Literal |
| 25 | `OPTION` | (20,41) | 2 | FSET, IC, NORM, NUM, UNPROT | (default; none specified) | HILIGHT=UNDERLINE; JUSTIFY=(RIGHT,ZERO) | (none) | **Input** (cursor home) |
| 26 | (literal #7 / stopper) | (20,44) | 0 | ASKIP, NORM | GREEN | — | (none) | Output/Literal (zero-length field-stop attribute byte) |
| 27 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | Output (program-filled: error message) |
| 28 | (literal #8) | (24,1) | 23 | ASKIP, NORM | YELLOW | — | `ENTER=Continue  F3=Exit` | Output/Literal |

---

## Input vs Output summary

- **Input field (UNPROT — operator can type):**
  - `OPTION` at (20,41), LENGTH=2, numeric (`NUM`), right-justified with zero fill (`JUSTIFY=(RIGHT,ZERO)`), underlined (`HILIGHT=UNDERLINE`), MDT preset (`FSET`). This is the only unprotected/keyable field.

- **Cursor (IC) field:**
  - `OPTION` at (20,41) — `IC` (Insert Cursor) places the initial cursor here.

- **Output / literal / program-filled fields (protected, ASKIP):** all other fields.
  - Static literals (constant text): `Tran:`, `Date:`, `Prog:`, `Time:`, `Admin Menu`, `Please select an option :`, `ENTER=Continue  F3=Exit`.
  - Program-filled dynamic output: `TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `OPTN001`-`OPTN012`, `ERRMSG`.
  - The zero-length GREEN field at (20,44) is a field-separator/attribute stopper that terminates the `OPTION` input field (so input cannot run past 2 chars into the rest of the line). It renders no visible text but sets the attribute byte for the position following the input field.

## Highlight / Justify / Color notes

- **HILIGHT:** Only `OPTION` uses `HILIGHT=UNDERLINE` (underscore styling under the 2-char input area).
- **JUSTIFY:** Only `OPTION` uses `JUSTIFY=(RIGHT,ZERO)` — right-justified, pad/fill with zeros.
- **NUM:** Only `OPTION` is numeric (`NUM`) — numeric-only data entry, numeric-lock keyboard behavior.
- **Colors used:** BLUE (labels + values), YELLOW (titles, bottom function-key line), NEUTRAL/white (Admin Menu heading), TURQUOISE (prompt line), RED (error message), GREEN (zero-length stopper). `OPTION` has no COLOR clause -> default device color (typically green/white per terminal default).
- **Intensity:** Most fields `NORM`. Bright (`BRT`): `Admin Menu` (literal #5), `Please select an option :` (literal #6), `ERRMSG`.
- **PICIN / PICOUT:** None present in this BMS map (no `PICIN`/`PICOUT` clauses on any DFHMDF).

## Byte-for-byte rendering hints (text renderer, 24x80)

- Row 1: `Tran:` at col 1-5; `TRNNAME` (4) at col 7-10; `TITLE01` (40) at col 21-60; `Date:` at col 65-69; `CURDATE`=`mm/dd/yy` at col 71-78.
- Row 2: `Prog:` at col 1-5; `PGMNAME` (8) at col 7-14; `TITLE02` (40) at col 21-60; `Time:` at col 65-69; `CURTIME`=`hh:mm:ss` at col 71-78.
- Row 4: `Admin Menu` (bright) at col 35-44.
- Rows 6-17: menu option text fields `OPTN001`-`OPTN012`, each 40 wide at col 20-59.
- Row 20: `Please select an option :` (bright turquoise) at col 15-39; input `OPTION` (2) at col 41-42; zero-length stopper at col 44.
- Row 23: `ERRMSG` (bright red, 78 wide) at col 1-78.
- Row 24: `ENTER=Continue  F3=Exit` (yellow) at col 1-23.
- In a real 3270, each field is preceded by a 1-byte attribute character occupying the column immediately before `POS`. A text renderer typically renders the field text starting exactly at the POS column and may leave the attribute column blank.

## Named field count

Named (DFHMDF-labeled) fields: **20**
`TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, OPTN001, OPTN002, OPTN003, OPTN004, OPTN005, OPTN006, OPTN007, OPTN008, OPTN009, OPTN010, OPTN011, OPTN012, OPTION, ERRMSG`
