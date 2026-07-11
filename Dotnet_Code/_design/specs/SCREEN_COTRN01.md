# Screen Spec: COTRN01 (View Transaction)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COTRN01.bms`

## Mapset

- **Mapset name:** `COTRN01`
- **DFHMSD options:** `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `LANG=COBOL`, `MODE=INOUT`, `STORAGE=AUTO`, `TIOAPFX=YES`, `TYPE=&&SYSPARM`
- **Map name:** `COTRN1A`
- **DFHMDI options:** `COLUMN=1`, `LINE=1`, `SIZE=(24,80)`
- **Screen size:** 24 rows x 80 columns

### Conventions
- POS=(row,col) is 1-based, matching BMS (row 1..24, col 1..80).
- ASKIP = autoskip protected field (cursor skips over it); UNPROT = unprotected input field.
- NORM = normal intensity; BRT = bright/high intensity.
- FSET = modified-data-tag set on (field transmitted on next read).
- IC = insert cursor (initial cursor position).
- "Stopper" fields with `LENGTH=0` are zero-length attribute-byte placeholders used to terminate the preceding field's display region. They occupy one screen position (the attribute byte) and render as a single space/break; they are unnamed and carry no data.

---

## Fields (in BMS order)

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | Kind |
|---|------|-----------|-----|-------|-------|---------|-------------------|------|
| 1 | (label) | (1,1) | 5 | ASKIP, NORM | BLUE | - | `Tran:` | Output literal |
| 2 | TRNNAME | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | - | (none) | Output (protected) |
| 3 | TITLE01 | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | - | (none) | Output (protected) |
| 4 | (label) | (1,65) | 5 | ASKIP, NORM | BLUE | - | `Date:` | Output literal |
| 5 | CURDATE | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | - | `mm/dd/yy` | Output (protected) |
| 6 | (label) | (2,1) | 5 | ASKIP, NORM | BLUE | - | `Prog:` | Output literal |
| 7 | PGMNAME | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | - | (none) | Output (protected) |
| 8 | TITLE02 | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | - | (none) | Output (protected) |
| 9 | (label) | (2,65) | 5 | ASKIP, NORM | BLUE | - | `Time:` | Output literal |
| 10 | CURTIME | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | - | `hh:mm:ss` | Output (protected) |
| 11 | (label) | (4,30) | 16 | ASKIP, BRT | NEUTRAL | - | `View Transaction` | Output literal (bright) |
| 12 | (label) | (6,6) | 14 | ASKIP, NORM | TURQUOISE | - | `Enter Tran ID:` | Output literal |
| 13 | TRNIDIN | (6,21) | 16 | FSET, IC, NORM, UNPROT | GREEN | UNDERLINE | `' '` (single space) | **Input (unprotected)** — cursor (IC) |
| 14 | (stopper) | (6,38) | 0 | ASKIP, NORM | (default) | - | (none) | Field stopper |
| 15 | (label) | (8,6) | 70 | ASKIP, NORM | NEUTRAL | - | `----------------------------------------------------------------------` (70 dashes) | Output literal (divider) |
| 16 | (label) | (10,6) | 15 | ASKIP, NORM | TURQUOISE | - | `Transaction ID:` | Output literal |
| 17 | TRNID | (10,22) | 16 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 18 | (stopper) | (10,39) | 0 | ASKIP, NORM | (default) | - | (none) | Field stopper |
| 19 | (label) | (10,45) | 12 | ASKIP, NORM | TURQUOISE | - | `Card Number:` | Output literal |
| 20 | CARDNUM | (10,58) | 16 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 21 | (stopper) | (10,75) | 0 | ASKIP, NORM | GREEN | - | (none) | Field stopper |
| 22 | (label) | (12,6) | 8 | ASKIP, NORM | TURQUOISE | - | `Type CD:` | Output literal |
| 23 | TTYPCD | (12,15) | 2 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 24 | (stopper) | (12,18) | 0 | (default) | (default) | - | (none) | Field stopper |
| 25 | (label) | (12,23) | 12 | ASKIP, NORM | TURQUOISE | - | `Category CD:` | Output literal |
| 26 | TCATCD | (12,36) | 4 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 27 | (stopper) | (12,41) | 0 | (default) | (default) | - | (none) | Field stopper |
| 28 | (label) | (12,46) | 7 | ASKIP, NORM | TURQUOISE | - | `Source:` | Output literal |
| 29 | TRNSRC | (12,54) | 10 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 30 | (stopper) | (12,65) | 0 | (default) | (default) | - | (none) | Field stopper |
| 31 | (label) | (14,6) | 12 | ASKIP, NORM | TURQUOISE | - | `Description:` | Output literal |
| 32 | TDESC | (14,19) | 60 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 33 | (stopper) | (14,80) | 0 | (default) | (default) | - | (none) | Field stopper |
| 34 | (label) | (16,6) | 7 | ASKIP, NORM | TURQUOISE | - | `Amount:` | Output literal |
| 35 | TRNAMT | (16,14) | 12 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 36 | (stopper) | (16,27) | 0 | (default) | (default) | - | (none) | Field stopper |
| 37 | (label) | (16,31) | 10 | ASKIP, NORM | TURQUOISE | - | `Orig Date:` | Output literal |
| 38 | TORIGDT | (16,42) | 10 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 39 | (stopper) | (16,53) | 0 | (default) | (default) | - | (none) | Field stopper |
| 40 | (label) | (16,57) | 10 | ASKIP, NORM | TURQUOISE | - | `Proc Date:` | Output literal |
| 41 | TPROCDT | (16,68) | 10 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 42 | (stopper) | (16,79) | 0 | (default) | (default) | - | (none) | Field stopper |
| 43 | (label) | (18,6) | 12 | ASKIP, NORM | TURQUOISE | - | `Merchant ID:` | Output literal |
| 44 | MID | (18,19) | 9 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 45 | (stopper) | (18,29) | 0 | (default) | (default) | - | (none) | Field stopper |
| 46 | (label) | (18,33) | 14 | ASKIP, NORM | TURQUOISE | - | `Merchant Name:` | Output literal |
| 47 | MNAME | (18,48) | 30 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 48 | (stopper) | (18,79) | 0 | (default) | (default) | - | (none) | Field stopper |
| 49 | (label) | (20,6) | 14 | ASKIP, NORM | TURQUOISE | - | `Merchant City:` | Output literal |
| 50 | MCITY | (20,21) | 25 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 51 | (stopper) | (20,47) | 0 | (default) | (default) | - | (none) | Field stopper |
| 52 | (label) | (20,53) | 13 | ASKIP, NORM | TURQUOISE | - | `Merchant Zip:` | Output literal |
| 53 | MZIP | (20,67) | 10 | ASKIP, NORM | BLUE | - | `' '` (single space) | Output (protected) |
| 54 | (stopper) | (20,78) | 0 | (default) | (default) | - | (none) | Field stopper |
| 55 | ERRMSG | (23,1) | 78 | ASKIP, BRT, FSET | RED | - | (none) | Output (protected, bright) — error message line |
| 56 | (label) | (24,1) | 47 | ASKIP, NORM | YELLOW | - | `ENTER=Fetch  F3=Back  F4=Clear  F5=Browse Tran.` | Output literal (function-key legend) |

---

## Named fields (12)

| Name | POS (r,c) | LEN | Direction | Notes |
|------|-----------|-----|-----------|-------|
| TRNNAME | (1,7) | 4 | Output | Transaction (CICS trans-id) name, header |
| TITLE01 | (1,21) | 40 | Output | App title line 1, yellow |
| CURDATE | (1,71) | 8 | Output | Current date `mm/dd/yy`, header |
| PGMNAME | (2,7) | 8 | Output | Program name, header |
| TITLE02 | (2,21) | 40 | Output | App title line 2, yellow |
| CURTIME | (2,71) | 8 | Output | Current time `hh:mm:ss`, header |
| TRNIDIN | (6,21) | 16 | **Input** | The ONLY unprotected/input field. Has IC (cursor) + FSET + HILIGHT=UNDERLINE, GREEN |
| TRNID | (10,22) | 16 | Output | Returned transaction ID (display) |
| CARDNUM | (10,58) | 16 | Output | Card number (display) |
| TTYPCD | (12,15) | 2 | Output | Transaction type code |
| TCATCD | (12,36) | 4 | Output | Transaction category code |
| TRNSRC | (12,54) | 10 | Output | Transaction source |
| TDESC | (14,19) | 60 | Output | Description |
| TRNAMT | (16,14) | 12 | Output | Amount |
| TORIGDT | (16,42) | 10 | Output | Original date |
| TPROCDT | (16,68) | 10 | Output | Process date |
| MID | (18,19) | 9 | Output | Merchant ID |
| MNAME | (18,48) | 30 | Output | Merchant name |
| MCITY | (20,21) | 25 | Output | Merchant city |
| MZIP | (20,67) | 10 | Output | Merchant zip |
| ERRMSG | (23,1) | 78 | Output | Error message line, RED + BRT |

> Note: the "named fields" count above is 21 rows in the convenience table, but the count of distinct *named* DFHMDF entries in the BMS is **21**. See field-count note below.

---

## Input vs Output summary

- **Input (unprotected) fields:** exactly ONE — `TRNIDIN` at (6,21), length 16. It carries `UNPROT`, `FSET`, `IC` (cursor), color GREEN, `HILIGHT=UNDERLINE`, initial single space.
- **Cursor (IC):** `TRNIDIN` (6,21).
- **All other named fields are protected** (ASKIP): they are display/output fields populated by the program (TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, TRNID, CARDNUM, TTYPCD, TCATCD, TRNSRC, TDESC, TRNAMT, TORIGDT, TPROCDT, MID, MNAME, MCITY, MZIP, ERRMSG).
- **Literals (unnamed ASKIP labels):** `Tran:`, `Date:`, `Prog:`, `Time:`, `View Transaction` (bright), `Enter Tran ID:`, divider of 70 dashes, plus the turquoise field captions, and the F-key legend on row 24.

## PICIN / PICOUT
- None. No `PICIN` or `PICOUT` clauses are present in this map; all fields use plain `LENGTH=` with character display.

## HILIGHT / Justify
- **HILIGHT:** only `TRNIDIN` uses `HILIGHT=UNDERLINE`. No other field has HILIGHT.
- **Justify:** no `JUSTIFY` clauses present anywhere in the map.

## Colors used
- BLUE: header labels and data display fields
- YELLOW: TITLE01, TITLE02, row-24 F-key legend
- NEUTRAL: `View Transaction` (bright), 70-dash divider
- TURQUOISE: field captions ("Enter Tran ID:", "Transaction ID:", "Card Number:", "Type CD:", etc.)
- GREEN: TRNIDIN input field (and one zero-length stopper at (10,75))
- RED: ERRMSG (bright)

## Notable layout details for byte-for-byte rendering
- Row 1: `Tran:` (1,1) | TRNNAME (1,7,4) | TITLE01 (1,21,40, yellow) | `Date:` (1,65) | CURDATE (1,71,8)=`mm/dd/yy`.
- Row 2: `Prog:` (2,1) | PGMNAME (2,7,8) | TITLE02 (2,21,40, yellow) | `Time:` (2,65) | CURTIME (2,71,8)=`hh:mm:ss`.
- Row 4: `View Transaction` centered-ish at (4,30), BRT, NEUTRAL.
- Row 6: `Enter Tran ID:` (6,6) | TRNIDIN input (6,21,16, GREEN, UNDERLINE, IC) | stopper (6,38).
- Row 8: divider of exactly 70 `-` characters at (8,6).
- Row 10: `Transaction ID:` (10,6) | TRNID (10,22,16) | `Card Number:` (10,45) | CARDNUM (10,58,16).
- Row 12: `Type CD:` (12,6) | TTYPCD (12,15,2) | `Category CD:` (12,23) | TCATCD (12,36,4) | `Source:` (12,46) | TRNSRC (12,54,10).
- Row 14: `Description:` (14,6) | TDESC (14,19,60).
- Row 16: `Amount:` (16,6) | TRNAMT (16,14,12) | `Orig Date:` (16,31) | TORIGDT (16,42,10) | `Proc Date:` (16,57) | TPROCDT (16,68,10).
- Row 18: `Merchant ID:` (18,6) | MID (18,19,9) | `Merchant Name:` (18,33) | MNAME (18,48,30).
- Row 20: `Merchant City:` (20,6) | MCITY (20,21,25) | `Merchant Zip:` (20,53) | MZIP (20,67,10).
- Row 23: ERRMSG (23,1,78), RED, BRT.
- Row 24: F-key legend (24,1,47) = `ENTER=Fetch  F3=Back  F4=Clear  F5=Browse Tran.`, YELLOW.

> Note on the 70-dash divider: in BMS the INITIAL string is split across source lines (lines 98-99) but concatenates to a single literal of 70 `-` characters, matching LENGTH=70.

> Note on row-24 legend literal: split across BMS lines 267-268; concatenates to `ENTER=Fetch  F3=Back  F4=Clear  F5=Browse Tran.` (47 chars incl. trailing period), matching LENGTH=47. Double spaces separate each key group as shown.
