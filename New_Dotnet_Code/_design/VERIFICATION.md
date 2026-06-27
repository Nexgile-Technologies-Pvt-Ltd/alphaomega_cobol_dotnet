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
- `dotnet test` (CardDemo.Tests) → **88 passed / 0 failed / 0 skipped**, deterministic across repeated runs.

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

**Closing the Track-E online breadth gap — 9 driven screen-flow tests added (one per remaining handler).**
Each drives a real RECEIVE/SEND turn through the dispatcher and asserts observable behaviour (painted master
values / DB-row effects / exact message literals, incl. preserved faithful bugs). Authoring this coverage
surfaced one more real defect — **COCRDLIC** [HIGH] `SaveProgCommarea` built a 35-char trailer image but then
sliced `Substring(25,25)`/`Substring(50,1)` (needs ≥51), throwing `ArgumentOutOfRangeException` on **every**
card-list turn — never caught because the list path had no behavioural test. Fixed to pack the full 57-byte
image (incl. the two 11-digit acct-ids) padded to 75, symmetric with `RestoreProgCommarea`. The new tests:
COCRDLIC (list page from CARD master), COCRDSLC (card detail paint), COCRDUPC (3-turn load→validate→F5 REWRITE,
asserting status flip + 3 faithful bugs), COACTUPC (account detail paint), COTRN01C (txn detail paint),
COTRN02C (keyed add → confirm prompt + card resolve), COBIL00C (confirm-Y pays full balance: writes the
bill-pay TRANSACTION + debits the account to zero), CORPT00C (monthly report submit + green confirmation),
COUSR03C (PF5 delete → row physically gone + confirmation literal).

## Tests (104)
SchemaRoundTripTests, BatchTests (incl. CSUTLDTC 2508/2517 classification), OnlineTests, OptionalTests,
JobControlTests (incl. CREASTMT statement job, UNLDPADB→LOADPADB round-trip job, UNLDGSAM GSAM job), and
**RemediationTests** (CBSTM03A statement gen; PAUDBUNL→PAUDBLOD round-trip; DBUNLDGS GSAM byte-identity;
EditedNumeric lowercase; COPAUS2C targeted fraud update). Second-audit regression locks: CBTRN03C DALY
pre-filter (all in-window rows reported, out-of-window excluded, totals written); COSGN00C blank-password
sets the err flag and skips the read; the COTRN00C/COUSR00C selected-from-list keys survive the XCTL; plus
the **9 driven online screen-flow tests** above (all 17 online handlers now have behavioural coverage).

## Boundaries (documented, not gaps)
- Verification is pure-.NET (per the no-COBOL directive): byte-exact schema round-trip + EXPORT/IMPORT
  round-trip + characterization tests, instead of a GnuCOBOL oracle.
- Online parity is characterization-based (no CICS oracle exists): field values + COMMAREA + next-TRANSID/XCTL.
- The program-private CICS COMMAREA trailer (COTRTLIC/COTRTUPC/COACTUPC) is carried via a content-stable
  side store keyed on the full nav-area image — the console runtime round-trips only the 160-byte nav area.
- The 5 newly-ported programs are exercised directly by tests AND wired into named JobControl sequences
  (CREASTMT, LOADPADB, UNLDPADB, UNLDGSAM), each with a job-level test.
- **Online test breadth (Track E) — NOW CLOSED.** Every one of the 17 online CICS handlers has a driven
  behavioural screen-flow test (the prior ~8 plus the 9 added above), in addition to per-program
  source-vs-COBOL fidelity review in both audit passes. Online parity remains characterization-based (no CICS
  oracle exists) — asserted on field values + COMMAREA + next-TRANSID/XCTL + DB effects, not 3270 datastream
  bytes — but no online handler is now wiring-only.

## Third independent 5-workflow audit + remediation (commit pending)
A fresh blind pass ran **five independent verification workflows** (~190 subagents total), each with an
adversarial verify stage so no finding survived unrefuted:
1. **Anti-pattern** (15 agents) → CLEAN: zero embedded/executed COBOL, zero real stubs, zero float/rounding in
   money paths, zero source corruption. (3 INFO notes were all correctly-identified benign modelling choices.)
2. **Coverage matrix** (11 agents) → 44/44 programs ported & real (not shells); 61/62 copybooks (the 1 is the
   dead orphan `UNUSED1Y`); 21/21 BMS maps. Found **3 genuine JCL job-flow gaps** (programs ported+tested but
   no runnable JobControl flow): CBPAUP0J→CBPAUP0C, MNTTRDB2→COBTUPDT, TRANEXTR (DSNTIAUL reference unload).
3. **Per-program fidelity** (67 agents, all 44) → **16 confirmed divergences** (2 HIGH, 5 MED, 9 LOW).
4. **Anti-hallucination** (68 agents, all 44) → **16 confirmed** (4 real-output literal/behaviour defects +
   12 comment/width inaccuracies — invented bug-numbers, wrong byte widths, a false "declares TRANSID" claim).
5. **Data/codec/schema/numeric** (13 agents) → schema↔copybook, codecs, round-trip net, FileStatus all
   verified; **3 confirmed** (1 HIGH-active, 2 latent). A tree-wide grep re-confirmed **zero** Math.Round /
   decimal.Round / Math.Floor/Ceiling / double / float in any money path.

**All confirmed defects fixed (35 items):**
- **Runtime codecs:** `EditedNumeric.Format` now supports floating leading sign (`----9` — the COTRTLIC
  `WS-DISP-SQLCODE` field rendered garbage for every nonzero DB2 SQLCODE), trailing sign, and CR/DB editing;
  `BinaryCodec.Encode` now applies the IBM TRUNC(STD) decimal-digit modulo (mod 10^n) like the zoned/packed
  codecs. (pinned by `VerificationFixTests`.)
- **HIGH fidelity:** COACTUPC 9000-READ-ACCT no longer early-returns on ACCTDAT-NOTFND — the COBOL 88
  `DID-NOT-FIND-ACCT-IN-ACCTDAT` is never set (its SET is commented out at cbl:3719), so control falls through
  to read the customer (9500 tolerates the blank account). CBPAUP0C checkpoint-frequency test now does the IBM
  numeric-vs-alphanumeric (zoned, right-space-padded) byte comparison instead of a numeric parse.
- **MED fidelity:** CBACT04C DISPLAY emits the EBCDIC sign overpunch (`}`,`J`–`R`) for a negative TRAN-CAT-BAL;
  COADM01C first-display OPTION is blank (LOW-VALUES), echoed only after PROCESS-ENTER-KEY; COCRDSLC/COCRDUPC
  menu-entry no longer wipes a carried COMMAREA (the `FROM-PROGRAM = LIT-MENUPGM` disjunct is dead because the
  COBOL tests it before the DFHCOMMAREA load).
- **LOW fidelity:** COBIL00C TRAN-AMT high-order-truncates to 9 digits on the S9(10)→S9(9) move; COUSR01C /
  COPAUS1C space/low-values guards now require ALL-spaces OR ALL-low-values; COUSR02C/03C build the id with
  DELIMITED BY SPACE (stop at first space); COACCT01 WS-KEY uses the faithful zoned low-nibble interpretation.
- **Hallucinations:** CBIMPORT/CBSTM03A/COACTUPC output-literal fixes (the 4 CBSTM03A HTML cells dropped a
  spurious space; COACTUPC file-error message lost an extra space; CBIMPORT statuses documented as the faithful
  QSAM values); COPAUS1C removed an invented blank→default XCTL fallback; 8 comment/width corrections
  (CBACT04C bug#, CBEXPORT 40-byte header, CBTRN03C row widths, CSUTLDTC 2513/2508, COSGN00C TRANSID wording,
  COPAUA0C 122-byte / 14-char, COPAUS2C 23-char WS-AUTH-TS).
- **Coverage:** the 3 missing JCL job flows are now authored (`CardDemoJobs.CbPaup0J/MntTrDb2/TranExtr`) with
  job-level tests; `MntTrDb2` adds a TRANSACTION_TYPE row from INPFILE, `TranExtr` writes byte-shaped 60-byte
  TRANTYPE.PS/TRANCATG.PS unloads. COADM01C gains a first-display blank-OPTION regression lock.

**Documented emulation boundaries (not fixed — no off-platform equivalent):** CBSTM03A TIOT-walk SYSOUT
(job/step names + DD list come from PSA/TCB/TIOT control blocks); COPAUS2C non-duplicate SQL-failure branch
(no DB2 SQLCODE in the relational store — branch is unreachable); COPAUS0C WS-MESSAGE STRING residual tail.

**Result: build 0 errors; 104/104 tests pass, verified 3× deterministically.**
