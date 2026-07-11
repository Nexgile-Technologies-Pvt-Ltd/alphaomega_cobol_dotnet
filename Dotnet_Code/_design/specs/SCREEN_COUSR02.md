# Screen Spec: COUSR02 (Update User)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COUSR02.bms`

## Mapset

| Property | Value |
|---|---|
| Mapset name | `COUSR02` |
| Map name | `COUSR2A` |
| DFHMSD CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` (extended attributes / color + highlight enabled) |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` (12-byte TIOA prefix on the symbolic map) |
| TYPE | `&&SYSPARM` |

## Map: COUSR2A

| Property | Value |
|---|---|
| DFHMDI LINE | `1` |
| DFHMDI COLUMN | `1` |
| SIZE | `(24, 80)` — 24 rows x 80 columns |

Notes on conventions:
- POS is `(row, col)`, both 1-based, as written in BMS. The attribute byte occupies the POS cell; the visible field content begins at the cell after POS (col+1). For a byte-for-byte text renderer, render literal/content starting at the POS column (the attribute byte is non-displayable / shows as a space).
- `ASKIP` = autoskip (protected, cursor skips over). `UNPROT` = unprotected (operator can type). `PROT` would be protected-but-stoppable (none here).
- `NORM` = normal intensity; `BRT` = bright/high intensity; `DRK` = dark (non-display, used for password).
- `FSET` = modified-data-tag set on (field returned on transmit even if unchanged).
- `IC` = insert cursor (initial cursor position).
- `HILIGHT=UNDERLINE` underlines the input field.
- No `JUSTIFY`, `PICIN`, or `PICOUT` clauses appear anywhere in this map.

## Fields (in source order)

Unnamed fields (literals/labels) are shown with name `(filler)`. Named fields are the program-addressable fields.

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | Kind |
|---|------|---------------|-----|-------|-------|---------|-------------------|------|
| 1 | (filler) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | Output literal |
| 2 | TRNNAME | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (protected) |
| 3 | TITLE01 | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (protected) |
| 4 | (filler) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | Output literal |
| 5 | CURDATE | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `mm/dd/yy` | Output (protected) |
| 6 | (filler) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | Output literal |
| 7 | PGMNAME | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (protected) |
| 8 | TITLE02 | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (protected) |
| 9 | (filler) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | Output literal |
| 10 | CURTIME | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `hh:mm:ss` | Output (protected) |
| 11 | (filler) | (4,35) | 11 | ASKIP, BRT | NEUTRAL | — | `Update User` | Output literal (bright) |
| 12 | (filler) | (6,6) | 14 | ASKIP, NORM | GREEN | — | `Enter User ID:` | Output literal |
| 13 | USRIDIN | (6,21) | 8 | FSET, IC, NORM, UNPROT | GREEN | UNDERLINE | (none) | **Input (unprotected); CURSOR (IC)** |
| 14 | (filler) | (6,30) | 0 | ASKIP, NORM | (default) | — | (none) | Stopper / field delimiter |
| 15 | (filler) | (8,6) | 70 | (default = UNPROT, NORM) | YELLOW | — | `**********************************************************************` (70 asterisks) | Output literal |
| 16 | (filler) | (11,6) | 11 | ASKIP, NORM | TURQUOISE | — | `First Name:` | Output literal |
| 17 | FNAME | (11,18) | 20 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | Input (unprotected) |
| 18 | (filler) | (11,39) | 0 | ASKIP, NORM | (default) | — | (none) | Stopper / field delimiter |
| 19 | (filler) | (11,45) | 10 | ASKIP, NORM | TURQUOISE | — | `Last Name:` | Output literal |
| 20 | LNAME | (11,56) | 20 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | Input (unprotected) |
| 21 | (filler) | (11,77) | 0 | ASKIP, NORM | GREEN | — | (none) | Stopper / field delimiter |
| 22 | (filler) | (13,6) | 9 | ASKIP, NORM | TURQUOISE | — | `Password:` | Output literal |
| 23 | PASSWD | (13,16) | 8 | DRK, FSET, UNPROT | GREEN | UNDERLINE | (none) | Input (unprotected, **dark / non-display**) |
| 24 | (filler) | (13,25) | 8 | ASKIP, NORM | BLUE | — | `(8 Char)` | Output literal |
| 25 | (filler) | (15,6) | 11 | ASKIP, NORM | TURQUOISE | — | `User Type: ` (trailing space) | Output literal |
| 26 | USRTYPE | (15,17) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | Input (unprotected) |
| 27 | (filler) | (15,19) | 17 | ASKIP, NORM | BLUE | — | `(A=Admin, U=User)` | Output literal |
| 28 | ERRMSG | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | Output (protected, bright error line) |
| 29 | (filler) | (24,1) | 58 | ASKIP, NORM | YELLOW | — | `ENTER=Fetch  F3=Save&Exit  F4=Clear  F5=Save  F12=Cancel` | Output literal |

DFHMSD TYPE=FINAL / END terminate the mapset.

## Named field summary

Named (program-addressable) fields = 9:
`TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, USRIDIN, FNAME, LNAME, PASSWD, USRTYPE, ERRMSG`

(That list is 12 — see correction below.)

Named fields, full list (12):
1. TRNNAME — output, (1,7), len 4
2. TITLE01 — output, (1,21), len 40
3. CURDATE — output, (1,71), len 8, init `mm/dd/yy`
4. PGMNAME — output, (2,7), len 8
5. TITLE02 — output, (2,21), len 40
6. CURTIME — output, (2,71), len 8, init `hh:mm:ss`
7. USRIDIN — INPUT, (6,21), len 8, **IC (cursor here)**, underline
8. FNAME — INPUT, (11,18), len 20, underline
9. LNAME — INPUT, (11,56), len 20, underline
10. PASSWD — INPUT, (13,16), len 8, **DARK (non-display)**, underline
11. USRTYPE — INPUT, (15,17), len 1, underline
12. ERRMSG — output, (23,1), len 78, bright red error line

## Input vs Output classification

**Input fields (UNPROT — operator types here):**
- `USRIDIN` (6,21) len 8 — user ID to fetch/update. **Has IC → initial cursor lands here.**
- `FNAME` (11,18) len 20 — first name.
- `LNAME` (11,56) len 20 — last name.
- `PASSWD` (13,16) len 8 — password; **DRK** so typed characters are not displayed.
- `USRTYPE` (15,17) len 1 — A=Admin / U=User.

**Output fields (ASKIP/PROT or literals — program writes, operator cannot type):**
- All `(filler)` literals (Tran:, Date:, Prog:, Time:, "Update User", "Enter User ID:", First Name:, Last Name:, Password:, "User Type: ", "(8 Char)", "(A=Admin, U=User)", the 70-asterisk separator line, and the F-key legend line).
- `TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME` — header info populated by the program.
- `ERRMSG` — bright red message line populated by the program.

**Cursor (IC):** `USRIDIN` at (6,21).

## Color / highlight / intensity notes

- Header labels and time/date values: **BLUE**, normal.
- Titles `TITLE01` / `TITLE02`: **YELLOW**, normal.
- Screen heading "Update User" (4,35): **NEUTRAL** color, **BRT** (bright).
- "Enter User ID:" label and all input fields: **GREEN**.
- Field prompt labels "First Name:", "Last Name:", "Password:", "User Type: ": **TURQUOISE**.
- 70-asterisk separator (8,6): **YELLOW**.
- `(8 Char)` and `(A=Admin, U=User)` hint text: **BLUE**.
- `ERRMSG` (23,1): **RED**, **BRT**.
- F-key legend (24,1): **YELLOW**.
- All five input fields use **HILIGHT=UNDERLINE**.
- `PASSWD` is **DRK** (dark / non-display) — typed text suppressed on screen.
- No `JUSTIFY` clauses; no `PICIN`/`PICOUT` clauses anywhere in this map.

## Literal-escape notes (for byte-for-byte rendering)

- The asterisk separator (field #15, POS (8,6), LEN 70) is one continuous run of **70 `*` characters**. In BMS source it is split across a continuation: `'***...***-` then `***...***'`. Concatenated length = 70, matching LENGTH=70.
- The F-key legend (field #29, POS (24,1), LEN 58) source contains `Save&&Exit`. In BMS, `&&` is the escape for a single literal `&`. The rendered text is:
  `ENTER=Fetch  F3=Save&Exit  F4=Clear  F5=Save  F12=Cancel`
  (note the double spaces between options; rendered length = 56 chars left-justified within the 58-byte field, padded with 2 trailing spaces).
- "User Type: " (field #25, POS (15,6), LEN 11) includes a **trailing space** to fill the 11-byte field after "User Type:".
