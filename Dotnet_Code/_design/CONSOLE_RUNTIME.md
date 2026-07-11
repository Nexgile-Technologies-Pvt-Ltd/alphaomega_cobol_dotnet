# CardDemo .NET 10 — CICS/BMS Console Runtime (design note)

Goal: a faithful, pure-C# emulation of the CICS pseudo-conversational 3270 experience on a
24x80 text console. This note fixes the model so the screen-model generator (`CardDemo.Online`),
the renderer/dispatcher (`CardDemo.ConsoleApp`), and the 17 online handlers all agree. **No code yet.**

Source of truth studied: `COCOM01Y` (COMMAREA), `CSSTRPFY` (AID->PFkey map), `CSSETATY`
(error-attr helper), `COTTL01Y`/`CSDAT01Y`/`CSMSG01Y` (title/date/messages), BMS maps
`COSGN00` (signon) + `COMEN01` (menu), and program `COSGN00C` (RECEIVE/SEND/EVALUATE EIBAID/RETURN
pattern). DFHAID/DFHBMSCA are CICS-supplied (not in repo); standard 3270 byte values used below.

---

## 1. Field model (`BmsField`)

A BMS map is a `BmsMap` = ordered list of `BmsField`. Each field is one 3270 field (one attribute
byte at `pos`, then `length` data bytes). One field per `DFHMDF`. Per field:

| Property | Source (BMS) | Type / meaning |
|---|---|---|
| `Name` | label on DFHMDF (blank = literal/constant field) | string? — named fields get I/O symbolic-map vars |
| `Row` | `POS=(line,col)` line, 1-based | int 1..24 |
| `Col` | `POS=(line,col)` col, 1-based | int 1..80 |
| `Length` | `LENGTH=` | int (0 = stopper field, see §1.4) |
| `Attrib` | `ATTRB=(...)` | `BmsAttr` flags (§1.1) |
| `Color` | `COLOR=` | `BmsColor` enum (§1.2) |
| `Hilight` | `HILIGHT=` | `BmsHilight` enum: Off/Blink/Reverse/Underline |
| `Justify` | `JUSTIFY=(RIGHT/LEFT,ZERO/BLANK)` | for NUM right-justify zero-fill (OPTION field) |
| `Initial` | `INITIAL='...'` | constant text painted on SEND with ERASE |
| `Picin/Picout` | `PICIN`/`PICOUT` (none used in these 2 maps) | edit pattern (§1.5) |

The map is rendered into the 24x80 character/attribute buffer (§5). Constant (unnamed) fields are
painted once from `Initial`; named fields carry runtime values via the symbolic map (§3).

### 1.1 Attribute bits (`[Flags] enum BmsAttr`)
BMS `ATTRB=` is the 3270 field attribute. Model the meaningful, observable bits:

- `Askip`   — ASKIP: protected **and** auto-skip (cursor jumps past on type). Implies Protected.
- `Prot`    — PROT: protected, no auto-skip (operator cannot key, cursor can rest).
- `Unprot`  — UNPROT: unprotected/keyable input field (default when neither PROT/ASKIP given).
- `Num`     — NUM: numeric-only input; with `JUSTIFY=(RIGHT,ZERO)` right-justify & zero-fill on RECEIVE.
- `Brt`     — BRT: bright/high intensity.
- `Norm`    — NORM: normal intensity (default).
- `Drk`     — DRK: dark / non-display (e.g. password `PASSWD`, hidden stopper fields). Not shown but keyable.
- `Fset`    — FSET: MDT pre-set ON at SEND, so field is returned on next RECEIVE even if unkeyed (§4).
- `Ic`      — IC: insert-cursor — this field receives the cursor on SEND (one per map; §5.3).

Intensity is mutually-exclusive (`Brt`|`Norm`|`Drk`); protection is mutually-exclusive
(`Askip`|`Prot`|`Unprot`). The generator normalizes: e.g. `ASKIP` sets Protected+AutoSkip;
absence of any protection on a named field = `Unprot`.

Derived booleans used by the renderer/input loop:
`IsProtected = Askip|Prot`, `IsKeyable = !IsProtected`, `IsHidden = Drk`,
`AutoSkip = Askip`, `IsNumeric = Num`.

### 1.2 Color (`enum BmsColor`) — BMS `COLOR=`
Values seen: `BLUE, YELLOW, GREEN, RED, TURQUOISE, NEUTRAL` (NEUTRAL = white/default), plus the
full 3270 set `PINK, DEFAULT`. Map to `System.ConsoleColor` for the renderer:
BLUE->DarkBlue/Blue, YELLOW->Yellow, GREEN->Green, RED->Red, TURQUOISE->Cyan,
NEUTRAL->Gray/White, PINK->Magenta. BRT lifts the dark variants to the bright variant.

### 1.3 Hilight (`enum BmsHilight`) — BMS `HILIGHT=`
`OFF, BLINK, REVERSE, UNDERLINE`. On a console: UNDERLINE/REVERSE rendered where the terminal
supports it (ANSI SGR), else approximated; BLINK is a no-op visually but kept in the model so the
screen-parity tests can assert the *logical* attribute (the parity harness compares logical attrs,
not pixels — see ARCHITECTURE §Verification.4).

### 1.4 Stopper / zero-length fields
`LENGTH=0` fields (e.g. POS=(19,52), POS=(20,44)) carry **only** an attribute byte and no data; they
"stop" the preceding input field so keystrokes can't overflow into following text. Modeled as a
zero-data field that still emits an attribute cell at its position (bounds the keyable run in §5.2).

### 1.5 PICIN / PICOUT
None appear in COSGN00/COMEN01, but other maps use them. `PICOUT` (output editing, e.g.
`PIC ZZ,ZZ9.99`) is delegated to `CardDemo.Runtime.CobolEditedNumeric` when the handler MOVEs a
numeric to the symbolic out-field. `PICIN` (input picture) governs RECEIVE de-editing; for the
ported handlers we keep the COBOL semantics (handler validates the raw `X(n)` it receives).

---

## 2. AID / PF-key set

CICS reports the key that ended the RECEIVE in `EIBAID`. The console runtime captures a physical
keystroke, translates to a logical `Aid`, exposes it as `Eib.Aid`, and the `YYYY-STORE-PFKEY`
idiom (`CSSTRPFY`) maps it into the COMMAREA PF flags.

`enum Aid` (standard 3270 AID bytes, given for fixture/parity fidelity):

| Aid | DFHAID name | byte (hex) | Console key |
|---|---|---|---|
| Enter | DFHENTER | x'7D' | Enter |
| Clear | DFHCLEAR | x'6D' | Esc (or Ctrl+Home) |
| Pa1 | DFHPA1 | x'6C' | Ctrl+P,1 (or PageUp) |
| Pa2 | DFHPA2 | x'6E' | Ctrl+P,2 (or PageDown) |
| Pf1..Pf12 | DFHPF1..PF12 | x'F1'..x'C9'* | F1..F12 |
| Pf13..Pf24 | DFHPF13..PF24 | (PF1..12 cont.) | Shift+F1..F12 |

\*Exact PF byte table is the standard DFHAID set; the runtime only needs the enum + byte for
fixtures. **Faithful detail from `CSSTRPFY`:** PF13..PF24 are folded onto PFK01..PFK12 (the copybook
sets `CCARD-AID-PFK01` for both DFHPF1 and DFHPF13, etc.). The .NET map replicates this folding so
Shift+F3 behaves exactly like F3.

COMMAREA-side flags (`CCARD-AID-*`): `Enter, Clear, Pa1, Pa2, Pfk01..Pfk12`. The dispatcher and
each handler test these (handlers mostly check `Enter` / `Pf3=Exit` / `Pf7,Pf8=page` per the
24th-line legends, e.g. COSGN00 `ENTER=Sign-on F3=Exit`, COMEN01 `ENTER=Continue F3=Exit`).

Unmapped key on a screen -> handler "Invalid key pressed" path (CSMSG01Y `CCDA-MSG-INVALID-KEY`).

---

## 3. COMMAREA (`CardDemoCommarea`) — mirror of COCOM01Y

A POCO that exactly mirrors `01 CARDDEMO-COMMAREA` (the chained-program state carried across the
pseudo-conversational RETURN). Fields keep COBOL widths so re-serialization is faithful:

```
CardDemoCommarea (CDEMO-)
  GeneralInfo
    FromTranId   X(4)    FromProgram  X(8)
    ToTranId     X(4)    ToProgram    X(8)
    UserId       X(8)    UserType     X(1)   // 88: 'A'=Admin, 'U'=User
    PgmContext   9(1)    // 88: 0=Enter(first entry), 1=Reenter
  CustomerInfo  CustId 9(9), FName X(25), MName X(25), LName X(25)
  AccountInfo   AcctId 9(11), AcctStatus X(1)
  CardInfo      CardNum 9(16)
  MoreInfo      LastMap X(7), LastMapSet X(7)
```

Helpers: `IsAdmin`, `IsUser`, `IsFirstEntry (PgmContext==0)`, `IsReenter (PgmContext==1)`. The
shim stores/loads it as the `COMMAREA` argument of RETURN/XCTL/LINK; total length is fixed (sum of
widths). Empty COMMAREA == CICS `EIBCALEN = 0` (cold start), modeled as `Commarea == null`.

The shared screen header (top 3 lines of every map) is fed from:
`COTTL01Y` -> `TITLE01='AWS Mainframe Modernization'`, `TITLE02='CardDemo'`;
`CSDAT01Y` -> current date `mm/dd/yy` (CURDATE) and time `hh:mm:ss` (CURTIME) from `IClock`;
`TRNNAME`/`PGMNAME` from the running transaction; plus `APPLID`/`SYSID` (signon only).

---

## 4. Pseudo-conversational loop & MDT tracking

CICS pattern (from COSGN00C): each task is **one** RECEIVE + business logic + one SEND, then
`RETURN TRANSID(self) COMMAREA(...)` hands control back to the terminal and **ends the task**.
State lives only in the COMMAREA; Working-Storage is reinitialized every task.

Per-turn algorithm the runtime executes for each transaction handler:

```
1. RESTORE state : load COMMAREA (null on cold start -> EIBCALEN=0 branch).
2. REINIT WS     : fresh handler instance; WS fields start at COBOL VALUE/SPACES/LOW-VALUES.
3. EIBAID branch :
     if EIBCALEN == 0  -> first-display path:
          MOVE LOW-VALUES to map-out; set IC field length = -1 (cursor); SEND map (ERASE).
     else -> EVALUATE EIBAID:
          ENTER  -> RECEIVE map (de-edit input), run handler logic.
          PF3    -> exit/thank-you (SEND TEXT or XCTL back to caller).
          other  -> set error msg 'Invalid key pressed', re-SEND map.
4. RECEIVE map   : copy keyed cells from screen buffer into symbolic *in*-map (§4.1 MDT rule).
5. HANDLER LOGIC : validate, read/write SQL repos, set out-fields, error attrs (CSSETATY: turn
                   field RED + '*' on blank/error when CDEMO-PGM-REENTER).
6. SEND map      : paint symbolic *out*-map to buffer (ERASE clears screen first); place cursor
                   at IC field or the field with length set to -1.
7. RETURN        : persist COMMAREA; RETURN TRANSID(next) — usually self; or XCTL PROGRAM(other)
                   to chain (e.g. signon success -> XCTL COMEN01C/COADM01C by user type).
```

`XCTL` = transfer with COMMAREA, no return (program switch). `LINK` = call sub-program, returns.
`RETURN TRANSID` = end task, next keystroke re-enters that transaction. The dispatcher (§6)
implements all three over an in-process program table.

### 4.1 MDT (Modified Data Tag) tracking — the heart of RECEIVE fidelity
On a real 3270 only fields whose **MDT is on** are transmitted on RECEIVE. The runtime tracks an
`Mdt` bit per keyable field in the screen buffer:

- SEND with a field flagged `FSET` -> set that field's MDT **on** (so it returns even if the
  operator typed nothing — e.g. TRNNAME/TITLE/PGMNAME header fields are FSET).
- Operator typing into a field -> set its MDT **on**.
- `ERASE` on SEND / a fresh field attribute -> MDT **off** unless FSET re-sets it.
- RECEIVE copies into the symbolic in-map **only** fields with MDT on; unmodified non-FSET fields
  come back as `LOW-VALUES` (modeled as the field's null/`\0` sentinel), exactly as COBOL tests
  (`USERIDI = SPACES OR LOW-VALUES`). This distinction (blank vs not-entered) is preserved so the
  signon "Please enter User ID" branch reproduces faithfully.

Symbolic map per named field exposes the BMS suffixes:
`...L` (input length / cursor: -1 = put cursor here), `...F` (attribute/flag),
`...I` (input value), `...O` (output value), `...C` (color override, used by CSSETATY -> DFHRED),
`...A` (attribute override on output). The generator emits these for each named DFHMDF.

---

## 5. 24x80 text renderer

Two parallel 24x80 grids: `char[24,80] Chars` and `Cell[24,80] Attrs` (color, hilight, intensity,
protected, hidden, mdt). Origin (1,1) top-left = index [0,0].

### 5.1 Draw (SEND)
- `ERASE` -> clear both grids to space / default attribute, all MDT off.
- For each `BmsField` in map order: write the attribute cell at `(Row,Col)`, then paint `Length`
  data chars starting at `(Row,Col)` (constants from `Initial`; named fields from symbolic `...O`,
  applying `Picout`/edited-numeric and `Justify`). Apply `Color`,`Hilight`,intensity. `Drk`
  fields paint spaces (or masked) regardless of value.
- Field attribute precedence: per-turn `...A`/`...C` override (e.g. DFHRED from CSSETATY) beats the
  static BMS attribute. FSET fields get MDT set on after paint.
- Flush to the real console with ANSI/`Console` color + cursor positioning; redraw whole frame each
  SEND (no diffing needed at 24x80).

### 5.2 Read (RECEIVE)
- Position the hardware cursor at the IC field (or `...L = -1` field), default top-most keyable.
- Input loop over keyable runs (an unprotected field spans from its data start to the next
  attribute cell — a following stopper/`LENGTH=0` field bounds it). Handle: printable -> store +
  set MDT on + advance; AutoSkip at field end -> jump to next keyable; Tab/Shift-Tab -> next/prev
  keyable; Backspace; `Num` fields reject non-digits; `Drk` fields echo masked.
- Terminate on an AID key (§2); record `Eib.Aid`. Then build symbolic in-map honoring the MDT rule
  (§4.1) and `JUSTIFY=(RIGHT,ZERO)` for NUM fields (OPTION on COMEN01).

### 5.3 Cursor (IC)
Exactly one field per map normally carries `IC` (USERID on signon, OPTION on menu). On SEND the
cursor goes there unless a handler set some field's symbolic `...L = -1` (COBOL `MOVE -1 TO xxxL`),
which overrides IC — used by COSGN00C to drop the cursor on the error field
(`MOVE -1 TO USERIDL`/`PASSWDL`).

---

## 6. Dispatcher / CICS shim wiring

- `Eib` object exposes `Aid`, `CalLen` (0 on cold start), `TranId`, `CursorPos` — the bits handlers
  read. `IClock`/`IApplId`/`ISysId` feed the header + ASSIGN calls.
- Program table maps program name -> handler (`COSGN00C`,`COMEN01C`,`COADM01C`,...). Transaction
  table maps TRANSID (`CC00` signon, etc.) -> entry program.
- `RETURN TRANSID(t)` -> loop reads next keystroke, re-dispatches transaction `t` with stored
  COMMAREA. `XCTL(p)` -> replace current handler with `p`, same task, pass COMMAREA. `LINK(p)` ->
  nested call, returns to caller.
- `SEND TEXT ... ERASE FREEKB` (COSGN00C exit path) -> clear screen, print one line, RETURN with no
  TRANSID (drops to a "press a key" terminal end).

This model lets every online handler be a near-mechanical port of its COBOL `PROCEDURE DIVISION`:
the EVALUATE-EIBAID structure, COMMAREA field moves, symbolic-map I/O, and CSSETATY error styling
all have direct .NET equivalents, and the screen-parity tests (ARCHITECTURE §Verification.4) assert
field values + logical attributes + post-turn COMMAREA + next TRANSID/XCTL.
