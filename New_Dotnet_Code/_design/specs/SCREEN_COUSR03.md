# SCREEN SPEC — COUSR03 (Delete User)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COUSR03.bms`
Purpose: CardDemo — Delete User screen.

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COUSR03` |
| Map name | `COUSR3A` |
| DFHMSD CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` (extended attributes enabled: color + highlight) |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` |
| TYPE | `&&SYSPARM` |

## Map: COUSR3A

| Property | Value |
|----------|-------|
| DFHMDI COLUMN | 1 |
| DFHMDI LINE | 1 |
| SIZE (rows, cols) | (24, 80) |

Console grid is 24 rows × 80 columns. All POS values below are `(row, col)` 1-based, matching BMS.

> Note on BMS attribute byte: in CICS, each DFHMDF field is preceded by a 1-byte attribute position on the screen. POS=(row,col) is the attribute byte; the visible field data begins at `col+1` and occupies `LENGTH` characters. The renderer should reserve 1 attribute column before each field. Column/length values below are reproduced exactly as in the BMS for byte-for-byte fidelity.

---

## Fields (in BMS order)

Legend for ATTRB:
- ASKIP = autoskip (protected; cursor skips over it). Output/label.
- PROT = protected (not enterable).
- UNPROT = unprotected (user input).
- NORM = normal intensity. BRT = bright/high intensity. DRK = dark (non-display) — none present.
- FSET = modified-data-tag set on transmit.
- IC = insert cursor (initial cursor position).
- NUM = numeric-only — none present.

### 1. Label "Tran:" (unnamed)
- Name: (none / literal)
- POS: (1,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected output)
- COLOR: BLUE
- INITIAL: `Tran:`
- I/O: Output literal
- HILIGHT/Justify: none

### 2. TRNNAME
- Name: `TRNNAME`
- POS: (1,7)
- LENGTH: 4
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- INITIAL: (none)
- I/O: Output (program-supplied transaction id)
- HILIGHT/Justify: none

### 3. TITLE01
- Name: `TITLE01`
- POS: (1,21)
- LENGTH: 40
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: YELLOW
- INITIAL: (none)
- I/O: Output (program-supplied title line 1)
- HILIGHT/Justify: none

### 4. Label "Date:" (unnamed)
- Name: (none / literal)
- POS: (1,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected output)
- COLOR: BLUE
- INITIAL: `Date:`
- I/O: Output literal
- HILIGHT/Justify: none

### 5. CURDATE
- Name: `CURDATE`
- POS: (1,71)
- LENGTH: 8
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- INITIAL: `mm/dd/yy`
- I/O: Output (program-supplied current date)
- HILIGHT/Justify: none

### 6. Label "Prog:" (unnamed)
- Name: (none / literal)
- POS: (2,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected output)
- COLOR: BLUE
- INITIAL: `Prog:`
- I/O: Output literal
- HILIGHT/Justify: none

### 7. PGMNAME
- Name: `PGMNAME`
- POS: (2,7)
- LENGTH: 8
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- INITIAL: (none)
- I/O: Output (program-supplied program name)
- HILIGHT/Justify: none

### 8. TITLE02
- Name: `TITLE02`
- POS: (2,21)
- LENGTH: 40
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: YELLOW
- INITIAL: (none)
- I/O: Output (program-supplied title line 2)
- HILIGHT/Justify: none

### 9. Label "Time:" (unnamed)
- Name: (none / literal)
- POS: (2,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected output)
- COLOR: BLUE
- INITIAL: `Time:`
- I/O: Output literal
- HILIGHT/Justify: none

### 10. CURTIME
- Name: `CURTIME`
- POS: (2,71)
- LENGTH: 8
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- INITIAL: `hh:mm:ss`
- I/O: Output (program-supplied current time)
- HILIGHT/Justify: none

### 11. Heading "Delete User" (unnamed)
- Name: (none / literal)
- POS: (4,35)
- LENGTH: 11
- ATTRB: ASKIP, BRT (protected, bright)
- COLOR: NEUTRAL
- INITIAL: `Delete User`
- I/O: Output literal (screen heading)
- HILIGHT/Justify: none

### 12. Label "Enter User ID:" (unnamed)
- Name: (none / literal)
- POS: (6,6)
- LENGTH: 14
- ATTRB: ASKIP, NORM (protected output)
- COLOR: GREEN
- INITIAL: `Enter User ID:`
- I/O: Output literal
- HILIGHT/Justify: none

### 13. USRIDIN  ← INPUT FIELD, CURSOR (IC)
- Name: `USRIDIN`
- POS: (6,21)
- LENGTH: 8
- ATTRB: FSET, IC, NORM, UNPROT (UNPROTECTED — user input; **initial cursor position**)
- COLOR: GREEN
- HILIGHT: UNDERLINE
- INITIAL: (none)
- I/O: **Input (unprotected)** — User ID to delete
- Justify: none

### 14. Field stopper after USRIDIN (unnamed)
- Name: (none)
- POS: (6,30)
- LENGTH: 0
- ATTRB: ASKIP, NORM (protected) — zero-length attribute byte that terminates/protects the USRIDIN input field
- COLOR: (default; not specified)
- INITIAL: (none)
- I/O: Field delimiter (no visible data)
- HILIGHT/Justify: none

### 15. Separator line of asterisks (unnamed)
- Name: (none / literal)
- POS: (8,6)
- LENGTH: 70
- ATTRB: (none specified — defaults to UNPROT in BMS, but it is a literal decorative line)
- COLOR: YELLOW
- INITIAL: `**********************************************************************` (70 asterisks)
- I/O: Output literal (decorative separator)
- HILIGHT/Justify: none

### 16. Label "First Name:" (unnamed)
- Name: (none / literal)
- POS: (11,6)
- LENGTH: 11
- ATTRB: ASKIP, NORM (protected output)
- COLOR: TURQUOISE
- INITIAL: `First Name:`
- I/O: Output literal
- HILIGHT/Justify: none

### 17. FNAME
- Name: `FNAME`
- POS: (11,18)
- LENGTH: 20
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- HILIGHT: UNDERLINE
- INITIAL: (none)
- I/O: Output (program-supplied first name of fetched user; protected/display-only)
- Justify: none

### 18. Field stopper after FNAME (unnamed)
- Name: (none)
- POS: (11,39)
- LENGTH: 0
- ATTRB: ASKIP, NORM (protected) — zero-length field delimiter
- COLOR: (default; not specified)
- INITIAL: (none)
- I/O: Field delimiter (no visible data)
- HILIGHT/Justify: none

### 19. Label "Last Name:" (unnamed)
- Name: (none / literal)
- POS: (13,6)
- LENGTH: 10
- ATTRB: ASKIP, NORM (protected output)
- COLOR: TURQUOISE
- INITIAL: `Last Name:`
- I/O: Output literal
- HILIGHT/Justify: none

### 20. LNAME
- Name: `LNAME`
- POS: (13,18)
- LENGTH: 20
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- HILIGHT: UNDERLINE
- INITIAL: (none)
- I/O: Output (program-supplied last name of fetched user; protected/display-only)
- Justify: none

### 21. Field stopper after LNAME (unnamed)
- Name: (none)
- POS: (13,39)
- LENGTH: 0
- ATTRB: ASKIP, NORM (protected) — zero-length field delimiter
- COLOR: GREEN
- INITIAL: (none)
- I/O: Field delimiter (no visible data)
- HILIGHT/Justify: none

### 22. Label "User Type: " (unnamed)
- Name: (none / literal)
- POS: (15,6)
- LENGTH: 11
- ATTRB: ASKIP, NORM (protected output)
- COLOR: TURQUOISE
- INITIAL: `User Type: ` (trailing space; 11 chars)
- I/O: Output literal
- HILIGHT/Justify: none

### 23. USRTYPE
- Name: `USRTYPE`
- POS: (15,17)
- LENGTH: 1
- ATTRB: ASKIP, FSET, NORM (protected output)
- COLOR: BLUE
- HILIGHT: UNDERLINE
- INITIAL: (none)
- I/O: Output (program-supplied user type code, A or U; protected/display-only)
- Justify: none

### 24. Label "(A=Admin, U=User)" (unnamed)
- Name: (none / literal)
- POS: (15,19)
- LENGTH: 17
- ATTRB: ASKIP, NORM (protected output)
- COLOR: BLUE
- INITIAL: `(A=Admin, U=User)`
- I/O: Output literal
- HILIGHT/Justify: none

### 25. ERRMSG
- Name: `ERRMSG`
- POS: (23,1)
- LENGTH: 78
- ATTRB: ASKIP, BRT, FSET (protected, bright)
- COLOR: RED
- INITIAL: (none)
- I/O: Output (program-supplied error/status message line)
- HILIGHT/Justify: none

### 26. Function-key legend (unnamed)
- Name: (none / literal)
- POS: (24,1)
- LENGTH: 58
- ATTRB: ASKIP, NORM (protected output)
- COLOR: YELLOW
- INITIAL: `ENTER=Fetch  F3=Back  F4=Clear  F5=Delete`
- I/O: Output literal (function-key legend)
- HILIGHT/Justify: none

---

## Input vs Output summary

| Category | Fields |
|----------|--------|
| Input (UNPROT) | `USRIDIN` (6,21) only |
| Output named (protected) | `TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `FNAME`, `LNAME`, `USRTYPE`, `ERRMSG` |
| Output literals (protected) | Tran:, Date:, Prog:, Time:, "Delete User", "Enter User ID:", asterisk line, "First Name:", "Last Name:", "User Type: ", "(A=Admin, U=User)", function-key legend |
| Field delimiters (LENGTH=0) | (6,30), (11,39), (13,39) |
| **Cursor (IC)** | **`USRIDIN` at (6,21)** |

## Highlight / color notes
- EXTATT=YES, so COLOR and HILIGHT are honored.
- HILIGHT=UNDERLINE on: `USRIDIN` (input), `FNAME`, `LNAME`, `USRTYPE` (the three are protected display fields shown underlined).
- Colors used: BLUE (most labels/data), YELLOW (titles, asterisk line, function-key legend), GREEN ("Enter User ID:" label and USRIDIN input), NEUTRAL ("Delete User" heading), TURQUOISE ("First Name:", "Last Name:", "User Type: " labels), RED (ERRMSG).
- No NUM (numeric), no DRK (dark), no explicit JUSTIFY, no PICIN/PICOUT clauses present anywhere in this map.

## Behavior (CTRL)
- ALARM: sound terminal alarm when map is sent.
- FREEKB: keyboard unlocked on display so the operator can type.

## Field count
Named fields: 11 (`TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `USRIDIN`, `FNAME`, `LNAME`, `USRTYPE`, `ERRMSG`).
