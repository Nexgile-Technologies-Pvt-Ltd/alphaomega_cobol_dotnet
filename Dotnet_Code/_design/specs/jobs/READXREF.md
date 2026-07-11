# JOB SPEC: READXREF

## Overview

| Attribute | Value |
|-----------|-------|
| **Member** | `READXREF.jcl` |
| **Job name** | `READXREF` |
| **Job description** | `'Read Cross Ref file'` |
| **Source** | `app/jcl/READXREF.jcl` (CardDemo) |
| **Purpose** | **Report / dump utility.** Runs the batch COBOL program `CBACT03C`, which sequentially reads the Card Cross-Reference VSAM KSDS master file and prints (DISPLAYs) every record to SYSOUT. This is a read-only diagnostic/report job — it does **not** update any file, table, or GDG. |
| **Step count** | 1 (`STEP05`) |
| **Programs invoked** | `CBACT03C` (CardDemo batch COBOL) |
| **Utilities invoked** | None (no IDCAMS / SORT / IEFBR14) |

### JOB card

```jcl
//READXREF JOB 'Read Cross Ref file',CLASS=A,MSGCLASS=0,
// NOTIFY=&SYSUID
```

| Parameter | Value | Notes |
|-----------|-------|-------|
| `CLASS` | `A` | Job class |
| `MSGCLASS` | `0` | Message output class |
| `NOTIFY` | `&SYSUID` | Notify the submitting user at completion |

No `COND` at the JOB level. No `RESTART`, no `TYPRUN`.

---

## STEP05 — Read & print the XREF master VSAM file

```jcl
//STEP05 EXEC PGM=CBACT03C
//STEPLIB  DD DISP=SHR,
//         DSN=AWS.M2.CARDDEMO.LOADLIB
//XREFFILE DD DISP=SHR,
//         DSN=AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS
//SYSOUT   DD SYSOUT=*
//SYSPRINT DD SYSOUT=*
```

| Item | Value |
|------|-------|
| **EXEC** | `PGM=CBACT03C` |
| **PARM** | None |
| **COND / RC gating** | None — this is the only step; runs unconditionally. No `COND=` on the EXEC and no preceding step to gate against. |
| **Program type** | Batch COBOL (CardDemo `CBACT03C`) — "Read and print account cross reference data file." |

### DD / dataset usage

| DD name | DSN | DISP | I/O | Type | Corresponds to |
|---------|-----|------|-----|------|----------------|
| `STEPLIB` | `AWS.M2.CARDDEMO.LOADLIB` | `SHR` | read | Load library (PDS) | Program load module library where `CBACT03C` resides — infrastructure, not data. |
| `XREFFILE` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | `SHR` | **read (INPUT)** | VSAM **KSDS** (indexed) | **Card Cross-Reference file** → relational table **`CARD_XREF`** (card-number ↔ customer-id ↔ account-id mapping). Opened `INPUT`, read **sequentially** by key. |
| `SYSOUT` | (spool) | — | write | SYSOUT=`*` | Report / `DISPLAY` output of each cross-reference record + program start/end and any error messages. |
| `SYSPRINT` | (spool) | — | write | SYSOUT=`*` | System/runtime messages (LE, abend diagnostics). |

### Program behavior (CBACT03C)

`CBACT03C` defines `XREFFILE-FILE` as `ORGANIZATION INDEXED`, `ACCESS MODE SEQUENTIAL`, `RECORD KEY = FD-XREF-CARD-NUM`. Record layout (50 bytes via copybook `CVACT03Y` / `CARD-XREF-RECORD`):

- `FD-XREF-CARD-NUM PIC X(16)` — 16-byte card number (the KSDS key)
- `FD-XREF-DATA PIC X(34)` — remainder of the cross-reference record (customer-id, account-id)

Flow: `OPEN INPUT` → loop `READ ... NEXT` until EOF (file status `'10'`) → `DISPLAY CARD-XREF-RECORD` for each record → `CLOSE`. On any unexpected file status it displays the status, prints a `FILE STATUS IS: NNNN` line, and abends via `CALL 'CEE3ABD'` with code 999. No writes are performed against `XREFFILE` or any other dataset.

---

## IDCAMS / SORT control statements

None. No IDCAMS step (no `DEFINE` / `REPRO` / `DELETE`) and no `SORT FIELDS` exist in this member.

## GDG usage

None. No generation data groups are referenced (no `(+1)` / `(0)` / `GDGMOD`).

## Cross-job notes

- The `CARDXREF` KSDS read here is normally created/loaded by upstream setup jobs (e.g., DEFGDGB / file-load jobs) and consumed by online (`COCRDxxx`) and batch (`CBTRN0xC`) programs that resolve a card number to its account/customer. `READXREF` is purely an inspection/report job over that file.

## .NET JobControl mapping hints

- Single-step job → one runner step invoking the .NET port of `CBACT03C`.
- `XREFFILE` → repository read over the `CARD_XREF` table (or sequential KSDS-equivalent store), iterated in key order ascending by 16-char card number.
- `SYSOUT`/`SYSPRINT` → step log / report sink; each record `DISPLAY` becomes a logged line.
- No conditional gating, no GDG resolution, no utility control-card parsing required.
