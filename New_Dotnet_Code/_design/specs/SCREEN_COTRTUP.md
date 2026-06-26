# SCREEN SPEC — COTRTUP (Transaction Type Update)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/bms/COTRTUP.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name (DFHMSD) | `COTRTUP` |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES |
| TYPE | `&&SYSPARM` |

## Map

| Property | Value |
|----------|-------|
| Map name (DFHMDI) | `CTRTUPA` |
| CTRL | FREEKB (free keyboard — auto-unlock keyboard on display) |
| DSATTS | COLOR, HILIGHT, PS, VALIDN |
| MAPATTS | COLOR, HILIGHT, PS, VALIDN |
| SIZE | (24, 80) — 24 rows x 80 cols |

Notes on conventions:
- POS=(row,col) is 1-based; row 1..24, col 1..80.
- In CICS BMS, each field is preceded by a 1-byte attribute byte. The named field's data starts at the POS column; the attribute byte occupies the column immediately before POS (POS col - 1). Effective rendered start column for the field text/data is the POS column.
- `LENGTH=0` fields are "stopper" / attribute-only fields (no data, no name): they place a fresh attribute byte at POS to terminate the preceding unprotected input field so the renderer knows where the input field visually ends.
- Default for unspecified ATTRB protection in these defs: fields without ASKIP/UNPROT explicit are PROT (protected) unless stated. Fields with explicit UNPROT are input.

---

## Fields in screen order

### 1. Label "Tran:" (unnamed literal)
- Name: (none — literal label)
- POS: (1,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM (autoskip, normal intensity → protected, skip)
- COLOR: BLUE
- INITIAL: `Tran:`
- Type: OUTPUT (literal/protected)

### 2. TRNNAME
- Name: `TRNNAME`
- POS: (1,7)
- LENGTH: 4
- ATTRB: ASKIP, FSET, NORM (autoskip/protected, FSET = modified-data-tag preset on so field is always returned)
- COLOR: BLUE
- INITIAL: (none)
- Type: OUTPUT (protected; program populates transaction id). FSET present.

### 3. TITLE01
- Name: `TITLE01`
- POS: (1,21)
- LENGTH: 40
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: YELLOW
- INITIAL: (none)
- Type: OUTPUT (protected; program sets title line 1)

### 4. Label "Date:" (unnamed literal)
- Name: (none — literal label)
- POS: (1,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: `Date:`
- Type: OUTPUT (literal/protected)

### 5. CURDATE
- Name: `CURDATE`
- POS: (1,71)
- LENGTH: 8
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: `mm/dd/yy`
- Type: OUTPUT (protected; program sets current date)

### 6. Label "Prog:" (unnamed literal)
- Name: (none — literal label)
- POS: (2,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: `Prog:`
- Type: OUTPUT (literal/protected)

### 7. PGMNAME
- Name: `PGMNAME`
- POS: (2,7)
- LENGTH: 8
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: (none)
- Type: OUTPUT (protected; program sets program name)

### 8. TITLE02
- Name: `TITLE02`
- POS: (2,21)
- LENGTH: 40
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: YELLOW
- INITIAL: (none)
- Type: OUTPUT (protected; program sets title line 2)

### 9. Label "Time:" (unnamed literal)
- Name: (none — literal label)
- POS: (2,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: `Time:`
- Type: OUTPUT (literal/protected)

### 10. CURTIME
- Name: `CURTIME`
- POS: (2,71)
- LENGTH: 8
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: BLUE
- INITIAL: `hh:mm:ss`
- Type: OUTPUT (protected; program sets current time)

### 11. Heading "Maintain Transaction Type" (unnamed literal)
- Name: (none — literal label)
- POS: (7,28)
- LENGTH: 25
- ATTRB: (none specified — defaults to protected, NORM; no ASKIP given)
- COLOR: NEUTRAL
- INITIAL: `Maintain Transaction Type`
- Type: OUTPUT (literal/protected, screen heading)

### 12. Label "Transaction Type  :" (unnamed literal)
- Name: (none — literal label)
- POS: (12,4)
- LENGTH: 19
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: TURQUOISE
- INITIAL: `Transaction Type  :`
- Type: OUTPUT (literal/protected)

### 13. TRTYPCD  ← INPUT, CURSOR (IC)
- Name: `TRTYPCD`
- POS: (12,26)
- LENGTH: 2
- ATTRB: IC, UNPROT (Insert Cursor = initial cursor position here; UNPROT = unprotected input field)
- HILIGHT: UNDERLINE
- COLOR: (none specified — default screen/terminal default color)
- INITIAL: (none)
- Type: INPUT (unprotected). **Cursor (IC) lands here.** User enters the 2-char transaction type code.

### 14. Stopper for TRTYPCD (unnamed, LENGTH=0)
- Name: (none)
- POS: (12,29)
- LENGTH: 0
- ATTRB: (none — attribute-only stopper field)
- COLOR: (none)
- INITIAL: (none)
- Type: attribute byte / field terminator (ends the TRTYPCD input field). Visually: places attribute at col 29 on row 12 so the input region for TRTYPCD spans cols 26–28 (2 data chars + autoskip boundary).

### 15. Label "Description       :" (unnamed literal)
- Name: (none — literal label)
- POS: (14,4)
- LENGTH: 19
- ATTRB: (none specified — defaults to protected, NORM; no ASKIP given)
- COLOR: TURQUOISE
- INITIAL: `Description       :`
- Type: OUTPUT (literal/protected)

### 16. TRTYDSC  ← INPUT
- Name: `TRTYDSC`
- POS: (14,26)
- LENGTH: 50
- ATTRB: UNPROT (unprotected input field)
- HILIGHT: UNDERLINE
- COLOR: (none specified — default color)
- INITIAL: (none)
- Type: INPUT (unprotected). User enters the transaction type description (up to 50 chars).

### 17. Stopper for TRTYDSC (unnamed, LENGTH=0)
- Name: (none)
- POS: (14,77)
- LENGTH: 0
- ATTRB: (none — attribute-only stopper field)
- COLOR: (none)
- INITIAL: (none)
- Type: attribute byte / field terminator (ends TRTYDSC). Input region spans cols 26–76 (50 data chars), attribute at col 77.

### 18. INFOMSG
- Name: `INFOMSG`
- POS: (22,23)
- LENGTH: 45
- ATTRB: ASKIP (protected/skip)
- HILIGHT: OFF
- COLOR: NEUTRAL
- INITIAL: (none)
- Type: OUTPUT (protected; program sets informational message)

### 19. Stopper for INFOMSG (unnamed, LENGTH=0)
- Name: (none)
- POS: (22,69)
- LENGTH: 0
- ATTRB: (none — attribute-only stopper field)
- COLOR: (none)
- INITIAL: (none)
- Type: attribute byte / field terminator (ends INFOMSG, attribute at col 69; message region cols 23–67).

### 20. ERRMSG
- Name: `ERRMSG`
- POS: (23,1)
- LENGTH: 78
- ATTRB: ASKIP, BRT, FSET (protected/skip, BRT = bright/high intensity, FSET = modified-tag preset)
- COLOR: RED
- INITIAL: (none)
- Type: OUTPUT (protected; program sets error message, displayed bright red)

### 21. FKEYS
- Name: `FKEYS`
- POS: (24,1)
- LENGTH: 21
- ATTRB: ASKIP, NORM (protected/skip)
- COLOR: YELLOW
- INITIAL: `ENTER=Process F3=Exit`
- Type: OUTPUT (literal/protected, function-key legend)

### 22. FKEY04
- Name: `FKEY04`
- POS: (24,23)
- LENGTH: 9
- ATTRB: ASKIP, DRK (protected/skip, DRK = dark/non-display — hidden unless program sets NORM/BRT)
- COLOR: YELLOW
- INITIAL: `F4=Delete`
- Type: OUTPUT (protected, initially dark/hidden; conditionally shown by program)

### 23. FKEY05
- Name: `FKEY05`
- POS: (24,33)
- LENGTH: 8
- ATTRB: ASKIP, DRK (protected/skip, dark/non-display)
- COLOR: YELLOW
- INITIAL: `F5=Save`
- Type: OUTPUT (protected, initially dark/hidden; conditionally shown)

### 24. FKEY06
- Name: `FKEY06`
- POS: (24,43)
- LENGTH: 6
- ATTRB: ASKIP, DRK (protected/skip, dark/non-display)
- COLOR: YELLOW
- INITIAL: `F6=Add`
- Type: OUTPUT (protected, initially dark/hidden; conditionally shown)

### 25. FKEY12
- Name: `FKEY12`
- POS: (24,69)
- LENGTH: 10
- ATTRB: ASKIP, DRK (protected/skip, dark/non-display)
- COLOR: YELLOW
- INITIAL: `F12=Cancel`
- Type: OUTPUT (protected, initially dark/hidden; conditionally shown)

---

## Input vs Output summary

### Input fields (UNPROT — user can type)
| Name | POS | LEN | HILIGHT | Cursor (IC) |
|------|-----|-----|---------|-------------|
| TRTYPCD | (12,26) | 2 | UNDERLINE | YES (IC) |
| TRTYDSC | (14,26) | 50 | UNDERLINE | no |

### Cursor (IC) field
- `TRTYPCD` at POS (12,26) — initial cursor position when the map is displayed.

### Output / protected fields (literals + program-populated)
- Literals: "Tran:" (1,1), "Date:" (1,65), "Prog:" (2,1), "Time:" (2,65), "Maintain Transaction Type" (7,28), "Transaction Type  :" (12,4), "Description       :" (14,4), FKEYS legend (24,1), FKEY04/05/06/12.
- Program-populated: TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, INFOMSG, ERRMSG.

---

## HILIGHT / Justify / Color notes
- HILIGHT=UNDERLINE on both input fields (`TRTYPCD`, `TRTYDSC`) — render input cells with an underline style.
- HILIGHT=OFF on `INFOMSG` (explicitly no highlight).
- No JUSTIFY (left/right) attributes specified on any field — all default to left-justified.
- No PICIN / PICOUT clauses present on any field in this map.
- Colors used: BLUE (header labels/values), YELLOW (titles + function keys), TURQUOISE (form field labels), NEUTRAL (heading + INFOMSG), RED (ERRMSG). TRTYPCD/TRTYDSC have no explicit COLOR (terminal default, typically green/neutral).
- Intensity: most NORM; ERRMSG is BRT (bright); FKEY04/05/06/12 are DRK (dark/non-display until enabled); IC on TRTYPCD.

## Renderer reproduction checklist (byte-for-byte text)
- Grid: 24 rows x 80 cols, all spaces by default.
- Place each literal INITIAL string starting at its POS column on its POS row (1-based), occupying exactly LENGTH columns.
- Reserve LENGTH columns for program-populated fields (blank in the static template).
- Input fields TRTYPCD (12,26..27) and TRTYDSC (14,26..75) shown as underline placeholders; LENGTH=0 stoppers mark their right boundary (col 29 / col 77 respectively) — these consume no visible character but reset attributes.
- Initial cursor at row 12, col 26 (TRTYPCD).
