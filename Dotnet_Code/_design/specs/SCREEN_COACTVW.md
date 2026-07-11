# SCREEN SPEC: COACTVW (Account Viewer)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COACTVW.bms`
Title: `BMS MAP FOR ACCOUNT VIEWING`
Version stamp: `CardDemo_v1.0-70-g193b394-123  Date: 2022-08-22 17:02:42 CDT`

---

## Mapset

| Property | Value |
|----------|-------|
| Mapset name (DFHMSD) | `COACTVW` |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES |
| TYPE | `&&SYSPARM` (DSECT/MAP at assembly time) |

## Map

| Property | Value |
|----------|-------|
| Map name (DFHMDI) | `CACTVWA` |
| CTRL | `FREEKB` (free keyboard after write) |
| DSATTS | COLOR, HILIGHT, PS, VALIDN |
| MAPATTS | COLOR, HILIGHT, PS, VALIDN |
| SIZE | **(24 rows, 80 cols)** |

### Attribute / default conventions used in this spec
- BMS rows/cols are **1-based**. `POS=(row,col)` is the position of the field's **attribute byte**; the displayable data begins at `col+1` and runs for `LENGTH` characters. For a byte-for-byte text renderer, reserve 1 leading attribute cell per field (rendered as a space) then `LENGTH` data cells.
- An explicit `ATTRB` was NOT coded on the output data fields (ADTOPEN, ACRDLIM, etc.) and on most label fields. In BMS the default attribute when `ATTRB` is omitted is `(ASKIP)` — i.e. **autoskip / protected, normal intensity**. These fields are therefore display-only.
- Fields shown with `LENGTH=0` are **stopper / delimiter fields** (no name, no data) used to terminate the preceding unprotected/highlighted field's attribute region. They carry a default `ASKIP` attribute and occupy a single attribute byte at the given POS. They are listed but are not "named" data fields.
- The line-373 footer literal is coded as `INITIAL='  F3=Exit '` (leading two spaces, trailing space) within a 60-char field.

---

## Fields (in source order)

Legend for INPUT/OUTPUT column:
- **INPUT** = unprotected (operator can type) — only `ACCTSID`.
- **OUTPUT** = protected/ASKIP literal label OR protected program-filled data field (display only).
- **STOPPER** = unnamed LENGTH=0 attribute-terminator field.

| # | Name | POS (r,c) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | PICIN/PICOUT | JUSTIFY/VALIDN | IN/OUT |
|---|------|-----------|-----|-------|-------|---------|-------------------|--------------|----------------|--------|
| 1 | *(unnamed)* | (1,1) | 5 | ASKIP, NORM | BLUE | - | `Tran:` | - | - | OUTPUT (literal) |
| 2 | **TRNNAME** | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | - | *(none)* | - | - | OUTPUT (data) |
| 3 | **TITLE01** | (1,21) | 40 | ASKIP, NORM | YELLOW | - | *(none)* | - | - | OUTPUT (data) |
| 4 | *(unnamed)* | (1,65) | 5 | ASKIP, NORM | BLUE | - | `Date:` | - | - | OUTPUT (literal) |
| 5 | **CURDATE** | (1,71) | 8 | ASKIP, NORM | BLUE | - | `mm/dd/yy` | - | - | OUTPUT (data) |
| 6 | *(unnamed)* | (2,1) | 5 | ASKIP, NORM | BLUE | - | `Prog:` | - | - | OUTPUT (literal) |
| 7 | **PGMNAME** | (2,7) | 8 | ASKIP, NORM | BLUE | - | *(none)* | - | - | OUTPUT (data) |
| 8 | **TITLE02** | (2,21) | 40 | ASKIP, NORM | YELLOW | - | *(none)* | - | - | OUTPUT (data) |
| 9 | *(unnamed)* | (2,65) | 5 | ASKIP, NORM | BLUE | - | `Time:` | - | - | OUTPUT (literal) |
| 10 | **CURTIME** | (2,71) | 8 | ASKIP, NORM | BLUE | - | `hh:mm:ss` | - | - | OUTPUT (data) |
| 11 | *(unnamed)* | (4,33) | 12 | *(default ASKIP)* | NEUTRAL | - | `View Account` | - | - | OUTPUT (literal) |
| 12 | *(unnamed)* | (5,19) | 16 | ASKIP, NORM | TURQUOISE | - | `Account Number :` | - | - | OUTPUT (literal) |
| 13 | **ACCTSID** | (5,38) | 11 | FSET, IC, NORM, **UNPROT** | GREEN | UNDERLINE | *(none)* | PICIN=`99999999999` | VALIDN=(MUSTFILL) | **INPUT** (cursor / IC) |
| 14 | *(unnamed)* | (5,50) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 15 | *(unnamed)* | (5,57) | 12 | *(default ASKIP)* | TURQUOISE | - | `Active Y/N: ` | - | - | OUTPUT (literal) |
| 16 | **ACSTTUS** | (5,70) | 1 | ASKIP | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 17 | *(unnamed)* | (5,72) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 18 | *(unnamed)* | (6,8) | 7 | *(default ASKIP)* | TURQUOISE | - | `Opened:` | - | - | OUTPUT (literal) |
| 19 | **ADTOPEN** | (6,17) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 20 | *(unnamed)* | (6,28) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 21 | *(unnamed)* | (6,39) | 21 | ASKIP, NORM | TURQUOISE | - | `Credit Limit        :` | - | - | OUTPUT (literal) |
| 22 | **ACRDLIM** | (6,61) | 15 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | PICOUT=`+ZZZ,ZZZ,ZZZ.99` | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 23 | *(unnamed)* | (6,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 24 | *(unnamed)* | (7,8) | 7 | *(default ASKIP)* | TURQUOISE | - | `Expiry:` | - | - | OUTPUT (literal) |
| 25 | **AEXPDT** | (7,17) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 26 | *(unnamed)* | (7,28) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 27 | *(unnamed)* | (7,39) | 21 | ASKIP, NORM | TURQUOISE | - | `Cash credit Limit   :` | - | - | OUTPUT (literal) |
| 28 | **ACSHLIM** | (7,61) | 15 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | PICOUT=`+ZZZ,ZZZ,ZZZ.99` | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 29 | *(unnamed)* | (7,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 30 | *(unnamed)* | (8,8) | 8 | *(default ASKIP)* | TURQUOISE | - | `Reissue:` | - | - | OUTPUT (literal) |
| 31 | **AREISDT** | (8,17) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 32 | *(unnamed)* | (8,28) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 33 | *(unnamed)* | (8,39) | 21 | ASKIP, NORM | TURQUOISE | - | `Current Balance     :` | - | - | OUTPUT (literal) |
| 34 | **ACURBAL** | (8,61) | 15 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | PICOUT=`+ZZZ,ZZZ,ZZZ.99` | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 35 | *(unnamed)* | (8,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 36 | *(unnamed)* | (9,39) | 21 | ASKIP, NORM | TURQUOISE | - | `Current Cycle Credit:` | - | - | OUTPUT (literal) |
| 37 | **ACRCYCR** | (9,61) | 15 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | PICOUT=`+ZZZ,ZZZ,ZZZ.99` | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 38 | *(unnamed)* | (9,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 39 | *(unnamed)* | (10,8) | 14 | *(default ASKIP)* | TURQUOISE | - | `Account Group:` | - | - | OUTPUT (literal) |
| 40 | **AADDGRP** | (10,23) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 41 | *(unnamed)* | (10,34) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 42 | *(unnamed)* | (10,39) | 21 | ASKIP, NORM | TURQUOISE | - | `Current Cycle Debit :` | - | - | OUTPUT (literal) |
| 43 | **ACRCYDB** | (10,61) | 15 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | PICOUT=`+ZZZ,ZZZ,ZZZ.99` | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 44 | *(unnamed)* | (10,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 45 | *(unnamed)* | (11,32) | 16 | *(default ASKIP)* | NEUTRAL | - | `Customer Details` | - | - | OUTPUT (literal) |
| 46 | *(unnamed)* | (12,8) | 14 | *(default ASKIP)* | TURQUOISE | - | `Customer id  :` | - | - | OUTPUT (literal) |
| 47 | **ACSTNUM** | (12,23) | 9 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 48 | *(unnamed)* | (12,33) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 49 | *(unnamed)* | (12,49) | 4 | *(default ASKIP)* | TURQUOISE | - | `SSN:` | - | - | OUTPUT (literal) |
| 50 | **ACSTSSN** | (12,54) | 12 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 51 | *(unnamed)* | (12,67) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 52 | *(unnamed)* | (13,8) | 14 | *(default ASKIP)* | TURQUOISE | - | `Date of birth:` | - | - | OUTPUT (literal) |
| 53 | **ACSTDOB** | (13,23) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 54 | *(unnamed)* | (13,34) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 55 | *(unnamed)* | (13,49) | 11 | *(default ASKIP)* | TURQUOISE | - | `FICO Score:` | - | - | OUTPUT (literal) |
| 56 | **ACSTFCO** | (13,61) | 3 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 57 | *(unnamed)* | (13,65) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 58 | *(unnamed)* | (14,1) | 10 | *(default ASKIP)* | TURQUOISE | - | `First Name` | - | - | OUTPUT (literal) |
| 59 | *(unnamed)* | (14,28) | 13 | *(default ASKIP)* | TURQUOISE | - | `Middle Name: ` | - | - | OUTPUT (literal) |
| 60 | *(unnamed)* | (14,55) | 12 | *(default ASKIP)* | TURQUOISE | - | `Last Name : ` | - | - | OUTPUT (literal) |
| 61 | **ACSFNAM** | (15,1) | 25 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 62 | *(unnamed)* | (15,27) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 63 | **ACSMNAM** | (15,28) | 25 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 64 | *(unnamed)* | (15,54) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 65 | **ACSLNAM** | (15,55) | 25 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 66 | *(unnamed)* | (16,1) | 8 | *(default ASKIP)* | TURQUOISE | - | `Address:` | - | - | OUTPUT (literal) |
| 67 | **ACSADL1** | (16,10) | 50 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 68 | *(unnamed)* | (16,61) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 69 | *(unnamed)* | (16,63) | 6 | *(default ASKIP)* | TURQUOISE | - | `State ` | - | - | OUTPUT (literal) |
| 70 | **ACSSTTE** | (16,73) | 2 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 71 | *(unnamed)* | (16,76) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 72 | **ACSADL2** | (17,10) | 50 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 73 | *(unnamed)* | (17,61) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 74 | *(unnamed)* | (17,63) | 3 | *(default ASKIP)* | TURQUOISE | - | `Zip` | - | - | OUTPUT (literal) |
| 75 | **ACSZIPC** | (17,73) | 5 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | JUSTIFY=(RIGHT) | OUTPUT (data) |
| 76 | *(unnamed)* | (17,79) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 77 | *(unnamed)* | (18,1) | 5 | *(default ASKIP)* | TURQUOISE | - | `City ` | - | - | OUTPUT (literal) |
| 78 | **ACSCITY** | (18,10) | 50 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 79 | *(unnamed)* | (18,61) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 80 | *(unnamed)* | (18,63) | 7 | *(default ASKIP)* | TURQUOISE | - | `Country` | - | - | OUTPUT (literal) |
| 81 | **ACSCTRY** | (18,73) | 3 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 82 | *(unnamed)* | (18,77) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 83 | *(unnamed)* | (19,1) | 8 | *(default ASKIP)* | TURQUOISE | - | `Phone 1:` | - | - | OUTPUT (literal) |
| 84 | **ACSPHN1** | (19,10) | 13 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 85 | *(unnamed)* | (19,24) | 30 | *(default ASKIP)* | TURQUOISE | - | `Government Issued Id Ref    : ` | - | - | OUTPUT (literal) |
| 86 | **ACSGOVT** | (19,58) | 20 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 87 | *(unnamed)* | (19,79) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 88 | *(unnamed)* | (20,1) | 8 | *(default ASKIP)* | TURQUOISE | - | `Phone 2:` | - | - | OUTPUT (literal) |
| 89 | **ACSPHN2** | (20,10) | 13 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 90 | *(unnamed)* | (20,24) | 16 | *(default ASKIP)* | TURQUOISE | - | `EFT Account Id: ` | - | - | OUTPUT (literal) |
| 91 | **ACSEFTC** | (20,41) | 10 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 92 | *(unnamed)* | (20,52) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 93 | *(unnamed)* | (20,53) | 24 | *(default ASKIP)* | TURQUOISE | - | `Primary Card Holder Y/N:` | - | - | OUTPUT (literal) |
| 94 | **ACSPFLG** | (20,78) | 1 | *(default ASKIP)* | *(default)* | UNDERLINE | *(none)* | - | - | OUTPUT (data) |
| 95 | *(unnamed)* | (20,80) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 96 | **INFOMSG** | (22,23) | 45 | **PROT** | NEUTRAL | OFF | *(none)* | - | - | OUTPUT (data, protected) |
| 97 | *(unnamed)* | (22,69) | 0 | *(default ASKIP)* | - | - | *(none)* | - | - | STOPPER |
| 98 | *(unnamed)* | (1,1) | 9 | *(default ASKIP)* | *(default)* | - | *(none)* | - | - | OUTPUT (overlay; see note) |
| 99 | **ERRMSG** | (23,1) | 78 | ASKIP, **BRT**, FSET | RED | - | *(none)* | - | - | OUTPUT (error msg, bright) |
| 100 | *(unnamed)* | (24,1) | 60 | ASKIP, NORM | TURQUOISE | - | `  F3=Exit ` | - | - | OUTPUT (literal footer) |

---

## Input vs Output summary

- **Input (unprotected) fields — exactly 1:**
  - `ACCTSID` (5,38) LEN 11, GREEN, UNDERLINE, `PICIN='99999999999'`, `VALIDN=(MUSTFILL)`, ATTRB `(FSET,IC,NORM,UNPROT)`. This is the ONLY operator-editable field.
- **Cursor (IC) field:** `ACCTSID` carries `IC` → initial cursor lands here (row 5, col 39 in display terms — i.e., POS col 38 attribute byte + 1).
- **All other named fields are OUTPUT** (program-populated display data or protected). `INFOMSG` is explicitly `PROT`; `ERRMSG` is `ASKIP,BRT,FSET` (bright red error line). Everything else uses the BMS default `ASKIP` (autoskip/protected).

## Named data fields (program symbolic map fields)
TRNNAME, TITLE01, CURDATE, PGMNAME, TITLE02, CURTIME, ACCTSID, ACSTTUS, ADTOPEN, ACRDLIM, AEXPDT, ACSHLIM, AREISDT, ACURBAL, ACRCYCR, AADDGRP, ACRCYDB, ACSTNUM, ACSTSSN, ACSTDOB, ACSTFCO, ACSFNAM, ACSMNAM, ACSLNAM, ACSADL1, ACSSTTE, ACSADL2, ACSZIPC, ACSCITY, ACSCTRY, ACSPHN1, ACSGOVT, ACSPHN2, ACSEFTC, ACSPFLG, INFOMSG, ERRMSG
= **37 named fields.**

## HILIGHT / JUSTIFY notes
- **HILIGHT=UNDERLINE** on all program-data value fields: ACCTSID, ACSTTUS, ADTOPEN, ACRDLIM, AEXPDT, ACSHLIM, AREISDT, ACURBAL, ACRCYCR, AADDGRP, ACRCYDB, ACSTNUM, ACSTSSN, ACSTDOB, ACSTFCO, ACSFNAM, ACSMNAM, ACSLNAM, ACSADL1, ACSSTTE, ACSADL2, ACSZIPC, ACSCITY, ACSCTRY, ACSPHN1, ACSGOVT, ACSPHN2, ACSEFTC, ACSPFLG. (Renderer should draw these as underlined value cells.)
- **HILIGHT=OFF** on INFOMSG.
- **JUSTIFY=(RIGHT)** on: ACRDLIM, ACSHLIM, ACURBAL, ACRCYCR, ACRCYDB (all the `+ZZZ,ZZZ,ZZZ.99` money fields) and ACSZIPC.
- **BRT** (bright intensity) on ERRMSG only. **NORM** intensity elsewhere.

## PICIN / PICOUT
- PICIN: `ACCTSID` = `99999999999` (11 numeric input digits).
- PICOUT: `ACRDLIM`, `ACSHLIM`, `ACURBAL`, `ACRCYCR`, `ACRCYDB` all = `+ZZZ,ZZZ,ZZZ.99` (signed, zero-suppressed, grouped, 2 decimals → formatted width 15).

## COLOR map (for renderer)
- BLUE: header labels (Tran/Date/Prog/Time) and their values (TRNNAME, CURDATE, PGMNAME, CURTIME).
- YELLOW: TITLE01, TITLE02.
- NEUTRAL (white): `View Account`, `Customer Details`, INFOMSG.
- TURQUOISE (cyan): all descriptive field labels.
- GREEN: ACCTSID input field.
- RED: ERRMSG.
- Data value fields (ADTOPEN, ACRDLIM, etc.) have **no explicit COLOR** → inherit default (typically GREEN/NEUTRAL per terminal default); render with default foreground.

## Special rows
- Row 22: INFOMSG (info line, neutral).
- Row 23: ERRMSG (bright red error line, full width 78).
- Row 24: footer literal `  F3=Exit ` (turquoise), 60 chars wide starting col 1.

## Note on field #98 (overlay at (1,1) LEN 9)
Near end of source (BMS line 363-364) an unnamed `DFHMDF LENGTH=9, POS=(1,1)` is coded after INFOMSG. It overlays the top-left corner (same start as field #1 `Tran:`). It has no INITIAL and no ATTRB → default ASKIP, blank. In practice it re-defines/overwrites the attribute region at row 1 col 1; the renderer can treat the visible top-left as the `Tran:` literal (field #1) since this overlay carries no data and no color. Documented for completeness/byte-fidelity.
