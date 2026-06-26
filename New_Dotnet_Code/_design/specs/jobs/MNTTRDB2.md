# JOB SPEC: MNTTRDB2

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/jcl/MNTTRDB2.jcl`

## Overall Purpose

Batch maintenance of the DB2 reference table `CARDDEMO.TRANSACTION_TYPE`. The job
applies a flat sequential **change file** (`INPFILE`) against the relational table,
performing **ADD / UPDATE / DELETE** of transaction-type rows in batch. It is the
batch counterpart of the online CICS transactions CTTU (add/edit) and CTLI
(list/update/delete).

This is a **data-maintenance / table-update** job (not file setup, posting, report,
or backup). It is driven by the TSO/E batch terminal monitor program (`IKJEFT01`),
which runs DB2's `DSN` command processor to bind the application to a plan and
execute the COBOL+DB2 program `COBTUPDT` under it.

There is a single job step.

---

## JOB Card

```
//MNTTRDB2 JOB (COBOL),'MNTTRDB2',CLASS=A,MSGCLASS=H,MSGLEVEL=(1,1),
//         NOTIFY=&SYSUID,TIME=1440
```

| Attribute | Value |
|-----------|-------|
| Job name | `MNTTRDB2` |
| Accounting | `(COBOL)` |
| Programmer | `MNTTRDB2` |
| CLASS | `A` |
| MSGCLASS | `H` |
| MSGLEVEL | `(1,1)` |
| NOTIFY | `&SYSUID` (submitting TSO user) |
| TIME | `1440` (effectively unlimited CPU time) |

---

## STEP 1: `STEP1` — Run COBTUPDT under DB2 via TSO/E (IKJEFT01)

```
//STEP1   EXEC PGM=IKJEFT01,REGION=0M
```

| Item | Value |
|------|-------|
| Step name | `STEP1` |
| PGM | `IKJEFT01` (TSO/E batch terminal monitor program, TMP) |
| REGION | `0M` (no storage limit — allow maximum region) |
| PARM | none on the EXEC; the real work is driven by `SYSTSIN` in-stream commands |
| COND / RC gating | none — there is no `COND=` parameter and no prior step to gate against |
| GDG usage | none — no generation data groups are referenced |

### What actually runs

`IKJEFT01` is only the launcher. The `SYSTSIN` stream issues:

```
DSN SYSTEM(DAZ1)
RUN PROGRAM(COBTUPDT) PLAN(CARDDEMO)
```

So the executed application program is **`COBTUPDT`** (a `CB*`-style CardDemo COBOL
program with embedded static SQL), run under DB2 subsystem **`DAZ1`** using plan
**`CARDDEMO`**.

> Note: `RUN` here omits an explicit `LIB(...)` operand, so the load library is
> resolved from the step's `STEPLIB` concatenation.

### DD / Dataset usage

| DD name   | DSN / target                       | DISP     | Role | Reads / Writes | Corresponds to |
|-----------|------------------------------------|----------|------|----------------|----------------|
| `STEPLIB` | `OEM.DB2.DAZ1.SDSNEXIT`            | SHR      | Load lib | read | DB2 subsystem exit/parmlib load library (DAZ1) |
| `STEPLIB` | `OEMA.DB2.VERSIONA.SDSNLOAD`       | SHR      | Load lib | read | DB2 base load library (DSN modules, runtime) |
| `STEPLIB` | `AWS.M2.CARDDEMO.LOADLIB`          | SHR      | Load lib | read | CardDemo application load library (where `COBTUPDT` lives) |
| `DBRMLIB` | `AWS.M2.CARDDEMO.DBRMLIB`          | SHR      | DBRM lib | read | DB2 database request modules used by/for the `CARDDEMO` plan |
| `SYSTSPRT`| `SYSOUT=*`                          | —        | Print | write | TSO/DSN messages + program `DISPLAY` output (run log) |
| `INPFILE` | `DSN=INPFILE` (placeholder dataset) | SHR      | Sequential input | read | The **change/transaction file** of maintenance records (see layout below) |
| `SYSTSIN` | in-stream `DD *`                    | —        | TSO command input | read | `DSN` / `RUN` commands that launch `COBTUPDT` under plan `CARDDEMO` |

> `DSN=INPFILE` is a literal placeholder; in real use it is overridden with the
> actual fully-qualified sequential dataset that holds the maintenance records.
> The program reads it via `SELECT TR-RECORD ASSIGN TO INPFILE` (QSAM, fixed,
> sequential, record length 53).

### Relational table affected

- **`CARDDEMO.TRANSACTION_TYPE`** — the only table touched. (No other table is
  read or written by `COBTUPDT`.) Definition (from `ddl/TRNTYPE.ddl`):

  | Column | Type | Notes |
  |--------|------|-------|
  | `TR_TYPE` | `CHAR(2)` NOT NULL | Primary key — the 2-char transaction-type code |
  | `TR_DESCRIPTION` | `VARCHAR(50)` NOT NULL | Transaction-type description |

  DCLGEN declaration: `dcl/DCLTRTYP.dcl` (`DCLTRANSACTION-TYPE`).

---

## `INPFILE` record layout (the change file)

Fixed-length 53-byte records (RECFM F). Columns, per the JCL header comments and
the program's `WS-INPUT-REC`:

| Cols | Field | PIC | Meaning |
|------|-------|-----|---------|
| 1    | `INPUT-REC-TYPE`   | `X(1)`  | Action code |
| 2–3  | `INPUT-REC-NUMBER` | `X(2)`  | Transaction type (numeric value) → maps to `TR_TYPE` |
| 4–53 | `INPUT-REC-DESC`   | `X(50)` | Transaction description → maps to `TR_DESCRIPTION` |

Action codes (column 1):

| Code | Action |
|------|--------|
| `A`  | ADD — `INSERT INTO CARDDEMO.TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION)` |
| `U`  | UPDATE — `UPDATE ... SET TR_DESCRIPTION = :desc WHERE TR_TYPE = :num` |
| `D`  | DELETE — `DELETE FROM ... WHERE TR_TYPE = :num` |
| `*`  | COMMENT — line ignored |
| other | invalid → program builds an error message and abends with `RETURN-CODE = 4` |

---

## Program behavior (COBTUPDT) — for the step-runner

`cbl/COBTUPDT.cbl`. Reads `INPFILE` sequentially to EOF, and for each record:

- Routes on column 1 (`A`/`U`/`D`/`*`/other) via an `EVALUATE`.
- Executes the corresponding **static embedded SQL** against
  `CARDDEMO.TRANSACTION_TYPE`, using host variables `:INPUT-REC-NUMBER` and
  `:INPUT-REC-DESC`.
- Checks `SQLCODE` after each statement:
  - `0` → success (`DISPLAY` of a success message).
  - `+100` (on UPDATE/DELETE) → "No records found." → **abend path** (sets
    `RETURN-CODE = 4`).
  - `< 0` → "Error accessing TRANSACTION_TYPE table. SQLCODE: nnnn" → **abend path**
    (`RETURN-CODE = 4`).
- Closes the file and `STOP RUN`.

Because the program runs under the `DSN RUN` command of `IKJEFT01`, a non-zero
`RETURN-CODE` from `COBTUPDT` surfaces as the step's return code (DB2 commit/abend
handling is provided by the TMP/DSN environment; the program itself issues no
explicit `COMMIT`/`ROLLBACK`).

---

## Control statements (IDCAMS / SORT)

None. This job contains **no IDCAMS, SORT, IEFBR14, REPRO, DEFINE, DELETE, or SORT
FIELDS** statements. The only in-stream control is the `SYSTSIN` TSO/DSN command
sequence shown above (`DSN SYSTEM(DAZ1)` / `RUN PROGRAM(COBTUPDT) PLAN(CARDDEMO)`).

---

## .NET JobControl mapping notes

- One step → one runner invocation of a ported `COBTUPDT`.
- `IKJEFT01` + `DSN ... RUN PROGRAM(...) PLAN(...)` collapses to "execute program
  `COBTUPDT` with a DB2/SQL connection (subsystem `DAZ1`, plan `CARDDEMO`)".
- `INPFILE` → a sequential input file binding (fixed 53-byte records); resolve the
  real path at run time (the JCL `DSN=INPFILE` is a placeholder).
- Target store: relational table `CARDDEMO.TRANSACTION_TYPE` (PK `TR_TYPE` CHAR(2),
  `TR_DESCRIPTION` VARCHAR(50)).
- Map program return code: success = 0; not-found on U/D and SQL errors = 4 (abend).
- No GDG, no COND gating, no SORT/IDCAMS utilities to emulate.
