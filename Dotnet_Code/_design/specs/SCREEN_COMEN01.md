# SCREEN SPEC — COMEN01 (Main Menu)

Source BMS: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/bms/COMEN01.bms`
Purpose: CardDemo Main Menu screen.

## Mapset

| Property | Value |
|----------|-------|
| Mapset name | `COMEN01` |
| Macro | `DFHMSD` |
| CTRL | `(ALARM,FREEKB)` — sound alarm on send; keyboard freed (FREEKB) until input enabled |
| EXTATT | `YES` (extended attributes: COLOR / HILIGHT enabled) |
| LANG | `COBOL` |
| MODE | `INOUT` |
| STORAGE | `AUTO` |
| TIOAPFX | `YES` (12-byte TIOA prefix on the generated symbolic map) |
| TYPE | `&&SYSPARM` (assembled per generation parm) |

## Map

| Property | Value |
|----------|-------|
| Map name | `COMEN1A` |
| Macro | `DFHMDI` |
| COLUMN | 1 |
| LINE | 1 |
| SIZE | (rows=24, cols=80) |

Screen is a 24-row x 80-column 3270 panel. All positions below are 1-based `(row, col)` exactly as coded in `POS=`. On a 3270 the attribute byte occupies the position immediately before the field data; the renderer should place the field text starting at the coded `POS` column.

---

## Fields (in source order)

Legend for ATTRB: ASKIP = autoskip (protected, cursor skips over it); NORM = normal intensity; BRT = bright/high intensity; PROT = protected; UNPROT = unprotected (operator may type); NUM = numeric-only; FSET = modified-data-tag set on send (field returned on next read even if untyped); IC = insert cursor (initial cursor position). DARK is not used in this map.

### 1. (filler) — "Tran:" label
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (1, 1) |
| LENGTH | 5 |
| ATTRB | ASKIP, NORM (protected, autoskip; normal intensity) |
| COLOR | BLUE |
| INITIAL | `Tran:` |
| I/O | Output / static literal |

### 2. TRNNAME — transaction id value
| Attr | Value |
|------|-------|
| Name | `TRNNAME` |
| POS | (1, 7) |
| LENGTH | 4 |
| ATTRB | ASKIP, FSET, NORM (protected, autoskip; modified-tag set) |
| COLOR | BLUE |
| INITIAL | (none) |
| I/O | Output (program-populated; protected) |

### 3. TITLE01 — title line 1
| Attr | Value |
|------|-------|
| Name | `TITLE01` |
| POS | (1, 21) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | YELLOW |
| INITIAL | (none) |
| I/O | Output (program-populated; protected) |

### 4. (filler) — "Date:" label
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (1, 65) |
| LENGTH | 5 |
| ATTRB | ASKIP, NORM |
| COLOR | BLUE |
| INITIAL | `Date:` |
| I/O | Output / static literal |

### 5. CURDATE — current date value
| Attr | Value |
|------|-------|
| Name | `CURDATE` |
| POS | (1, 71) |
| LENGTH | 8 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `mm/dd/yy` |
| I/O | Output (program overwrites with real date; protected) |

### 6. (filler) — "Prog:" label
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (2, 1) |
| LENGTH | 5 |
| ATTRB | ASKIP, NORM |
| COLOR | BLUE |
| INITIAL | `Prog:` |
| I/O | Output / static literal |

### 7. PGMNAME — program name value
| Attr | Value |
|------|-------|
| Name | `PGMNAME` |
| POS | (2, 7) |
| LENGTH | 8 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | (none) |
| I/O | Output (program-populated; protected) |

### 8. TITLE02 — title line 2
| Attr | Value |
|------|-------|
| Name | `TITLE02` |
| POS | (2, 21) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | YELLOW |
| INITIAL | (none) |
| I/O | Output (program-populated; protected) |

### 9. (filler) — "Time:" label
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (2, 65) |
| LENGTH | 5 |
| ATTRB | ASKIP, NORM |
| COLOR | BLUE |
| INITIAL | `Time:` |
| I/O | Output / static literal |

### 10. CURTIME — current time value
| Attr | Value |
|------|-------|
| Name | `CURTIME` |
| POS | (2, 71) |
| LENGTH | 8 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `hh:mm:ss` |
| I/O | Output (program overwrites with real time; protected) |

### 11. (filler) — "Main Menu" heading
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (4, 35) |
| LENGTH | 9 |
| ATTRB | ASKIP, BRT (protected, autoskip; bright/high intensity) |
| COLOR | NEUTRAL (white) |
| INITIAL | `Main Menu` |
| I/O | Output / static literal |

### 12. OPTN001 — menu option line 1
| Attr | Value |
|------|-------|
| Name | `OPTN001` |
| POS | (6, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (program-populated menu text; protected) |

### 13. OPTN002 — menu option line 2
| Attr | Value |
|------|-------|
| Name | `OPTN002` |
| POS | (7, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 14. OPTN003 — menu option line 3
| Attr | Value |
|------|-------|
| Name | `OPTN003` |
| POS | (8, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 15. OPTN004 — menu option line 4
| Attr | Value |
|------|-------|
| Name | `OPTN004` |
| POS | (9, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 16. OPTN005 — menu option line 5
| Attr | Value |
|------|-------|
| Name | `OPTN005` |
| POS | (10, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 17. OPTN006 — menu option line 6
| Attr | Value |
|------|-------|
| Name | `OPTN006` |
| POS | (11, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 18. OPTN007 — menu option line 7
| Attr | Value |
|------|-------|
| Name | `OPTN007` |
| POS | (12, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 19. OPTN008 — menu option line 8
| Attr | Value |
|------|-------|
| Name | `OPTN008` |
| POS | (13, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 20. OPTN009 — menu option line 9
| Attr | Value |
|------|-------|
| Name | `OPTN009` |
| POS | (14, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 21. OPTN010 — menu option line 10
| Attr | Value |
|------|-------|
| Name | `OPTN010` |
| POS | (15, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 22. OPTN011 — menu option line 11
| Attr | Value |
|------|-------|
| Name | `OPTN011` |
| POS | (16, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 23. OPTN012 — menu option line 12
| Attr | Value |
|------|-------|
| Name | `OPTN012` |
| POS | (17, 20) |
| LENGTH | 40 |
| ATTRB | ASKIP, FSET, NORM |
| COLOR | BLUE |
| INITIAL | `' '` (single blank) |
| I/O | Output (protected) |

### 24. (filler) — "Please select an option :" prompt
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (20, 15) |
| LENGTH | 25 |
| ATTRB | ASKIP, BRT (protected, autoskip; bright) |
| COLOR | TURQUOISE |
| INITIAL | `Please select an option :` |
| I/O | Output / static literal |

### 25. OPTION — option entry field  ← INPUT FIELD / CURSOR
| Attr | Value |
|------|-------|
| Name | `OPTION` |
| POS | (20, 41) |
| LENGTH | 2 |
| ATTRB | FSET, IC, NORM, NUM, UNPROT (unprotected, numeric-only, normal intensity; modified-tag set; **insert cursor positioned here**) |
| COLOR | (none coded — terminal default) |
| HILIGHT | UNDERLINE |
| JUSTIFY | (RIGHT, ZERO) — right-justified, zero-filled |
| INITIAL | (none) |
| I/O | **INPUT** (the only unprotected/operator-enterable field) |
| Cursor | **YES — IC (initial cursor) is on this field** |

### 26. (filler) — stopper field after OPTION
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (20, 44) |
| LENGTH | 0 |
| ATTRB | ASKIP, NORM |
| COLOR | GREEN |
| INITIAL | (none) |
| I/O | Output / attribute "stopper" (LENGTH=0 — places a protected ASKIP attribute byte at col 44 to terminate the OPTION input field; no displayable data) |

### 27. ERRMSG — error message line
| Attr | Value |
|------|-------|
| Name | `ERRMSG` |
| POS | (23, 1) |
| LENGTH | 78 |
| ATTRB | ASKIP, BRT, FSET (protected, autoskip; bright; modified-tag set) |
| COLOR | RED |
| INITIAL | (none) |
| I/O | Output (program-populated error text; protected) |

### 28. (filler) — function-key footer
| Attr | Value |
|------|-------|
| Name | (unnamed / filler) |
| POS | (24, 1) |
| LENGTH | 23 |
| ATTRB | ASKIP, NORM |
| COLOR | YELLOW |
| INITIAL | `ENTER=Continue  F3=Exit` (note: two spaces between "Continue" and "F3=Exit") |
| I/O | Output / static literal |

---

## Input vs Output Summary

- **Input (UNPROT) field — exactly one:** `OPTION` at (20,41), LENGTH=2, NUM, right-justified + zero-filled, underline highlight.
- **Cursor (IC):** on `OPTION` (20,41) — the only IC in the map; cursor lands here on display.
- **Output / protected fields (all others):** every other field is ASKIP (autoskip, protected). Program-populated output fields: `TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `OPTN001`–`OPTN012`, `ERRMSG`. Static literal labels: "Tran:", "Date:", "Prog:", "Time:", "Main Menu", "Please select an option :", and the F-key footer "ENTER=Continue  F3=Exit".
- **Stopper:** unnamed LENGTH=0 ASKIP field at (20,44) (GREEN) bounds the OPTION input to 2 bytes.

## Highlight / Justify Notes

- `HILIGHT=UNDERLINE` applies only to `OPTION`.
- `JUSTIFY=(RIGHT,ZERO)` applies only to `OPTION` (right-justify entry, pad with zeros).
- No DARK fields in this map. BRT (bright) fields: "Main Menu" (4,35), "Please select an option :" (20,15), and `ERRMSG` (23,1). All other display fields are NORM intensity.

## Color Map (for renderer)

| Color keyword | Used by |
|---------------|---------|
| BLUE | Tran:/Date:/Prog:/Time: labels, TRNNAME, CURDATE, PGMNAME, CURTIME, OPTN001–OPTN012 |
| YELLOW | TITLE01, TITLE02, footer line |
| NEUTRAL (white) | "Main Menu" heading |
| TURQUOISE | "Please select an option :" prompt |
| GREEN | LENGTH=0 stopper at (20,44) |
| RED | ERRMSG |
| (default) | OPTION input field (no COLOR coded) |

## Named field count

20 named fields: `TRNNAME`, `TITLE01`, `CURDATE`, `PGMNAME`, `TITLE02`, `CURTIME`, `OPTN001`, `OPTN002`, `OPTN003`, `OPTN004`, `OPTN005`, `OPTN006`, `OPTN007`, `OPTN008`, `OPTN009`, `OPTN010`, `OPTN011`, `OPTN012`, `OPTION`, `ERRMSG`.

(Plus 8 unnamed filler/literal DFHMDF entries — 6 visible labels, 1 stopper, 1 footer — for 28 total DFHMDF definitions.)
