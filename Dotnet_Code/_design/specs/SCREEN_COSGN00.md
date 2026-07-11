# SCREEN SPEC — COSGN00 (CardDemo Login / Sign-on Screen)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COSGN00.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COSGN00` (DFHMSD) |
| Map name | `COSGN0A` (DFHMDI) |
| SIZE (rows, cols) | `(24, 80)` |
| Map origin | `LINE=1, COLUMN=1` |
| CTRL | `ALARM, FREEKB` |
| EXTATT | `YES` (extended attributes / color enabled) |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` |
| TYPE | `&SYSPARM` |

Notes for the renderer:
- 24 rows x 80 columns text grid.
- In 3270, each field is preceded by a 1-byte attribute character that occupies the column **immediately before** `POS`. `POS=(r,c)` is where the field DATA begins. The attribute byte sits at `(r, c-1)` and renders as a blank/space. The renderer should leave that cell blank.
- `ASKIP` = autoskip (protected, cursor skips over it) → output/literal.
- `PROT` = protected (no input) → output.
- `UNPROT` = unprotected → user input field.
- `NORM` = normal intensity; `BRT` = bright; `DRK` = dark (non-display, e.g. password).
- `FSET` = Field-Set MDT on (field returned as modified on read).
- `IC` = Insert Cursor (initial cursor position).
- `HILIGHT=OFF` = no extended highlighting.
- No `PICIN`/`PICOUT`, no `JUSTIFY` clauses are present anywhere in this map.

---

## Field List (in source order)

Legend — Kind: **L** = literal/static (unnamed), **O** = output (named, protected), **I** = input (named, unprotected).

| # | Name | Kind | POS (row,col) | LEN | ATTRB | COLOR | INITIAL / Literal value |
|---|------|------|---------------|-----|-------|-------|--------------------------|
| 1 | *(unnamed)* | L | (1,1) | 6 | ASKIP, NORM | BLUE | `Tran :` |
| 2 | `TRNNAME` | O | (1,8) | 4 | ASKIP, FSET, NORM | BLUE | *(none — dynamic, transaction id)* |
| 3 | `TITLE01` | O | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | *(none — dynamic title line 1)* |
| 4 | *(unnamed)* | L | (1,64) | 6 | ASKIP, NORM | BLUE | `Date :` |
| 5 | `CURDATE` | O | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | `mm/dd/yy` |
| 6 | *(unnamed)* | L | (2,1) | 6 | ASKIP, NORM | BLUE | `Prog :` |
| 7 | `PGMNAME` | O | (2,8) | 8 | FSET, NORM, PROT | BLUE | *(none — dynamic program name)* |
| 8 | `TITLE02` | O | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | *(none — dynamic title line 2)* |
| 9 | *(unnamed)* | L | (2,64) | 6 | ASKIP, NORM | BLUE | `Time :` |
| 10 | `CURTIME` | O | (2,71) | 9 | FSET, NORM, PROT | BLUE | `Ahh:mm:ss` |
| 11 | *(unnamed)* | L | (3,1) | 6 | FSET, NORM, PROT | BLUE | `AppID:` |
| 12 | `APPLID` | O | (3,8) | 8 | FSET, NORM, PROT | BLUE | *(none — dynamic CICS APPLID)* |
| 13 | *(unnamed)* | L | (3,64) | 6 | ASKIP, NORM | BLUE | `SysID:` |
| 14 | `SYSID` | O | (3,71) | 8 | FSET, NORM, PROT | BLUE | `        ` (8 spaces) |
| 15 | *(unnamed)* | L | (5,6) | 66 | ASKIP, NORM | NEUTRAL | `This is a Credit Card Demo Application for Mainframe Modernization` |
| 16 | *(unnamed)* | L | (7,21) | 42 | ASKIP, NORM | BLUE | `+========================================+` |
| 17 | *(unnamed)* | L | (8,21) | 42 | ASKIP, NORM | BLUE | `\|%%%%%%%  NATIONAL RESERVE NOTE  %%%%%%%%\|` |
| 18 | *(unnamed)* | L | (9,21) | 42 | ASKIP, NORM | BLUE | `\|%(1)  THE UNITED STATES OF KICSLAND (1)%\|` |
| 19 | *(unnamed)* | L | (10,21) | 42 | ASKIP, NORM | BLUE | `\|%$$              ___       ********  $$%\|` |
| 20 | *(unnamed)* | L | (11,21) | 42 | ASKIP, NORM | BLUE | `\|%$    {x}       (o o)                 $%\|` |
| 21 | *(unnamed)* | L | (12,21) | 42 | ASKIP, NORM | BLUE | `\|%$     ******  (  V  )      O N E     $%\|` |
| 22 | *(unnamed)* | L | (13,21) | 42 | ASKIP, NORM | BLUE | `\|%(1)          ---m-m---             (1)%\|` |
| 23 | *(unnamed)* | L | (14,21) | 42 | ASKIP, NORM | BLUE | `\|%%~~~~~~~~~~~ ONE DOLLAR ~~~~~~~~~~~~~%%\|` |
| 24 | *(unnamed)* | L | (15,21) | 42 | ASKIP, NORM | BLUE | `+========================================+` |
| 25 | *(unnamed)* | L | (17,16) | 49 | ASKIP, NORM | TURQUOISE | `Type your User ID and Password, then press ENTER:` |
| 26 | *(unnamed)* | L | (19,29) | 13 | ASKIP, NORM | TURQUOISE | `User ID     :` |
| 27 | `USERID` | **I** | (19,43) | 8 | FSET, IC, NORM, UNPROT | GREEN | *(none — user input)* — HILIGHT=OFF, **cursor (IC)** |
| 28 | *(unnamed)* | L | (19,52) | 0 | ASKIP, NORM | GREEN | *(none — zero-length stopper/attribute terminator for USERID)* |
| 29 | *(unnamed)* | L | (19,52) | 8 | ASKIP, NORM | BLUE | `(8 Char)` |
| 30 | *(unnamed)* | L | (20,29) | 13 | ASKIP, NORM | TURQUOISE | `Password    :` |
| 31 | `PASSWD` | **I** | (20,43) | 8 | DRK, FSET, UNPROT | GREEN | `________` (8 underscores) — HILIGHT=OFF, **dark (non-display)** |
| 32 | *(unnamed)* | L | (20,52) | 0 | ASKIP, NORM | GREEN | *(none — zero-length stopper/attribute terminator for PASSWD)* |
| 33 | *(unnamed)* | L | (20,52) | 8 | ASKIP, NORM | BLUE | `(8 Char)` |
| 34 | *(unnamed)* | L | (20,61) | 1 | DRK, UNPROT | *(default)* | `' '` (single space; dark, unprotected) |
| 35 | *(unnamed)* | L | (20,63) | 0 | ASKIP, NORM | *(default)* | *(none — zero-length stopper)* |
| 36 | `ERRMSG` | O | (23,1) | 78 | ASKIP, BRT, FSET | RED | *(none — dynamic error message, bright red)* |
| 37 | *(unnamed)* | L | (24,1) | 22 | ASKIP, NORM | YELLOW | `ENTER=Sign-on  F3=Exit` |

Total `DFHMDF` field definitions: **37**.
Named fields: **11** (`TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `APPLID`, `SYSID`, `USERID`, `PASSWD`, `ERRMSG`).

---

## Input vs Output Classification

### Input fields (UNPROT — user can type)
| Name | POS | LEN | Notes |
|------|-----|-----|-------|
| `USERID` | (19,43) | 8 | GREEN, NORM, FSET, **IC (initial cursor here)**, HILIGHT=OFF. Visible input. |
| `PASSWD` | (20,43) | 8 | GREEN, **DRK (non-display — typed chars not shown)**, FSET, HILIGHT=OFF. INITIAL `________`. Password masking. |
| *(unnamed, item 34)* | (20,61) | 1 | DRK, UNPROT, default color. INITIAL single space. Hidden 1-char input/spacer; effectively a non-display filler. |

### Output / dynamic fields (named, protected or autoskip)
| Name | POS | LEN | Color | Purpose |
|------|-----|-----|-------|---------|
| `TRNNAME` | (1,8) | 4 | BLUE | Transaction ID (filled at runtime) |
| `TITLE01` | (1,21) | 40 | YELLOW | Title line 1 |
| `CURDATE` | (1,71) | 8 | BLUE | Current date, INITIAL `mm/dd/yy` |
| `PGMNAME` | (2,8) | 8 | BLUE | Program name |
| `TITLE02` | (2,21) | 40 | YELLOW | Title line 2 |
| `CURTIME` | (2,71) | 9 | BLUE | Current time, INITIAL `Ahh:mm:ss` |
| `APPLID` | (3,8) | 8 | BLUE | CICS APPLID |
| `SYSID` | (3,71) | 8 | BLUE | CICS SYSID, INITIAL 8 spaces |
| `ERRMSG` | (23,1) | 78 | RED | Error message, **BRT** (bright) |

### Literal / static text fields
All unnamed `DFHMDF` entries (items 1, 4, 6, 9, 11, 13, 15–26, 28, 29, 30, 32, 33, 34, 35, 37) are static literals or non-display stoppers — rendered exactly as their INITIAL text at their POS. Zero-length fields (LEN=0) are 3270 attribute terminators / stoppers and render no visible characters (they only reset the attribute after the preceding field).

---

## Cursor (IC)

- **Initial cursor position: `USERID` field at POS (19,43).** This is the only field with the `IC` attribute.

---

## Color Summary

| Color | Fields |
|-------|--------|
| BLUE | Labels `Tran :`, `Date :`, `Prog :`, `Time :`, `AppID:`, `SysID:`, the dollar-note ASCII art (rows 7–15), `(8 Char)` hints (x2); dynamic `TRNNAME`, `CURDATE`, `PGMNAME`, `CURTIME`, `APPLID`, `SYSID` |
| YELLOW | `TITLE01`, `TITLE02`, footer `ENTER=Sign-on  F3=Exit` |
| NEUTRAL | Application banner line (row 5): `This is a Credit Card Demo Application for Mainframe Modernization` |
| TURQUOISE | Prompt `Type your User ID...`, `User ID     :`, `Password    :` |
| GREEN | `USERID`, `PASSWD` input fields and their zero-length stoppers |
| RED | `ERRMSG` (bright) |
| *(default)* | Items 34, 35 (no COLOR clause) |

---

## Highlighting / Justify

- `HILIGHT=OFF` explicitly on `USERID` and `PASSWD` (no extended highlight).
- No `JUSTIFY` clause anywhere.
- Intensity: `BRT` only on `ERRMSG`; `DRK` on `PASSWD` and the item-34 spacer; all other named/literal fields are `NORM`.

---

## Byte-for-byte rendering notes

1. Grid is 24 rows x 80 cols, space-filled background.
2. Place each field's INITIAL/literal text starting at its `POS=(row,col)` (1-based row and col), writing `LENGTH` characters (truncate/pad with spaces to LENGTH for fixed output; dynamic fields are blank until filled at runtime).
3. Reserve the cell at `(row, col-1)` as a blank attribute byte for each field (do not overwrite preceding text into it).
4. `PASSWD` is DRK — render as spaces (or masked) on screen even though INITIAL is `________`; typed input must not display.
5. `ERRMSG` row 23 is normally blank (no INITIAL); it is the error line.
6. Footer row 24: `ENTER=Sign-on  F3=Exit` (note two spaces between `Sign-on` and `F3`).
7. Row 5 banner text begins at column 6.
8. The currency ASCII-art box (rows 7–15) is each exactly 42 chars wide, starting at column 21, ending at column 62.
