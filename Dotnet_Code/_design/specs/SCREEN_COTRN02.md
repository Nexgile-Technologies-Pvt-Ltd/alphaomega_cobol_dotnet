# Screen Spec: COTRN02 — Add Transaction

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COTRN02.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COTRN02` |
| Map name | `COTRN2A` |
| DFHMSD CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` |
| TYPE | `&&SYSPARM` |
| Map origin | LINE=1, COLUMN=1 |
| SIZE (rows, cols) | **(24, 80)** |

Notes on mapset defaults:
- `FREEKB` — keyboard unlocked when map is displayed.
- `ALARM` — terminal alarm sounds when the map is sent.
- `EXTATT=YES` — extended attributes (COLOR, HILIGHT) are honored.

---

## Field List (in source order)

Legend:
- **In/Out**: `OUT` = protected/literal (display only); `IN` = unprotected (operator can type).
- Attribute keywords: `ASKIP` (autoskip/protected, cursor skips), `UNPROT` (unprotected/input), `PROT` (protected, none here), `NORM` (normal intensity), `BRT` (bright/high intensity), `FSET` (modified-data-tag set on send — field returned even if unchanged), `IC` (insert cursor — initial cursor position).
- All fields are `DFHMDF`. Unnamed fields are labels/literals/stoppers. POS is (row, col), 1-based, as in BMS.
- No `PICIN`/`PICOUT` clauses are present anywhere in this map (all fields are plain character fields).
- No `JUSTIFY` clauses are present anywhere in this map.

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | In/Out |
|---|------|---------------|-----|-------|-------|---------|-------------------|--------|
| — | (label) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | OUT |
| 1 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | OUT |
| 2 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | OUT |
| — | (label) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | OUT |
| 3 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `mm/dd/yy` | OUT |
| — | (label) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | OUT |
| 4 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | — | (none) | OUT |
| 5 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | OUT |
| — | (label) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | OUT |
| 6 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `hh:mm:ss` | OUT |
| — | (label) | (4,30) | 15 | ASKIP, BRT | NEUTRAL | — | `Add Transaction` | OUT |
| — | (label) | (6,6) | 13 | ASKIP, NORM | TURQUOISE | — | `Enter Acct #:` | OUT |
| 7 | `ACTIDIN` | (6,21) | 11 | UNPROT, FSET, IC, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (6,33) | 0 | ASKIP, NORM | (none) | — | (none) | OUT |
| — | (label) | (6,37) | 4 | ASKIP, NORM | NEUTRAL | — | `(or)` | OUT |
| — | (label) | (6,46) | 7 | ASKIP, NORM | TURQUOISE | — | `Card #:` | OUT |
| 8 | `CARDNIN` | (6,55) | 16 | UNPROT, FSET, NORM | GREEN | UNDERLINE | (none) | **IN** |
| — | (stopper) | (6,72) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (8,6) | 70 | ASKIP, NORM | NEUTRAL | — | `----------------------------------------------------------------------` (70 dashes) | OUT |
| — | (label) | (10,6) | 8 | ASKIP, NORM | TURQUOISE | — | `Type CD:` | OUT |
| 9 | `TTYPCD` | (10,15) | 2 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (10,18) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (10,23) | 12 | ASKIP, NORM | TURQUOISE | — | `Category CD:` | OUT |
| 10 | `TCATCD` | (10,36) | 4 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (10,41) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (10,46) | 7 | ASKIP, NORM | TURQUOISE | — | `Source:` | OUT |
| 11 | `TRNSRC` | (10,54) | 10 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (10,65) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (12,6) | 12 | ASKIP, NORM | TURQUOISE | — | `Description:` | OUT |
| 12 | `TDESC` | (12,19) | 60 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (12,80) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (14,6) | 7 | ASKIP, NORM | TURQUOISE | — | `Amount:` | OUT |
| 13 | `TRNAMT` | (14,14) | 12 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (14,27) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (14,31) | 10 | ASKIP, NORM | TURQUOISE | — | `Orig Date:` | OUT |
| 14 | `TORIGDT` | (14,42) | 10 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (14,53) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (14,57) | 10 | ASKIP, NORM | TURQUOISE | — | `Proc Date:` | OUT |
| 15 | `TPROCDT` | (14,68) | 10 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (14,79) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (15,13) | 14 | ASKIP, NORM | BLUE | — | `(-99999999.99)` | OUT |
| — | (label) | (15,41) | 12 | ASKIP, NORM | BLUE | — | `(YYYY-MM-DD)` | OUT |
| — | (label) | (15,67) | 12 | ASKIP, NORM | BLUE | — | `(YYYY-MM-DD)` | OUT |
| — | (label) | (16,6) | 12 | ASKIP, NORM | TURQUOISE | — | `Merchant ID:` | OUT |
| 16 | `MID` | (16,19) | 9 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (16,29) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (16,33) | 14 | ASKIP, NORM | TURQUOISE | — | `Merchant Name:` | OUT |
| 17 | `MNAME` | (16,48) | 30 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (16,79) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (18,6) | 14 | ASKIP, NORM | TURQUOISE | — | `Merchant City:` | OUT |
| 18 | `MCITY` | (18,21) | 25 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (18,47) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (18,53) | 13 | ASKIP, NORM | TURQUOISE | — | `Merchant Zip:` | OUT |
| 19 | `MZIP` | (18,67) | 10 | UNPROT, FSET, NORM | GREEN | UNDERLINE | `' '` (single space) | **IN** |
| — | (stopper) | (18,78) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (21,6) | 55 | ASKIP, NORM | TURQUOISE | — | `You are about to add this transaction. Please confirm :` | OUT |
| 20 | `CONFIRM` | (21,63) | 1 | UNPROT, FSET, NORM | GREEN | UNDERLINE | (none) | **IN** |
| — | (stopper) | (21,65) | 0 | (default) | (none) | — | (none) | OUT |
| — | (label) | (21,66) | 5 | ASKIP, NORM | NEUTRAL | — | `(Y/N)` | OUT |
| 21 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | OUT |
| — | (label) | (24,1) | 53 | ASKIP, NORM | YELLOW | — | `ENTER=Continue  F3=Back  F4=Clear  F5=Copy Last Tran.` | OUT |

**Named field count: 21**

---

## Input vs Output Summary

### Input fields (UNPROT — operator types here)
All input fields are `GREEN`, `HILIGHT=UNDERLINE`, `NORM` intensity, `FSET`:

| Field | POS | LEN | Notes |
|-------|-----|-----|-------|
| `ACTIDIN` | (6,21) | 11 | Account number. **Holds the cursor (IC).** Initial = single space. |
| `CARDNIN` | (6,55) | 16 | Card number. No INITIAL clause. |
| `TTYPCD` | (10,15) | 2 | Transaction type code. Initial = single space. |
| `TCATCD` | (10,36) | 4 | Category code. Initial = single space. |
| `TRNSRC` | (10,54) | 10 | Source. Initial = single space. |
| `TDESC` | (12,19) | 60 | Description. Initial = single space. |
| `TRNAMT` | (14,14) | 12 | Amount. Format hint `(-99999999.99)` shown at (15,13). Initial = single space. |
| `TORIGDT` | (14,42) | 10 | Original date. Format hint `(YYYY-MM-DD)` at (15,41). Initial = single space. |
| `TPROCDT` | (14,68) | 10 | Processing date. Format hint `(YYYY-MM-DD)` at (15,67). Initial = single space. |
| `MID` | (16,19) | 9 | Merchant ID. Initial = single space. |
| `MNAME` | (16,48) | 30 | Merchant name. Initial = single space. |
| `MCITY` | (18,21) | 25 | Merchant city. Initial = single space. |
| `MZIP` | (18,67) | 10 | Merchant zip. Initial = single space. |
| `CONFIRM` | (21,63) | 1 | Y/N confirmation. No INITIAL clause. |

### Output / display fields (named, program-populated)
- `TRNNAME` (1,7,4) BLUE — transaction (PF/trans) name.
- `TITLE01` (1,21,40) YELLOW — title line 1.
- `CURDATE` (1,71,8) BLUE — current date, initial `mm/dd/yy`.
- `PGMNAME` (2,7,8) BLUE — program name.
- `TITLE02` (2,21,40) YELLOW — title line 2.
- `CURTIME` (2,71,8) BLUE — current time, initial `hh:mm:ss`.
- `ERRMSG` (23,1,78) RED, BRT — error/status message line.

### Cursor (IC)
- **`ACTIDIN` at POS (6,21)** carries the `IC` attribute. The cursor is positioned there when the map is first displayed.

---

## Rendering notes for the 24x80 text renderer

- BMS fields each consume their declared LENGTH starting at POS column. In 3270 hardware there is also a leading 1-byte attribute position immediately before each field; for a pure text renderer, render the literal/value starting at the POS column and reserve the column at POS-1 as the field's attribute byte (blank) if matching real 3270 spacing.
- **Zero-length fields** (LEN=0) are "stopper"/delimiter fields placed just after each input field (e.g. (6,33) after `ACTIDIN` at (6,21)+11=col 32; (6,72) after `CARDNIN`, etc.). They mark the end of the preceding unprotected field so the operator cannot type past it. They render no visible characters but reset the attribute (treat as protected, no color). They are intentionally listed above to preserve byte-for-byte field boundaries.
- The horizontal rule at (8,6) is exactly **70 dash characters** (`-`), formed in BMS by a continued INITIAL literal split across lines 115-116.
- The confirmation prompt at (21,6) is exactly `You are about to add this transaction. Please confirm :` (55 chars), formed by a continued INITIAL literal split across lines 279-280.
- The function-key footer at (24,1) is exactly `ENTER=Continue  F3=Back  F4=Clear  F5=Copy Last Tran.` (53 chars), continued across lines 301-302. Note the double spaces between ENTER/F3, F3/F4, F4/F5 groups, and a single space inside `Copy Last Tran.`
- Colors used: BLUE (headers), YELLOW (titles + footer), NEUTRAL/white (Add Transaction title, `(or)`, `(Y/N)`, rule), TURQUOISE (field labels), GREEN (input fields), RED (error line).
- HILIGHT=UNDERLINE applies only to the 14 input fields; render as underlined if the target console supports it.
- No PICIN/PICOUT and no JUSTIFY anywhere — all fields are plain left-aligned character fields.
