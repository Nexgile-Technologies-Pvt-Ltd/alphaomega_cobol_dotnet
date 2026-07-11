# CardDemo on .NET 10 (console + SQLite)

A modernization of the AWS CardDemo mainframe application as a single **.NET 10
console** program backed by **SQLite** (EF Core 10). Built strictly from the
verified documentation in `../Documentation` and the COBOL source in
`../Old_Cobol_Code`. The business rules, record layouts, seed data and batch
arithmetic are ported from the COBOL; nothing here is invented.

## Solution layout

```
New_Dotnet_Code/
  CardDemo.slnx
  Directory.Build.props / Directory.Packages.props / global.json   # pinned SDK + packages
  src/
    CardDemo.Domain          # entities, pure engines (posting, interest), validation
    CardDemo.Application      # use-case services + ports (no EF, no Console)
    CardDemo.Infrastructure   # EF Core + SQLite, fixture codecs/seeder, batch runners
    CardDemo.Console          # Generic Host, CLI router, interactive terminal (the only exe)
  tests/
    CardDemo.Tests            # 80 unit + integration + end-to-end tests
```

Clean-architecture layering per `Documentation/09-DotNet-Target-Architecture.md`:
domain rules are pure and provider-agnostic; EF Core is isolated in Infrastructure;
the Console project is the only executable.

## Build, test, run

```bash
dotnet build   CardDemo.slnx        # 0 errors
dotnet test    CardDemo.slnx        # 80 passing

# from any working directory (the DB + reports/rejects are written under the cwd):
dotnet run --project src/CardDemo.Console -- database initialize
dotnet run --project src/CardDemo.Console -- interactive
```

Or run the built binary directly: `src/CardDemo.Console/bin/Debug/net10.0/carddemo(.exe)`.

> A NuGet advisory (NU1903) is reported for the transitive `SQLitePCLRaw` native
> package that ships with EF Core 10's SQLite provider. There is no fixed version
> in the 10.0.x band, so it is surfaced as a (non-fatal) warning and tracked for a
> future provider bump; `TreatWarningsAsErrors` remains on for all real code.

## Command surface (`carddemo --help`)

Implemented (core):

| Command | Behaviour |
|---|---|
| `interactive` | Menu-driven terminal: sign-on → main/admin menu → all core screens. Requires a real TTY. |
| `database initialize` | Create the schema and load the fixtures (clean slate). |
| `database migrate` | Ensure the schema exists. |
| `database verify` | Reconcile row counts + referential integrity against the fixture oracle. |
| `batch refresh-masters` | Reload master/reference tables from fixtures. |
| `batch post-transactions` | Post `dailytran.txt` into TRANSACT, writing rejects (exit 4 if any). |
| `batch calculate-interest` | Accrue interest per account and write interest transactions. |
| `batch generate-report` | Dated transaction report (fixed-width text). |
| `batch run-pending-reports` | Process durable report requests queued from the UI. |
| `batch rebuild-transaction-index` | No-op (relational indexes are automatic). |
| `batch full-cycle` | refresh-masters → post-transactions → calculate-interest. |
| `batch generate-statements` | Per-account text + HTML statements (CBSTM03A). |
| `transfer export-branch` / `import-branch` | 500-byte branch export/import, types C/A/X/T/D (CBEXPORT/CBIMPORT). |
| `authorization submit` / `process` / `purge-expired` | Pending-authorization decisioning + purge (COPAUA0C/CBPAUP0C). |
| `worker authorization` | Drain-process pending authorizations. |
| `inquiry account` / `date` / `replies` | Enqueue and read account-inquiry / system-date requests. |
| `worker account-inquiry` / `system-date` | Answer pending inquiry requests (COACCT01/CODATE01). |

Interactive menu additionally offers **transaction-type maintenance** (CTLI/CTTU) and
**pending-authorization** inquiry with fraud toggle (CPVS/CPVD).

`batch combine-transactions` reconciles the posted + interest generations into the
transaction master; `authorization load`/`unload` externalize pending-auth data;
`full-cycle` accepts `--profile Safe|StrictLegacy`. Account update enforces the full
COACTUPC customer-validation set (SSN, DOB, US state table, state/ZIP cross-check,
phone area codes from CSLKPCDY); the transaction report emits CBTRN03C page headers,
per-card account totals and type/category descriptions. Every documented command is
now implemented.

Exit codes follow the documented contract: `0` ok, `2` usage error, `4` business
rejects, `8` unavailable/optional, `12` fatal, `130` cancelled.

## Seed data (loaded from the shipped ASCII fixtures)

| Table | Rows | Table | Rows |
|---|--:|---|--:|
| Accounts | 50 | Disclosure groups | 51 |
| Customers | 50 | Transaction types | 7 |
| Cards | 50 | Transaction categories | 18 |
| Card cross-references | 50 | Users | 10 |
| Transaction category balances | 50 | | |

Money is decoded from zoned/overpunch display fields (e.g. `0000005047G` → `+504.77`)
and stored as integer minor units; rates as integer hundredths of a percent.
`database verify` checks these exact counts plus referential integrity.

### Default users (bootstrap password `PASSWORD`)

`ADMIN001`–`ADMIN005` are administrators (type `A`); `USER0001`–`USER0005` are
regular users (type `U`). Passwords are stored **hashed** (PBKDF2-SHA256), never
in cleartext — the legacy plaintext scheme (DEF-SEC-001) is not reproduced.

## Verified behaviour (representative)

- Posting the delivered `dailytran.txt`: **262 accepted, 38 rejected (all reason 102 overlimit)** — matches the documented oracle; posting returns exit 4.
- Interest: `(balance × rate) / 1200` truncated to 2 dp, type `01` / category `0005`, transaction id = `<cycle-id>` + 6-digit suffix; the safe profile updates the final account (the strict-legacy EOF-skip quirk is available behind a flag).
- Seed values are byte-exact (e.g. account `00000000001` = balance 194.00, credit limit 2020.00).
- The full `initialize → post → interest → report` flow is deterministic and re-runnable.

## Tests (132)

Domain (overpunch, money truncation, validation, posting engine, interest engine),
persistence/seed integration (exact count oracle, referential verify, posting
262/38, re-run idempotency, verify-after-cycle), online services (auth, account
view/update, card list/view/update, transaction add/list, bill pay, user admin,
report-request persistence), data-value fidelity, two end-to-end interactive-terminal
sessions over scripted input, and the optional modules (transaction-type maintenance,
statements incl. HTML escaping, branch export/import round-trip, authorization
approve/decline/fraud/purge, and account-inquiry/system-date services).

## Modelling note on the optional modules

The optional authorization (IMS/Db2/MQ), branch export/import, and inquiry/date
services are implemented at **safe-target functional level**: IMS segments, Db2
tables and MQ request/reply are modelled as relational tables and a durable local
queue (as `Documentation/09-DotNet-Target-Architecture.md` prescribes), not as
byte-exact COMP-3 / packed-IMS-key / IBM-MQ wire parity. Behaviour and round-trip
tests are the acceptance bar. Only `batch combine-transactions` remains a stub.
