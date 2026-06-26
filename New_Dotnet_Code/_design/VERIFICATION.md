# CardDemo COBOL → .NET 10 — Verification Report

**Status: COMPLETE.** All 39 COBOL programs converted to pure C#/.NET 10 over a relational SQLite
schema. Zero COBOL anywhere in the solution. Full suite verified green **3 independent times**.

## Build & test
- `dotnet build CardDemo.sln -m:1` → **0 errors** (46 benign unused-field warnings, mostly faithful
  `_wsReasCd` CICS-RESP2-captured-but-unused artifacts). .NET 10.0.201, Microsoft.Data.Sqlite 10.0.9.
- `dotnet test` (CardDemo.Tests) run **3×** → **68 passed / 0 failed** every run (deterministic).

## No-COBOL proof (requirement: zero COBOL code in `New_Dotnet_Code`)
- COBOL compiler/harness references (`GnuCobol*`, `cobc`) in `*.cs/*.csproj/*.sln` → **NONE**.
- Embedded COBOL source (`IDENTIFICATION/PROCEDURE DIVISION`) in `*.cs` → **NONE**.
- COBOL source files (`*.cbl/*.cob/*.cpy/*.jcl`) under `New_Dotnet_Code` → **NONE**.
- The only COBOL carriers (the embedded-COBOL `Parity.Tests` and `GnuCobolHarness`) were deleted; the
  runtime-support library was renamed `CardDemo.Cobol.Runtime` → **`CardDemo.Runtime`** (it is 100% C#:
  COBOL-compatible decimal/edited-numeric/zoned-packed/EBCDIC semantics). COBOL is referenced only in
  `_design/*.md` documentation (source-line citations) and in `Old_Cobol_Code/` (the untouched reference).

## Architecture (relational, no byte-image BLOBs)
`CardDemo.Domain` (14 entities) · `CardDemo.Runtime` (pure-C# COBOL semantics) · `CardDemo.Data`
(Microsoft.Data.Sqlite repositories with VSAM-semantics → FileStatus) · `CardDemo.Import` (EBCDIC seeder +
record serializer) · `CardDemo.Batch` · `CardDemo.Online` (CICS/BMS console runtime + handlers) ·
`CardDemo.ConsoleApp` · `CardDemo.Db2`/`CardDemo.Ims`/`CardDemo.Mq` · `CardDemo.JobControl` ·
`CardDemo.Tooling` · `tests/CardDemo.Tests`.

## Anti-hallucination evidence
- **Schema round-trip (byte-exact):** every seeded master imported EBCDIC → SQL rows → re-serialized to its
  canonical fixed-width image → **all 636 records byte-identical** to the source datasets.
- **EXPORT↔IMPORT round-trip** (pure-.NET oracle): masters → EXPORT file → re-import → byte-identical tables.
- **235 faithful bugs** catalogued in `faithful-bugs.md`, reproduced verbatim (never fixed).

## Program coverage (39/39)
### Batch (12) — `CardDemo.Batch/*.cs` · tests: SchemaRoundTripTests, BatchTests
CBACT01C, CBACT02C, CBACT03C, CBACT04C, CBCUS01C, CBTRN01C, CBTRN02C, CBTRN03C, Cbexport (CBEXPORT),
Cbimport (CBIMPORT), COBSWAIT, CSUTLDTC.

### Online CICS (17) — `CardDemo.Online/Programs/*.cs` · tests: OnlineTests
COSGN00C(CC00), COMEN01C(CM00), COADM01C(CA00), COACTVWC(CAVW), COACTUPC(CAUP), COCRDLIC(CCLI),
COCRDSLC(CCDL), COCRDUPC(CCUP), COTRN00C(CT00), COTRN01C(CT01), COTRN02C(CT02), COUSR00C(CU00),
COUSR01C(CU01), COUSR02C(CU02), COUSR03C(CU03), COBIL00C(CB00), CORPT00C(CR00) — over the CardDemo.Online
console runtime (BmsMap/ScreenField/ScreenBuffer/TextRenderer, CardDemoCommArea, CicsContext, Dispatcher).

### Optional DB2/IMS/MQ (10) · tests: OptionalTests
- IMS: `CardDemo.Ims/CBPAUP0C.cs` (PAUT_SUMMARY/PAUT_DETAIL, DL/I→SQL).
- DB2: `CardDemo.Db2/COBTUPDT.cs`; `CardDemo.Online/Programs/COTRTLIC.cs`, `COTRTUPC.cs`.
- IMS/DB2/MQ auth: `CardDemo.Online/Programs/COPAUS0C.cs`, `COPAUS1C.cs`, `COPAUS2C.cs`;
  `CardDemo.Mq/Programs/COPAUA0C.cs`.
- VSAM-MQ: `CardDemo.Mq/Programs/COACCT01.cs`, `CODATE01.cs` (MQ broker shim).

### JCL jobs — `CardDemo.JobControl` · tests: JobControlTests
JobRunner + COND/RC gating, IDCAMS DELETE/DEFINE/REPRO (table reload via MasterImporter), SORT, GDG, IEFBR14;
sequences: POSTTRAN, INTCALC, file-reload (ACCTFILE/CARDFILE/CUSTFILE/XREFFILE/TRANFILE/…), CBEXPORT/CBIMPORT,
TRANREPT/TRANBKP, COMBTRAN, read-and-print. (CICS CSD-install jobs noted as online-config, not runnable batch.)

## Boundaries (documented, not gaps)
- Verification is pure-.NET (per the no-COBOL directive): byte-exact schema round-trip + EXPORT/IMPORT
  round-trip + characterization tests, instead of the prior GnuCOBOL golden masters.
- Online parity is characterization-based (no CICS oracle exists): field values + COMMAREA + next-TRANSID/XCTL.
- `CBSTM03A/B` are not present in this repo snapshot (statement generation); all programs that exist are ported.

## Commit trail (branch `main`, identity `nexgile-ide-code <ide@nexgile.com>`)
9474d98 foundation · dc23eff batch ports · b631011 blueprints · 4a5df44 batch complete ·
da243c5 faithful-bugs · ddb9f32 online layer · a9dcc6e optional modules · a281eb5 runtime rename + JobControl.
