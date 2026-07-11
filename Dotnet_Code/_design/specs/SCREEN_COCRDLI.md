# SCREEN SPEC — COCRDLI (Card Listing Screen)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COCRDLI.bms`
Purpose: List Credit Cards (paged list with per-row select + account/card filter inputs).

---

## Mapset

| Property | Value |
|---|---|
| Mapset name (DFHMSD) | `COCRDLI` |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES |
| TYPE | `&&SYSPARM` |

## Map

| Property | Value |
|---|---|
| Map name (DFHMDI) | `CCRDLIA` |
| CTRL | FREEKB (free keyboard) |
| DSATTS | COLOR, HILIGHT, PS, VALIDN |
| MAPATTS | COLOR, HILIGHT, PS, VALIDN |
| SIZE | (24 rows, 80 cols) |

Notes on conventions:
- POS is `(row, col)` 1-based, matching CICS BMS. Column shown is the attribute-byte position; the visible field text begins at `col+1` on a real 3270. For a byte-for-byte text renderer treat POS as the first character cell of the field content (the typical 1:1 emulation), unless the renderer reserves the attribute byte.
- `ASKIP` = autoskip (protected, cursor skips over). `PROT` = protected (no input). `UNPROT` = unprotected (input field). `NORM` = normal intensity. `BRT` = bright. `DRK` = dark (non-display). `FSET` = modified-data-tag set on. `IC` = insert cursor (initial cursor position). `NORM`/`DRK`/`BRT` control intensity.
- Fields with no name (blank label column on the DFHMDF line) are literal/label "stopper" fields; the LENGTH=0 entries are zero-length attribute-byte stoppers that terminate the preceding data field.

---

## Fields (in source order)

Legend: I/O column — **OUT** = protected/literal output, **IN** = unprotected input, **STOP** = zero-length stopper attribute byte.

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / Literal | I/O |
|---|---|---|---|---|---|---|---|---|
| — | (label) | (1,1) | 5 | ASKIP, NORM | BLUE | — | `Tran:` | OUT |
| 1 | `TRNNAME` | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | — | (none) | OUT |
| 2 | `TITLE01` | (1,21) | 40 | ASKIP, NORM | YELLOW | — | (none) | OUT |
| — | (label) | (1,65) | 5 | ASKIP, NORM | BLUE | — | `Date:` | OUT |
| 3 | `CURDATE` | (1,71) | 8 | ASKIP, NORM | BLUE | — | `mm/dd/yy` | OUT |
| — | (label) | (2,1) | 5 | ASKIP, NORM | BLUE | — | `Prog:` | OUT |
| 4 | `PGMNAME` | (2,7) | 8 | ASKIP, NORM | BLUE | — | (none) | OUT |
| 5 | `TITLE02` | (2,21) | 40 | ASKIP, NORM | YELLOW | — | (none) | OUT |
| — | (label) | (2,65) | 5 | ASKIP, NORM | BLUE | — | `Time:` | OUT |
| 6 | `CURTIME` | (2,71) | 8 | ASKIP, NORM | BLUE | — | `hh:mm:ss` | OUT |
| — | (label) | (4,31) | 17 | (default) | NEUTRAL | — | `List Credit Cards` | OUT |
| — | (label) | (4,70) | 5 | (default) | (default) | — | `Page ` | OUT |
| 7 | `PAGENO` | (4,76) | 3 | (default) | (default) | — | (none) | OUT |
| — | (label) | (6,22) | 19 | ASKIP, NORM | TURQUOISE | — | `Account Number    :` | OUT |
| 8 | `ACCTSID` | (6,44) | 11 | FSET, IC, NORM, UNPROT | GREEN | UNDERLINE | (none) | **IN** (cursor IC) |
| — | (stopper) | (6,56) | 0 | (default) | (default) | — | (none) | STOP |
| — | (label) | (7,22) | 19 | ASKIP, NORM | TURQUOISE | — | `Credit Card Number:` | OUT |
| 9 | `CARDSID` | (7,44) | 16 | FSET, NORM, UNPROT | GREEN | UNDERLINE | (none) | **IN** |
| — | (stopper) | (7,61) | 0 | (default) | (default) | — | (none) | STOP |
| — | (label) | (9,10) | 10 | (default) | NEUTRAL | — | `Select    ` | OUT |
| — | (label) | (9,21) | 14 | (default) | NEUTRAL | — | `Account Number` | OUT |
| — | (label) | (9,45) | 13 | (default) | NEUTRAL | — | ` Card Number ` | OUT |
| — | (label) | (9,66) | 7 | (default) | NEUTRAL | — | `Active ` | OUT |
| — | (label) | (10,10) | 6 | (default) | NEUTRAL | — | `------` | OUT |
| — | (label) | (10,20) | 15 | (default) | NEUTRAL | — | `---------------` | OUT |
| — | (label) | (10,43) | 15 | (default) | NEUTRAL | — | `---------------` | OUT |
| — | (label) | (10,65) | 8 | (default) | NEUTRAL | — | `--------` | OUT |
| 10 | `CRDSEL1` | (11,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 1) |
| — | (stopper) | (11,14) | 0 | (default) | (default) | — | (none) | STOP |
| 11 | `ACCTNO1` | (11,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 1 acct) |
| 12 | `CRDNUM1` | (11,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 1 card) |
| 13 | `CRDSTS1` | (11,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 1 active) |
| 14 | `CRDSEL2` | (12,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 2) |
| — | (stopper) | (12,14) | 0 | (default) | (default) | — | (none) | STOP |
| 15 | `CRDSTP2` | (12,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 2) |
| 16 | `ACCTNO2` | (12,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 2 acct) |
| 17 | `CRDNUM2` | (12,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 2 card) |
| 18 | `CRDSTS2` | (12,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 2 active) |
| 19 | `CRDSEL3` | (13,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 3) |
| — | (stopper) | (13,14) | 0 | (default) | (default) | — | (none) | STOP |
| 20 | `CRDSTP3` | (13,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 3) |
| 21 | `ACCTNO3` | (13,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 3 acct) |
| 22 | `CRDNUM3` | (13,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 3 card) |
| 23 | `CRDSTS3` | (13,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 3 active) |
| 24 | `CRDSEL4` | (14,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 4) |
| — | (stopper) | (14,14) | 0 | (default) | (default) | — | (none) | STOP |
| 25 | `CRDSTP4` | (14,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 4) |
| 26 | `ACCTNO4` | (14,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 4 acct) |
| 27 | `CRDNUM4` | (14,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 4 card) |
| 28 | `CRDSTS4` | (14,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 4 active) |
| 29 | `CRDSEL5` | (15,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 5) |
| — | (stopper) | (15,14) | 0 | (default) | (default) | — | (none) | STOP |
| 30 | `CRDSTP5` | (15,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 5) |
| 31 | `ACCTNO5` | (15,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 5 acct) |
| 32 | `CRDNUM5` | (15,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 5 card) |
| 33 | `CRDSTS5` | (15,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 5 active) |
| 34 | `CRDSEL6` | (16,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 6) |
| — | (stopper) | (16,14) | 0 | (default) | (default) | — | (none) | STOP |
| 35 | `CRDSTP6` | (16,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 6) |
| 36 | `ACCTNO6` | (16,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 6 acct) |
| 37 | `CRDNUM6` | (16,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 6 card) |
| 38 | `CRDSTS6` | (16,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 6 active) |
| 39 | `CRDSEL7` | (17,12) | 1 | FSET, NORM, PROT | DEFAULT | UNDERLINE | (none) | OUT (select marker, row 7) |
| — | (stopper) | (17,14) | 0 | (default) | (default) | — | (none) | STOP |
| 40 | `CRDSTP7` | (17,14) | 1 | ASKIP, DRK, FSET | DEFAULT | OFF | (none) | OUT (hidden/dark stopper, row 7) |
| 41 | `ACCTNO7` | (17,22) | 11 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 7 acct) |
| 42 | `CRDNUM7` | (17,43) | 16 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 7 card) |
| 43 | `CRDSTS7` | (17,67) | 1 | NORM, PROT | DEFAULT | OFF | (none) | OUT (row 7 active) |
| 44 | `INFOMSG` | (20,19) | 45 | PROT | NEUTRAL | OFF | (none) | OUT (info message) |
| — | (stopper) | (20,65) | 0 | (default) | (default) | — | (none) | STOP |
| 45 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | — | (none) | OUT (error message, bright) |
| — | (label) | (24,1) | 78 | ASKIP, NORM | TURQUOISE | — | `  F3=Exit F7=Backward  F8=Forward` | OUT |

---

## Input vs Output summary

- **Input (unprotected) fields:** `ACCTSID` (6,44 len 11), `CARDSID` (7,44 len 16). These are the account-number and card-number filter entry fields.
- **Cursor (IC) field:** `ACCTSID` at POS (6,44) — initial cursor lands here.
- **All other named fields are protected/output** (literals, headers, the 7-row list grid, the per-row select markers `CRDSELn`, the dark stoppers `CRDSTPn`, the info and error message lines).
- **Per-row select markers** `CRDSEL1`..`CRDSEL7` are `PROT` (output in this map definition) with `UNDERLINE` highlight; the program toggles/reads them via the symbolic map. The `CRDSTPn` (n=2..7) fields are `DRK` (non-display) autoskip stopper fields overlaying the same column 14 as the select-field stopper.

## HILIGHT / Justify notes

- `HILIGHT=UNDERLINE`: `ACCTSID`, `CARDSID`, and all `CRDSEL1..7`.
- `HILIGHT=OFF`: all `ACCTNOn`, `CRDNUMn`, `CRDSTSn`, `CRDSTPn`, plus `INFOMSG`.
- No JUSTIFY / PICIN / PICOUT clauses are present anywhere in this map (no numeric editing or explicit justification specified).

## Color map

- BLUE: `Tran:` label, `TRNNAME`, `Date:` label, `CURDATE`, `Prog:` label, `PGMNAME`, `Time:` label, `CURTIME`.
- YELLOW: `TITLE01`, `TITLE02`.
- NEUTRAL: `List Credit Cards` literal, column headers (`Select`, `Account Number`, ` Card Number `, `Active `), the dashed underline rules, `INFOMSG`.
- TURQUOISE: `Account Number    :` label, `Credit Card Number:` label, footer F-key line.
- GREEN: `ACCTSID`, `CARDSID` (the two input fields).
- RED: `ERRMSG`.
- DEFAULT: `Page ` literal, `PAGENO`, and all grid data fields (`CRDSELn`, `CRDSTPn`, `ACCTNOn`, `CRDNUMn`, `CRDSTSn`).

## Map terminator
- `DFHMSD TYPE=FINAL`
- `END`
