# SCREEN SPEC — CORPT00 (Transaction Reports)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/CORPT00.bms`
Comment header says "Main Menu Screen" but the actual screen is the **Transaction Reports** screen.

---

## Mapset

| Property | Value |
|---|---|
| Mapset name | `CORPT00` |
| DFHMSD options | `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `LANG=COBOL`, `MODE=INOUT`, `STORAGE=AUTO`, `TIOAPFX=YES`, `TYPE=&&SYSPARM` |
| Notes | `EXTATT=YES` → extended attributes (color + highlight) enabled. `FREEKB` → keyboard unlocked on display. `ALARM` → terminal alarm sounds. |

## Map

| Property | Value |
|---|---|
| Map name | `CORPT0A` |
| LINE | 1 |
| COLUMN | 1 |
| SIZE | (24 rows, 80 cols) |
| Map-level default justify | none specified (BMS default = left for alpha, right for numeric) |

---

## Field table (in BMS source order)

Notes on conventions:
- POS is `(row, col)`, 1-based, exactly as in BMS. POS marks the **attribute byte**; the first displayable character is at `col+1` on a real 3270. For a text renderer, place literal text starting at POS column (or col+1 if you choose to model the attribute byte as one consumed cell — keep consistent across all maps).
- `LENGTH` is the data length of the field (does not include the attribute byte).
- ASKIP = autoskip (protected, cursor skips over it) → **output/label**. UNPROT = unprotected → **input**. PROT (none here besides ASKIP) .
- NORM = normal intensity; BRT = bright/high intensity; (no DRK/dark fields in this map).
- FSET = modified-data-tag set on (field returned to program even if unchanged).
- IC = insert cursor (initial cursor position).
- NUM = numeric-only input.
- HILIGHT=UNDERLINE applies to all unprotected input fields here.
- Fields with `LENGTH=0` are **stopper/attribute-only** fields (they terminate the preceding input field; no visible data, no name). They are listed but excluded from the named-field count.

| # | Name | POS (r,c) | LEN | ATTRB | TYPE | COLOR | HILIGHT | INITIAL / literal | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | (unnamed) | (1,1) | 5 | ASKIP, NORM | output/label | BLUE | — | `Tran:` | Static label |
| 2 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | output | BLUE | — | (none) | Transaction id, filled by program |
| 3 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | output | YELLOW | — | (none) | Title line 1 |
| 4 | (unnamed) | (1,65) | 5 | ASKIP, NORM | output/label | BLUE | — | `Date:` | Static label |
| 5 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | output | BLUE | — | `mm/dd/yy` | Current date |
| 6 | (unnamed) | (2,1) | 5 | ASKIP, NORM | output/label | BLUE | — | `Prog:` | Static label |
| 7 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | output | BLUE | — | (none) | Program name |
| 8 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | output | YELLOW | — | (none) | Title line 2 |
| 9 | (unnamed) | (2,65) | 5 | ASKIP, NORM | output/label | BLUE | — | `Time:` | Static label |
| 10 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | output | BLUE | — | `hh:mm:ss` | Current time |
| 11 | (unnamed) | (4,30) | 19 | ASKIP, BRT | output/label | NEUTRAL | — | `Transaction Reports` | Screen heading (bright) |
| 12 | `MONTHLY` | (7,10) | 1 | FSET, IC, NORM, UNPROT | **input** | GREEN | UNDERLINE | `' '` (1 space) | **Cursor (IC) starts here.** Selection flag |
| — | (unnamed stopper) | (7,12) | 0 | ASKIP, NORM | stopper | — | — | (none) | Terminates MONTHLY input |
| 13 | (unnamed) | (7,15) | 23 | ASKIP, BRT | output/label | TURQUOISE | — | `Monthly (Current Month)` | Option text |
| 14 | `YEARLY` | (9,10) | 1 | FSET, NORM, UNPROT | **input** | GREEN | UNDERLINE | `' '` (1 space) | Selection flag |
| — | (unnamed stopper) | (9,12) | 0 | ASKIP, NORM | stopper | — | — | (none) | Terminates YEARLY input |
| 15 | (unnamed) | (9,15) | 23 | ASKIP, BRT | output/label | TURQUOISE | — | `Yearly (Current Year)` | Option text |
| 16 | `CUSTOM` | (11,10) | 1 | FSET, NORM, UNPROT | **input** | GREEN | UNDERLINE | `' '` (1 space) | Selection flag |
| — | (unnamed stopper) | (11,12) | 0 | ASKIP, NORM | stopper | — | — | (none) | Terminates CUSTOM input |
| 17 | (unnamed) | (11,15) | 23 | ASKIP, BRT | output/label | TURQUOISE | — | `Custom (Date Range)` | Option text |
| 18 | (unnamed) | (13,15) | 12 | ASKIP, NORM | output/label | TURQUOISE | — | `Start Date :` | Label |
| 19 | `SDTMM` | (13,29) | 2 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'  '` (2 spaces) | Start month |
| 20 | (unnamed) | (13,32) | 1 | ASKIP, NORM | output/label | BLUE | — | `/` | Separator |
| 21 | `SDTDD` | (13,34) | 2 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'  '` (2 spaces) | Start day |
| 22 | (unnamed) | (13,37) | 1 | ASKIP, NORM | output/label | BLUE | — | `/` | Separator |
| 23 | `SDTYYYY` | (13,39) | 4 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'    '` (4 spaces) | Start year |
| — | (unnamed stopper) | (13,44) | 0 | (default: UNPROT) | stopper | — | — | (none) | Terminates SDTYYYY input |
| 24 | (unnamed) | (13,46) | 12 | (default: UNPROT) | output/label | BLUE | — | `(MM/DD/YYYY)` | Format hint |
| 25 | (unnamed) | (14,15) | 12 | ASKIP, NORM | output/label | TURQUOISE | — | `  End Date :` | Label (2 leading spaces) |
| 26 | `EDTMM` | (14,29) | 2 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'  '` (2 spaces) | End month |
| 27 | (unnamed) | (14,32) | 1 | ASKIP, NORM | output/label | BLUE | — | `/` | Separator |
| 28 | `EDTDD` | (14,34) | 2 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'  '` (2 spaces) | End day |
| 29 | (unnamed) | (14,37) | 1 | ASKIP, NORM | output/label | BLUE | — | `/` | Separator |
| 30 | `EDTYYYY` | (14,39) | 4 | FSET, NORM, NUM, UNPROT | **input (numeric)** | GREEN | UNDERLINE | `'    '` (4 spaces) | End year |
| — | (unnamed stopper) | (14,44) | 0 | (default: UNPROT) | stopper | — | — | (none) | Terminates EDTYYYY input |
| 31 | (unnamed) | (14,46) | 12 | (default: UNPROT) | output/label | BLUE | — | `(MM/DD/YYYY)` | Format hint |
| 32 | (unnamed) | (19,6) | 59 | ASKIP, NORM | output/label | TURQUOISE | — | `The Report will be submitted for printing. Please confirm: ` | Continued literal across two source lines (note trailing space) |
| 33 | `CONFIRM` | (19,66) | 1 | FSET, NORM, UNPROT | **input** | GREEN | UNDERLINE | (none) | Y/N confirm flag |
| — | (unnamed stopper) | (19,68) | 0 | (default: UNPROT) | stopper | — | — | (none) | Terminates CONFIRM input |
| 34 | (unnamed) | (19,69) | 5 | ASKIP, NORM | output/label | NEUTRAL | — | `(Y/N)` | Hint |
| 35 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | output | RED | — | (none) | Error message line (bright red) |
| 36 | (unnamed) | (24,1) | 23 | ASKIP, NORM | output/label | YELLOW | — | `ENTER=Continue  F3=Back` | Function-key legend |

---

## Input vs output summary

### Input (unprotected) fields — 8 named
| Name | POS | LEN | Numeric? | Purpose |
|---|---|---|---|---|
| `MONTHLY` | (7,10) | 1 | no | Select Monthly report (flag) |
| `YEARLY` | (9,10) | 1 | no | Select Yearly report (flag) |
| `CUSTOM` | (11,10) | 1 | no | Select Custom date-range report (flag) |
| `SDTMM` | (13,29) | 2 | **yes** | Start date month |
| `SDTDD` | (13,34) | 2 | **yes** | Start date day |
| `SDTYYYY` | (13,39) | 4 | **yes** | Start date year |
| `EDTMM` | (14,29) | 2 | **yes** | End date month |
| `EDTDD` | (14,34) | 2 | **yes** | End date day |
| `EDTYYYY` | (14,39) | 4 | **yes** | End date year |
| `CONFIRM` | (19,66) | 1 | no | Confirm submit (Y/N) |

(10 input fields total — table above lists them all; `SDT*`/`EDT*` are the 6 numeric date sub-fields.)

### Output / program-filled named fields — 6
`TRNNAME` (1,7), `TITLE01` (1,21), `CURDATE` (1,71), `PGMNAME` (2,7), `TITLE02` (2,21), `CURTIME` (2,71), `ERRMSG` (23,1).
(7 named output fields; all `ASKIP` so non-enterable.)

### Cursor (IC)
- **`MONTHLY` at POS (7,10)** is the only field with `IC` → initial cursor position.

---

## Color / highlight notes
- All input fields: `COLOR=GREEN`, `HILIGHT=UNDERLINE`.
- Labels/static text colors: BLUE (header labels, separators, format hints), YELLOW (`TITLE01`, `TITLE02`, F-key legend), TURQUOISE (option descriptions, date labels, confirm prompt), NEUTRAL (white — "Transaction Reports" heading and "(Y/N)"), RED (`ERRMSG`).
- BRT (bright) fields: "Transaction Reports" heading (11), the three option-description lines (13/15/17), and `ERRMSG` (35). All others NORM.
- No DRK/dark fields. No explicit JUSTIFY specified anywhere; numeric fields default right-justify, alpha default left-justify per BMS.

## PICIN / PICOUT
- None present. No `PICIN=` or `PICOUT=` clauses appear in this map; field formatting is by `LENGTH` + `NUM` only. The COBOL symbolic map (copybook) derives PIC from LENGTH/NUM at generation time.

## Byte-for-byte rendering checklist
- Grid is 24 rows × 80 cols.
- Place each literal `INITIAL` text starting at its POS column (apply your consistent attribute-byte offset rule).
- Reproduce the spaces: `MONTHLY`/`YEARLY`/`CUSTOM`/`CONFIRM` show a single blank; `SDT*`/`EDT*` show blanks of their LENGTH.
- Long literal at (19,6) is one continuous 59-char string spanning two BMS source lines: `The Report will be submitted for printing. Please confirm: ` (ends with a trailing space; total 59 chars).
- "  End Date :" at (14,15) and "Start Date :" at (13,15) keep their internal spacing so the colons and the date fields line up at columns 29/32/34/37/39.
