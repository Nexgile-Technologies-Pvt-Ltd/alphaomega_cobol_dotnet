# JOB SPEC: COMBTRAN

## Overview

- **JCL member**: `COMBTRAN.jcl`
- **JOB name**: `COMBTRAN`
- **JOB description**: `'COMBINE TRANSACTIONS'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **Posting-prep / file setup (merge + load).** A two-step batch job that **combines two transaction inputs into one sorted file and then bulk-loads that file into the transaction master VSAM KSDS.**
  1. `STEP05R` runs **SORT** to merge the latest **backup transaction** generation (`TRANSACT.BKUP(0)`) with the latest **system-generated transaction** generation (`SYSTRAN(0)`), sorting the combined records ascending by transaction id, producing a **new GDG generation** `TRANSACT.COMBINED(+1)`.
  2. `STEP10` runs **IDCAMS REPRO** to load that combined sequential file into the keyed transaction master `TRANSACT.VSAM.KSDS`.
  - This is the **front of the daily transaction-master refresh sequence**: it rebuilds the `TRANSACT` KSDS from a sorted union of already-posted (backup) transactions and newly system-generated transactions, so downstream jobs (e.g. `POSTTRAN`/`CBTRN02C`, transaction reporting) read a current, sorted, keyed master. It is **not** a report or a calculation job; it is a **merge/sort + reload (file setup) job**.

## Step summary

| Step | PGM | Type | Action |
|------|-----|------|--------|
| STEP05R | SORT | Utility (DFSORT/SORT) | Merge `TRANSACT.BKUP(0)` + `SYSTRAN(0)` into one file, sort ascending by `TRAN-ID` (positions 1-16, char); write new generation `TRANSACT.COMBINED(+1)` |
| STEP10 | IDCAMS | Utility (Access Method Services) | `REPRO` the combined sequential file `TRANSACT.COMBINED(+1)` into the transaction master VSAM KSDS `TRANSACT.VSAM.KSDS` |

There are **2 EXEC steps** invoking **2 utility programs: `SORT` and `IDCAMS`** (no custom `CB*` COBOL program, no `IEFBR14`). **GDG is used** (three generation data groups). **No `COND=`/`PARM=` is coded** on either EXEC.

---

## STEP05R — Merge + sort transactions (PGM=SORT)

- **EXEC**: `PGM=SORT` (the system SORT utility / DFSORT; alias of `ICEMAN`/`SYNCSORT` depending on installation).
- **PARM=**: none.
- **COND/RC gating**: none coded.
- **Purpose**: concatenate two input transaction files and produce a single output file ordered by transaction id. Because two `SORTIN` datasets are concatenated, this is effectively a **merge-by-sort** of the prior backup transactions and the freshly system-generated transactions.

### Control statements (exact)

`SYMNAMES DD *` (symbolic field name definitions used by `SORT FIELDS`):

```
TRAN-ID,1,16,CH
```

- Defines symbol `TRAN-ID` = byte offset **1**, length **16**, format **CH** (character). This corresponds to the leading 16-byte `TRAN-ID` key of the transaction record (copybook `CVTRA05Y`, `TRAN-RECORD`, field `TRAN-ID PIC X(16)`).

`SYSIN DD *` (sort control):

```
 SORT FIELDS=(TRAN-ID,A)
```

- **SORT FIELDS=(TRAN-ID,A)** — single control field, the symbol `TRAN-ID` (cols 1-16, CH), **A = ascending**. The output is the full combined record set ordered ascending by transaction id. No `INCLUDE`/`OMIT`, no `SUM`, no reformatting (`OUTREC`) — a straight sort/merge that preserves whole records.

### DD statements / datasets

| DD | Disposition | DSN | I/O | GDG | Maps to (file / relational table) |
|----|-------------|-----|-----|-----|-----------------------------------|
| `SORTIN` (1st) | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.BKUP(0)` | **INPUT** | **GDG, `(0)` = current/latest generation** | **Transaction backup** sequential file — the most recent backup of already-posted transactions (produced by `TRANBKP`). Record layout = `TRAN-RECORD` (`CVTRA05Y`). Logically the prior contents of the **`TRANSACTION`** table. |
| `SORTIN` (2nd, concatenated) | `DISP=SHR` | `AWS.M2.CARDDEMO.SYSTRAN(0)` | **INPUT** | **GDG, `(0)` = current/latest generation** | **System-generated transactions** sequential file — newly created/online-generated transactions awaiting consolidation into the master. Same `TRAN-RECORD` layout. New rows destined for the **`TRANSACTION`** table. |
| `SORTOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.TRANSACT.COMBINED(+1)` | **OUTPUT** | **GDG, `(+1)` = new generation created this run** | **Combined + sorted** sequential transaction file. Consumed by STEP10's REPRO. DCB copied from `SORTIN` (`DCB=(*.SORTIN)`); `UNIT=SYSDA`; `SPACE=(CYL,(1,1),RLSE)`. Same `TRAN-RECORD` (`CVTRA05Y`) layout. |
| `SYMNAMES` | `DD *` (instream) | (instream control) | INPUT | n/a | SORT symbolic field name table (defines `TRAN-ID`). No data mapping. |
| `SYSIN` | `DD *` (instream) | (instream control) | INPUT | n/a | SORT control statements. No data mapping. |
| `SYSOUT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | Sort messages / job log. No data mapping. |

- **GDG detail**: `(0)` references the **current (latest catalogued) generation** of the input GDGs; `(+1)` creates a **new generation** of the output GDG, catalogued on successful close. The GDG bases `AWS.M2.CARDDEMO.TRANSACT.BKUP`, `AWS.M2.CARDDEMO.SYSTRAN`, and `AWS.M2.CARDDEMO.TRANSACT.COMBINED` are all defined in **`DEFGDGB.jcl`** (each `LIMIT(5) SCRATCH`).
- **DCB**: `DCB=(*.SORTIN)` makes `SORTOUT` inherit record format/length/blocksize from the first `SORTIN` DD (the backup file's attributes). `RLSE` releases unused space at close.

---

## STEP10 — Load combined file to transaction master (PGM=IDCAMS)

- **EXEC**: `PGM=IDCAMS` (Access Method Services).
- **PARM=**: none.
- **COND/RC gating**: none coded on the EXEC (no `COND=`). Note: there is **no explicit gating preventing STEP10 from running if STEP05R fails** — standard JCL would still attempt STEP10 unless a step abends and abend-bypass kicks in. For the .NET port, treat STEP10 as **dependent on STEP05R success** (it consumes STEP05R's output).
- **Purpose**: copy/load the sorted combined transaction file into the keyed transaction master VSAM cluster.

### Control statements (exact)

`SYSIN DD *`:

```
   REPRO INFILE(TRANSACT) OUTFILE(TRANVSAM)
```

- **REPRO INFILE(TRANSACT) OUTFILE(TRANVSAM)** — copies every record from the DD `TRANSACT` (the combined sequential file) into the DD `TRANVSAM` (the transaction master KSDS). No `DELETE`/`DEFINE` is issued here, so the **KSDS must already exist** (defined elsewhere, e.g. `TRANFILE.jcl`). Because the target is `DISP=SHR` and REPRO loads into an existing cluster, records are **inserted by key**; for an exact rebuild the cluster is normally emptied/redefined by a prior job — see conversion note.

### DD statements / datasets

| DD | Disposition | DSN | I/O | GDG | Maps to (file / relational table) |
|----|-------------|-----|-----|-----|-----------------------------------|
| `TRANSACT` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.COMBINED(+1)` | **INPUT** | **GDG, `(+1)` = the generation just created by STEP05R** | The sorted combined sequential transaction file from STEP05R. Layout `TRAN-RECORD` (`CVTRA05Y`). |
| `TRANVSAM` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | **OUTPUT (REPRO target)** | n/a (fixed-name VSAM KSDS) | **Transaction master** — the keyed VSAM KSDS keyed on `TRAN-ID`. This is the relational **`TRANSACTION`** table (TRANSACT entity). Read downstream by `POSTTRAN`/`CBTRN02C` (`TRANFILE` DD) and transaction-reporting jobs. |
| `SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | IDCAMS print / job log. No data mapping. |
| `SYSIN` | `DD *` (instream) | (instream control) | INPUT | n/a | IDCAMS control (REPRO). No data mapping. |

- **GDG reference note**: STEP10 references `TRANSACT.COMBINED(+1)` — the **same `(+1)`** created in STEP05R. Within one job, `(+1)` consistently refers to the generation being created in this job step, so STEP10 reads exactly what STEP05R wrote.

---

## PARM / COND / GDG / SORT / IDCAMS summary

- **PARM=**: none on either EXEC.
- **COND/RC gating**: none coded (no `COND=`, no `IF/THEN/ENDIF`). STEP10 implicitly depends on STEP05R's output; enforce that ordering/dependency in the .NET runner.
- **GDG**: **used.**
  - Inputs `AWS.M2.CARDDEMO.TRANSACT.BKUP(0)` and `AWS.M2.CARDDEMO.SYSTRAN(0)` — read **current** generation (`(0)`).
  - Output `AWS.M2.CARDDEMO.TRANSACT.COMBINED(+1)` — **create new** generation (`(+1)`), referenced again by STEP10.
  - All three GDG bases defined in `DEFGDGB.jcl` (`LIMIT(5)`, `SCRATCH`).
- **SORT**: `STEP05R` only. Symbol `TRAN-ID = 1,16,CH` (via `SYMNAMES`); `SORT FIELDS=(TRAN-ID,A)` — sort whole records ascending by 16-byte transaction id. Two concatenated `SORTIN` inputs = merge. No INCLUDE/OMIT/SUM/OUTREC.
- **IDCAMS**: `STEP10` only. `REPRO INFILE(TRANSACT) OUTFILE(TRANVSAM)` — load combined sequential file into the transaction master KSDS. **No `DEFINE`/`DELETE`** issued in this job (target cluster pre-exists).
- **IEFBR14**: not used.

## Conversion notes for the .NET JobControl step-runner

- Implement as **two ordered steps**; STEP10 must only run after STEP05R succeeds and must consume STEP05R's output generation.
- **STEP05R (SORT)** -> a **merge+sort** operation: read the latest backup-transactions file and the latest system-transactions file, concatenate them, order by the 16-char `TRAN-ID` key ascending, and write a new "combined" generation. Records are the full `CVTRA05Y` `TRAN-RECORD`; no reformatting.
- **STEP10 (IDCAMS REPRO)** -> a **bulk load** of the combined file into the `TRANSACTION` keyed store (VSAM KSDS == relational `TRANSACTION` table). In the .NET data layer this is "insert/replace all records from the sorted file into the transaction master, keyed on TRAN-ID."
- **GDG semantics**: model `(0)` as "latest existing generation" and `(+1)` as "create next generation, catalog on success, and let later steps in the same job resolve `(+1)` to that same new generation." Honor the `LIMIT(5)` retention (keep last 5 generations, scratch older) from `DEFGDGB`.
- **Idempotency / rebuild caveat**: REPRO here targets an **existing** `DISP=SHR` KSDS without a preceding DELETE/DEFINE. If the intent is a clean rebuild (typical for a "combined transaction master refresh"), the runner should ensure the transaction master is emptied/replaced before the REPRO-equivalent load (or perform an upsert-by-key), to avoid duplicate-key insert failures from records already present. Decide and document this explicitly before running against live data.
- Preserve the **posting-sequence role**: the output `TRANSACTION` KSDS is the master that `POSTTRAN` (`CBTRN02C`) and transaction reports read; this job must complete before those run in a daily cycle.
