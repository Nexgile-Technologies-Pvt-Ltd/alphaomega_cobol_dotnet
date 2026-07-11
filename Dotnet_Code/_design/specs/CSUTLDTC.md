# PORT SPEC — CSUTLDTC (date-validation subroutine via CEEDAYS)

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CSUTLDTC.cbl`
Callers (COBOL `CALL 'CSUTLDTC'`):
- `app/cpy/CSUTLDPY.cpy` paragraph `EDIT-DATE-LE` (CSUTLDPY.cpy:293-296) — used by all programs that COPY the
  `CSUTLDPY` date-edit procedure copybook.
- `app/cbl/COTRN02C.cbl` (COTRN02C.cbl:393-395 orig date, COTRN02C.cbl:413-415 proc date) — Add-Transaction screen.
- `app/cbl/CORPT00C.cbl` (CORPT00C.cbl:392-394 start date, CORPT00C.cbl:412-414 end date) — Transaction-Report screen.
Target: `New_Dotnet_Code/src/CardDemo.Batch/` (or a shared `CardDemo.Runtime` date service — see §7) — per `_design/ARCHITECTURE.md`.
Program kind: **util** (called subprogram / "sub"). No screen, no transaction, no file I/O, no DB2/IMS/MQ.

---

## 1. Purpose

`CSUTLDTC` is a thin COBOL wrapper around the **Language Environment (LE) callable service `CEEDAYS`**.
Given a date string and a date-format/picture string, it asks `CEEDAYS` to convert the date to a Lillian
day number; it does **not** use the Lillian result for anything — it inspects the `CEEDAYS` **feedback
code** purely to decide whether the supplied date is valid, builds a fixed-layout 80-byte human-readable
result message (severity, message number, a 15-char verdict text, the test date, and the mask used), and
returns that message plus the LE severity in `RETURN-CODE`. In effect it is a **single-date validator**:
"is this date string parseable under this mask?". // source: CSUTLDTC.cbl:1-2 (header "CALL TO CEEDAYS"),
CSUTLDTC.cbl:20 (PROGRAM-ID), CSUTLDTC.cbl:116-120 (CALL CEEDAYS), CSUTLDTC.cbl:128-149 (verdict mapping).

### How it is invoked

- It is a **called subprogram** (`CALL 'CSUTLDTC' USING ...`), NOT a JCL step and NOT a CICS transaction.
  // source: CSUTLDTC.cbl:88 (`PROCEDURE DIVISION USING LS-DATE, LS-DATE-FORMAT, LS-RESULT`).
- **Three parameters, all BY REFERENCE**, in this exact order:
  1. `LS-DATE` `PIC X(10)` — the date string to validate. // source: CSUTLDTC.cbl:84
  2. `LS-DATE-FORMAT` `PIC X(10)` — the CEEDAYS picture/mask (e.g. `'YYYY-MM-DD'`, `'YYYYMMDD'`). // source: CSUTLDTC.cbl:85
  3. `LS-RESULT` `PIC X(80)` — OUTPUT: the formatted result message. // source: CSUTLDTC.cbl:86
- On return it also sets the COBOL special register `RETURN-CODE` to the numeric severity (`WS-SEVERITY-N`).
  // source: CSUTLDTC.cbl:98.
- It ends with `EXIT PROGRAM` (returns to caller), not `STOP RUN`/`GOBACK` (GOBACK is commented out).
  // source: CSUTLDTC.cbl:100-101.

### Caller contract (how the result is consumed)

The two online callers (and the `CSUTLDPY` copybook) **only read the first 8 bytes of the result** and a
4-byte message number, via the redefining layout `CSUTLDTC-RESULT`:
- `CSUTLDTC-RESULT-SEV-CD PIC X(04)` = bytes 1-4 = the severity text. They test `= '0000'` for "valid".
  // source: COTRN02C.cbl:66, COTRN02C.cbl:397; CORPT00C.cbl:133, CORPT00C.cbl:396.
- `FILLER PIC X(11)` = bytes 5-15.
- `CSUTLDTC-RESULT-MSG-NUM PIC X(04)` = bytes 16-19 = the message number; callers special-case `'2513'`.
  // source: COTRN02C.cbl:68, COTRN02C.cbl:400; CORPT00C.cbl:135, CORPT00C.cbl:399.
- `CSUTLDTC-RESULT-MSG PIC X(61)` = bytes 20-80 (the rest of the message; callers ignore it).
  // source: COTRN02C.cbl:69; CORPT00C.cbl:136.

> Caller alignment note: this 4/11/4/61 caller view of the 80-byte result MUST line up with the producer's
> `WS-MESSAGE` layout (see §3) so that bytes 1-4 = severity and bytes 16-19 = message number. The producer's
> `WS-SEVERITY X(4)` occupies bytes 1-4. The producer's `WS-MSG-NO X(4)` occupies bytes 16-19 (4 + 11 = 15
> bytes precede it: `WS-SEVERITY` 4 + the literal `'Mesg Code:'` FILLER X(11)). The port must keep this byte
> geometry exact. // source: CSUTLDTC.cbl:42-57 vs COTRN02C.cbl:65-69.

---

## 2. FILE / TABLE access

**None.** `CSUTLDTC` has no `ENVIRONMENT DIVISION`, no `FILE-CONTROL`, no `FD`, no SELECT, and touches no
relational table. It performs no VSAM/QSAM I/O and no database access. The repository contract in
ARCHITECTURE.md §"VSAM-semantics -> SQL" does not apply.

| COBOL resource | Direction | Relational table | Operation | SQL |
|---|---|---|---|---|
| `CEEDAYS` (LE callable service) | call | — (system date service) | `CALL "CEEDAYS" USING ...` | — |

> Nothing maps to any of the 11 base-app tables or any optional-module table.

---

## 3. DATA layout (WORKING-STORAGE + LINKAGE)

### LINKAGE (the three parameters)
| COBOL field | PIC | Dir | C# type | Notes |
|---|---|---|---|---|
| `LS-DATE` | `X(10)` | in | `string` (10) | date string to validate. // source: CSUTLDTC.cbl:84 |
| `LS-DATE-FORMAT` | `X(10)` | in | `string` (10) | CEEDAYS mask. // source: CSUTLDTC.cbl:85 |
| `LS-RESULT` | `X(80)` | out | `string` (80) | formatted message (see WS-MESSAGE). // source: CSUTLDTC.cbl:86 |

### `WS-DATE-TO-TEST` / `WS-DATE-FORMAT` — CEEDAYS VSTRING (length-prefixed string)
Both are LE "VSTRING" structures: a `PIC S9(4) BINARY` length followed by a variable-length character array
`OCCURS 0 TO 256 DEPENDING ON` that length. // source: CSUTLDTC.cbl:25-39.
| Field | PIC / USAGE | Meaning | C# |
|---|---|---|---|
| `WS-DATE-TO-TEST.Vstring-length` | `S9(4) BINARY` | length of the date text (set to `LENGTH OF LS-DATE` = 10) | `short` |
| `WS-DATE-TO-TEST.Vstring-text` (`Vstring-char OCCURS 0..256 DEPENDING ON length`) | `X` ×len | the date chars | `string` |
| `WS-DATE-FORMAT.Vstring-length` | `S9(4) BINARY` | length of the mask (set to `LENGTH OF LS-DATE-FORMAT` = 10) | `short` |
| `WS-DATE-FORMAT.Vstring-text` | `X` ×len | the mask chars | `string` |

> Both VSTRING lengths are hard-set to the LINKAGE field LENGTHs (10 and 10) — see §4 steps 1-2. So in
> practice both VSTRINGs are always exactly 10 chars long regardless of trailing spaces. This is a faithful
> behavior to preserve (see §6 bug #2). // source: CSUTLDTC.cbl:105-113.

### `OUTPUT-LILLIAN`
`PIC S9(9) BINARY` — CEEDAYS output Lillian day number. **Set to 0 before the call and never read after.**
// source: CSUTLDTC.cbl:41, CSUTLDTC.cbl:114.

### `WS-MESSAGE` (the 80-byte result template) — exact byte layout
// source: CSUTLDTC.cbl:42-57.
| Bytes | Field | PIC | VALUE | Notes |
|---|---|---|---|---|
| 1-4 | `WS-SEVERITY` | `X(04)` | — | severity text; redefined as `WS-SEVERITY-N PIC 9(4)` (numeric). |
| (1-4) | `WS-SEVERITY-N` REDEFINES `WS-SEVERITY` | `9(4)` | — | numeric view, receives `SEVERITY OF FEEDBACK-CODE`. |
| 5-15 | FILLER | `X(11)` | `'Mesg Code:'` | literal label (10 chars + 1 trailing space → 11). |
| 16-19 | `WS-MSG-NO` | `X(04)` | — | message-number text; redefined as `WS-MSG-NO-N PIC 9(4)`. |
| (16-19) | `WS-MSG-NO-N` REDEFINES `WS-MSG-NO` | `9(4)` | — | numeric view, receives `MSG-NO OF FEEDBACK-CODE`. |
| 20 | FILLER | `X(01)` | SPACE | |
| 21-35 | `WS-RESULT` | `X(15)` | — | 15-char verdict text (see §5). |
| 36 | FILLER | `X(01)` | SPACE | |
| 37-45 | FILLER | `X(09)` | `'TstDate:'` | label (8 chars + 1 space → 9). |
| 46-55 | `WS-DATE` | `X(10)` | SPACES | echoes the tested date. |
| 56 | FILLER | `X(01)` | SPACE | |
| 57-66 | FILLER | `X(10)` | `'Mask used:'` | label. |
| 67-76 | `WS-DATE-FMT` | `X(10)` | — | echoes the mask used. |
| 77 | FILLER | `X(01)` | SPACE | |
| 78-80 | FILLER | `X(03)` | SPACES | |

Total = 4+11+4+1+15+1+9+10+1+10+1+3 = **80 bytes** = `LS-RESULT`.

### `FEEDBACK-CODE` (CEEDAYS feedback / condition token, 12 bytes)
// source: CSUTLDTC.cbl:60-80.
| Field | PIC / USAGE | Notes |
|---|---|---|
| `FEEDBACK-TOKEN-VALUE` | group (8 bytes) | the LE 8-byte condition token; carries 88-level value tests (see below). |
| ↳ `CASE-1-CONDITION-ID.SEVERITY` | `S9(4) BINARY` | LE severity (halfword). Read into `WS-SEVERITY-N`. |
| ↳ `CASE-1-CONDITION-ID.MSG-NO` | `S9(4) BINARY` | LE message number (halfword). Read into `WS-MSG-NO-N`. |
| ↳ `CASE-2-CONDITION-ID` REDEFINES `CASE-1-CONDITION-ID` (`CLASS-CODE`, `CAUSE-CODE` both `S9(4) BINARY`) | alt view of first 4 bytes. |
| ↳ `CASE-SEV-CTL` | `X` | severity-control byte. |
| ↳ `FACILITY-ID` | `XXX` | 3-char facility id (e.g. `CEE`). |
| `I-S-INFO` | `S9(9) BINARY` | instance-specific information (last 4 bytes of the 12-byte token). |

**88-level feedback values** (tested against the full 8-byte `FEEDBACK-TOKEN-VALUE`):
// source: CSUTLDTC.cbl:62-70.
| 88 condition | Hex value (8 bytes) | Meaning |
|---|---|---|
| `FC-INVALID-DATE` | `X'0000000000000000'` | all-zero token = CEEDAYS success → **date is VALID**. |
| `FC-INSUFFICIENT-DATA` | `X'000309CB59C3C5C5'` | not enough data. |
| `FC-BAD-DATE-VALUE` | `X'000309CC59C3C5C5'` | bad date value. |
| `FC-INVALID-ERA` | `X'000309CD59C3C5C5'` | invalid era. |
| `FC-UNSUPP-RANGE` | `X'000309D159C3C5C5'` | unsupported range. |
| `FC-INVALID-MONTH` | `X'000309D559C3C5C5'` | invalid month. |
| `FC-BAD-PIC-STRING` | `X'000309D659C3C5C5'` | bad picture string. |
| `FC-NON-NUMERIC-DATA` | `X'000309D859C3C5C5'` | non-numeric data. |
| `FC-YEAR-IN-ERA-ZERO` | `X'000309D959C3C5C5'` | year-in-era is zero. |

> Note the **misnamed** 88: `FC-INVALID-DATE` is the **all-zeros = success** token, i.e. it actually means
> "date is VALID". The verdict text for it is `'Date is valid'`. This is a faithful naming quirk, not a bug
> in logic. // source: CSUTLDTC.cbl:62 vs CSUTLDTC.cbl:129-130.
>
> The trailing `59C3C5C5` in the error tokens = EBCDIC for severity-control `0x59` + facility `C3C5C5` =
> `"CEE"`. The leading `000309xx` encodes severity 3 (error) + the LE message number (e.g. `0x09CB` = 2507,
> `0x09D9` = 2521). These are EBCDIC byte patterns and must be matched as raw 8-byte values, NOT re-encoded.

---

## 4. PARAGRAPH-BY-PARAGRAPH outline

Three procedure bodies: the unnamed mainline (the body that follows `PROCEDURE DIVISION USING ...`),
`A000-MAIN`, and `A000-MAIN-EXIT`. Port as methods preserving statement order.

### Mainline (entry body) — method `Run(string lsDate, string lsDateFormat, out string lsResult, out int returnCode)`
// source: CSUTLDTC.cbl:88-102.
1. `INITIALIZE WS-MESSAGE` — reset the 80-byte template: alphanumeric items → spaces, numeric items
   (`WS-SEVERITY-N`, `WS-MSG-NO-N` via REDEFINES — but INITIALIZE acts on the *named* group items) → see
   note. The FILLER literal labels (`'Mesg Code:'`, `'TstDate:'`, `'Mask used:'`) are **not** re-applied by
   INITIALIZE because FILLERs are untouched by INITIALIZE — they retain their VALUE from program load.
   // source: CSUTLDTC.cbl:90. (See §6 / §7 for the INITIALIZE-vs-VALUE subtlety.)
2. `MOVE SPACES TO WS-DATE` — blank the echoed test-date field. // source: CSUTLDTC.cbl:91.
3. `PERFORM A000-MAIN THRU A000-MAIN-EXIT` — do the conversion + verdict. // source: CSUTLDTC.cbl:93-94.
4. `MOVE WS-MESSAGE TO LS-RESULT` — return the 80-byte formatted message to the caller. // source: CSUTLDTC.cbl:97.
5. `MOVE WS-SEVERITY-N TO RETURN-CODE` — set RETURN-CODE to the numeric severity. // source: CSUTLDTC.cbl:98.
6. `EXIT PROGRAM` — return to caller. // source: CSUTLDTC.cbl:100.

### `A000-MAIN` — method `A000Main()`
// source: CSUTLDTC.cbl:103-151.
1. `MOVE LENGTH OF LS-DATE TO VSTRING-LENGTH OF WS-DATE-TO-TEST` — set the VSTRING length to 10.
   // source: CSUTLDTC.cbl:105-106.
2. `MOVE LS-DATE TO VSTRING-TEXT OF WS-DATE-TO-TEST, WS-DATE` — copy the date into the VSTRING text AND into
   the echoed `WS-DATE` field (multi-receiver MOVE). // source: CSUTLDTC.cbl:107-108.
3. `MOVE LENGTH OF LS-DATE-FORMAT TO VSTRING-LENGTH OF WS-DATE-FORMAT` — set mask VSTRING length to 10.
   // source: CSUTLDTC.cbl:109-110.
4. `MOVE LS-DATE-FORMAT TO VSTRING-TEXT OF WS-DATE-FORMAT, WS-DATE-FMT` — copy the mask into the VSTRING text
   AND into the echoed `WS-DATE-FMT` field (multi-receiver MOVE). // source: CSUTLDTC.cbl:111-113.
5. `MOVE 0 TO OUTPUT-LILLIAN` — clear the Lillian output. // source: CSUTLDTC.cbl:114.
6. `CALL "CEEDAYS" USING WS-DATE-TO-TEST, WS-DATE-FORMAT, OUTPUT-LILLIAN, FEEDBACK-CODE` — invoke the LE
   service; it sets `OUTPUT-LILLIAN` (ignored) and `FEEDBACK-CODE`. // source: CSUTLDTC.cbl:116-120.
7. `MOVE WS-DATE-TO-TEST TO WS-DATE` — re-copy the VSTRING (length halfword + text) into `WS-DATE X(10)`.
   **Group move** of a structure whose first 2 bytes are the binary length → the echoed `WS-DATE` may be
   corrupted (see §6 bug #1). // source: CSUTLDTC.cbl:122.
8. `MOVE SEVERITY OF FEEDBACK-CODE TO WS-SEVERITY-N` — store severity (binary→`9(4)`). // source: CSUTLDTC.cbl:123.
9. `MOVE MSG-NO OF FEEDBACK-CODE TO WS-MSG-NO-N` — store message number (binary→`9(4)`). // source: CSUTLDTC.cbl:124.
10. `EVALUATE TRUE` over the feedback 88s → `MOVE <verdict text> TO WS-RESULT`; `WHEN OTHER` → `'Date is invalid'`.
    // source: CSUTLDTC.cbl:128-149. (Verdict text table in §5.)

### `A000-MAIN-EXIT` — method (no-op / label)
`EXIT.` — return target of the `PERFORM ... THRU`. // source: CSUTLDTC.cbl:152-154.

**COMPUTE / arithmetic:** there are **no COMPUTE statements**. The only numeric conversions are the binary
`S9(4)`→`9(4)` MOVEs at lines 123-124 (severity, msg-no) and the `LENGTH OF` MOVEs at 105-106/109-110.
No rounding, no signed-zero handling, no truncation beyond the implicit halfword→4-digit move (values fit).

---

## 5. VALIDATION RULES and literal messages

CSUTLDTC itself performs **no field-level validation logic**; it delegates the decision to CEEDAYS and maps
the feedback code to one of ten fixed 15-char verdict strings. The exact literals (note embedded trailing
spaces to pad to 15) — // source: CSUTLDTC.cbl:128-149:

| Feedback condition (88) | Verdict text moved to `WS-RESULT` (X15) | Source line |
|---|---|---|
| `FC-INVALID-DATE` (all-zeros = success) | `'Date is valid'` | CSUTLDTC.cbl:130 |
| `FC-INSUFFICIENT-DATA` | `'Insufficient'` | CSUTLDTC.cbl:132 |
| `FC-BAD-DATE-VALUE` | `'Datevalue error'` | CSUTLDTC.cbl:134 |
| `FC-INVALID-ERA` | `'Invalid Era    '` | CSUTLDTC.cbl:136 |
| `FC-UNSUPP-RANGE` | `'Unsupp. Range  '` | CSUTLDTC.cbl:138 |
| `FC-INVALID-MONTH` | `'Invalid month  '` | CSUTLDTC.cbl:140 |
| `FC-BAD-PIC-STRING` | `'Bad Pic String '` | CSUTLDTC.cbl:142 |
| `FC-NON-NUMERIC-DATA` | `'Nonnumeric data'` | CSUTLDTC.cbl:144 |
| `FC-YEAR-IN-ERA-ZERO` | `'YearInEra is 0 '` | CSUTLDTC.cbl:146 |
| `WHEN OTHER` (any other token) | `'Date is invalid'` | CSUTLDTC.cbl:148 |

> These verdict strings are the literal contract. Reproduce them **byte-for-byte including the trailing
> spaces** so the 80-byte result image is identical. Note `'Date is valid'` is 13 chars (padded to 15 with
> 2 trailing spaces by COBOL MOVE to X(15)); `'Insufficient'` is 12 chars (padded to 15). The ones with
> explicit trailing spaces in the source (e.g. `'Invalid Era    '`) are already 15.

### Caller-side rules (context, for completeness — implemented in the callers, not here)
- Callers treat **severity `'0000'`** (bytes 1-4) as VALID; anything else as error. // source: COTRN02C.cbl:397; CORPT00C.cbl:396.
- Callers **suppress** the error when **message number = `'2513'`** (bytes 16-19) — i.e. they tolerate that
  specific CEEDAYS condition (LE msg 2513). The error message they then show is e.g.
  `'Orig Date - Not a valid date...'`, `'Proc Date - Not a valid date...'`,
  `'Start Date - Not a valid date...'`, `'End Date - Not a valid date...'`.
  // source: COTRN02C.cbl:400-401, COTRN02C.cbl:420-421; CORPT00C.cbl:399-400, CORPT00C.cbl:419-420.
- `CSUTLDPY.cpy` `EDIT-DATE-LE` instead checks `WS-SEVERITY-N = 0` and on non-zero builds
  `'<name> validation error Sev code: <sev> Message code: <msgno>'`. // source: CSUTLDPY.cpy:298-314.

---

## 6. FAITHFUL BUGS / quirks to reproduce verbatim (do NOT fix)

1. **`MOVE WS-DATE-TO-TEST TO WS-DATE` after the call copies the VSTRING *length halfword* into the echoed
   date.** `WS-DATE-TO-TEST` is a VSTRING group whose first 2 bytes are `Vstring-length PIC S9(4) BINARY`
   (value 10 = `X'000A'`), followed by the text. Moving the whole group (a group MOVE, alphanumeric, left
   to right, no de-edit) into `WS-DATE X(10)` puts those 2 raw binary length bytes (`X'000A'`, i.e. two
   non-printable/low-value bytes) into the first 2 positions of the echoed `WS-DATE`, then the first 8 chars
   of the date text. So the "TstDate:" portion of the result message is shifted/garbled by 2 bytes, not the
   clean 10-char date. The earlier line 108 had already moved the clean `LS-DATE` into `WS-DATE`; line 122
   overwrites it with the VSTRING image. Reproduce this exact corruption — do not "fix" it to echo the clean
   date. // source: CSUTLDTC.cbl:122 (vs CSUTLDTC.cbl:25-31, CSUTLDTC.cbl:108).

2. **VSTRING length hard-pinned to 10 (`LENGTH OF` the LINKAGE fields), ignoring trailing spaces.** Both
   `Vstring-length` values are set to the fixed field LENGTH (10), never to the trimmed length of the actual
   date/mask. CEEDAYS therefore always receives a 10-char input even when the caller passed a shorter date
   or a mask like `'YYYYMMDD'` (8 significant chars + 2 trailing spaces). The trailing spaces become part of
   the picture string / date and influence CEEDAYS' result. Keep this — do not trim. // source: CSUTLDTC.cbl:105-106, CSUTLDTC.cbl:109-110.

3. **Severity returned via `RETURN-CODE` is the LE severity number, but only `WS-SEVERITY-N` (X4 numeric
   view) is moved.** `MOVE WS-SEVERITY-N TO RETURN-CODE` (CSUTLDTC.cbl:98) — the severity comes from
   `WS-SEVERITY-N`, a `PIC 9(4)` REDEFINES of the 4-byte `WS-SEVERITY`. The producer set `WS-SEVERITY-N`
   from the binary `SEVERITY OF FEEDBACK-CODE`. For success the token is all zeros so severity = 0 →
   RETURN-CODE 0. For errors LE severity is typically 3. Preserve the numeric path exactly. // source: CSUTLDTC.cbl:98, CSUTLDTC.cbl:123.

4. **`OUTPUT-LILLIAN` is computed by CEEDAYS but never used.** Dead output. Keep the call signature (CEEDAYS
   requires the arg) but the value is discarded. // source: CSUTLDTC.cbl:41, CSUTLDTC.cbl:114, CSUTLDTC.cbl:119.

5. **Misnamed `FC-INVALID-DATE` 88-level actually means "valid".** The all-zeros feedback token (CEEDAYS
   success) is named `FC-INVALID-DATE` yet maps to the verdict `'Date is valid'`. Do not rename in a way
   that changes the mapping; the all-zero token → "valid" branch is correct behavior. // source: CSUTLDTC.cbl:62, CSUTLDTC.cbl:129-130.

6. **`INITIALIZE WS-MESSAGE` does not restore the FILLER literal labels.** Per COBOL rules INITIALIZE leaves
   FILLER items untouched (and re-applies VALUE only to the named items it processes). The labels
   `'Mesg Code:'`, `'TstDate:'`, `'Mask used:'` survive only because they were never overwritten (they hold
   their load-time VALUE). The port must guarantee those literal labels appear at their fixed byte offsets in
   every result, matching the COBOL image. // source: CSUTLDTC.cbl:90, CSUTLDTC.cbl:45/51/54.

7. **`GOBACK` is commented out; uses `EXIT PROGRAM`.** Behaviorally a subprogram return either way, but note
   the source deliberately uses `EXIT PROGRAM`. // source: CSUTLDTC.cbl:100-101.

---

## 7. PORT NOTES (relational-access translation plan + COBOL semantics)

### Relational access
- **Nothing to translate.** No file, no table, no DbContext, no repository. §2 is intentionally empty.

### Where it lives in the target
- Implement as a shared utility/date service callable by both batch and online ports (it is invoked from
  online programs COTRN02C/CORPT00C and from the CSUTLDPY copybook used widely). A natural home is
  `CardDemo.Runtime` as a `CsutldtcDateValidator` (pure C#), referenced by `CardDemo.Online` and
  `CardDemo.Batch`. // ARCHITECTURE.md "src/CardDemo.Runtime", "src/CardDemo.Online", "src/CardDemo.Batch".

### Reproducing CEEDAYS without LE
There is **no LE/CEEDAYS** in .NET. The port must emulate CEEDAYS' date-parse-and-validate behavior for the
masks actually used by callers and return an equivalent feedback model. Observed masks in callers:
- `'YYYY-MM-DD'` — COTRN02C (WS-DATE-FORMAT), and CSUTLDPY passes... actually `'YYYYMMDD'`. // source: COTRN02C.cbl:60; CSUTLDPY.cpy:291.
- `'YYYYMMDD'` — CSUTLDPY EDIT-DATE-LE. // source: CSUTLDPY.cpy:291.
- `'YYYY-MM-DD'` / `'YYYYMMDD'` also from CORPT00C build (`WS-DATE-FORMAT`). 
Plan:
1. Build the 10-char date and 10-char mask exactly as the COBOL does (no trimming — bug #2), including
   trailing spaces. 
2. Map the mask to a parse pattern (`YYYY`→year, `MM`→month, `DD`→day; literal separators like `-` matched
   positionally; trailing spaces are literals that must also "match" — emulate CEEDAYS leniency/strictness
   from captured fixtures, not assumptions).
3. Produce a **feedback result object** carrying `Severity` (int), `MsgNo` (int), and the equivalent 8-byte
   token classification so the verdict `EVALUATE` (§5) selects the same branch. The simplest faithful model:
   - success → severity 0, msgNo 0, token = all-zeros → `'Date is valid'`.
   - failure → severity 3 and the LE message number that CEEDAYS would have returned (e.g. 2513 is the one
     callers special-case; 2507/2508/etc. for the other conditions) → matching verdict text.
   Because callers branch on **severity bytes `'0000'`** and **msg-num bytes `'2513'`**, the emulated msgNo
   must at minimum reproduce the **valid/invalid** decision and emit **2513** for the exact condition the
   real CEEDAYS does for the date strings in the golden fixtures. This MUST be pinned by characterization
   tests against captured outputs, not invented. (See §8.)

### Exact byte image of the 80-byte result
The result is consumed positionally by callers (bytes 1-4 severity, 16-19 msg-num) AND, in the wider system,
may be displayed. Build `LS-RESULT` as the fixed 80-byte layout in §3:
- bytes 1-4: severity as 4-digit zero-filled numeric text (from `WS-SEVERITY-N PIC 9(4)`; e.g. `0000`/`0003`).
- bytes 5-15: literal `"Mesg Code: "` (the X(11) FILLER value `'Mesg Code:'` left-justified → `Mesg Code:` + 1 space).
- bytes 16-19: msg-num as 4-digit zero-filled numeric text (from `WS-MSG-NO-N PIC 9(4)`).
- byte 20: space.
- bytes 21-35: the 15-char verdict text (left-justified, space-padded).
- byte 36: space.
- bytes 37-45: literal `"TstDate: "`.
- bytes 46-55: echoed test date — **reproduce the VSTRING-image corruption from bug #1** (2 binary length
  bytes then 8 chars of the date). For a pure-.NET model decide whether to emit the literal corrupted bytes
  or a documented stand-in; default plan = reproduce the 2 leading low-value bytes (`0x00 0x0A`) then 8 chars,
  and pin with a test, because the field is part of the diffed 80-byte image.
- byte 56: space.
- bytes 57-66: literal `"Mask used:"`.
- bytes 67-76: echoed mask (`WS-DATE-FMT`, the clean 10-char mask from line 113).
- byte 77: space.
- bytes 78-80: spaces.

### COBOL semantics to honor faithfully
- **`INITIALIZE WS-MESSAGE`** → set named alphanumeric items to spaces, named numeric items to 0; leave the
  FILLER literal labels at their VALUE (bug #6). Use the Runtime fixed-width record helper so the byte image
  is exact.
- **Multi-receiver `MOVE`** (lines 107-108, 111-113) → copy source into BOTH receivers; each receiver gets
  COBOL's per-receiver alphanumeric MOVE (left-justify, space-pad/truncate to its own width).
- **Group MOVE of a VSTRING into `X(10)`** (line 122) → byte-copy including the 2 binary length bytes
  (bug #1). Do NOT format/trim.
- **Binary `S9(4)`→`9(4)` MOVE** (lines 123-124) → take the numeric value of the LE halfword and store as a
  4-digit zero-padded numeric (no sign in the X(4) display view). Values fit in 4 digits.
- **`MOVE WS-SEVERITY-N TO RETURN-CODE`** → return the severity as the subprogram return code (int).
- **REDEFINES** (`WS-SEVERITY`/`WS-SEVERITY-N`, `WS-MSG-NO`/`WS-MSG-NO-N`, `CASE-1`/`CASE-2-CONDITION-ID`) →
  in C# model the numeric and text views as two projections over the same 4 bytes; the producer writes the
  numeric view, callers read the text view — keep them coherent.
- **OCCURS DEPENDING ON** (VSTRING) → represent as a length + a 10-char buffer; length is always pinned to 10
  (bug #2).
- **88-level token tests** → compare the emulated 8-byte token (or, equivalently, the (severity, msgNo) pair)
  against the table in §3 to drive the verdict `EVALUATE`.

---

## 8. OPEN QUESTIONS / risks

1. **CEEDAYS exact behavior is the whole program.** The validity decision, the precise LE **message numbers**
   (especially the `2513` the callers tolerate), and how CEEDAYS treats the 2 trailing spaces in a 10-char
   mask/date are all defined by the (absent) LE service. These MUST be reproduced from captured golden
   fixtures / characterization tests, not guessed. Which LE message number corresponds to which 88 token, and
   what `2513` specifically means for the caller inputs, needs pinning. // source: CSUTLDTC.cbl:62-70; COTRN02C.cbl:400; CORPT00C.cbl:399.
2. **The echoed `TstDate:` corruption (bug #1).** Decide and pin whether the 80-byte image test reproduces the
   raw `X'000A'` + 8-char form. Since callers only read bytes 1-4 and 16-19, the corruption is cosmetic for
   the validity decision but matters for a byte-exact result diff. // source: CSUTLDTC.cbl:122.
3. **Severity/msg-num numeric width.** `WS-SEVERITY-N`/`WS-MSG-NO-N` are `9(4)`; if LE ever returned a value
   > 9999 it would truncate. In practice severity ∈ {0,3} and msg-num is small. Confirm no >4-digit message
   numbers occur in the corpus. // source: CSUTLDTC.cbl:44, CSUTLDTC.cbl:47.
4. **Mask set used in practice.** Only `'YYYY-MM-DD'` and `'YYYYMMDD'` are seen in the three call sites; the
   emulator only needs those plus whatever the golden fixtures exercise. // source: COTRN02C.cbl:60; CSUTLDPY.cpy:291.

---

## 9. Coverage hooks (for the verification matrix)

- Unit: valid date+mask (e.g. `'2024-01-15'`,`'YYYY-MM-DD'`) → severity `'0000'`, verdict `'Date is valid'`,
  RETURN-CODE 0; assert full 80-byte result image. // source: CSUTLDTC.cbl:129-130, CSUTLDTC.cbl:98.
- Unit: invalid date → severity `'0003'` (non-zero), correct verdict per the §5 table, RETURN-CODE = severity.
- Unit (faithful-bug #1): assert the `TstDate:` bytes reproduce the VSTRING-image corruption. // source: CSUTLDTC.cbl:122.
- Unit (faithful-bug #2): pass an 8-char mask `'YYYYMMDD'` (with 2 trailing spaces) → assert VSTRING length is
  10 and behavior matches the captured CEEDAYS result. // source: CSUTLDTC.cbl:109-110.
- Caller-integration: feed CSUTLDTC output into the COTRN02C/CORPT00C branch logic; assert `'2513'`
  suppression and the four caller error messages. // source: COTRN02C.cbl:397-407; CORPT00C.cbl:396-406.
- Sub-mapping: `CALL 'CSUTLDTC'` from CSUTLDPY `EDIT-DATE-LE` → this util. // source: CSUTLDPY.cpy:293-296.
