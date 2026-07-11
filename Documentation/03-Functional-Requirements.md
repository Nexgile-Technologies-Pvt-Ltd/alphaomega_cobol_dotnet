# 3. Functional requirements

[<- System context](02-System-Context-and-Architecture.md) | [Home](Home.md) | [Online screens ->](04-Online-Screens-and-Navigation.md)

## Requirements index

This page is the consolidated implementation checklist. “Shall” describes the .NET 10 product. `Parity` means the observed source result must be reproducible in characterization tests. `Safe` means the production default deliberately corrects an observed weakness; the legacy result remains documented and testable where it is safe to do so. Detailed validation order, fields, messages, byte layouts, defects and evidence are normative on the linked pages.

| Area | Requirement range | Detailed specification |
|---|---|---|
| Authentication/session | `FR-AUTH-001`-`006` | [Sign-on and session](#authentication-and-session) |
| Accounts/customers | `FR-ACCT-001`-`010` | [Account workflows](#account-and-customer-workflows) |
| Cards | `FR-CARD-001`-`008` | [Card workflows](#card-workflows) |
| Transactions | `FR-TRAN-001`-`011` | [Transaction workflows](#transaction-workflows) |
| Reports/bill payment | `FR-RPT-001`-`006`, `FR-BILL-001`-`005` | [Reports](#report-and-statement-workflows), [Bill payment](#bill-payment) |
| Users | `FR-USER-001`-`008` | [Security-user administration](#security-user-administration) |
| Batch/transfer | `FR-BATCH-001`-`018` | [Batch](#batch-and-transfer-requirements) |
| Optional modules | `FR-OPT-001`-`017` | [Optional](#optional-module-requirements) |
| Data/nonfunctional | `DATA-001`-`009`, `NFR-001`-`011` | [Data](#data-contract-requirements), [Quality](#non-functional-requirements) |

## Authentication and session

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-AUTH-001 | Parity | Interactive mode shall begin on the 24x80 sign-on screen with eight-character user ID and masked eight-character password fields. Blank values fail in user-ID-then-password order. | [Sign-on field contract](04-Online-Screens-and-Navigation.md#sign-on), [`COSGN00.bms`](../Old_Cobol_Code/app/bms/COSGN00.bms#L75-L200) |
| FR-AUTH-002 | Parity/Safe | Legacy parity shall uppercase entered user ID and password, look up by user ID, and preserve the distinct `User not found. Try again ...` and `Wrong Password. Try again ...` outcomes. The safe target shall expose one generic authentication-failure message while retaining only a sanitized internal reason code. | [`COSGN00C` input normalization](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L108-L140), [`COSGN00C` distinct outcomes](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L221-L256), [safe credential behavior](08-Security-and-Controls.md#safe-target-credential-model) |
| FR-AUTH-003 | Safe | Imported plaintext fixture passwords shall be converted to hashes; no production store, log, screen model or error may retain/expose a reusable plaintext credential. | [Credential controls](08-Security-and-Controls.md#credential-storage-and-migration) |
| FR-AUTH-004 | Parity/Safe | Stored role `A` shall route to the admin menu; the source routes all other values to the regular menu. Safe mode shall accept only explicit roles `A` and `U` and reject corrupt roles. | [`COSGN00C` routing](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L218-L257), [`COCOM01Y`](../Old_Cobol_Code/app/cpy/COCOM01Y.cpy#L25-L31) |
| FR-AUTH-005 | Safe | Each privileged use case shall re-check the authenticated role; authorization must not trust a mutable route/session field supplied by a screen. | [Legacy authorization weakness](08-Security-and-Controls.md#legacy-security-model) |
| FR-AUTH-006 | Parity | F3 from sign-on shall exit; F3 from subordinate screens shall return through the documented menu/list route while retaining only the context named by that workflow. | [Key contract](04-Online-Screens-and-Navigation.md#key-contract) |

## Account and customer workflows

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-ACCT-001 | Parity | Account view shall require an 11-cell, numeric, nonzero account ID and resolve cross-reference -> account -> customer in that order. | [Account view](04-Online-Screens-and-Navigation.md#account-view), [`COACTVWC`](../Old_Cobol_Code/app/cbl/COACTVWC.cbl#L622-L870) |
| FR-ACCT-002 | Parity | A successful view shall render every mapped account/customer field at its BMS width, including the source’s five-cell ZIP and thirteen-cell phone display clipping. | [Account view field table](04-Online-Screens-and-Navigation.md#account-view) |
| FR-ACCT-003 | Parity | Account update shall fetch by validated account ID, protect the key/customer/country fields as observed, expose editable fields, and make F5 Save available only after validation. F12 restores the fetched values. | [Account update state machine](04-Online-Screens-and-Navigation.md#account-update) |
| FR-ACCT-004 | Parity | Update validation shall run in the exact documented order: account status/dates/amounts; customer SSN/DOB/FICO/names/address/state/ZIP/city/country/phones/EFT/primary flag; then state/ZIP cross-check. | [`COACTUPC` orchestration](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L1429-L1675) |
| FR-ACCT-005 | Parity | Date validation shall reproduce the 19/20-century calendar and leap rules; DOB must be strictly earlier than the injected current date. | [`CSUTLDPY`](../Old_Cobol_Code/app/cpy/CSUTLDPY.cpy#L18-L370) |
| FR-ACCT-006 | Parity | FICO shall be 300-850; SSN, US state, state/ZIP and phone-area rules shall use the literal source tables/rules. Fields for which source has no semantic edit shall receive only width/encoding validation. | [`COACTUPC`](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L2431-L2558), [`CSLKPCDY` phone-area](../Old_Cobol_Code/app/cpy/CSLKPCDY.cpy#L521-L930), [`CSLKPCDY` state & state/ZIP](../Old_Cobol_Code/app/cpy/CSLKPCDY.cpy#L1012-L1313) |
| FR-ACCT-007 | Safe | Update shall use optimistic concurrency against the fetched account and customer versions and report a conflict without overwriting a later change. | [Persistence ordering](04-Online-Screens-and-Navigation.md#data-access-and-persistence-ordering) |
| FR-ACCT-008 | Safe | Account and customer changes shall commit atomically. Fault injection between their writes must leave both unchanged. Strict characterization may expose source ordering without using it in production. | [`COACTUPC` source ordering](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L3888-L4193) |
| FR-ACCT-009 | Safe | The target shall persist ZIP and disclosure group in their correct independent fields. It shall never emit the source update layout that shifts group into ZIP unless a named migration-only codec is explicitly selected. | [Account layout defect](06-Domain-Data-Model.md#source-level-integrity-gaps) |
| FR-ACCT-010 | Parity/Safe | Decimal text shall be parsed with the documented invariant `NUMVAL-C`-compatible grammar and money shall be rounded/persisted to exact cents, never binary floating point. | [Numeric interpretation](Documentation-Conventions.md#numeric-and-date-interpretation) |

## Card workflows

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-CARD-001 | Parity | Card list shall accept account and/or card filters, show seven rows, accept the first row action `S/s` or `U/u`, and implement F7/F8 keyset paging plus F3 return. | [Card list](04-Online-Screens-and-Navigation.md#card-list) |
| FR-CARD-002 | Safe | Page availability shall be computed from the next matching row. A strict test switch may reproduce the unfiltered look-ahead defect. | [Pagination contract](04-Online-Screens-and-Navigation.md#list-pagination-contract) |
| FR-CARD-003 | Parity | Card view shall require numeric account and card entries and display the mapped card values. | [Card view](04-Online-Screens-and-Navigation.md#card-view-and-card-update) |
| FR-CARD-004 | Safe | View/update shall verify that the requested card belongs to the requested account; the direct card-key lookup in the source is not sufficient authorization. | [Card data-access defect](04-Online-Screens-and-Navigation.md#source-observed-defects-and-safe-target-decisions) |
| FR-CARD-005 | Parity | Card update shall edit embossed name, active status and expiration month/year, validate name characters, `Y/N`, month 1-12 and year 1950-2099, and expose F5 Save/F12 Cancel/F3 Return as documented. | [Card update](04-Online-Screens-and-Navigation.md#card-view-and-card-update) |
| FR-CARD-006 | Safe | The full expiration date, including the source-hidden day, shall be validated before persistence; an invalid calendar date shall not be written. | [Card defects](04-Online-Screens-and-Navigation.md#source-observed-defects-and-safe-target-decisions) |
| FR-CARD-007 | Safe | Update shall preserve CVV unless an authorized, explicitly implemented CVV use case supplies a valid replacement; it shall never write the source’s uninitialized new-CVV field. | [`COCRDUPC`](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L1464-L1465) |
| FR-CARD-008 | Safe | Card mutation shall use optimistic concurrency and atomic relationship validation/update. | [Target persistence](09-DotNet-Target-Architecture.md#units-of-work) |

## Transaction workflows

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-TRAN-001 | Parity | Transaction list shall accept a 16-cell search value, show ten rows, accept the first `S/s`, and implement F7/F8/F3 navigation. | [Transaction list](04-Online-Screens-and-Navigation.md#transaction-list) |
| FR-TRAN-002 | Parity | Transaction detail shall look up a 16-character transaction ID and render all mapped transaction/card/account-derived values; Enter searches, F4 clears, F5 returns to list, F3 returns to menu. | [Transaction view](04-Online-Screens-and-Navigation.md#transaction-view) |
| FR-TRAN-003 | Safe | Detail is read-only and shall not acquire an update lock merely to display a record. | [Observed read-for-update defect](04-Online-Screens-and-Navigation.md#source-observed-defects-and-safe-target-decisions) |
| FR-TRAN-004 | Parity | Add shall resolve account first when supplied (and select its cross-reference card); otherwise resolve supplied card to account; both absent is an error. | [`COTRN02C`](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L193-L230) |
| FR-TRAN-005 | Parity | Add shall apply the documented required-field and format validation order for type, category, source, description, signed amount, origin/process dates, and merchant data. | [Transaction add](04-Online-Screens-and-Navigation.md#transaction-add) |
| FR-TRAN-006 | Parity | The strict contract does not infer reference lookups, account/card status checks, chronological ordering or extra ZIP rules absent from the source. Safe mode may add them only as approved decisions with separate tests. | [`COTRN02C` validation](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L235-L437) |
| FR-TRAN-007 | Safe | Confirmation shall never bypass validation. In the source, each validation error runs a send-screen paragraph ending in `EXEC CICS RETURN`, so the task terminates before the confirmation is evaluated and invalid data is not added; the safe target shall gate mutation on an explicit validated-state check rather than on control-flow termination. | [Transaction-add validation gate](04-Online-Screens-and-Navigation.md#source-observed-defects-and-safe-target-decisions) |
| FR-TRAN-008 | Parity | F5 shall populate non-key input fields from the greatest transaction-ID record and then use normal validation/confirmation behavior. | [`COTRN02C`](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L471-L495) |
| FR-TRAN-009 | Safe | New IDs shall be unique under concurrency. A database allocator/sequence shall preserve 16-character zero-padded presentation without highest-ID races. | [`COTRN02C` legacy allocation](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L442-L466) |
| FR-TRAN-010 | Parity | Transaction records shall retain type/category, source, description, signed amount, card, merchant values, origin timestamp and processing timestamp at their exact logical widths. | [Transaction layout](Appendix-File-and-Record-Layouts.md#transaction-and-daily-transaction-records--350-bytes) |
| FR-TRAN-011 | Safe | Insert and any related account/category mutation in a use case shall be one transaction; failures shall not leave a partial business event. | [Units of work](09-DotNet-Target-Architecture.md#units-of-work) |

## Report and statement workflows

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-RPT-001 | Parity | Report request shall support monthly, yearly and custom selectors with legacy priority monthly -> yearly -> custom, date checks, confirmation and submitted result. | [Report request](04-Online-Screens-and-Navigation.md#report-request) |
| FR-RPT-002 | Safe | Safe mode shall require exactly one selector and `start <= end`; strict tests shall retain the source’s multiple-selector priority and reversed-range behavior. | [`CORPT00C`](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L212-L436) |
| FR-RPT-003 | Safe | Confirmed requests shall persist structured report parameters in a durable queue; no user-supplied value may become executable JCL/shell text. | [Report queue mapping](02-System-Context-and-Architecture.md#external-interfaces) |
| FR-RPT-004 | Parity | Transaction report command shall filter/sort and format the 133-column report according to `TRANREPT`/`CBTRN03C`, including page headers and account/card totals in strict mode. | [Transaction report](05-Batch-Processing.md#transaction-report-cbtrn03c) |
| FR-RPT-005 | Parity | Statement generation shall create fixed-width text and HTML outputs for each card/account group with the fields and detail rows documented in the batch specification. | [Statements](05-Batch-Processing.md#statements-cbstm03a-and-cbstm03b) |
| FR-RPT-006 | Safe | Safe report/statement mode shall fix final-record control-flow defects, bounds limits and HTML escaping/completeness. Strict golden mode shall preserve byte-level legacy output where safe. | [Known batch anomalies](05-Batch-Processing.md#known-batch-source-anomalies) |

## Bill payment

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-BILL-001 | Parity | Screen shall accept account ID, show current balance, request `Y/N` confirmation, support F4 clear and F3 return. | [Bill payment fields](04-Online-Screens-and-Navigation.md#bill-payment) |
| FR-BILL-002 | Safe | Account ID shall be validated as an 11-character numeric nonzero ID before conversion/read. | [`COBIL00C` legacy path](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L154-L244) |
| FR-BILL-003 | Parity | A non-positive balance shall create no payment. A confirmed positive balance shall create a full-balance payment using the source type/category/source/description/merchant values and the card returned by the single account alternate-key read. The source proves no first/lowest/primary-card tie-break; target selection remains `DEC-ONL-002`. | [`COBIL00C` payment values](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L208-L267), [`COBIL00C` xref read](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L408-L436), [non-unique alternate-index decision](14-Known-Defects-and-Open-Decisions.md#decision-register) |
| FR-BILL-004 | Parity | Successful payment shall reduce account current balance by the exact payment amount. | [Bill payment](04-Online-Screens-and-Navigation.md#bill-payment) |
| FR-BILL-005 | Safe | ID allocation, transaction insert and account mutation shall be concurrency-safe and atomic. | [Units of work](09-DotNet-Target-Architecture.md#units-of-work) |

## Security-user administration

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-USER-001 | Parity | User list shall support ID positioning/filter, ten rows, first `U/u` or `D/d` action, F7/F8 paging, F3 return, and an Add route. | [User list](04-Online-Screens-and-Navigation.md#user-list) |
| FR-USER-002 | Parity | Add shall collect first name, last name, eight-cell ID/password and one-cell type; strict validation order is first, last, ID, password, type. Duplicate ID is reported. | [`COUSR01C`](../Old_Cobol_Code/app/cbl/COUSR01C.cbl#L117-L160) |
| FR-USER-003 | Safe | Add/update shall normalize IDs consistently with sign-on, restrict role to `A/U`, hash password, validate configured password policy, and reject an ID that would be unusable at sign-on. | [Credential controls](08-Security-and-Controls.md#credential-storage-and-migration) |
| FR-USER-004 | Parity | Update shall fetch by nonblank ID, show editable values, validate in ID/first/last/password/type order, compare with fetched data, and write only a change. | [`COUSR02C`](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L143-L245) |
| FR-USER-005 | Safe | F3 shall not silently save. Save and Return shall be separate target actions; a validation or concurrency error shall keep the user on the update screen. | [`COUSR02C` PF3 defect](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L108-L119) |
| FR-USER-006 | Safe | Delete shall require explicit confirmation and prevent deletion of the active user and the final administrator. Strict tests may characterize the unconfirmed legacy delete without exposing it in production. | [`COUSR03C`](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L267-L335) |
| FR-USER-007 | Safe | User add/update/delete and role changes shall create redacted audit events with actor, target, time and outcome. | [Audit requirements](08-Security-and-Controls.md#audit-events) |
| FR-USER-008 | Safe | All administration use cases shall require an authenticated administrator regardless of navigation origin. | [Authorization matrix](08-Security-and-Controls.md#authorization-matrix) |

## Batch and transfer requirements

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-BATCH-001 | Parity | Master refresh shall create/load account, card, customer, cross-reference, user, transaction type/category, disclosure and category-balance stores from the supplied record contracts. | [Batch workload catalog](05-Batch-Processing.md#batch-workload-catalog) |
| FR-BATCH-002 | Parity | Posting shall read every daily 350-byte record and validate xref, account, limit and expiry in source order, producing reason 100-103 rejects as documented. | [Posting validation](05-Batch-Processing.md#validation-order-and-rejects) |
| FR-BATCH-003 | Parity | A reject shall preserve the original record plus reason; any reject yields completion code 4, while successful accepted records remain processed. | [`CBTRN02C`](../Old_Cobol_Code/app/cbl/CBTRN02C.cbl#L210-L231) |
| FR-BATCH-004 | Parity | Accepted posting shall update category balance, account current/cycle balances and transaction master using the source arithmetic and generated processing time. | [Accepted mutation](05-Batch-Processing.md#accepted-record-mutation) |
| FR-BATCH-005 | Safe | Each posted input shall commit those three effects or its reject atomically; retry shall not duplicate an already committed input. | [Restart policy](05-Batch-Processing.md#restart-idempotency-and-recovery) |
| FR-BATCH-006 | Parity | Interest shall select group-specific then `DEFAULT` rate, calculate `balance * rate / 1200`, write type `01`/category `0005` entries for nonzero rates, update account and reset cycle values. | [Interest](05-Batch-Processing.md#interest-cbact04c) |
| FR-BATCH-007 | Safe | Interest shall update the final account and make one account’s calculation atomic; strict mode shall reproduce the documented final-account omission for characterization only. | [Strict interest oracle](05-Batch-Processing.md#strict-interest-result-after-posting) |
| FR-BATCH-008 | Parity | Fee processing shall remain an explicit no-op until a new approved requirement exists; source contains only a stub. | [`CBACT04C`](../Old_Cobol_Code/app/cbl/CBACT04C.cbl#L518-L520) |
| FR-BATCH-009 | Parity/Safe | Report command shall implement source filtering, sorting, formatting and totals; safe mode fixes EOF duplication/final-total omissions while strict mode is golden-tested. | [Transaction report](05-Batch-Processing.md#transaction-report-cbtrn03c) |
| FR-BATCH-010 | Parity/Safe | Statement commands shall group the sorted transaction stream and produce text/HTML contracts; safe mode removes the fixed ten-row in-memory bound and escapes HTML. | [Statements](05-Batch-Processing.md#statements-cbstm03a-and-cbstm03b) |
| FR-BATCH-011 | Parity | Combine/rebuild commands shall merge transaction generations and recreate required lookup ordering/indexes deterministically. | [Batch data lineage](05-Batch-Processing.md#batch-data-lineage) |
| FR-BATCH-012 | Parity | Export shall emit heterogeneous 500-byte customer/account/xref/transaction/disclosure records with the exact common header and payload codecs. | [Branch export](05-Batch-Processing.md#export), [Transfer layout](Appendix-File-and-Record-Layouts.md#branch-exportimport-record--500-bytes) |
| FR-BATCH-013 | Safe | Export shall use a consistent snapshot. Any intended branch filter must be an approved new rule because the source writes hard-coded branch metadata without filtering. | [Export anomalies](05-Batch-Processing.md#branch-export-and-import) |
| FR-BATCH-014 | Parity | Import shall dispatch by record type and write each entity to its correct output; error output shall preserve the failing record/reason contract. | [Import](05-Batch-Processing.md#import) |
| FR-BATCH-015 | Safe | Import shall validate header/type/sequence/payload relationships and commit atomically; unknown types and missing outputs must fail nonzero rather than report success. | [Import anomalies](05-Batch-Processing.md#known-batch-source-anomalies) |
| FR-BATCH-016 | Parity | Diagnostic programs shall parse/print the supplied account/card/customer/xref formats; `CBACT01C` format demonstrations remain codec characterization, not an end-user capability. | [Diagnostic programs](05-Batch-Processing.md#diagnostic-and-format-demonstration-programs) |
| FR-BATCH-017 | Safe | Every command shall validate options/files before mutation, obtain the appropriate application lock, honor cancellation, log counts and return the documented code. | [Command surface](09-DotNet-Target-Architecture.md#console-command-surface) |
| FR-BATCH-018 | Parity | The supplied fixture shall satisfy the exact posting, interest and statement counts/totals in the [deterministic fixture oracle](05-Batch-Processing.md#deterministic-fixture-oracle). | [Fixture oracle](05-Batch-Processing.md#deterministic-fixture-oracle) |

## Optional module requirements

| ID | Track | Requirement and acceptance result | Evidence |
|---|---|---|---|
| FR-OPT-001 | Parity | Transaction-type list shall provide seven rows, type/description filtering, add/update/delete routing, F7/F8 paging and delete confirmation. | [CTLI workflow](07-Optional-Modules-and-Integrations.md#ctli-list-workflow) |
| FR-OPT-002 | Parity | Type maintenance shall require numeric nonzero two-character type and nonblank <=50-character ASCII alphanumeric/space description, selecting create or update by existence. | [CTTU workflow](07-Optional-Modules-and-Integrations.md#cttu-maintenance-workflow) |
| FR-OPT-003 | Safe | Type CRUD shall use actual trimmed lengths, optimistic concurrency, valid references and one unit of work; it shall not reproduce stale SQL status/length defects. | [Type compatibility defects](07-Optional-Modules-and-Integrations.md#transaction-type-compatibility-defects) |
| FR-OPT-004 | Parity | Batch type maintenance shall parse 53-byte action/type/description records; extract/synchronization shall preserve 60-byte type/category output. | [Batch maintenance](07-Optional-Modules-and-Integrations.md#batch-maintenance-and-synchronization) |
| FR-OPT-005 | Parity | Authorization worker shall parse the observed 18-field CSV request variant and emit the six-field CSV response including the trailing delimiter. The contradictory fixed copybook is a separately versioned parser. | [Authorization request/response](07-Optional-Modules-and-Integrations.md#authorization-mq-request) |
| FR-OPT-006 | Parity | Worker shall resolve cross-reference/account/customer, use pending summary or base account to calculate available credit, and apply source approval/decline response codes and reason codes. | [Decision rules](07-Optional-Modules-and-Integrations.md#decision-rules) |
| FR-OPT-007 | Safe | Request consumption, decision state/history/fraud effects and reply/outbox shall be atomic/idempotent. A crash cannot lose a request or send an unrecorded reply. | [Safe atomic behavior](07-Optional-Modules-and-Integrations.md#safe-atomic-behavior) |
| FR-OPT-008 | Safe | Each authorization shall start with clean per-request state; no account/found/amount/response buffer value may leak from the prior message. | [Authorization defects](07-Optional-Modules-and-Integrations.md#authorization-compatibility-defects) |
| FR-OPT-009 | Parity | Pending summary shall list five accounts per page; first `S/s` routes to detail; detail shall show history and allow forward history paging. | [CPVS](07-Optional-Modules-and-Integrations.md#cpvs-pending-summary-inquiry), [CPVD](07-Optional-Modules-and-Integrations.md#cpvd-detail-and-fraud-toggle) |
| FR-OPT-010 | Safe | Selection must identify exactly one current row and not continue on missing/stale records. History is unbounded in persistence and paged by query rather than a 20-entry array. | [Authorization compatibility defects](07-Optional-Modules-and-Integrations.md#authorization-compatibility-defects) |
| FR-OPT-011 | Parity | Fraud toggle shall preserve the observed IMS/Db2 state meanings and F5 action while safe mode commits both stores atomically. | [CPVD fraud toggle](07-Optional-Modules-and-Integrations.md#cpvd-detail-and-fraud-toggle) |
| FR-OPT-012 | Parity/Safe | Purge shall accept the documented control input and expiry comparison; safe mode fixes approved/declined checks, restart/checkpoint errors and cleans related fraud state per an approved retention decision. | [Purge](07-Optional-Modules-and-Integrations.md#purge-behavior) |
| FR-OPT-013 | Parity | Load/unload commands shall implement the supplied IMS/Db2 fixed-record contracts and report record-level failures. | [Load/unload](07-Optional-Modules-and-Integrations.md#load-and-unload-behavior) |
| FR-OPT-014 | Parity | Account inquiry worker shall recognize `INQA` at bytes 1-4, read the 11-byte account key at 5-15 and return the documented labeled fixed response. | [VSAM/MQ service behavior](07-Optional-Modules-and-Integrations.md#service-behavior) |
| FR-OPT-015 | Parity | Date worker shall answer every received message with the exact `SYSTEM DATE`/`SYSTEM TIME` format, fixed-pad in strict mode, and use an injected clock. | [Date service](07-Optional-Modules-and-Integrations.md#service-behavior) |
| FR-OPT-016 | Safe | MQ adapters shall use configured destinations/credentials, correlate request/reply correctly, honor reply-to where approved, bound retries, and commit consumption/output safely. | [VSAM/MQ deployment and defects](07-Optional-Modules-and-Integrations.md#vsammq-deployment-and-defects) |
| FR-OPT-017 | Safe | Optional modules shall be disabled by default unless their configuration is valid; their absence shall yield the source-style “not installed” navigation result rather than crash core. | [Installation behavior](07-Optional-Modules-and-Integrations.md#installation-behavior) |

## Data contract requirements

| ID | Requirement | Evidence |
|---|---|---|
| DATA-001 | Each record type shall have a dedicated, length-checking codec implementing its documented one-based offsets and COBOL numeric representation. | [Layout index](Appendix-File-and-Record-Layouts.md#layout-index) |
| DATA-002 | Core identifiers shall remain strings at exact maximum widths; leading zeroes and spaces shall not be lost by numeric database keys. | [Core entity catalog](06-Domain-Data-Model.md#core-entity-catalog) |
| DATA-003 | Relationships and composite keys shall match the [logical relationship model](06-Domain-Data-Model.md#logical-relationship-model), especially type+category and account+type+category. | [`CVTRA01Y`](../Old_Cobol_Code/app/cpy/CVTRA01Y.cpy#L4-L10) |
| DATA-004 | Currency shall calculate as `decimal` and persist as signed integer cents; disclosure rate shall retain two decimal places. | [Default store](09-DotNet-Target-Architecture.md#default-store) |
| DATA-005 | ASCII, EBCDIC, overpunch, packed decimal, binary and fixed report codecs shall be explicit; process locale/default encoding shall never decide a record format. | [File adapters](09-DotNet-Target-Architecture.md#file-and-encoding-adapters) |
| DATA-006 | Raw input and filler bytes shall be retained whenever byte-for-byte re-export/reject output is required. | [Storage conventions](Appendix-File-and-Record-Layouts.md#storage-conventions) |
| DATA-007 | Imported records shall be provenance-tagged by source file, record number, codec and batch run; parsing failures name field and safe record identifier. | [Operations](12-Operations-and-Deployment.md#data-ingestion-and-provenance) |
| DATA-008 | Database schema shall enforce required uniqueness/relationships that the safe target relies on and shall reject inconsistent card/xref/account links. | [Target persistence model](06-Domain-Data-Model.md#net-10-target-persistence-model) |
| DATA-009 | Schema changes shall be versioned migrations; production startup shall verify compatibility but not race automatic migrations. | [Persistence design](09-DotNet-Target-Architecture.md#default-store) |

## Non-functional requirements

| ID | Requirement / measurable acceptance |
|---|---|
| NFR-001 | Build and tests shall target `net10.0`; the delivered product has one console entry point and starts no HTTP listener. |
| NFR-002 | Interactive rendering shall fit 24x80; every map/key route has a virtual-terminal golden test and masked fields never echo secrets. |
| NFR-003 | All command behavior shall be testable without a physical terminal, wall clock, real filesystem root, Db2/IMS/MQ, or process-global culture. |
| NFR-004 | Batch and worker commands shall be cancellation-aware, bounded, lock/idempotency protected, and return the [exit-code contract](09-DotNet-Target-Architecture.md#exit-code-contract). |
| NFR-005 | Safe-mode logical mutations shall be atomic. Restart tests shall kill the process at each persistence boundary and prove no duplicate/partial result. |
| NFR-006 | Logs shall be structured/correlated and shall never contain password, CVV, SSN, government ID, full EFT ID, queue credentials, or raw sensitive records. |
| NFR-007 | Configuration shall be validated before data access, allow JSON/environment/CLI precedence, and keep secrets outside committed files. |
| NFR-008 | Golden fixture tests shall reproduce every total/count/record length in [Batch Processing](05-Batch-Processing.md#deterministic-fixture-oracle). |
| NFR-009 | Every compatibility deviation shall be individually named, disabled by default, linked to a decision, and exercised by both strict and safe tests. Security weaknesses are never compatibility switches. |
| NFR-010 | Release validation shall prove every Markdown link/source path, all 44 COBOL program coverage, all 55 JCL coverage, all 21 BMS maps/585 named fields, and the 329-artifact inventory. |
| NFR-011 | Console commands shall provide deterministic `--help`, reject unknown arguments, keep diagnostics on standard error, and emit no ANSI sequences when output is redirected. |

## Requirement acceptance rule

A requirement is complete only when its implementation, automated test, source evidence and any safe-vs-strict decision are linked in [Traceability and Coverage](13-Traceability-and-Coverage.md#requirements-to-tests). Passing a happy-path screen demo is not sufficient when the requirement includes error ordering, byte output, rollback, concurrency, paging or restart behavior.

---

[<- System context](02-System-Context-and-Architecture.md) | [Home](Home.md) | [Online screens ->](04-Online-Screens-and-Navigation.md)
