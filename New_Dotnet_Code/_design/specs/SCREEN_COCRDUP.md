# Screen Spec: COCRDUP (Update Credit Card Details)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COCRDUP.bms`

## Mapset

- **Mapset name:** `COCRDUP` (DFHMSD)
- **LANG:** COBOL
- **MODE:** INOUT
- **STORAGE:** AUTO
- **TIOAPFX:** YES
- **TYPE:** `&&SYSPARM`

## Map

- **Map name:** `CCRDUPA` (DFHMDI)
- **SIZE:** 24 rows x 80 cols
- **CTRL:** FREEKB (free keyboard)
- **DSATTS:** COLOR, HILIGHT, PS, VALIDN
- **MAPATTS:** COLOR, HILIGHT, PS, VALIDN

### Screen title (program intent)
"BMS MAP FOR UPDATE CARDS" — Update Credit Card Details screen.

---

## Fields (in BMS source order)

Notes on conventions:
- ATTRB tokens: ASKIP = auto-skip (protected, cursor skips over), PROT = protected, UNPROT = unprotected (input), NORM = normal intensity, BRT = bright/high intensity, DRK = dark (non-display), FSET = modified-data-tag set on output, IC = insert cursor here.
- Unnamed `DFHMDF` entries are literals or attribute-byte / stopper fields; named entries are addressable program fields.
- `LENGTH=0` fields are zero-length "stopper" fields that terminate the preceding input field's data area (they reserve the attribute byte position immediately after an input field). They are unnamed and carry no data.
- POS is 1-based (row, col) of the field's first data character. In 3270, the attribute byte occupies the position immediately BEFORE POS, so an input field's first usable screen column visually starts at POS.

| # | Field name | POS (row,col) | LENGTH | ATTRB | COLOR | HILIGHT | JUSTIFY | INITIAL / literal | Kind |
|---|-----------|---------------|--------|-------|-------|---------|---------|-------------------|------|
| 1 | (literal) | (1,1) | 5 | ASKIP, NORM | BLUE | - | - | `Tran:` | Output literal |
| 2 | **TRNNAME** | (1,7) | 4 | ASKIP, FSET, NORM | BLUE | - | - | (none) | Output (protected) |
| 3 | **TITLE01** | (1,21) | 40 | ASKIP, NORM | YELLOW | - | - | (none) | Output (protected) |
| 4 | (literal) | (1,65) | 5 | ASKIP, NORM | BLUE | - | - | `Date:` | Output literal |
| 5 | **CURDATE** | (1,71) | 8 | ASKIP, NORM | BLUE | - | - | `mm/dd/yy` | Output (protected) |
| 6 | (literal) | (2,1) | 5 | ASKIP, NORM | BLUE | - | - | `Prog:` | Output literal |
| 7 | **PGMNAME** | (2,7) | 8 | ASKIP, NORM | BLUE | - | - | (none) | Output (protected) |
| 8 | **TITLE02** | (2,21) | 40 | ASKIP, NORM | YELLOW | - | - | (none) | Output (protected) |
| 9 | (literal) | (2,65) | 5 | ASKIP, NORM | BLUE | - | - | `Time:` | Output literal |
| 10 | **CURTIME** | (2,71) | 8 | ASKIP, NORM | BLUE | - | - | `hh:mm:ss` | Output (protected) |
| 11 | (literal) | (4,30) | 26 | (none specified → default) | NEUTRAL | - | - | `Update Credit Card Details` | Output literal (heading) |
| 12 | (literal) | (7,23) | 19 | ASKIP, NORM | TURQUOISE | - | - | `Account Number    :` | Output literal |
| 13 | **ACCTSID** | (7,45) | 11 | FSET, IC, NORM, PROT | DEFAULT | UNDERLINE | - | (none) | Output (protected); **CURSOR (IC)** |
| 14 | (stopper) | (7,57) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 15 | (literal) | (8,23) | 19 | ASKIP, NORM | TURQUOISE | - | - | `Card Number       :` | Output literal |
| 16 | **CARDSID** | (8,45) | 16 | FSET, NORM, UNPROT | DEFAULT | UNDERLINE | - | (none) | **Input (unprotected)** |
| 17 | (stopper) | (8,62) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 18 | (literal) | (11,4) | 20 | (none specified → default) | TURQUOISE | - | - | `Name on card      :` | Output literal |
| 19 | **CRDNAME** | (11,25) | 50 | UNPROT | (default) | UNDERLINE | - | (none) | **Input (unprotected)** |
| 20 | (stopper) | (11,76) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 21 | (literal) | (13,4) | 20 | (none specified → default) | TURQUOISE | - | - | `Card Active Y/N   : ` | Output literal |
| 22 | **CRDSTCD** | (13,25) | 1 | UNPROT | (default) | UNDERLINE | - | (none) | **Input (unprotected)** |
| 23 | (stopper) | (13,27) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 24 | (literal) | (15,4) | 20 | (none specified → default) | TURQUOISE | - | - | `Expiry Date       : ` | Output literal |
| 25 | **EXPMON** | (15,25) | 2 | UNPROT | (default) | UNDERLINE | RIGHT | (none) | **Input (unprotected)** |
| 26 | (literal) | (15,28) | 1 | (none specified → default) | (default) | - | - | `/` | Output literal (separator) |
| 27 | **EXPYEAR** | (15,30) | 4 | UNPROT | (default) | UNDERLINE | RIGHT | (none) | **Input (unprotected)** |
| 28 | (stopper) | (15,35) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 29 | **EXPDAY** | (15,36) | 2 | DRK, FSET, PROT | (default) | OFF | RIGHT | (none) | Output (protected, hidden/dark) |
| 30 | (stopper) | (15,39) | 0 | (none) | - | - | - | (none) | Zero-length stopper |
| 31 | **INFOMSG** | (20,25) | 40 | PROT | NEUTRAL | OFF | - | (none) | Output (protected) message line |
| 32 | **ERRMSG** | (23,1) | 80 | ASKIP, BRT, FSET | RED | - | - | (none) | Output (protected, bright) error line |
| 33 | **FKEYS** | (24,1) | 21 | ASKIP, NORM | YELLOW | - | - | `ENTER=Process F3=Exit` | Output literal (function keys) |
| 34 | **FKEYSC** | (24,23) | 18 | ASKIP, DRK | YELLOW | - | - | `F5=Save F12=Cancel` | Output literal (hidden until enabled) |

---

## Input vs Output summary

### Input fields (UNPROT — user can type)
- **CARDSID** (8,45) len 16 — Card Number entry.
- **CRDNAME** (11,25) len 50 — Name on card.
- **CRDSTCD** (13,25) len 1 — Card Active Y/N.
- **EXPMON** (15,25) len 2, RIGHT justified — Expiry month.
- **EXPYEAR** (15,30) len 4, RIGHT justified — Expiry year.

### Output / protected fields (program-supplied or literal, not user-editable)
- **TRNNAME** (1,7) — transaction id (ASKIP, FSET).
- **TITLE01** (1,21) / **TITLE02** (2,21) — title lines.
- **CURDATE** (1,71), **CURTIME** (2,71) — date/time, initial placeholders `mm/dd/yy` / `hh:mm:ss`.
- **PGMNAME** (2,7) — program name.
- **ACCTSID** (7,45) — Account Number; protected (PROT) AND carries the cursor (IC). Display-only but cursor lands here.
- **EXPDAY** (15,36) — protected, DRK (non-display), FSET, RIGHT justified. Hidden expiry-day holding field.
- **INFOMSG** (20,25) — informational message (protected).
- **ERRMSG** (23,1) — error message (protected, bright, red).
- **FKEYS** (24,1) — `ENTER=Process F3=Exit` (always shown).
- **FKEYSC** (24,23) — `F5=Save F12=Cancel` (DRK/hidden by default; revealed when applicable).
- All unnamed literals: `Tran:`, `Date:`, `Prog:`, `Time:`, `Update Credit Card Details`, the row labels (`Account Number    :`, `Card Number       :`, `Name on card      :`, `Card Active Y/N   : `, `Expiry Date       : `), and the `/` separator.

### Cursor (IC)
- **ACCTSID** at POS (7,45) has `IC` — initial cursor position. Note ACCTSID is PROT, so although the cursor starts there it is a protected field; the first editable field the user reaches by tabbing is CARDSID (8,45).

---

## HILIGHT / Justify details
- HILIGHT=UNDERLINE on: ACCTSID, CARDSID, CRDNAME, CRDSTCD, EXPMON, EXPYEAR (underline the data entry area).
- HILIGHT=OFF on: EXPDAY, INFOMSG (explicitly no highlight).
- JUSTIFY=RIGHT on: EXPMON (15,25), EXPYEAR (15,30), EXPDAY (15,36).
- No PICIN/PICOUT clauses are present anywhere in this map.

---

## Color legend (3270 colors used)
- BLUE — header labels and program/transaction/date/time values.
- YELLOW — title lines and function-key lines.
- TURQUOISE — field prompt labels in the body.
- NEUTRAL — main heading "Update Credit Card Details" and INFOMSG (white/neutral).
- RED — ERRMSG error line.
- DEFAULT — ACCTSID and CARDSID input attributes (terminal default color).
- (default) — fields with no COLOR clause (CRDNAME, CRDSTCD, EXPMON, EXPYEAR, EXPDAY, `/` separator) inherit terminal default.

---

## Renderer notes (24x80 reproduction)
- Grid is 24 rows x 80 columns, 1-based.
- Place each literal's INITIAL text starting at its POS (row,col) for exactly LENGTH characters.
- For input fields, render LENGTH underscores/underline cells starting at POS; underline reflects HILIGHT=UNDERLINE.
- ACCTSID, EXPDAY are protected; EXPDAY (DRK) renders as blanks (non-display).
- Initial cursor sits at (7,45) (ACCTSID / IC).
- Line 23 reserved full-width (80) for ERRMSG (red, bright).
- Line 24: `ENTER=Process F3=Exit` at col 1, `F5=Save F12=Cancel` at col 23 (hidden until enabled).
- The `/` literal at (15,28) sits between EXPMON (15,25-26) and EXPYEAR (15,30-33), forming the `mm / yyyy` expiry display.
