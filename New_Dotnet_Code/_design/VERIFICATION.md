# CardDemo COBOL → .NET 10 — Verification Report

**Status: COMPLETE.** All **44** CardDemo COBOL programs converted to pure C#/.NET 10 over a relational
SQLite schema. Zero COBOL anywhere in the solution. Full suite verified green multiple independent times.

This report reflects TWO independent multi-workflow verification passes: a first **5-workflow** pass
(≈110 subagents) and then a second, fully independent **7-workflow** pass (≈64 subagents over all 44
programs, each flagged discrepancy adversarially refuted). The second pass re-confirmed the no-COBOL,
coverage, anti-hallucination and codec tracks as clean and surfaced a further round of fidelity defects
(see **§ Second independent 7-track audit** below), all fixed.

## Build & test
- `dotnet build CardDemo.sln -m:1` → **0 errors**. ~60 warnings, all the benign faithful-dead-code class
  (CS0414/CS0169/CS0649 = inert WORKING-STORAGE fields carried verbatim; CS1717 = faithful RESP2 self-moves;
  CS0162 = documented unreachable faithful branches). .NET 10.0.201, Microsoft.Data.Sqlite 10.0.9.
- `dotnet test` (CardDemo.Tests) → **79 passed / 0 failed / 0 skipped**, deterministic across repeated runs.

## No-COBOL proof (requirement: zero COBOL code in `New_Dotnet_Code`) — INDEPENDENTLY RE-VERIFIED
A 5-facet audit (embedded source, committed source files, toolchain, identifiers, binaries) → **all PASS**:
- COBOL compiler/runtime references (`GnuCobol*`, `cobc`, OpenCOBOL, Micro Focus, isCOBOL, libcob) → **NONE**.
- Embedded COBOL source (`IDENTIFICATION/PROCEDURE DIVISION`, `PIC`, `EXEC CICS/SQL`) executing in `*.cs` → **NONE**
  (every hit is an acceptable `// source: PROG.cbl:NNN` citation comment beside genuine C#).
- COBOL source files (`*.cbl/*.cob/*.cpy/*.jcl/*.bms`) under `New_Dotnet_Code` → **NONE**.
- The runtime-support library is `CardDemo.Runtime` (100% C#: COBOL-compatible decimal / edited-numeric /
  zoned-packed / EBCDIC semantics). COBOL is referenced only in `_design/*.md` and `Old_Cobol_Code/`.

## Architecture (relational, no byte-image BLOBs)
`CardDemo.Domain` · `CardDemo.Runtime` (pure-C# COBOL semantics) · `CardDemo.Data` (Microsoft.Data.Sqlite
repositories with VSAM-semantics → FileStatus) · `CardDemo.Import` (EBCDIC seeder + record serializer) ·
`CardDemo.Batch` · `CardDemo.Online` (CICS/BMS console runtime + handlers) · `CardDemo.ConsoleApp` ·
`CardDemo.Db2`/`CardDemo.Ims`/`CardDemo.Mq` · `CardDemo.JobControl` · `CardDemo.Tooling` · `tests/CardDemo.Tests`.

## Anti-hallucination evidence — INDEPENDENTLY RE-VERIFIED
- **Schema round-trip (byte-exact):** all 10 seeded masters (636 records) imported EBCDIC → SQL rows →
  re-serialized to canonical fixed-width image → **byte-identical** to the source datasets. Proven genuine
  by injecting a 1-bit corruption into the encoder and confirming the suite fails loudly with an exact offset.
- **EXPORT↔IMPORT round-trip** (pure-.NET oracle): masters → EXPORT file → re-import → byte-identical tables.
- **No floats in money math:** scan found zero `double`/`float`/`Math.Round`/`decimal.Round` in any
  balance/interest/rate path — all `decimal.Truncate`, truncate-toward-zero, matching no-ROUNDED COBOL.
- **No stubs:** zero `NotImplementedException`/TODO in ported program paths; every empty body is a documented
  faithful no-op (e.g. COPAUA0C 5600-READ-PROFILE-DATA = COBOL `CONTINUE`).
- **235 faithful bugs** catalogued in `faithful-bugs.md`, reproduced verbatim (8/8 sampled confirmed in code).

## Program coverage (44/44)
### Batch (14) — `CardDemo.Batch/*.cs`
CBACT01C, CBACT02C, CBACT03C, CBACT04C, CBCUS01C, CBTRN01C, CBTRN02C, CBTRN03C, Cbexport (CBEXPORT),
Cbimport (CBIMPORT), COBSWAIT, CSUTLDTC, **Cbstm03a (CBSTM03A)**, **Cbstm03b (CBSTM03B)** — the last two are the
statement-generation driver (ALTER/GO-TO, TIOT walk, per-account plain-text + HTML statement) and its I/O subroutine.

### Online CICS (17) — `CardDemo.Online/Programs/*.cs`
COSGN00C, COMEN01C, COADM01C, COACTVWC, COACTUPC, COCRDLIC, COCRDSLC, COCRDUPC, COTRN00C, COTRN01C, COTRN02C,
COUSR00C, COUSR01C, COUSR02C, COUSR03C, COBIL00C, CORPT00C — over the CardDemo.Online console runtime.

### Optional DB2/IMS/MQ (13)
- IMS: `CardDemo.Ims/CBPAUP0C.cs`; **`PAUDBLOD.cs`** (load), **`PAUDBUNL.cs`** (unload), **`DBUNLDGS.cs`**
  (GSAM unload) over the PAUT summary/detail relational model (shared `PautSegmentImages`).
- DB2: `CardDemo.Db2/COBTUPDT.cs`; `CardDemo.Online/Programs/COTRTLIC.cs`, `COTRTUPC.cs`.
- IMS/DB2/MQ auth: `COPAUS0C/1C/2C`, `CardDemo.Mq/Programs/COPAUA0C.cs`.
- VSAM-MQ: `CardDemo.Mq/Programs/COACCT01.cs`, `CODATE01.cs`.

### Copybooks 62/62 · BMS maps 17/17 · JCL fully accounted
26 runnable batch jobs in `CardDemoJobs` — including **CREASTMT** (CBSTM03A/CBSTM03B statement gen) and the
IMS **LOADPADB**/**UNLDPADB**/**UNLDGSAM** load/unload jobs (PAUDBLOD/PAUDBUNL/DBUNLDGS) faithful to their JCL
decks. The remaining JCL members are CICS-online config or GDG/AIX DEFINEs modeled by `GdgManager.Define`
(all catalogued in `OnlineConfigJobs`, incl. DALYREJS/REPTFILE). 0 missing batch jobs.

## Per-program fidelity (independent COBOL-vs-C# review of all 39 originally-shipped ports + adversarial verify)
**33 FAITHFUL** with no real discrepancies. **6 confirmed-real discrepancies were found and FIXED:**
1. **CBTRN03C** [HIGH] `NEXT SENTENCE` inside the inline `PERFORM UNTIL` was mistranslated as `continue`; it
   actually branches past `END-PERFORM.` to `9000-TRANFILE-CLOSE`, so an out-of-range date **terminates** the
   report loop. Fixed (`break`) + regression test.
2. **COACTUPC** [HIGH] DOB future-date check read an uninitialized `_now` (year 1) → rejected all real DOBs;
   now seeded from the clock at turn start, matching `FUNCTION CURRENT-DATE` in EDIT-DATE-OF-BIRTH.
3. **COPAUS1C** [HIGH] approved-amount edit picture `-zzzzzzz9.99` (lowercase) emitted literal `z`s because
   `EditedNumeric` only handled uppercase. Fixed by case-folding the picture (COBOL picture symbols are
   case-insensitive) + regression test.
4. **COTRN00C** [MED] page number `9(8)→X(8)` move now renders zero-filled `00000001`, not `1`.
5. **CORPT00C** [MED] calendar-impossible dates (Feb-30, Apr-31, month 00) were silently accepted: the shared
   CEEDAYS emulation returned the tolerated msg `2513` for every bad date. Fixed to the faithful LE codes —
   `2508` (bad date value), `2517` (invalid month), `2521` (year-in-era zero); only `2513` (unsupported range)
   is tolerated. This corrected the **shared `CSUTLDTC` engine** and the COTRN02C stand-in too.
6. **COPAUS2C** [MED] duplicate-key FRAUD-UPDATE overwrote all 24 non-key columns; COBOL sets only
   `AUTH_FRAUD` + `FRAUD_RPT_DATE`. Added `AuthFraudRepository.UpdateFraudFlag` + regression test.

One flagged item was an adversarially-confirmed **false positive** (CSUTLDTC "fabricated message numbers" — the
numbers ARE derived from the COBOL FEEDBACK-CODE 88-levels). All "FAITHFUL/MINOR" programs with no escalated
discrepancy carried only LOW documented-boundary/cosmetic notes.

## Second independent 7-track audit (re-run over all 44 programs)
A fresh, fully independent pass — **7 blind tracks** (A no-COBOL ×3 facets, B coverage, C per-program fidelity
for all 44 with adversarial refute, D anti-hallucination ×2 facets, E test quality, F codec byte-exactness,
G completeness critic). Tracks **A / B / D / F PASSED clean** (zero COBOL; 44/44 ported with real logic; no
stubs/fabrication; no float in money math; copybook/record layouts byte-exact). Track C confirmed the prior 6
fixes hold and surfaced **8 further real discrepancies**, plus a re-review of 5 programs (whose first-pass
reviewer failed to emit) found 1 more — **all FIXED:**
1. **COSGN00C** [HIGH] blank-password branch dropped `MOVE 'Y' TO WS-ERR-FLG` (cbl:124) — it still ran
   READ-USER-SEC-FILE, so a blank password showed "Wrong Password" or could log in on a blank stored pwd.
   Now sets the err flag (skips the read; shows "Please enter Password"). A bogus "FB-1" comment was removed.
2. **COTRN00C → COTRN01C** [HIGH] `SaveCt00Info` packed only 41 bytes, dropping `TRN-SEL-FLG`(41) +
   `TRN-SELECTED`(42-57) that COTRN01C reads — "type S to view" lost the selected transaction across the XCTL.
3. **COUSR00C → COUSR02C/COUSR03C** [HIGH] same class: `SaveCu00Info` omitted `USR-SEL-FLG`(25) +
   `USR-SELECTED`(26-33), so selecting a user to update/delete lost the id across the XCTL. (Surfaced by the
   COUSR02C re-review's interop flag, then cross-checked against the COBOL `CDEMO-CU00-INFO` layout.)
4. **CBTRN03C** [HIGH] read the full TRANSACTION master, so its faithful NEXT-SENTENCE date filter truncated
   the report at the first out-of-window row. The mainframe TRANFILE is `TRANSACT.DALY` (date-filtered +
   card-sorted by the upstream SORT); the cursor now models DALY, so the NEXT-SENTENCE bug stays in code but
   is **dormant** (never fires), exactly as on the host — the report covers all in-window rows with totals.
5. **COACTUPC** [MED] WHEN-OTHER abend threw code "0001" (the display-only ABEND-CODE field); ABEND-ROUTINE
   actually issues `EXEC CICS ABEND ABCODE('9999')`. Corrected to "9999".
6. **CBPAUP0C** [MED ×2] `DISPLAY` of `S9(8) COMP` counters and `S9(11) COMP-3 PA-ACCT-ID` emitted a spurious
   leading sign/space byte; IBM `DISPLAY` of a plain signed numeric carries the sign as a trailing overpunch
   and no leading byte. The non-negative values now render as bare PICTURE-width digits, abutting the literal.
7. **COUSR00C** [MED ×2] PF8/PF7 paging rendered `PAGENUM` via `ToString()` ("1") instead of the zoned
   `9(08)→X(8)` value "00000001". Fixed to `ToString("D8")`, matching sibling COTRN00C.
8. **COPAUS0C** [LOW] no-summary path used `SetValue("0")`; the BMS fields are `PIC X`, and `MOVE ZERO` to an
   alphanumeric fills the whole field with '0' (e.g. CREDBAL → "000000000000"). Fixed to width-matched fills.

Also fixed: **COTRTLIC.cs** carried **5 embedded NUL bytes** (corrupted `'\0'` escapes collapsed to raw
`0x00`) flagged by the codec/critic tracks — de-corrupted to proper `\0` escapes (identical compiled value,
clean ASCII source). COBTUPDT (whose reviewer never emitted, twice) was reviewed by hand and is FAITHFUL.

## Tests (79)
SchemaRoundTripTests, BatchTests (incl. CSUTLDTC 2508/2517 classification), OnlineTests, OptionalTests,
JobControlTests (incl. CREASTMT statement job, UNLDPADB→LOADPADB round-trip job, UNLDGSAM GSAM job), and
**RemediationTests** (CBSTM03A statement gen; PAUDBUNL→PAUDBLOD round-trip; DBUNLDGS GSAM byte-identity;
EditedNumeric lowercase; COPAUS2C targeted fraud update). Second-audit regression locks: CBTRN03C DALY
pre-filter (all in-window rows reported, out-of-window excluded, totals written); COSGN00C blank-password
sets the err flag and skips the read; and the COTRN00C/COUSR00C selected-from-list keys survive the XCTL.

## Boundaries (documented, not gaps)
- Verification is pure-.NET (per the no-COBOL directive): byte-exact schema round-trip + EXPORT/IMPORT
  round-trip + characterization tests, instead of a GnuCOBOL oracle.
- Online parity is characterization-based (no CICS oracle exists): field values + COMMAREA + next-TRANSID/XCTL.
- The program-private CICS COMMAREA trailer (COTRTLIC/COTRTUPC/COACTUPC) is carried via a content-stable
  side store keyed on the full nav-area image — the console runtime round-trips only the 160-byte nav area.
- The 5 newly-ported programs are exercised directly by tests AND wired into named JobControl sequences
  (CREASTMT, LOADPADB, UNLDPADB, UNLDGSAM), each with a job-level test.
- **Online test breadth (Track E, documented gap, not a conversion defect):** the batch / JobControl /
  IMS / MQ / DB2 spine and ~8 online handlers have direct behavioral coverage with specific oracles; the
  remaining online CICS handlers (e.g. COACTUPC, COCRDLIC/SLC/UPC, COTRN02C, COBIL00C, CORPT00C, COUSR03C)
  are covered by registry/routing wiring plus per-program source-vs-COBOL fidelity review (both audit passes)
  rather than a driven screen-flow test each. Closing this fully means one scripted RECEIVE/SEND turn per
  remaining handler; the ports themselves were read paragraph-by-paragraph against the COBOL in both audits.
