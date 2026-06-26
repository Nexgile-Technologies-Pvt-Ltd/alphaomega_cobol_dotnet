# SCREEN SPEC: COTRTLI (Transaction Type Listing Screen)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/bms/COTRTLI.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name (DFHMSD) | `COTRTLI` |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES |
| TYPE | `&&SYSPARM` (DSECT/MAP at assembly time) |

## Map

| Property | Value |
|----------|-------|
| Map name (DFHMDI) | `CTRTLIA` |
| CTRL | FREEKB (keyboard freed on display) |
| SIZE | (24, 80) — 24 rows x 80 columns |
| DSATTS | COLOR, HILIGHT, PS, VALIDN |
| MAPATTS | COLOR, HILIGHT, PS, VALIDN |

Notes on conventions used below:
- ROW/COL are 1-based, exactly as in the BMS `POS=(row,col)`.
- In CICS BMS, byte at POS is the attribute byte; the displayed field content begins at COL+1. The renderer must reserve 1 attribute byte before each field, but the spec lists the BMS-declared POS verbatim so the screen reproduces byte-for-byte.
- ASKIP = autoskip (protected, cursor skips over). PROT = protected (no input). UNPROT = unprotected (user input). NORM = normal intensity. BRT = bright/high intensity. FSET = modified-data-tag set on (field returned even if unchanged). IC = insert cursor (initial cursor position). NUM = numeric (none present in this map).
- Fields with no field name and an INITIAL value are static literals (output/decoration). Fields with LENGTH=0 are "stopper"/attribute-only fields used to terminate the preceding field; they have no name and carry no data.
- No PICIN/PICOUT clauses appear anywhere in this map. No JUSTIFY clauses appear. No explicitly NUMERIC fields appear.

---

## All fields in order (top-to-bottom, as declared in BMS)

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | Kind |
|---|------|---------------|-----|-------|-------|---------|-------------------|------|
| - | (literal) | (1,1) | 5 | ASKIP, NORM | BLUE | - | `Tran:` | Output literal |
| 1 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | - | (none) | Output (protected, value set by program; transaction id) |
| 2 | `TITLE01` | (1,21) | 40 | ASKIP, NORM | YELLOW | - | (none) | Output (program-supplied title line 1) |
| - | (literal) | (1,65) | 5 | ASKIP, NORM | BLUE | - | `Date:` | Output literal |
| 3 | `CURDATE` | (1,71) | 8 | ASKIP, NORM | BLUE | - | `mm/dd/yy` | Output (current date) |
| - | (literal) | (2,1) | 5 | ASKIP, NORM | BLUE | - | `Prog:` | Output literal |
| 4 | `PGMNAME` | (2,7) | 8 | ASKIP, NORM | BLUE | - | (none) | Output (program name) |
| 5 | `TITLE02` | (2,21) | 40 | ASKIP, NORM | YELLOW | - | (none) | Output (program-supplied title line 2) |
| - | (literal) | (2,65) | 5 | ASKIP, NORM | BLUE | - | `Time:` | Output literal |
| 6 | `CURTIME` | (2,71) | 8 | ASKIP, NORM | BLUE | - | `hh:mm:ss` | Output (current time) |
| - | (literal) | (4,28) | 25 | (default) | NEUTRAL | - | `Maintain Transaction Type` | Output literal (heading) |
| - | (literal) | (4,70) | 5 | (default) | (default) | - | `Page ` | Output literal |
| 7 | `PAGENO` | (4,76) | 3 | (default) | (default) | - | (none) | Output (page number) |
| - | (literal) | (6,30) | 12 | ASKIP, NORM | TURQUOISE | - | `Type Filter:` | Output literal |
| 8 | `TRTYPE` | (6,44) | 2 | FSET, IC, NORM, UNPROT | GREEN | UNDERLINE | (none) | **INPUT** (type filter) — **CURSOR (IC)** |
| - | (stopper) | (6,47) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| - | (literal) | (8,4) | 19 | ASKIP, NORM | TURQUOISE | - | `Description Filter:` | Output literal |
| 9 | `TRDESC` | (8,25) | 50 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | **INPUT** (description filter) |
| - | (stopper) | (8,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| - | (literal) | (10,4) | 10 | (default) | NEUTRAL | - | `Select    ` (trailing spaces) | Output literal (column header) |
| - | (literal) | (10,16) | 4 | (default) | NEUTRAL | - | `Type` | Output literal (column header) |
| - | (literal) | (10,42) | 11 | (default) | NEUTRAL | - | `Description` | Output literal (column header) |
| - | (literal) | (11,4) | 6 | (default) | NEUTRAL | - | `------` | Output literal (rule) |
| - | (literal) | (11,15) | 5 | (default) | NEUTRAL | - | `-----` | Output literal (rule) |
| - | (literal) | (11,25) | 50 | (default) | NEUTRAL | - | `--------------------------------------------------` (50 dashes) | Output literal (rule) |
| 10 | `TRTSEL1` | (12,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 1 select; PROT — see note) |
| - | (stopper) | (12,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 11 | `TRTTYP1` | (12,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 1 type code) |
| - | (stopper) | (12,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 12 | `TRTYPD1` | (12,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 1 description) |
| - | (stopper) | (12,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 13 | `TRTSEL2` | (13,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 2 select) |
| - | (stopper) | (13,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 14 | `TRTTYP2` | (13,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 2 type code) |
| - | (stopper) | (13,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 15 | `TRTYPD2` | (13,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 2 description) |
| - | (stopper) | (13,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 16 | `TRTSEL3` | (14,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 3 select) |
| - | (stopper) | (14,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 17 | `TRTTYP3` | (14,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 3 type code) |
| - | (stopper) | (14,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 18 | `TRTYPD3` | (14,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 3 description) |
| - | (stopper) | (14,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 19 | `TRTSEL4` | (15,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 4 select) |
| - | (stopper) | (15,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 20 | `TRTTYP4` | (15,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 4 type code) |
| - | (stopper) | (15,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 21 | `TRTYPD4` | (15,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 4 description) |
| - | (stopper) | (15,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 22 | `TRTSEL5` | (16,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 5 select) |
| - | (stopper) | (16,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 23 | `TRTTYP5` | (16,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 5 type code) |
| - | (stopper) | (16,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 24 | `TRTYPD5` | (16,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 5 description) |
| - | (stopper) | (16,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 25 | `TRTSEL6` | (17,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 6 select) |
| - | (stopper) | (17,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 26 | `TRTTYP6` | (17,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 6 type code) |
| - | (stopper) | (17,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 27 | `TRTYPD6` | (17,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 6 description) |
| - | (stopper) | (17,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 28 | `TRTSEL7` | (18,6) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | Output/selectable (row 7 select) |
| - | (stopper) | (18,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 29 | `TRTTYP7` | (18,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 7 type code) |
| - | (stopper) | (18,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 30 | `TRTYPD7` | (18,25) | 50 | FSET, NORM, UNPROT | DEFAULT | OFF | (none) | INPUT/Output (row 7 description) |
| - | (stopper) | (18,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 31 | `TRTSELA` | (19,6) | 1 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output/selectable (row 8 ["A"] select; HILIGHT OFF unlike rows 1-7) |
| - | (stopper) | (19,8) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 32 | `TRTTYPA` | (19,17) | 2 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 8 type code) |
| - | (stopper) | (19,20) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 33 | `TRTDSCA` | (19,25) | 50 | FSET, NORM, PROT | DEFAULT | OFF | (none) | Output (row 8 description; PROT unlike rows 1-7 which are UNPROT) |
| - | (stopper) | (19,76) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 34 | `INFOMSG` | (21,19) | 45 | PROT | NEUTRAL | OFF | (none) | Output (informational message) |
| - | (stopper) | (21,65) | 0 | (default) | (default) | - | (none) | Attribute-only field stop |
| 35 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | - | (none) | Output (error message, bright red) |
| 36 | `BUTNF02` | (24,1) | 7 | ASKIP, NORM | TURQUOISE | - | `F2=Add` | Output literal (PF-key legend) |
| 37 | `BUTNF03` | (24,10) | 7 | ASKIP, NORM | TURQUOISE | - | `F3=Exit` | Output literal (PF-key legend) |
| 38 | `BUTNF07` | (24,19) | 10 | ASKIP, NORM | TURQUOISE | - | `F7=Page Up` | Output literal (PF-key legend) |
| 39 | `BUTNF08` | (24,32) | 10 | ASKIP, NORM | TURQUOISE | - | `F8=Page Dn` | Output literal (PF-key legend) |
| 40 | `BUTNF10` | (24,44) | 8 | ASKIP, NORM | TURQUOISE | - | `F10=Save` | Output literal (PF-key legend) |

(default) = attribute not coded in BMS; CICS applies the default (protected/ASKIP behavior for fields without UNPROT; NORM intensity; default color). The `Page `/`PAGENO`/`Maintain Transaction Type` and the column header/rule literals have no ATTRB coded.

---

## Input vs Output classification

### Unprotected INPUT fields (user can type)
| Name | POS | LEN | Notes |
|------|-----|-----|-------|
| `TRTYPE` | (6,44) | 2 | Type filter. **IC = initial cursor lands here.** UNDERLINE highlight, GREEN. |
| `TRDESC` | (8,25) | 50 | Description filter. UNDERLINE highlight, GREEN. |
| `TRTYPD1` | (12,25) | 50 | Row 1 description (UNPROT). |
| `TRTYPD2` | (13,25) | 50 | Row 2 description (UNPROT). |
| `TRTYPD3` | (14,25) | 50 | Row 3 description (UNPROT). |
| `TRTYPD4` | (15,25) | 50 | Row 4 description (UNPROT). |
| `TRTYPD5` | (16,25) | 50 | Row 5 description (UNPROT). |
| `TRTYPD6` | (17,25) | 50 | Row 6 description (UNPROT). |
| `TRTYPD7` | (18,25) | 50 | Row 7 description (UNPROT). |

> Note: `TRTYPD1`–`TRTYPD7` are coded UNPROT (unprotected) in the BMS, so technically modifiable, while the row-8 equivalent `TRTDSCA` is PROT. In this listing/maintain screen the description cells for rows 1-7 are editable; the select cells (`TRTSELn`/`TRTSELA`) are PROT and used to receive a selection character placed by the program/operator behavior.

### Cursor (IC)
- Initial cursor position: **`TRTYPE` at POS (6,44)** — the only field with the `IC` attribute.

### Protected / literal OUTPUT fields
- Header line literals: `Tran:` (1,1), `Date:` (1,65), `Prog:` (2,1), `Time:` (2,65).
- Program-populated output: `TRNNAME` (1,7), `TITLE01` (1,21), `CURDATE` (1,71), `PGMNAME` (2,7), `TITLE02` (2,21), `CURTIME` (2,71), `PAGENO` (4,76).
- Section heading literal: `Maintain Transaction Type` (4,28), `Page ` (4,70).
- Filter labels: `Type Filter:` (6,30), `Description Filter:` (8,4).
- Column headers: `Select    ` (10,4), `Type` (10,16), `Description` (10,42).
- Rule lines: `------` (11,4), `-----` (11,15), 50-dash line (11,25).
- Grid select/type cells (PROT, program-driven): `TRTSEL1-7`, `TRTSELA`, `TRTTYP1-7`, `TRTTYPA`, and row-8 description `TRTDSCA`.
- Messages: `INFOMSG` (21,19, PROT), `ERRMSG` (23,1, BRT/RED).
- PF-key legend: `BUTNF02` (24,1), `BUTNF03` (24,10), `BUTNF07` (24,19), `BUTNF08` (24,32), `BUTNF10` (24,44).

---

## HILIGHT / Justify summary
- **HILIGHT=UNDERLINE**: `TRTYPE`, `TRDESC`, and all select cells `TRTSEL1`–`TRTSEL7`.
- **HILIGHT=OFF** (explicitly): `TRTTYP1`–`TRTTYP7`, `TRTYPD1`–`TRTYPD7`, `TRTSELA`, `TRTTYPA`, `TRTDSCA`, `INFOMSG`.
- **No HILIGHT coded** (default): all header literals, titles, page fields, column headers, rule lines, `ERRMSG`, and PF-key legend fields.
- **JUSTIFY**: none coded on any field.
- **NUMERIC (NUM)**: none coded on any field.
- **PICIN / PICOUT**: none present anywhere in this map.

## Color summary
- BLUE: `Tran:`, `TRNNAME`, `Date:`, `CURDATE`, `Prog:`, `PGMNAME`, `Time:`, `CURTIME`.
- YELLOW: `TITLE01`, `TITLE02`.
- NEUTRAL: `Maintain Transaction Type`, all column headers, all rule lines, `INFOMSG`.
- TURQUOISE: `Type Filter:`, `Description Filter:`, all PF-key legend buttons (`BUTNF02/03/07/08/10`).
- GREEN: `TRTYPE`, `TRDESC` (the two input filters).
- DEFAULT: all grid cells `TRTSEL*`, `TRTTYP*`, `TRTYPD*`, `TRTDSCA`.
- RED: `ERRMSG`.
- (no COLOR coded): `Page ` literal (4,70) and `PAGENO` (4,76) inherit default.

## Renderer reproduction notes (byte-for-byte)
- Grid occupies rows 12–19 (8 data rows). Columns: select cell at col 6 (1 char), type code at col 17 (2 chars), description at col 25 (50 chars). The BMS reserves an attribute byte before each, so visible data starts one column right of POS in real 3270; for a plain text 24x80 renderer, place the literal/value beginning at the listed POS column to match the design grid.
- The 50-dash rule on row 11 spans the continuation lines 131-132 of the BMS (`'----...---'`) = exactly 50 `-` characters at POS (11,25).
- `Select    ` literal at (10,4) is 10 chars including 6 trailing spaces (LENGTH=10).
- `ERRMSG` is 78 chars wide starting at (23,1), bright RED, used for error display.
