# SCREEN SPEC — COPAU01 (Pending / View Authorization Details)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/bms/COPAU01.bms`

## Mapset

| Property | Value |
|----------|-------|
| Mapset name (DFHMSD) | `COPAU01` |
| CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` |
| TYPE | `&&SYSPARM` |

## Map

| Property | Value |
|----------|-------|
| Map name (DFHMDI) | `COPAU1A` |
| COLUMN | 1 |
| LINE | 1 |
| SIZE (rows, cols) | (24, 80) |

Notes on conventions:
- All fields in this map carry `ASKIP` (auto-skip) — there are **NO unprotected/input fields**. The entire screen is output-only (display). The operator drives it with PF keys only (F3/F5/F8), per the function-key footer and CTRL=FREEKB.
- No field specifies `IC` — there is **no cursor field** defined in the BMS. Cursor placement is left to the program / default (row 1, col 1).
- No field specifies `NUM` (numeric), `JUSTIFY`, `HILIGHT`, `PICIN`, or `PICOUT`. There are none in this map.
- Attribute `NORM` = normal intensity; `BRT` = bright/high intensity.
- One field (the `Merchant Details ...` separator, line 17) has **no ATTRB clause at all** — it defaults to protected, normal intensity (effectively a protected literal).
- Field lengths below are the BMS-declared display lengths. In the generated symbolic map, CICS reserves an extra leading attribute byte per field, but the rendered text occupies the stated LENGTH starting at POS.

### Fields in declaration order

Legend for Type: **LIT** = unnamed protected literal (label/heading), **OUT** = named protected output field (display only).

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | INITIAL / Literal | Type |
|---|------|---------------|-----|-------|-------|-------------------|------|
| 1 | (unnamed) | (1,1) | 5 | ASKIP, NORM | BLUE | `Tran:` | LIT |
| 2 | `TRNNAME` | (1,7) | 4 | ASKIP, NORM | BLUE | (none) | OUT |
| 3 | `TITLE01` | (1,21) | 40 | ASKIP, NORM | YELLOW | (none) | OUT |
| 4 | (unnamed) | (1,65) | 5 | ASKIP, NORM | BLUE | `Date:` | LIT |
| 5 | `CURDATE` | (1,71) | 8 | ASKIP, NORM | BLUE | `mm/dd/yy` | OUT |
| 6 | (unnamed) | (2,1) | 5 | ASKIP, NORM | BLUE | `Prog:` | LIT |
| 7 | `PGMNAME` | (2,7) | 8 | ASKIP, NORM | BLUE | (none) | OUT |
| 8 | `TITLE02` | (2,21) | 40 | ASKIP, NORM | YELLOW | (none) | OUT |
| 9 | (unnamed) | (2,65) | 5 | ASKIP, NORM | BLUE | `Time:` | LIT |
| 10 | `CURTIME` | (2,71) | 8 | ASKIP, NORM | BLUE | `hh:mm:ss` | OUT |
| 11 | (unnamed) | (4,27) | 26 | ASKIP, BRT | NEUTRAL | `View Authorization Details` | LIT (heading, bright) |
| 12 | (unnamed) | (7,2) | 7 | ASKIP, NORM | TURQUOISE | `Card #:` | LIT |
| 13 | `CARDNUM` | (7,11) | 16 | ASKIP, NORM | PINK | (none) | OUT |
| 14 | (unnamed) | (7,31) | 10 | ASKIP, NORM | TURQUOISE | `Auth Date:` | LIT |
| 15 | `AUTHDT` | (7,43) | 10 | ASKIP, NORM | PINK | `' '` (single space) | OUT |
| 16 | (unnamed) | (7,56) | 10 | ASKIP, NORM | TURQUOISE | `Auth Time:` | LIT |
| 17 | `AUTHTM` | (7,68) | 10 | ASKIP, NORM | PINK | `' '` (single space) | OUT |
| 18 | (unnamed) | (9,2) | 10 | ASKIP, NORM | TURQUOISE | `Auth Resp:` | LIT |
| 19 | `AUTHRSP` | (9,14) | 1 | ASKIP, NORM | PINK | `' '` (single space) | OUT |
| 20 | (unnamed) | (9,18) | 12 | ASKIP, NORM | TURQUOISE | `Resp Reason:` | LIT |
| 21 | `AUTHRSN` | (9,32) | 20 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 22 | (unnamed) | (9,56) | 10 | ASKIP, NORM | TURQUOISE | `Auth Code:` | LIT |
| 23 | `AUTHCD` | (9,68) | 6 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 24 | (unnamed) | (11,2) | 7 | ASKIP, NORM | TURQUOISE | `Amount:` | LIT |
| 25 | `AUTHAMT` | (11,11) | 12 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 26 | (unnamed) | (11,29) | 15 | ASKIP, NORM | TURQUOISE | `POS Entry Mode:` | LIT |
| 27 | `POSEMD` | (11,46) | 4 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 28 | (unnamed) | (11,56) | 10 | ASKIP, NORM | TURQUOISE | `Source   :` | LIT |
| 29 | `AUTHSRC` | (11,68) | 10 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 30 | (unnamed) | (13,2) | 9 | ASKIP, NORM | TURQUOISE | `MCC Code:` | LIT |
| 31 | `MCCCD` | (13,13) | 4 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 32 | (unnamed) | (13,25) | 15 | ASKIP, NORM | TURQUOISE | `Card Exp. Date:` | LIT |
| 33 | `CRDEXP` | (13,42) | 5 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 34 | (unnamed) | (13,52) | 10 | ASKIP, NORM | TURQUOISE | `Auth Type:` | LIT |
| 35 | `AUTHTYP` | (13,64) | 14 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 36 | (unnamed) | (15,2) | 18 | ASKIP, NORM | TURQUOISE | `Tran Id:` | LIT |
| 37 | `TRNID` | (15,12) | 15 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 38 | (unnamed) | (15,31) | 13 | ASKIP, NORM | TURQUOISE | `Match Status:` | LIT |
| 39 | `AUTHMTC` | (15,46) | 1 | ASKIP, NORM | RED | `' '` (single space) | OUT |
| 40 | (unnamed) | (15,52) | 13 | ASKIP, NORM | TURQUOISE | `Fraud Status:` | LIT |
| 41 | `AUTHFRD` | (15,67) | 10 | ASKIP, NORM | RED | `' '` (single space) | OUT |
| 42 | (unnamed) | (17,2) | 76 | (none — defaults protected, NORM) | NEUTRAL | `Merchant Details ----------------------------------------------------------` (see note) | LIT (separator bar) |
| 43 | (unnamed) | (19,2) | 5 | ASKIP, NORM | TURQUOISE | `Name:` | LIT |
| 44 | `MERNAME` | (19,9) | 25 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 45 | (unnamed) | (19,41) | 12 | ASKIP, NORM | TURQUOISE | `Merchant ID:` | LIT |
| 46 | `MERID` | (19,55) | 15 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 47 | (unnamed) | (21,2) | 5 | ASKIP, NORM | TURQUOISE | `City:` | LIT |
| 48 | `MERCITY` | (21,9) | 25 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 49 | (unnamed) | (21,41) | 6 | ASKIP, NORM | TURQUOISE | `State:` | LIT |
| 50 | `MERST` | (21,49) | 2 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 51 | (unnamed) | (21,55) | 4 | ASKIP, NORM | TURQUOISE | `Zip:` | LIT |
| 52 | `MERZIP` | (21,61) | 10 | ASKIP, NORM | BLUE | `' '` (single space) | OUT |
| 53 | `ERRMSG` | (23,1) | 78 | ASKIP, BRT, FSET | RED | (none) | OUT (error line, bright, FSET) |
| 54 | (unnamed) | (24,1) | 45 | ASKIP, NORM | YELLOW | ` F3=Back  F5=Mark/Remove Fraud  F8=Next Auth` | LIT (PF-key footer) |

### Note on field #42 (line 17 separator)

The INITIAL value spans two continued BMS lines (lines 232–233). LENGTH=76. The literal is:

```
Merchant Details ------------------------------------------------------------
```

i.e. the text `Merchant Details ` (with a trailing space after "Details") followed by enough hyphens to fill the 76-character field. Reconstructed from the BMS continuation:
`'Merchant Details -------------------------------'` + `'-----------------------------'` = `Merchant Details ` (17 chars including trailing space) then 59 hyphens = 76 chars total.

### Note on field #54 (line 24 footer)

INITIAL value is exactly ` F3=Back  F5=Mark/Remove Fraud  F8=Next Auth` — note the **leading space** before `F3` and the **double space** between `F3=Back` and `F5=...`, and the **double space** between `Fraud` and `F8=...`. LENGTH=45 (footer text is 44 chars + the field is declared length 45, so it is padded by 1 trailing space).

## Input vs Output summary

- **Input (unprotected) fields:** NONE. Every DFHMDF carries ASKIP (or, for field #42, default protection). This is a pure display screen.
- **Output (protected) named fields (12):** `TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `CARDNUM`, `AUTHDT`, `AUTHTM`, `AUTHRSP`, `AUTHRSN`, `AUTHCD`, `AUTHAMT`, `POSEMD`, `AUTHSRC`, `MCCCD`, `CRDEXP`, `AUTHTYP`, `TRNID`, `AUTHMTC`, `AUTHFRD`, `MERNAME`, `MERID`, `MERCITY`, `MERST`, `MERZIP`, `ERRMSG` (27 named output fields total).
- **Protected literals/labels (unnamed, 27):** the `Tran:`, `Prog:`, `Date:`, `Time:`, `View Authorization Details` heading, all field captions, the merchant-details separator, and the PF-key footer.
- **Cursor (IC) field:** NONE declared. Default cursor position (1,1).
- **HILIGHT / JUSTIFY:** NONE declared on any field.

## Color map (for renderer)

| BMS COLOR | Used by |
|-----------|---------|
| BLUE | `Tran:`/`Prog:`/`Date:`/`Time:` labels, TRNNAME, CURDATE, PGMNAME, CURTIME, and all blue OUT data fields (AUTHRSN, AUTHCD, AUTHAMT, POSEMD, AUTHSRC, MCCCD, CRDEXP, AUTHTYP, TRNID, MERNAME, MERID, MERCITY, MERST, MERZIP) |
| YELLOW | TITLE01, TITLE02, footer (#54) |
| NEUTRAL (white) | `View Authorization Details` heading (#11, BRT), merchant separator (#42) |
| TURQUOISE | all field captions in the body |
| PINK | CARDNUM, AUTHDT, AUTHTM, AUTHRSP |
| RED | AUTHMTC, AUTHFRD, ERRMSG (#53, BRT) |

## Intensity (bright) fields

- #11 `View Authorization Details` — BRT (heading)
- #53 `ERRMSG` — BRT, FSET (error message line)

All other fields are NORM (normal) intensity. `ERRMSG` is the only field with FSET (modified-data-tag set on).
