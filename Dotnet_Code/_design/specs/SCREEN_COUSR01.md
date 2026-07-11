# SCREEN SPEC — COUSR01 (Add User)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COUSR01.bms`
CardDemo function: **Add User**

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | **COUSR01** |
| DFHMSD CTRL | `(ALARM,FREEKB)` |
| EXTATT | `YES` (extended attributes — color/highlight enabled) |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES (12-byte TIOA prefix on symbolic map) |
| TYPE | `&&SYSPARM` |

`CTRL=FREEKB` → keyboard is unlocked when the map is sent. `CTRL=ALARM` → terminal alarm (beep) sounds on send.

## Map

| Property | Value |
|----------|-------|
| Map name | **COUSR1A** |
| LINE | 1 |
| COLUMN | 1 |
| SIZE | **(24 rows, 80 cols)** |

---

## Field list (in BMS source order)

Notes on conventions:
- Row/Col are 1-based (BMS POS = (line,column)).
- In CICS/BMS, the **attribute byte occupies the position at POS**, and the visible field data begins at **col+1**. The next field must start at least `LENGTH+1` columns later (attribute byte + data). The text below records POS exactly as written in the BMS; the renderer should place the attribute marker at POS and the data immediately after.
- "Unnamed" fields are literal/static text (no symbolic-map name generated except a stopper). They are output-only.
- Default ATTRB when omitted: BMS default is `(ASKIP,NORM,PROT)`-like; the only field with no ATTRB clause here is `First Name:` label (line 80) which therefore defaults to protected/normal static text.

| # | Name | POS (row,col) | LEN | ATTRB | COLOR | HILIGHT | INITIAL / literal | I/O |
|---|------|---------------|-----|-------|-------|---------|-------------------|-----|
| 1 | (unnamed) | (1,1) | 5 | ASKIP, NORM (protected, auto-skip) | BLUE | — | `Tran:` | Output/literal |
| 2 | **TRNNAME** | (1,7) | 4 | ASKIP, FSET, NORM (protected, auto-skip, FSET) | BLUE | — | (none) | Output (transaction id) |
| 3 | **TITLE01** | (1,21) | 40 | ASKIP, FSET, NORM (protected) | YELLOW | — | (none) | Output (title line 1) |
| 4 | (unnamed) | (1,65) | 5 | ASKIP, NORM (protected) | BLUE | — | `Date:` | Output/literal |
| 5 | **CURDATE** | (1,71) | 8 | ASKIP, FSET, NORM (protected) | BLUE | — | `mm/dd/yy` | Output (current date) |
| 6 | (unnamed) | (2,1) | 5 | ASKIP, NORM (protected) | BLUE | — | `Prog:` | Output/literal |
| 7 | **PGMNAME** | (2,7) | 8 | ASKIP, FSET, NORM (protected) | BLUE | — | (none) | Output (program name) |
| 8 | **TITLE02** | (2,21) | 40 | ASKIP, FSET, NORM (protected) | YELLOW | — | (none) | Output (title line 2) |
| 9 | (unnamed) | (2,65) | 5 | ASKIP, NORM (protected) | BLUE | — | `Time:` | Output/literal |
| 10 | **CURTIME** | (2,71) | 8 | ASKIP, FSET, NORM (protected) | BLUE | — | `hh:mm:ss` | Output (current time) |
| 11 | (unnamed) | (4,35) | 9 | ASKIP, BRT (protected, bright) | NEUTRAL | — | `Add User` | Output/literal (screen heading) |
| 12 | (unnamed) | (8,6) | 11 | (default: protected, NORM) | TURQUOISE | — | `First Name:` | Output/literal label |
| 13 | **FNAME** | (8,18) | 20 | FSET, IC, NORM, **UNPROT** | GREEN | UNDERLINE | (none) | **INPUT** — first name. **IC = initial cursor here** |
| 14 | (unnamed stopper) | (8,39) | 0 | ASKIP, NORM (protected) | (default) | — | (none) | Field stopper (terminates FNAME input) |
| 15 | (unnamed) | (8,45) | 10 | ASKIP, NORM (protected) | TURQUOISE | — | `Last Name:` | Output/literal label |
| 16 | **LNAME** | (8,56) | 20 | FSET, NORM, **UNPROT** | GREEN | UNDERLINE | (none) | **INPUT** — last name |
| 17 | (unnamed stopper) | (8,77) | 0 | ASKIP, NORM (protected) | GREEN | — | (none) | Field stopper (terminates LNAME input) |
| 18 | (unnamed) | (11,6) | 8 | ASKIP, NORM (protected) | TURQUOISE | — | `User ID:` | Output/literal label |
| 19 | **USERID** | (11,15) | 8 | FSET, NORM, **UNPROT** | GREEN | UNDERLINE | (none) | **INPUT** — user id (8 chars) |
| 20 | (unnamed) | (11,24) | 8 | ASKIP, NORM (protected) | BLUE | — | `(8 Char)` | Output/literal hint |
| 21 | (unnamed) | (11,45) | 9 | ASKIP, NORM (protected) | TURQUOISE | — | `Password:` | Output/literal label |
| 22 | **PASSWD** | (11,55) | 8 | DRK, FSET, **UNPROT** (dark/non-display) | GREEN | UNDERLINE | (none) | **INPUT** — password (8 chars). **Dark = non-display (masked)** |
| 23 | (unnamed) | (11,64) | 8 | ASKIP, NORM (protected) | BLUE | — | `(8 Char)` | Output/literal hint |
| 24 | (unnamed) | (14,6) | 11 | ASKIP, NORM (protected) | TURQUOISE | — | `User Type: ` (trailing space) | Output/literal label |
| 25 | **USRTYPE** | (14,17) | 1 | FSET, NORM, **UNPROT** | GREEN | UNDERLINE | (none) | **INPUT** — user type (1 char) |
| 26 | (unnamed) | (14,19) | 17 | ASKIP, NORM (protected) | BLUE | — | `(A=Admin, U=User)` | Output/literal hint |
| 27 | **ERRMSG** | (23,1) | 78 | ASKIP, BRT, FSET (protected, bright) | RED | — | (none) | Output (error message line) |
| 28 | (unnamed) | (24,1) | 43 | ASKIP, NORM (protected) | YELLOW | — | `ENTER=Add User  F3=Back  F4=Clear  F12=Exit` | Output/literal (PF-key legend) |

---

## Input vs Output summary

**Input fields (UNPROT — user-editable):**
- **FNAME** (8,18) len 20 — first name — GREEN, UNDERLINE — **holds initial cursor (IC)**
- **LNAME** (8,56) len 20 — last name — GREEN, UNDERLINE
- **USERID** (11,15) len 8 — user id — GREEN, UNDERLINE
- **PASSWD** (11,55) len 8 — password — GREEN, UNDERLINE, **DRK (non-display / masked)**
- **USRTYPE** (14,17) len 1 — user type — GREEN, UNDERLINE

**Output / dynamic fields (protected, filled by program):**
- TRNNAME (1,7), TITLE01 (1,21), CURDATE (1,71), PGMNAME (2,7), TITLE02 (2,21), CURTIME (2,71), ERRMSG (23,1)

**Static literal fields (protected, fixed text):** all unnamed fields listed above, plus the two literal `(8 Char)` hints, the `(A=Admin, U=User)` hint, the `Add User` heading (row 4), and the PF-key legend (row 24).

## Cursor (IC)

Initial cursor is positioned at **FNAME** — POS (8,18), the only field carrying the `IC` attribute. On first display the cursor rests at the start of the First Name input.

## HILIGHT / Justify

- **HILIGHT=UNDERLINE** on all five input fields: FNAME, LNAME, USERID, PASSWD, USRTYPE. The renderer should draw an underline across the field length for these (visual cue for entry area).
- No `JUSTIFY` clauses present in this map — all fields use BMS default left-justify, no zero/blank fill override.

## PICIN / PICOUT

- No `PICIN` or `PICOUT` clauses are present on any field in COUSR01. All symbolic-map fields are plain character (alphanumeric) of the stated LENGTH; no numeric editing/picture is applied.

## Color legend (extended attributes, EXTATT=YES)

| BMS color | Meaning |
|-----------|---------|
| BLUE | header labels & program/transaction/date/time values |
| YELLOW | titles (TITLE01/TITLE02) and PF-key legend |
| NEUTRAL | `Add User` heading (renders white/default-bright) |
| TURQUOISE | field prompt labels (First Name, Last Name, User ID, Password, User Type) |
| GREEN | all input fields |
| RED | error message line (ERRMSG) |

## Rendering notes for 24x80 console

- Total grid 24 rows x 80 cols (SIZE=(24,80)).
- Reserve 1 cell for the attribute byte at each POS; visible content starts at the next column. For byte-for-byte text reproduction, the simplest faithful approach is: place each literal/field's data starting at its POS column (treating the BMS attribute byte as a leading space/marker), and underline input-field spans.
- BRT fields (`Add User`, `ERRMSG`) render bright/bold. DRK field (PASSWD) renders non-display (mask input, e.g., do not echo or echo as blanks).
- ASKIP fields are auto-skip (cursor cannot land in them and tabs past them); UNPROT fields accept input and are tab stops.
- FSET means the Modified Data Tag is preset so the field is always returned to the program on the next read, regardless of whether the user typed in it.
