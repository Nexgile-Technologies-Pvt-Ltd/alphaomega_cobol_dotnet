# 1. Product scope and capability map

[<- Documentation conventions](Documentation-Conventions.md) | [Home](Home.md) | [System context ->](02-System-Context-and-Architecture.md)

## Scope statement

This specification covers every artifact in `Old_Cobol_Code` and defines what must be understood before a functionally equivalent **.NET 10 console application** is implemented. The source snapshot is a CardDemo credit-card servicing system with a base CICS/VSAM online application, a z/OS batch estate, and three separately packaged optional extensions. The [source inventory](Appendix-Source-Inventory.md#inventory-snapshot) is the completeness ledger; functional claims are proved by the linked source, not by filenames alone.

The replacement product is one console executable targeting `net10.0`. It has an interactive terminal mode for the online workflows and non-interactive commands for batch, transfer, reporting, maintenance, and optional queue workers. The runtime design is specified in [.NET target architecture](09-DotNet-Target-Architecture.md#target-constraints).

## Product boundary

### In scope

| Boundary | Included behavior | Detailed contract |
|---|---|---|
| Sign-on and session routing | user lookup, password comparison, administrator/regular routing, sign-off | [Authentication](03-Functional-Requirements.md#authentication-and-session) |
| Regular-user terminal functions | account view/update, card list/view/update, transaction list/view/add, report request, bill payment | [Online screens](04-Online-Screens-and-Navigation.md#transaction-and-screen-catalog) |
| Administrator terminal functions | security-user list/add/update/delete and optional transaction-type maintenance | [User administration](03-Functional-Requirements.md#security-user-administration) |
| Core data | customer, account, card, cross-reference, transaction, type/category, category balance, disclosure, and users | [Domain data](06-Domain-Data-Model.md#core-entity-catalog) |
| Core batch | master refresh, posting/rejects, interest, report, statement, index rebuild, diagnostics, waits, import/export | [Batch workload](05-Batch-Processing.md#batch-workload-catalog) |
| Optional Db2 transaction-type module | type list, create/update/delete, extract and batch maintenance | [Transaction-type module](07-Optional-Modules-and-Integrations.md#transaction-type-db2-module) |
| Optional authorization module | MQ request/reply, IMS pending state/history, Db2 fraud state, inquiry/detail/fraud screens, purge/load/unload | [Authorization module](07-Optional-Modules-and-Integrations.md#authorization-imsdb2mq-module) |
| Optional VSAM/MQ services | account inquiry and date/time request/reply workers | [MQ services](07-Optional-Modules-and-Integrations.md#vsammq-account-and-date-servers) |
| Operational definitions | JCL/procedures, CSD resources, schedulers, shell streams, samples, diagrams and repository metadata | [Operations](12-Operations-and-Deployment.md#operational-workloads) |

### Supplied artifacts that are evidence, not product features

- Compile/link JCL, RACF/LISTCAT/SORT/REPRO examples, diagrams, build scripts, copyright/license files, and Git utilities describe development or operations; they do not create end-user functions.
- ASCII/EBCDIC data is a deterministic fixture. Counts and values may be used as acceptance oracles, but are not universal business limits.
- README text and diagrams are secondary evidence when executable code or runtime definitions disagree, per [source precedence](Documentation-Conventions.md#source-precedence).
- Mainframe-specific mechanics such as CICS `XCTL`, BMS map delivery, VSAM status codes, JES internal-reader submission, IMS calls, and MQMD fields are compatibility inputs. They become console navigation, repositories, command orchestration, and adapters; they are not requirements to emulate a mainframe runtime inside .NET.

### Not silently assumed

The source does not prove production-scale service levels, legal/regulatory classifications, geographic deployment, retention periods, disaster-recovery objectives, enterprise identity-provider integration, email delivery, PDF rendering, or a web API. These are not invented here. Where the shipped jobs reference site dependencies such as FTP or text-to-PDF conversion, the dependency is recorded as an [open deployment decision](14-Known-Defects-and-Open-Decisions.md#environment-and-external-dependencies). README roadmap items such as rewards, IMS DC, SFTP, web-service connectivity and distributed transaction exposure are outside shipped parity unless separately approved as new requirements; see [source contradictions](14-Known-Defects-and-Open-Decisions.md#source-contradictions).

## Capability map

| ID | Capability | Actors / trigger | Source entry point | Replacement surface |
|---|---|---|---|---|
| CAP-01 | Authenticate and route a user | terminal user | [`COSGN00C`](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L1) | `carddemo interactive` sign-on |
| CAP-02 | Navigate regular menu | regular user | [`COMEN01C`](../Old_Cobol_Code/app/cbl/COMEN01C.cbl#L93-L188) | interactive main menu |
| CAP-03 | View/update account and customer data | regular user | [`COACTVWC`](../Old_Cobol_Code/app/cbl/COACTVWC.cbl#L1), [`COACTUPC`](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L1) | account views/controllers |
| CAP-04 | List/view/update cards | regular user | [`COCRDLIC`](../Old_Cobol_Code/app/cbl/COCRDLIC.cbl#L1), [`COCRDSLC`](../Old_Cobol_Code/app/cbl/COCRDSLC.cbl#L1), [`COCRDUPC`](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L1) | card views/controllers |
| CAP-05 | List/view/add transactions | regular user | [`COTRN00C`](../Old_Cobol_Code/app/cbl/COTRN00C.cbl#L1), [`COTRN01C`](../Old_Cobol_Code/app/cbl/COTRN01C.cbl#L1), [`COTRN02C`](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L1) | transaction views/controllers |
| CAP-06 | Request dated transaction reports | regular user | [`CORPT00C`](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L1) | queued report request / batch command |
| CAP-07 | Pay an account balance in full | regular user | [`COBIL00C`](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L1) | bill-payment controller |
| CAP-08 | Navigate administrator menu | administrator | [`COADM01C`](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L1) | interactive admin menu |
| CAP-09 | Maintain security users | administrator | [`COUSR00C`](../Old_Cobol_Code/app/cbl/COUSR00C.cbl#L1) through [`COUSR03C`](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L1) | user administration controllers |
| CAP-10 | Post daily transactions and reject failures | scheduled/operator command | [`CBTRN02C`](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L197-L231), [`POSTTRAN.jcl`](../Old_Cobol_Code/app/jcl/POSTTRAN.jcl#L1) | `batch post-transactions` |
| CAP-11 | Calculate monthly interest | scheduled/operator command | [`CBACT04C`](../Old_Cobol_Code/app/cbl/CBACT04C.cbl#L188-L221), [`INTCALC.jcl`](../Old_Cobol_Code/app/jcl/INTCALC.jcl#L1) | `batch calculate-interest` |
| CAP-12 | Produce report and statements | scheduled/operator command | [`CBTRN03C`](../Old_Cobol_Code/app/cbl/CBTRN03C.cbl#L1), [`CBSTM03A`](../Old_Cobol_Code/app/cbl/CBSTM03A.CBL#L1), [`CBSTM03B`](../Old_Cobol_Code/app/cbl/CBSTM03B.CBL#L1) | report/statement commands |
| CAP-13 | Import/export branch records | operator command | [`CBEXPORT`](../Old_Cobol_Code/app/cbl/CBEXPORT.cbl#L1), [`CBIMPORT`](../Old_Cobol_Code/app/cbl/CBIMPORT.cbl#L1) | `transfer export-branch` / `import-branch` |
| CAP-14 | Maintain transaction types in Db2 | optional administrator/operator | [`COTRTLIC`](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTLIC.cbl#L1), [`COTRTUPC`](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1), [`COBTUPDT`](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COBTUPDT.cbl#L1) | optional interactive and batch commands |
| CAP-15 | Decide, inspect, flag, and purge pending authorizations | optional MQ/terminal/batch actor | [`COPAUA0C`](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L1), [`COPAUS0C`](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS0C.cbl#L1), [`CBPAUP0C`](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl#L1) | optional worker, screens, purge command |
| CAP-16 | Answer MQ account/date inquiries | optional MQ requester | [`COACCT01`](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L1), [`CODATE01`](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L1) | optional inquiry/date workers |

## Actors and access intent

| Actor | Legacy proof | Intended access |
|---|---|---|
| Regular user (`U`, and operationally any non-`A` sign-on role) | sign-on copies the stored type and branches on administrator; shared role values are declared in [`COCOM01Y`](../Old_Cobol_Code/app/cpy/COCOM01Y.cpy#L25-L31) | regular menu capabilities |
| Administrator (`A`) | [`COSGN00C`](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L218-L238) routes administrators to the admin menu | user maintenance and optional type maintenance |
| Batch operator / scheduler | 55 JCL artifacts plus stream and scheduler definitions | load, post, calculate, report, transfer, archive and maintenance commands |
| MQ requester | MQ request/reply programs consume request queues | optional authorization, account inquiry and date service |
| .NET operator | target-only operational actor | configure, initialize/migrate/verify database, run commands and workers |

The legacy role is carried in a mutable communication area and most called programs do not re-authorize it. That is an observed weakness, not the target policy. The safe target enforces authorization at every use case; see [authorization matrix](08-Security-and-Controls.md#authorization-matrix).

## Deployment profiles

| Profile | Required content | Optional content |
|---|---|---|
| Core | all base online workflows, core entities, core batch, reports/statements and transfer | none |
| Core + transaction types | Core | Db2-derived type/category persistence and maintenance |
| Core + authorization | Core | pending authorization queues/state/history/fraud and UI |
| Core + inquiry workers | Core data | MQ-compatible account and date services |
| Complete | Core plus all three optional packages | IBM MQ/Db2 interoperability adapters may be enabled independently of local equivalents |

The planned default .NET distribution shall contain the code for the complete profile but enable optional commands/workers only through validated configuration. No optional module may alter core behavior merely by being present. This is a target packaging requirement, not a claim that a distribution already exists.

## Completion definition

The rebuild is product-complete only when:

1. every capability above is implemented, explicitly excluded by an approved decision, or preserved as a documented no-op when the source itself is a stub;
2. every functional requirement has implementation and test traceability;
3. every core/optional COBOL program and operational JCL is mapped in [Traceability](13-Traceability-and-Coverage.md#artifact-coverage);
4. record codecs pass byte-level tests against supplied fixtures and layouts;
5. interactive maps and key paths pass deterministic terminal tests;
6. batch fixture results match the documented parity oracle, with safe-mode deviations separately approved;
7. every unresolved contradiction in [Known Defects and Open Decisions](14-Known-Defects-and-Open-Decisions.md#decision-register) has an owner/disposition before production release.

---

[<- Documentation conventions](Documentation-Conventions.md) | [Home](Home.md) | [System context ->](02-System-Context-and-Architecture.md)
