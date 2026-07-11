# SCREEN SPEC: COCRDSL (Card Selection / View Credit Card Detail)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COCRDSL.bms`

## Mapset

| Property | Value |
|---|---|
| Mapset name | `COCRDSL` |
| DFHMSD options | `LANG=COBOL`, `MODE=INOUT`, `STORAGE=AUTO`, `TIOAPFX=YES`, `TYPE=&&SYSPARM` |
| Title | `BMS MAP FOR SELECT CARDS` |

## Map

| Property | Value |
|---|---|
| Map name | `CCRDSLA` |
| DFHMDI options | `CTRL=(FREEKB)` (free keyboard) |
| DSATTS | `COLOR, HILIGHT, PS, VALIDN` |
| MAPATTS | `COLOR, HILIGHT, PS, VALIDN` |
| SIZE | `(24,80)` — 24 rows x 80 columns |

Notes on rendering semantics:
- BMS `POS=(row,col)` marks the **attribute byte** position. The visible field data starts at `col+1`; the renderer should treat the attribute byte cell as a blank separator and place the first data/literal character at `col+1`. (Within this spec, "POS" is given exactly as in BMS.)
- `LENGTH=0` fields are **stopper/attribute-only** fields (no data, no name). They reset attributes after a preceding unprotected field so the rest of the line is protected. They are listed but are not counted as named fields.
- Default intensity `NORM`. `BRT` = bright. No `DRK` (dark) fields present.
- `ASKIP` = autoskip (protected, cursor skips over). `PROT` = protected. `UNPROT` = unprotected (input). `FSET` = field-set / modified-data-tag forced on.
- `IC` = insert cursor (initial cursor position).
- No `PICIN`/`PICOUT` clauses are present anywhere in this map.
- No `JUSTIFY` clauses are present anywhere in this map.

---

## Fields in order (top to bottom, by POS)

### 1. Literal `Tran:` (unnamed)
- Name: (none — literal)
- POS: (1,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM (protected, autoskip, normal intensity)
- COLOR: BLUE
- INITIAL: `Tran:`
- Type: output/literal

### 2. TRNNAME
- Name: `TRNNAME`
- POS: (1,7)
- LENGTH: 4
- ATTRB: ASKIP, FSET, NORM (protected, autoskip, MDT on)
- COLOR: BLUE
- INITIAL: (none)
- Type: output (program-supplied transaction id)

### 3. TITLE01
- Name: `TITLE01`
- POS: (1,21)
- LENGTH: 40
- ATTRB: ASKIP, NORM
- COLOR: YELLOW
- INITIAL: (none)
- Type: output (program-supplied title line 1)

### 4. Literal `Date:` (unnamed)
- Name: (none — literal)
- POS: (1,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: `Date:`
- Type: output/literal

### 5. CURDATE
- Name: `CURDATE`
- POS: (1,71)
- LENGTH: 8
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: `mm/dd/yy`
- Type: output (current date)

### 6. Literal `Prog:` (unnamed)
- Name: (none — literal)
- POS: (2,1)
- LENGTH: 5
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: `Prog:`
- Type: output/literal

### 7. PGMNAME
- Name: `PGMNAME`
- POS: (2,7)
- LENGTH: 8
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: (none)
- Type: output (program name)

### 8. TITLE02
- Name: `TITLE02`
- POS: (2,21)
- LENGTH: 40
- ATTRB: ASKIP, NORM
- COLOR: YELLOW
- INITIAL: (none)
- Type: output (program-supplied title line 2)

### 9. Literal `Time:` (unnamed)
- Name: (none — literal)
- POS: (2,65)
- LENGTH: 5
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: `Time:`
- Type: output/literal

### 10. CURTIME
- Name: `CURTIME`
- POS: (2,71)
- LENGTH: 8
- ATTRB: ASKIP, NORM
- COLOR: BLUE
- INITIAL: `hh:mm:ss`
- Type: output (current time)

### 11. Literal `View Credit Card Detail` (unnamed)
- Name: (none — literal)
- POS: (4,30)
- LENGTH: 23
- ATTRB: (none specified — defaults to ASKIP/protected since no UNPROT and it is a literal; intensity NORM)
- COLOR: NEUTRAL
- INITIAL: `View Credit Card Detail`
- Type: output/literal (screen heading)

### 12. Literal `Account Number    :` (unnamed)
- Name: (none — literal)
- POS: (7,23)
- LENGTH: 19
- ATTRB: ASKIP, NORM
- COLOR: TURQUOISE
- INITIAL: `Account Number    :`
- Type: output/literal (label)

### 13. ACCTSID  — INPUT FIELD, INITIAL CURSOR
- Name: `ACCTSID`
- POS: (7,45)
- LENGTH: 11
- ATTRB: FSET, IC, NORM, UNPROT (unprotected/input, MDT on, **insert cursor here**, normal intensity)
- COLOR: DEFAULT
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: **INPUT** (account number entry). **Cursor (IC) starts here.**

### 14. Stopper (unnamed, LENGTH=0)
- Name: (none)
- POS: (7,57)
- LENGTH: 0
- ATTRB: (default protected attribute byte — terminates ACCTSID input field)
- Type: attribute stopper (not a named field)

### 15. Literal `Card Number       :` (unnamed)
- Name: (none — literal)
- POS: (8,23)
- LENGTH: 19
- ATTRB: ASKIP, NORM
- COLOR: TURQUOISE
- INITIAL: `Card Number       :`
- Type: output/literal (label)

### 16. CARDSID — INPUT FIELD
- Name: `CARDSID`
- POS: (8,45)
- LENGTH: 16
- ATTRB: FSET, NORM, UNPROT (unprotected/input, MDT on, normal intensity)
- COLOR: DEFAULT
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: **INPUT** (card number entry)

### 17. Stopper (unnamed, LENGTH=0)
- Name: (none)
- POS: (8,62)
- LENGTH: 0
- ATTRB: (default protected attribute byte — terminates CARDSID input field)
- Type: attribute stopper (not a named field)

### 18. Literal `Name on card      :` (unnamed)
- Name: (none — literal)
- POS: (11,4)
- LENGTH: 20
- ATTRB: (none specified — defaults to protected; intensity NORM)
- COLOR: TURQUOISE
- INITIAL: `Name on card      :`  (19 chars + trailing space within length 20)
- Type: output/literal (label)

### 19. CRDNAME
- Name: `CRDNAME`
- POS: (11,25)
- LENGTH: 50
- ATTRB: (none specified — defaults to protected output; intensity NORM)
- COLOR: (none specified — map/terminal default)
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: output (cardholder name, display only — protected)

### 20. Stopper (unnamed, LENGTH=0)
- Name: (none)
- POS: (11,76)
- LENGTH: 0
- ATTRB: (default attribute byte — terminates CRDNAME)
- Type: attribute stopper (not a named field)

### 21. Literal `Card Active Y/N   : ` (unnamed)
- Name: (none — literal)
- POS: (13,4)
- LENGTH: 20
- ATTRB: (none specified — defaults to protected; intensity NORM)
- COLOR: TURQUOISE
- INITIAL: `Card Active Y/N   : `  (includes trailing space, length 20)
- Type: output/literal (label)

### 22. CRDSTCD
- Name: `CRDSTCD`
- POS: (13,25)
- LENGTH: 1
- ATTRB: ASKIP (protected, autoskip; intensity NORM by default)
- COLOR: (none specified — map/terminal default)
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: output (card active status code Y/N — protected display)

### 23. Stopper (unnamed, LENGTH=0)
- Name: (none)
- POS: (13,27)
- LENGTH: 0
- ATTRB: (default attribute byte — terminates CRDSTCD)
- Type: attribute stopper (not a named field)

### 24. Literal `Expiry Date       : ` (unnamed)
- Name: (none — literal)
- POS: (15,4)
- LENGTH: 20
- ATTRB: (none specified — defaults to protected; intensity NORM)
- COLOR: TURQUOISE
- INITIAL: `Expiry Date       : `  (includes trailing space, length 20)
- Type: output/literal (label)

### 25. EXPMON
- Name: `EXPMON`
- POS: (15,25)
- LENGTH: 2
- ATTRB: ASKIP (protected, autoskip; intensity NORM by default)
- COLOR: (none specified — map/terminal default)
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: output (expiry month — protected display)

### 26. Literal `/` (unnamed)
- Name: (none — literal)
- POS: (15,28)
- LENGTH: 1
- ATTRB: (none specified — defaults to protected; intensity NORM)
- COLOR: (none specified — map/terminal default)
- INITIAL: `/`
- Type: output/literal (date separator)

### 27. EXPYEAR
- Name: `EXPYEAR`
- POS: (15,30)
- LENGTH: 4
- ATTRB: ASKIP (protected, autoskip; intensity NORM by default)
- COLOR: (none specified — map/terminal default)
- HILIGHT: UNDERLINE
- INITIAL: (none)
- Type: output (expiry year — protected display)

### 28. Stopper (unnamed, LENGTH=0)
- Name: (none)
- POS: (15,35)
- LENGTH: 0
- ATTRB: (default attribute byte — terminates EXPYEAR)
- Type: attribute stopper (not a named field)

### 29. INFOMSG
- Name: `INFOMSG`
- POS: (20,25)
- LENGTH: 40
- ATTRB: PROT (protected; intensity NORM by default)
- COLOR: NEUTRAL
- HILIGHT: OFF
- INITIAL: (none)
- Type: output (informational message line — protected)

### 30. ERRMSG
- Name: `ERRMSG`
- POS: (23,1)
- LENGTH: 80
- ATTRB: ASKIP, BRT, FSET (protected, autoskip, **bright** intensity, MDT on)
- COLOR: RED
- HILIGHT: (none)
- INITIAL: (none)
- Type: output (error message line — protected, bright red)

### 31. FKEYS
- Name: `FKEYS`
- POS: (24,1)
- LENGTH: 75
- ATTRB: ASKIP, NORM (protected, autoskip)
- COLOR: YELLOW
- INITIAL: `ENTER=Search Cards  F3=Exit`
- Type: output/literal (function-key legend footer)

---

## Summary

### Mapset / Map
- Mapset: `COCRDSL`, Map: `CCRDSLA`, SIZE = 24 rows x 80 cols.

### Input (unprotected) fields
| Field | POS | LENGTH | Notes |
|---|---|---|---|
| `ACCTSID` | (7,45) | 11 | UNPROT, FSET, **IC (initial cursor)**, HILIGHT=UNDERLINE |
| `CARDSID` | (8,45) | 16 | UNPROT, FSET, HILIGHT=UNDERLINE |

### Initial cursor (IC)
- `ACCTSID` at POS (7,45).

### Output / protected / literal fields
- Literals: `Tran:` (1,1); `Date:` (1,65); `Prog:` (2,1); `Time:` (2,65); `View Credit Card Detail` (4,30); `Account Number    :` (7,23); `Card Number       :` (8,23); `Name on card      :` (11,4); `Card Active Y/N   : ` (13,4); `Expiry Date       : ` (15,4); `/` (15,28); `ENTER=Search Cards  F3=Exit` (24,1, in FKEYS).
- Program-filled protected fields: `TRNNAME` (1,7); `CURDATE` (1,71); `PGMNAME` (2,7); `CURTIME` (2,71); `TITLE01` (1,21); `TITLE02` (2,21); `CRDNAME` (11,25); `CRDSTCD` (13,25); `EXPMON` (15,25); `EXPYEAR` (15,30); `INFOMSG` (20,25); `ERRMSG` (23,1); `FKEYS` (24,1).

### HILIGHT / intensity
- HILIGHT=UNDERLINE: `ACCTSID`, `CARDSID`, `CRDNAME`, `CRDSTCD`, `EXPMON`, `EXPYEAR`.
- HILIGHT=OFF: `INFOMSG`.
- BRT (bright): `ERRMSG` only. All other fields NORM.

### Justify
- None present.

### PICIN / PICOUT
- None present.

### Colors used
- BLUE: header labels and `TRNNAME`, `PGMNAME`, `CURDATE`, `CURTIME`.
- YELLOW: `TITLE01`, `TITLE02`, `FKEYS`.
- NEUTRAL: `View Credit Card Detail` heading, `INFOMSG`.
- TURQUOISE: field labels (Account Number, Card Number, Name on card, Card Active, Expiry Date).
- DEFAULT: `ACCTSID`, `CARDSID` (input fields).
- RED: `ERRMSG`.
- (map/terminal default, unspecified): `CRDNAME`, `CRDSTCD`, `EXPMON`, `EXPYEAR`, `/` literal.

### Named fields count: 15
`TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, ACCTSID, CARDSID, CRDNAME, CRDSTCD, EXPMON, EXPYEAR, INFOMSG, ERRMSG, FKEYS`
(the listing above also shows 11 unnamed literal DFHMDF entries plus 5 LENGTH=0 stopper entries, none of which are counted as named fields)

Recount of named: TRNNAME(1), TITLE01(2), CURDATE(3), PGMNAME(4), TITLE02(5), CURTIME(6), ACCTSID(7), CARDSID(8), CRDNAME(9), CRDSTCD(10), EXPMON(11), EXPYEAR(12), INFOMSG(13), ERRMSG(14), FKEYS(15) = **15 named fields**.
