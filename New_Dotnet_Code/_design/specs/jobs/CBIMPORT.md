# JOB SPEC: CBIMPORT

## Overview

- **JCL member**: `CBIMPORT.jcl`
- **JOB name**: `CBIMPORT`
- **JOB description**: `'Import CARDDEMO Data'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v2.0-41-g02a5b3e-251` (2025-10-02); driver program `CBIMPORT.cbl` tagged `CardDemo_v2.0-44-gb6e9c27-254` (2025-10-16)
- **Purpose**: **File setup / data import (branch-migration load).** A single-step batch job that runs the custom COBOL program `CBIMPORT`. It reads one **multi-record-type export file** (a combined dump produced by a branch/source system) and **demultiplexes / splits** it by record-type code into several **separate normalized output flat files** — one each for Customer, Account, Card-Xref, Transaction, and (in the program) Card — plus an error log. It is an **import / extract-split** job, not a posting, report, or backup job. Each record in the export carries a 1-byte type tag (`C`/`A`/`X`/`T`/`D`); the program routes each to the matching output file and maps the export fields into the target copybook layout. Output files are `(NEW,CATLG,DELETE)` so the job is a one-shot producer of fresh import-staging datasets that downstream load jobs (e.g. ACCTFILE/CUSTFILE-style REPRO loads) consume.

## Step summary

| Step | PGM | Type | Action |
|------|-----|------|--------|
| STEP01 | CBIMPORT | Custom COBOL (`CB*`) batch program | Read multi-record export file `EXPFILE`; split by record type into per-entity normalized output files (`CUSTOUT`, `ACCTOUT`, `XREFOUT`, `TRNXOUT`) + error log `ERROUT` |

There is **1 EXEC step** invoking **1 program: `CBIMPORT`** (a CardDemo custom COBOL program, not a utility). No IDCAMS, SORT, or IEFBR14 in this JCL. No GDG usage. No COND/PARM coded.

---

## STEP01 — Import and split the export file (PGM=CBIMPORT)

- **EXEC**: `PGM=CBIMPORT`
- **PARM=**: none.
- **COND/RC gating**: none coded on the EXEC and none on the JOB card. (The program enforces its own failure handling: on any open/read/write error it `DISPLAY`s a message and calls `CEE3ABD` to ABEND, so a hard failure surfaces as an abend rather than a return code.)
- **STEPLIB**: `DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` — load library containing the compiled `CBIMPORT` load module. No relational/file mapping; this is the program-binary search library.

### Program behavior (from `CBIMPORT.cbl`)

The program is driven by `0000-MAIN-PROCESSING`: `1000-INITIALIZE` (opens all files, stamps import date/time) -> `2000-PROCESS-EXPORT-FILE` (read loop) -> `3000-VALIDATE-IMPORT` (display only — no real validation) -> `4000-FINALIZE` (close + statistics).

The read loop reads each export record into `EXPORT-RECORD` (copybook `CVEXPORT`) and dispatches on the 1-byte `EXPORT-REC-TYPE` field (`2200-PROCESS-RECORD-BY-TYPE`):

| `EXPORT-REC-TYPE` | Paragraph | Output DD | Target entity |
|---|---|---|---|
| `C` | 2300-PROCESS-CUSTOMER-RECORD | `CUSTOUT` | Customer |
| `A` | 2400-PROCESS-ACCOUNT-RECORD | `ACCTOUT` | Account |
| `X` | 2500-PROCESS-XREF-RECORD | `XREFOUT` | Card cross-reference |
| `T` | 2600-PROCESS-TRAN-RECORD | `TRNXOUT` | Transaction |
| `D` | 2650-PROCESS-CARD-RECORD | `CARDOUT` (see discrepancy note) | Card |
| any other | 2700-PROCESS-UNKNOWN-RECORD -> 2750-WRITE-ERROR | `ERROUT` | error log entry |

For each matched type the program `INITIALIZE`s the target record, `MOVE`s the corresponding `EXP-*` export fields into the target copybook fields, then `WRITE`s one output record and increments a per-type counter. Final statistics (records read; customers/accounts/xrefs/transactions/cards imported; errors written; unknown types) are `DISPLAY`ed to job log at the end.

### DD statements / datasets

| DD | Disposition | DSN | I/O | Record layout | Maps to (file / relational table) |
|----|-------------|-----|-----|---------------|-----------------------------------|
| `EXPFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.EXPORT.DATA` | **INPUT** | Program declares it `ORGANIZATION IS INDEXED` (KSDS), `RECORD KEY IS EXPORT-SEQUENCE-NUM`, fixed 500-byte record, layout `CVEXPORT` (`EXPORT-RECORD`, leading `EXPORT-REC-TYPE` + `EXPORT-SEQUENCE-NUM`) | The combined **multi-record-type export / migration dump** (source extract). In .NET this is the single inbound import feed; each row is typed and fanned out to the per-entity stores below. |
| `CUSTOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.CUSTDATA.IMPORT` | **OUTPUT** | `RECFM=FB, LRECL=500, BLKSIZE=0`; copybook `CVCUS01Y` (`CUSTOMER-RECORD`) | **Customer** staging file -> relational **`CUSTOMER`** table (CUSTDAT entity). |
| `ACCTOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.ACCTDATA.IMPORT` | **OUTPUT** | `RECFM=FB, LRECL=300, BLKSIZE=0`; copybook `CVACT01Y` (`ACCOUNT-RECORD`) | **Account** staging file -> relational **`ACCOUNT`** table (ACCTDAT entity). |
| `XREFOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.CARDXREF.IMPORT` | **OUTPUT** | `RECFM=FB, LRECL=50, BLKSIZE=0`; copybook `CVACT03Y` (`CARD-XREF-RECORD`) | **Card cross-reference** staging file -> relational **`CARD_XREF`** table (card-number ↔ account ↔ customer). |
| `TRNXOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.TRANSACT.IMPORT` | **OUTPUT** | `RECFM=FB, LRECL=350, BLKSIZE=0`; copybook `CVTRA05Y` (`TRAN-RECORD`) | **Transaction** staging file -> relational **`TRANSACTION`** table (TRANSACT entity). |
| `ERROUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.IMPORT.ERRORS` | **OUTPUT** | `RECFM=FB, LRECL=132, BLKSIZE=0`; `WS-ERROR-RECORD` (timestamp \| rec-type \| sequence \| message) | **Error / reject log** for unknown record types. No relational table; an import-reject/audit log. |
| `SYSOUT` | `SYSOUT=*` | (spool) | OUTPUT | runtime `DISPLAY` / COBOL runtime messages | Job log; no data mapping. |
| `SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | system print | Job log; no data mapping. |

- **Space allocations** (all `UNIT=SYSDA`, `SPACE=(TRK,(prim,sec),RLSE)`): CUSTOUT `(50,25)`, ACCTOUT `(50,25)`, XREFOUT `(25,10)`, TRNXOUT `(100,50)`, ERROUT `(10,5)`. `RLSE` releases unused space at close. `BLKSIZE=0` lets the system pick an optimal block size for the FB record.

---

## Discrepancies / conversion-critical notes

- **Missing `CARDOUT` DD (live discrepancy).** The program `CBIMPORT.cbl` `SELECT`s a sixth output file `CARD-OUTPUT ASSIGN TO CARDOUT` (`DISP`/`ORG` sequential, 150-byte FB, copybook `CVACT02Y` = `CARD-RECORD`) and, in `2650-PROCESS-CARD-RECORD`, `OPEN`s it, `WRITE`s to it for record-type `D`, and `CLOSE`s it. **This `CARDOUT` DD is NOT present in `CBIMPORT.jcl`.** As written, if the export contains any type-`D` (Card) records, the `OPEN OUTPUT CARD-OUTPUT` in `1100-OPEN-FILES` will fail (no DD -> non-`00` status), the program detects `NOT WS-CARD-OK`, and it ABENDs via `CEE3ABD`. For the .NET port: either (a) add a Card output target (`AWS.M2.CARDDEMO.CARDDATA.IMPORT`-style, 150-byte FB, `CVACT02Y`, mapping to the **`CARD`** table) so type-`D` records are handled, or (b) treat the absence as intentional (export contains no `D` records) and document that type `D` is currently unsupported by the JCL. Recommend (a) to match program intent. The Card entity maps to relational **`CARD`** (CARDDAT).
- **Export file organization mismatch (informational).** The JCL codes `EXPFILE DD DISP=SHR,DSN=AWS.M2.CARDDEMO.EXPORT.DATA` with no DCB (so the catalog/VSAM definition governs), while the program declares the export as an **INDEXED (KSDS)** file keyed on `EXPORT-SEQUENCE-NUM` and reads it `ACCESS MODE IS SEQUENTIAL`. For the .NET runner, treat the input as a **sequentially-read, sequence-keyed feed of 500-byte records**; the leading byte is the record-type tag used for fan-out.

## PARM / COND / GDG / SORT / IDCAMS summary

- **PARM=**: none on the EXEC.
- **COND/RC gating**: none in the JCL (no `COND=` on EXEC, no `IF/THEN`, no `RC` checks). Failure handling is in-program (abend on I/O error).
- **GDG**: not used. All datasets are fixed-name; no `(+1)`/`(0)` generation references and no `IDCAMS DEFINE GDG`.
- **SORT**: not used; no SORT step, no `SORT FIELDS` control statements.
- **IDCAMS**: not used; no `DEFINE`/`REPRO`/`DELETE` control statements in this job (the cluster DELETE/DEFINE/REPRO for the produced `.IMPORT` files would live in separate load jobs).
- **IEFBR14**: not used.

## Conversion notes for the .NET JobControl step-runner

- Implement as a **single step** that runs the ported `CBIMPORT` program. The step is a **demultiplexer**: open one input feed, open N output sinks, loop reading 500-byte records, switch on the leading record-type byte, map export fields to the target schema, and write to the matching sink; route unknown types to the error log.
- **Output sinks** correspond to the import-staging files for `CUSTOMER` (500B/`CVCUS01Y`), `ACCOUNT` (300B/`CVACT01Y`), `CARD_XREF` (50B/`CVACT03Y`), `TRANSACTION` (350B/`CVTRA05Y`), and (add) `CARD` (150B/`CVACT02Y`), plus a non-relational `ERROR` reject log (132B). These are **staging extracts**, not the live tables — a later load step (REPRO-equivalent bulk load) inserts them into the actual relational tables.
- Preserve `(NEW,CATLG,DELETE)` semantics: each run produces **fresh** `.IMPORT` outputs (overwrite/replace prior contents); guard against partial output on abend.
- Reproduce the **statistics counters** (records read; per-type imported; errors written; unknown types) as run-summary output for observability.
- Decide explicitly how to treat record type `D` / the missing `CARDOUT` DD before running against real data (see discrepancy note) to avoid the program's abend path.
