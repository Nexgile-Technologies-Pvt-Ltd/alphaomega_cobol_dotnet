# Screen Spec: COBIL00 (Bill Payment)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COBIL00.bms`

## Mapset

| Property | Value |
|---|---|
| Mapset name | `COBIL00` (DFHMSD) |
| Map name | `COBIL0A` (DFHMDI) |
| Map origin | LINE=1, COLUMN=1 |
| SIZE | (24 rows, 80 cols) |
| Screen title | CardDemo - Main Menu Screen (header comment); functional purpose is **Bill Payment** |

### Mapset-level options (DFHMSD)

| Option | Value | Meaning |
|---|---|---|
| CTRL | (ALARM, FREEKB) | Sound alarm on display; free keyboard after write |
| EXTATT | YES | Extended attributes enabled (COLOR, HILIGHT) |
| LANG | COBOL | Generate COBOL symbolic map |
| MODE | INOUT | Map used for both input and output |
| STORAGE | AUTO | |
| TIOAPFX | YES | 12-byte TIOA prefix (`FILLER PIC X(12)`) precedes symbolic map |
| TYPE | &&SYSPARM | Resolved at assembly (DSECT/MAP) |

Notes on map-level defaults: DFHMDI for `COBIL0A` does not specify DATA, JUSTIFY, or default attributes, so 3270 defaults apply. No field declares JUSTIFY, so all numeric/alphanumeric use default left-justify (alphanumeric) behavior; there is no explicit JUSTIFY/justification override anywhere in this map.

---

## Field Inventory (in source order)

Rows/columns are 1-based as written in BMS POS=(row,col). The attribute byte for each named/visible field occupies the position at POS, and 3270 reserves the attribute byte so the field's displayable data begins at col+1. Field "LENGTH" is the BMS-declared data length (not counting the attribute byte). Fields with LENGTH=0 are "stopper" fields whose only purpose is to set an attribute byte that terminates the preceding field on screen.

Legend for ATTRB:
- **ASKIP** = autoskip + protected (cursor skips over; not enterable)
- **PROT** = protected (not enterable, cursor stops if MDT path) — none explicit here
- **UNPROT** = unprotected (input field)
- **NORM** = normal intensity
- **BRT** = bright / high intensity
- **FSET** = MDT pre-set (field transmitted even if unmodified)
- **IC** = Insert Cursor here (initial cursor position)
- No DRK (dark/non-display) field is present in this map.

### Map COBIL0A — fields

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|---|---|---|---|---|---|---|---|
| 1 | (unnamed label) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | Output / literal |
| 2 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (program-filled, transaction id) |
| 3 | `TITLE01` | (1,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (program-filled title line 1) |
| 4 | (unnamed label) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | Output / literal |
| 5 | `CURDATE` | (1,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `mm/dd/yy` | Output (program-filled current date) |
| 6 | (unnamed label) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | Output / literal |
| 7 | `PGMNAME` | (2,7) | 8 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (program-filled program name) |
| 8 | `TITLE02` | (2,21) | 40 | ASKIP, FSET, NORM | YELLOW | — | (none) | Output (program-filled title line 2) |
| 9 | (unnamed label) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | Output / literal |
| 10 | `CURTIME` | (2,71) | 8 | ASKIP, FSET, NORM | BLUE | — | `hh:mm:ss` | Output (program-filled current time) |
| 11 | (unnamed label) | (4,35) | 12 | ASKIP, BRT | NEUTRAL | — | `Bill Payment` | Output / literal (bright heading) |
| 12 | (unnamed label) | (6,6) | 14 | ASKIP, NORM | GREEN | — | `Enter Acct ID:` | Output / literal |
| 13 | `ACTIDIN` | (6,21) | 11 | FSET, IC, NORM, UNPROT | GREEN | UNDERLINE | (none) | **INPUT** (account id) — **cursor (IC) here** |
| 14 | (unnamed stopper) | (6,33) | 0 | ASKIP, NORM | (default) | — | (none) | Stopper (terminates ACTIDIN field) |
| 15 | (unnamed rule line) | (8,6) | 70 | (none specified → default) | YELLOW | — | `----------------------------------------------------------------------` (70 dashes) | Output / literal separator |
| 16 | (unnamed label) | (11,6) | 25 | ASKIP, NORM | TURQUOISE | — | `Your current balance is: ` (trailing space; 25 chars) | Output / literal |
| 17 | `CURBAL` | (11,32) | 14 | ASKIP, FSET, NORM | BLUE | — | (none) | Output (program-filled balance) |
| 18 | (unnamed stopper) | (11,47) | 0 | (none specified → default) | (default) | — | (none) | Stopper (terminates CURBAL field) |
| 19 | (unnamed label) | (15,6) | 53 | ASKIP, NORM | TURQUOISE | — | `Do you want to pay your balance now. Please confirm: ` (trailing space; 53 chars) | Output / literal |
| 20 | `CONFIRM` | (15,60) | 1 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | **INPUT** (confirm Y/N) |
| 21 | (unnamed stopper) | (15,62) | 0 | (none specified → default) | (default) | — | (none) | Stopper (terminates CONFIRM field) |
| 22 | (unnamed label) | (15,63) | 5 | ASKIP, NORM | NEUTRAL | — | `(Y/N)` | Output / literal |
| 23 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | Output (program-filled error message, bright red) |
| 24 | (unnamed label) | (24,1) | 33 | ASKIP, NORM | YELLOW | — | `ENTER=Continue  F3=Back  F4=Clear` | Output / literal (function-key legend) |

> Field #15 (rule line) literal in BMS is split across two continuation lines:
> `'------------------------------------------------'` + `'-----------------------'` = 48 + 22 = **70 dashes**, matching LENGTH=70.
>
> Field #19 literal in BMS is split: `'Do you want to pay your balance now. Please con-'` + `'firm: '` = the continuation joins as `...Please confirm: ` for a total of 53 characters, matching LENGTH=53.
>
> Field #16 literal `'Your current balance is: '` is 25 characters including the trailing space, matching LENGTH=25.

Full list of named (labelled) DFHMDF fields:
1. `TRNNAME`
2. `TITLE01`
3. `CURDATE`
4. `PGMNAME`
5. `TITLE02`
6. `CURTIME`
7. `ACTIDIN`
8. `CURBAL`
9. `CONFIRM`
10. `ERRMSG`

**Named field count = 10.**

---

## Input vs Output Summary

### Input fields (UNPROT)
| Name | POS | LEN | Notes |
|---|---|---|---|
| `ACTIDIN` | (6,21) | 11 | Account ID entry; GREEN; UNDERLINE highlight; FSET; **IC (initial cursor)** |
| `CONFIRM` | (15,60) | 1 | Y/N confirmation; GREEN; UNDERLINE highlight; FSET |

### Output / literal fields (ASKIP or protected)
All other fields are output/literal:
- Header labels: `Tran:`(1,1), `Date:`(1,65), `Prog:`(2,1), `Time:`(2,65).
- Program-filled output: `TRNNAME`(1,7), `TITLE01`(1,21), `CURDATE`(1,71), `PGMNAME`(2,7), `TITLE02`(2,21), `CURTIME`(2,71), `CURBAL`(11,32), `ERRMSG`(23,1).
- Static literals: `Bill Payment`(4,35, BRT), `Enter Acct ID:`(6,6), 70-dash rule line(8,6), `Your current balance is: `(11,6), `Do you want to pay your balance now. Please confirm: `(15,6), `(Y/N)`(15,63), function-key legend(24,1).
- Stopper fields (LENGTH=0) at (6,33), (11,47), (15,62) — no displayable content; set attribute byte to end the preceding field.

### Cursor (IC)
- Initial cursor position: **`ACTIDIN` at (6,21)** (only field with IC attribute).

### Highlight / Justify
- HILIGHT=UNDERLINE on the two input fields `ACTIDIN` and `CONFIRM`.
- No JUSTIFY clause on any field (defaults apply; alphanumeric left-justified).
- BRT (bright) intensity on `Bill Payment` heading (4,35) and `ERRMSG` (23,1). All other fields NORM.

### PICIN / PICOUT
- No `PICIN` or `PICOUT` clauses are present on any field in this map. (DFHMDF data lengths come from LENGTH only.)

---

## Color Map (3270 base colors used)
| COLOR | Fields |
|---|---|
| BLUE | `Tran:`, `TRNNAME`, `Date:`, `CURDATE`, `Prog:`, `PGMNAME`, `Time:`, `CURTIME`, `CURBAL` |
| YELLOW | `TITLE01`, `TITLE02`, rule line (8,6), function-key legend (24,1) |
| GREEN | `Enter Acct ID:` label, `ACTIDIN`, `CONFIRM` |
| TURQUOISE | `Your current balance is: ` (11,6), `Do you want to pay your balance now...` (15,6) |
| NEUTRAL (white/default) | `Bill Payment` (4,35), `(Y/N)` (15,63) |
| RED | `ERRMSG` (23,1) |
| (default) | stopper fields without explicit COLOR |

---

## Renderer Reproduction Notes (byte-for-byte)
- Grid is 24 rows x 80 cols. Each row index 1..24, col index 1..80.
- For each field, place a 3270 attribute byte at (row, col) (rendered as a single blank/space cell), then the field's content/INITIAL starting at (row, col+1).
- Literals must be written exactly; trailing spaces in `Your current balance is: ` and `Do you want to pay your balance now. Please confirm: ` are significant (lengths 25 and 53 include the trailing space).
- The rule line at (8,6) is exactly 70 `-` characters.
- Program-filled fields (`TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `CURBAL`, `ERRMSG`) are blank in the static map; render their reserved width as spaces unless data is supplied. Sample placeholders `mm/dd/yy` (CURDATE) and `hh:mm:ss` (CURTIME) are declared INITIAL values.
- Input fields `ACTIDIN` (11 cols at 6,22..6,32) and `CONFIRM` (1 col at 15,61) render as underlined entry areas; cursor starts in `ACTIDIN`.
