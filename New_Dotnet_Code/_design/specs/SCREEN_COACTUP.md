# SCREEN SPEC — COACTUP (Account Update)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COACTUP.bms`
Purpose: CardDemo "Update Account" screen.

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COACTUP` |
| Map name | `CACTUPA` |
| SIZE | 24 rows x 80 cols `SIZE=(24,80)` |
| LANG | COBOL |
| MODE | INOUT |
| STORAGE | AUTO |
| TIOAPFX | YES |
| TYPE | `&&SYSPARM` |
| Map CTRL | `FREEKB` (keyboard freed/unlocked on display) |
| DSATTS | COLOR, HILIGHT, PS, VALIDN |
| MAPATTS | COLOR, HILIGHT, PS, VALIDN |

### Attribute conventions used in this map
- `ASKIP` = autoskip (protected, cursor skips over it) — used for all static labels and output-only fields.
- `NORM` = normal intensity (visible, not bright).
- `BRT` = bright/high intensity.
- `DRK` = dark (non-display, hidden until program turns it on).
- `UNPROT` = unprotected (operator can type into it = INPUT field).
- `FSET` = modified-data-tag set on (field returned to program even if unchanged).
- `IC` = insert cursor here (initial cursor position).
- `HILIGHT=UNDERLINE` = field shown underlined; `HILIGHT=OFF` = no highlight.
- `JUSTIFY=(RIGHT)` = right-justified input.
- 3270 attribute byte note: every BMS field occupies its POS column plus the data; an attribute byte sits in the position immediately before the data, so adjacent fields are separated by 1 col. The `LENGTH=0` "stopper" fields below are zero-length attribute-only fields used to terminate the preceding input field's data area (they reset attributes; they carry no data and no name).

---

## Fields — in BMS source order

Legend: I = Input (UNPROT), O = Output/literal (ASKIP/protected), S = zero-length stopper (attribute reset, unnamed).
Cursor (IC) field = **ACCTSID**.

| # | Name | POS (row,col) | LEN | Kind | ATTRB | COLOR | HILIGHT | JUSTIFY | INITIAL / literal |
|---|------|--------------|-----|------|-------|-------|---------|---------|-------------------|
| — | (label) | (1,1) | 5 | O | ASKIP,NORM | BLUE | — | — | `Tran:` |
| 1 | TRNNAME | (1,7) | 4 | O | ASKIP,FSET,NORM | BLUE | — | — | (none — filled by program) |
| 2 | TITLE01 | (1,21) | 40 | O | ASKIP,NORM | YELLOW | — | — | (none — filled by program) |
| — | (label) | (1,65) | 5 | O | ASKIP,NORM | BLUE | — | — | `Date:` |
| 3 | CURDATE | (1,71) | 8 | O | ASKIP,NORM | BLUE | — | — | `mm/dd/yy` |
| — | (label) | (2,1) | 5 | O | ASKIP,NORM | BLUE | — | — | `Prog:` |
| 4 | PGMNAME | (2,7) | 8 | O | ASKIP,NORM | BLUE | — | — | (none — filled by program) |
| 5 | TITLE02 | (2,21) | 40 | O | ASKIP,NORM | YELLOW | — | — | (none — filled by program) |
| — | (label) | (2,65) | 5 | O | ASKIP,NORM | BLUE | — | — | `Time:` |
| 6 | CURTIME | (2,71) | 8 | O | ASKIP,NORM | BLUE | — | — | `hh:mm:ss` |
| — | (label) | (4,33) | 14 | O | (default; no ATTRB) | NEUTRAL | — | — | `Update Account` |
| — | (label) | (5,19) | 16 | O | ASKIP,NORM | TURQUOISE | — | — | `Account Number :` |
| 7 | ACCTSID | (5,38) | 11 | I | IC,UNPROT | (default) | UNDERLINE | — | (none — cursor starts here) |
| — | (stopper) | (5,50) | 0 | S | (default) | — | — | — | (zero-length, terminates ACCTSID) |
| — | (label) | (5,57) | 12 | O | (default; no ATTRB) | TURQUOISE | — | — | `Active Y/N: ` |
| 8 | ACSTTUS | (5,70) | 1 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (5,72) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSTTUS) |
| — | (label) | (6,8) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Opened :` |
| 9 | OPNYEAR | (6,17) | 4 | I | FSET,UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (6,22) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 10 | OPNMON | (6,24) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (6,27) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 11 | OPNDAY | (6,29) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (6,32) | 0 | S | (default) | — | — | — | (zero-length, terminates OPNDAY) |
| — | (label) | (6,39) | 21 | O | ASKIP,NORM | TURQUOISE | — | — | `Credit Limit        :` |
| 12 | ACRDLIM | (6,61) | 15 | I | FSET,UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (6,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACRDLIM) |
| — | (label) | (7,8) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Expiry :` |
| 13 | EXPYEAR | (7,17) | 4 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (7,22) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 14 | EXPMON | (7,24) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (7,27) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 15 | EXPDAY | (7,29) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (7,32) | 0 | S | (default) | — | — | — | (zero-length, terminates EXPDAY) |
| — | (label) | (7,39) | 21 | O | ASKIP,NORM | TURQUOISE | — | — | `Cash credit Limit   :` |
| 16 | ACSHLIM | (7,61) | 15 | I | FSET,UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (7,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSHLIM) |
| — | (label) | (8,8) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Reissue:` |
| 17 | RISYEAR | (8,17) | 4 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (8,22) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 18 | RISMON | (8,24) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (8,27) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 19 | RISDAY | (8,29) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (8,32) | 0 | S | (default) | — | — | — | (zero-length, terminates RISDAY) |
| — | (label) | (8,39) | 21 | O | ASKIP,NORM | TURQUOISE | — | — | `Current Balance     :` |
| 20 | ACURBAL | (8,61) | 15 | I | FSET,UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (8,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACURBAL) |
| — | (label) | (9,39) | 21 | O | ASKIP,NORM | TURQUOISE | — | — | `Current Cycle Credit:` |
| 21 | ACRCYCR | (9,61) | 15 | I | FSET,UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (9,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACRCYCR) |
| — | (label) | (10,8) | 14 | O | (default; no ATTRB) | TURQUOISE | — | — | `Account Group:` |
| 22 | AADDGRP | (10,23) | 10 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (10,34) | 0 | S | (default) | — | — | — | (zero-length, terminates AADDGRP) |
| — | (label) | (10,39) | 21 | O | ASKIP,NORM | TURQUOISE | — | — | `Current Cycle Debit :` |
| 23 | ACRCYDB | (10,61) | 15 | I | FSET,UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (10,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACRCYDB) |
| — | (label) | (11,32) | 16 | O | (default; no ATTRB) | NEUTRAL | — | — | `Customer Details` |
| — | (label) | (12,8) | 14 | O | (default; no ATTRB) | TURQUOISE | — | — | `Customer id  :` |
| 24 | ACSTNUM | (12,23) | 9 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (12,33) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSTNUM) |
| — | (label) | (12,49) | 4 | O | (default; no ATTRB) | TURQUOISE | — | — | `SSN:` |
| 25 | ACTSSN1 | (12,55) | 3 | I | UNPROT | (default) | UNDERLINE | — | `999` |
| — | (label) | (12,59) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 26 | ACTSSN2 | (12,61) | 2 | I | UNPROT | (default) | UNDERLINE | — | `99` |
| — | (label) | (12,64) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 27 | ACTSSN3 | (12,66) | 4 | I | UNPROT | (default) | UNDERLINE | — | `9999` |
| — | (stopper) | (12,71) | 0 | S | (default) | — | — | — | (zero-length, terminates ACTSSN3) |
| — | (label) | (13,8) | 14 | O | (default; no ATTRB) | TURQUOISE | — | — | `Date of birth:` |
| 28 | DOBYEAR | (13,23) | 4 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (13,28) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 29 | DOBMON | (13,30) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (label) | (13,33) | 1 | O | (default; no ATTRB) | (default) | — | — | `-` |
| 30 | DOBDAY | (13,35) | 2 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (13,38) | 0 | S | (default) | — | — | — | (zero-length, terminates DOBDAY) |
| — | (label) | (13,49) | 11 | O | (default; no ATTRB) | TURQUOISE | — | — | `FICO Score:` |
| 31 | ACSTFCO | (13,62) | 3 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (13,66) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSTFCO) |
| — | (label) | (14,1) | 10 | O | (default; no ATTRB) | TURQUOISE | — | — | `First Name` |
| — | (label) | (14,28) | 13 | O | (default; no ATTRB) | TURQUOISE | — | — | `Middle Name: ` |
| — | (label) | (14,55) | 12 | O | (default; no ATTRB) | TURQUOISE | — | — | `Last Name : ` |
| 32 | ACSFNAM | (15,1) | 25 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (15,27) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSFNAM) |
| 33 | ACSMNAM | (15,28) | 25 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (15,54) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSMNAM) |
| 34 | ACSLNAM | (15,55) | 25 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (label) | (16,1) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Address:` |
| 35 | ACSADL1 | (16,10) | 50 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (16,61) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSADL1) |
| — | (label) | (16,63) | 6 | O | (default; no ATTRB) | TURQUOISE | — | — | `State ` |
| 36 | ACSSTTE | (16,73) | 2 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (16,76) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSSTTE) |
| 37 | ACSADL2 | (17,10) | 50 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (17,61) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSADL2) |
| — | (label) | (17,63) | 3 | O | (default; no ATTRB) | TURQUOISE | — | — | `Zip` |
| 38 | ACSZIPC | (17,73) | 5 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (17,79) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSZIPC) |
| — | (label) | (18,1) | 5 | O | (default; no ATTRB) | TURQUOISE | — | — | `City ` |
| 39 | ACSCITY | (18,10) | 50 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (18,61) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSCITY) |
| — | (label) | (18,63) | 7 | O | (default; no ATTRB) | TURQUOISE | — | — | `Country` |
| 40 | ACSCTRY | (18,73) | 3 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (18,77) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSCTRY) |
| — | (label) | (19,1) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Phone 1:` |
| 41 | ACSPH1A | (19,10) | 3 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| 42 | ACSPH1B | (19,14) | 3 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| 43 | ACSPH1C | (19,18) | 4 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (19,23) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSPH1C) |
| — | (label) | (19,24) | 30 | O | (default; no ATTRB) | TURQUOISE | — | — | `Government Issued Id Ref    : ` |
| 44 | ACSGOVT | (19,58) | 20 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (19,79) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSGOVT) |
| — | (label) | (20,1) | 8 | O | (default; no ATTRB) | TURQUOISE | — | — | `Phone 2:` |
| 45 | ACSPH2A | (20,10) | 3 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| 46 | ACSPH2B | (20,14) | 3 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| 47 | ACSPH2C | (20,18) | 4 | I | UNPROT | (default) | UNDERLINE | RIGHT | (none) |
| — | (stopper) | (20,23) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSPH2C) |
| — | (label) | (20,24) | 16 | O | (default; no ATTRB) | TURQUOISE | — | — | `EFT Account Id: ` |
| 48 | ACSEFTC | (20,41) | 10 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (20,52) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSEFTC) |
| — | (label) | (20,53) | 24 | O | (default; no ATTRB) | TURQUOISE | — | — | `Primary Card Holder Y/N:` |
| 49 | ACSPFLG | (20,78) | 1 | I | UNPROT | (default) | UNDERLINE | — | (none) |
| — | (stopper) | (20,80) | 0 | S | (default) | — | — | — | (zero-length, terminates ACSPFLG) |
| 50 | INFOMSG | (22,23) | 45 | O | ASKIP | NEUTRAL | OFF | — | (none — informational message line) |
| — | (stopper) | (22,69) | 0 | S | (default) | — | — | — | (zero-length, terminates INFOMSG) |
| — | (label) | (1,1) | 9 | O | (default; no ATTRB) | (default) | — | — | (none — overlay field at row 1, see note) |
| 51 | ERRMSG | (23,1) | 78 | O | ASKIP,BRT,FSET | RED | — | — | (none — error message line, bright red) |
| 52 | FKEYS | (24,1) | 21 | O | ASKIP,NORM | YELLOW | — | — | `ENTER=Process F3=Exit` |
| 53 | FKEY05 | (24,23) | 7 | O | ASKIP,DRK | YELLOW | — | — | `F5=Save` (dark/hidden until enabled) |
| 54 | FKEY12 | (24,31) | 10 | O | ASKIP,DRK | YELLOW | — | — | `F12=Cancel` (dark/hidden until enabled) |

---

## Input (unprotected) fields — for the renderer's tab order / editability

All UNPROT fields are operator-editable and all carry `HILIGHT=UNDERLINE`. In BMS source order:

1. ACCTSID (5,38) len 11 — **IC: cursor starts here**
2. ACSTTUS (5,70) len 1
3. OPNYEAR (6,17) len 4, RIGHT, FSET
4. OPNMON (6,24) len 2, RIGHT
5. OPNDAY (6,29) len 2, RIGHT
6. ACRDLIM (6,61) len 15, FSET
7. EXPYEAR (7,17) len 4, RIGHT
8. EXPMON (7,24) len 2, RIGHT
9. EXPDAY (7,29) len 2, RIGHT
10. ACSHLIM (7,61) len 15, FSET
11. RISYEAR (8,17) len 4, RIGHT
12. RISMON (8,24) len 2, RIGHT
13. RISDAY (8,29) len 2, RIGHT
14. ACURBAL (8,61) len 15, FSET
15. ACRCYCR (9,61) len 15, FSET
16. AADDGRP (10,23) len 10
17. ACRCYDB (10,61) len 15, FSET
18. ACSTNUM (12,23) len 9
19. ACTSSN1 (12,55) len 3, INITIAL `999`
20. ACTSSN2 (12,61) len 2, INITIAL `99`
21. ACTSSN3 (12,66) len 4, INITIAL `9999`
22. DOBYEAR (13,23) len 4, RIGHT
23. DOBMON (13,30) len 2, RIGHT
24. DOBDAY (13,35) len 2, RIGHT
25. ACSTFCO (13,62) len 3
26. ACSFNAM (15,1) len 25
27. ACSMNAM (15,28) len 25
28. ACSLNAM (15,55) len 25
29. ACSADL1 (16,10) len 50
30. ACSSTTE (16,73) len 2
31. ACSADL2 (17,10) len 50
32. ACSZIPC (17,73) len 5
33. ACSCITY (18,10) len 50
34. ACSCTRY (18,73) len 3
35. ACSPH1A (19,10) len 3, RIGHT
36. ACSPH1B (19,14) len 3, RIGHT
37. ACSPH1C (19,18) len 4, RIGHT
38. ACSGOVT (19,58) len 20
39. ACSPH2A (20,10) len 3, RIGHT
40. ACSPH2B (20,14) len 3, RIGHT
41. ACSPH2C (20,18) len 4, RIGHT
42. ACSEFTC (20,41) len 10
43. ACSPFLG (20,78) len 1

## Output / protected (ASKIP) named fields the program writes

- TRNNAME (1,7) len 4 — transaction id, BLUE, FSET
- TITLE01 (1,21) len 40 — title line 1, YELLOW
- CURDATE (1,71) len 8 — current date, BLUE, INITIAL `mm/dd/yy`
- PGMNAME (2,7) len 8 — program name, BLUE
- TITLE02 (2,21) len 40 — title line 2, YELLOW
- CURTIME (2,71) len 8 — current time, BLUE, INITIAL `hh:mm:ss`
- INFOMSG (22,23) len 45 — info message, NEUTRAL, HILIGHT=OFF
- ERRMSG (23,1) len 78 — error message, RED, BRT, FSET
- FKEYS (24,1) len 21 — `ENTER=Process F3=Exit`, YELLOW
- FKEY05 (24,23) len 7 — `F5=Save`, YELLOW, DRK (hidden until enabled)
- FKEY12 (24,31) len 10 — `F12=Cancel`, YELLOW, DRK (hidden until enabled)

---

## Notes / edge cases for byte-accurate rendering

- **Cursor (IC):** only `ACCTSID` at (5,38) has the `IC` attribute. The renderer must place the initial cursor there.
- **PICIN / PICOUT:** none of the fields define PICIN/PICOUT clauses. The only data-formatting hints are `JUSTIFY=(RIGHT)` on the date-component and phone-component fields. The `999` / `99` / `9999` values on the SSN fields (ACTSSN1/2/3) are **INITIAL literals**, not picture clauses — they pre-fill the field with those characters.
- **HILIGHT:** all UNPROT input fields use `HILIGHT=UNDERLINE`. `INFOMSG` uses `HILIGHT=OFF`. No other highlight (no REVERSE/BLINK) appears.
- **Colors used:** BLUE (header labels + values), YELLOW (titles + fkeys), NEUTRAL (`Update Account`, `Customer Details`, INFOMSG), TURQUOISE (most section labels), RED (ERRMSG). Input fields specify no COLOR (terminal/map default — typically green/default).
- **DRK fields:** `FKEY05` (`F5=Save`) and `FKEY12` (`F12=Cancel`) are dark (non-display) by default; the program reveals them by changing the attribute when those keys become valid. Initial render = invisible but text is `F5=Save` / `F12=Cancel`.
- **`Update Account` (4,33) and `Customer Details` (11,32):** unnamed literals with `COLOR=NEUTRAL` and no `ATTRB` clause (defaults apply; effectively protected display text).
- **Overlapping field at (1,1) len 9 (line 487):** an extra unnamed `DFHMDF LENGTH=9, POS=(1,1)` is defined immediately before ERRMSG. It overlaps the `Tran:` label area at the top-left. This is a known artifact in the CardDemo BMS (it carries no INITIAL and no explicit attribute); render the `Tran:` label as authoritative for (1,1). It is included in the field table above as the "(overlay field at row 1)" entry for completeness.
- **Zero-length stopper fields:** every `DFHMDF LENGTH=0` entry is an attribute-reset boundary that ends the preceding unprotected field's data area so typed input cannot bleed past it. They are unnamed, display nothing, and need no glyph; the renderer uses them only to bound the editable region of the preceding input field.
- **Layout grid:** map is exactly 24 rows x 80 cols. All POS values are 1-based (row,col) as in BMS. Account/financial section spans rows 5-10, customer section rows 11-20, messages rows 22-23, function keys row 24.

## ASCII layout sketch (initial display, approximate)

```
Row 1 : Tran:[TRNNAME]    [TITLE01..............................]  Date:mm/dd/yy
Row 2 : Prog:[PGMNAME]    [TITLE02..............................]  Time:hh:mm:ss
Row 4 :                                 Update Account
Row 5 :                   Account Number :[ACCTSID...]   Active Y/N: [_]
Row 6 :        Opened :[YEAR]-[MM]-[DD]  Credit Limit        :[ACRDLIM........]
Row 7 :        Expiry :[YEAR]-[MM]-[DD]  Cash credit Limit   :[ACSHLIM........]
Row 8 :        Reissue:[YEAR]-[MM]-[DD]  Current Balance     :[ACURBAL........]
Row 9 :                                  Current Cycle Credit:[ACRCYCR........]
Row 10:        Account Group:[AADDGRP ]  Current Cycle Debit :[ACRCYDB........]
Row 11:                               Customer Details
Row 12:        Customer id  :[ACSTNUM ]      SSN:[999]-[99]-[9999]
Row 13:        Date of birth:[YR]-[MM]-[DD]  FICO Score:[___]
Row 14: First Name                 Middle Name:           Last Name :
Row 15: [ACSFNAM................] [ACSMNAM................] [ACSLNAM...............]
Row 16: Address:[ACSADL1.....................................] State [ST]
Row 17:         [ACSADL2.....................................] Zip   [ZIPC ]
Row 18: City    [ACSCITY.....................................] Country[CTY]
Row 19: Phone 1:[A]-[B]-[C ]  Government Issued Id Ref    : [ACSGOVT............]
Row 20: Phone 2:[A]-[B]-[C ]  EFT Account Id: [ACSEFTC ] Primary Card Holder Y/N:[_]
Row 22:                       [INFOMSG informational message line........]
Row 23: [ERRMSG bright-red error message line ........................................]
Row 24: ENTER=Process F3=Exit  F5=Save F12=Cancel   (F5/F12 dark until enabled)
```
