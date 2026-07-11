# 8. Security and controls

[&larr; Optional modules](07-Optional-Modules-and-Integrations.md) | [Home](Home.md) | [.NET target architecture &rarr;](09-DotNet-Target-Architecture.md)

This page separates controls proved by the supplied legacy artifacts from controls required for a safe .NET 10 replacement. Claim labels have the meanings in [Documentation conventions](Documentation-Conventions.md#claim-labels). A target recommendation is not evidence that the legacy application already has that control.

This is a technical security baseline, not a compliance certification. The repository does not establish a deployment jurisdiction, data-owner policy, retention schedule, or attestation scope, so it does not prove conformance with PCI DSS, GDPR, SOC 2, or any other external standard. Those obligations require a separate, deployment-specific assessment.

## Legacy security model

### Control layers actually present

| Layer | Observed behavior | Security consequence for parity work |
|---|---|---|
| Sign-on screen | The BMS password input is `DARK`, so the terminal does not echo its characters ([COSGN00 BMS lines 151-200](../Old_Cobol_Code/app/bms/COSGN00.bms#L151-L200)). | This is display masking only. It says nothing about storage, transport, hashing, retry limits, or authorization. |
| User store | `USRSEC` is an 80-byte record containing user ID, names, an eight-character password, a one-character type, and filler ([CSUSR01Y lines 17-23](../Old_Cobol_Code/app/cpy/CSUSR01Y.cpy#L17-L23), [security-user layout](Appendix-File-and-Record-Layouts.md#security-user-record--80-bytes)). | The password is recoverable plaintext in the record. The one-character type is the only application role attribute. |
| Authentication | The sign-on program requires nonblank user ID and password, uppercases both, reads `USRSEC` by user ID, and performs an exact password comparison ([COSGN00C lines 108-140](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L108-L140), [lines 209-257](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L209-L257)). | Authentication is case-insensitive at entry because both fields are uppercased. There is no password hash in this path. |
| Initial routing | A type of `A` routes to `COADM01C`; every other successfully authenticated value routes to `COMEN01C` ([COSGN00C lines 221-240](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L221-L240)). | The two menus are alternative routes, not evidence that administrators inherit every regular-user route. An arbitrary non-`A` stored type follows the regular route. |
| Session state | Identity, type, navigation context, customer, account, and card are carried in a 160-byte COMMAREA ([COCOM01Y lines 19-44](../Old_Cobol_Code/app/cpy/COCOM01Y.cpy#L19-L44), [online session contract](04-Online-Screens-and-Navigation.md#common-160-byte-commarea-and-target-session)). | The application treats mutable navigation data as identity context. The record is not a signed token and must not become the .NET security principal. |
| Menu check | The main menu can reject an option marked `A` when the COMMAREA type is `U`, but all eleven supplied main-menu entries are marked `U` ([COMEN01C lines 128-145](../Old_Cobol_Code/app/cbl/COMEN01C.cbl#L128-L145), [COMEN02Y lines 19-90](../Old_Cobol_Code/app/cpy/COMEN02Y.cpy#L19-L90)). | The check is local to that dispatcher. It is not centralized authorization on the destination use case. |
| Admin dispatcher | `COADM01C` requires only a nonzero COMMAREA length before displaying and dispatching its menu; it does not re-check `CDEMO-USER-TYPE` ([COADM01C lines 75-114](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L75-L114), [lines 119-158](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L119-L158)). | Arrival through the expected sign-on route is trusted. Direct or fabricated state is not rejected by an application-level role check. |
| CICS resource security | Supplied transaction definitions set `RESSEC(NO)` and `CMDSEC(NO)`; for example, `CAUP` also has `CONFDATA(NO)`, `STORAGECLEAR(NO)`, `DUMP(YES)`, and `TRACE(YES)`. Supplied program definitions use `CEDF(YES)`; `COUSR03C` is one example ([CARDDEMO.CSD lines 299-316](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L299-L316)). | The shipped CSD does not ask CICS to enforce transaction-resource or command security. The other flags are recorded facts; they are not proof that dumps or traces actually contain a particular value. |
| File recovery | Core file definitions use `RECOVERY(NONE)` and do not name a journal ([CARDDEMO.CSD lines 1-99](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L1-L99)). | Application write order and explicit rollback determine the observable partial-update behavior. |
| Optional integrations | Optional CSDs also use `RESSEC(NO)` and `CMDSEC(NO)` ([transaction-type CSD lines 25-43](../Old_Cobol_Code/app/app-transaction-type-db2/csd/CRDDEMOD.csd#L25-L43), [authorization CSD lines 39-67](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/csd/CRDDEMO2.csd#L39-L67), [VSAM/MQ CSD lines 17-35](../Old_Cobol_Code/app/app-vsam-mq/csd/CRDDEMOM.csd#L17-L35)). | Optional transactions inherit the same application trust problem unless deployment controls outside the repository add protection. |

**Observed — code:** sign-on distinguishes “user not found” from “wrong password” and immediately permits another attempt ([COSGN00C lines 241-256](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L241-L256)). No supplied sign-on code records failed-attempt counters, delays retries, locks accounts, expires passwords, requires a second factor, or creates a server-validated session identifier.

**Observed — data:** two shipped jobs contain sample user records with the same fixed password literal and `A`/`U` type bytes ([DUSRSECJ lines 29-49](../Old_Cobol_Code/app/jcl/DUSRSECJ.jcl#L29-L49), [ESDSRRDS lines 30-45](../Old_Cobol_Code/app/jcl/ESDSRRDS.jcl#L30-L45)). The literal is fixture data, not a universal business rule, and must not be copied into production documentation, logs, tests, or seed accounts.

**Observed — code:** several normal-flow programs explicitly set the shared role byte to `U`, including account view, account update, card list, card view, and card update ([COACTVWC line 344](../Old_Cobol_Code/app/cbl/COACTVWC.cbl#L344), [COACTUPC line 947](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L947), [COCRDLIC lines 320-388](../Old_Cobol_Code/app/cbl/COCRDLIC.cbl#L320-L388), [COCRDSLC line 326](../Old_Cobol_Code/app/cbl/COCRDSLC.cbl#L326), [COCRDUPC line 464](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L464)). This reinforces that the byte is workflow state, not an immutable authentication result.

**Observed — repository boundary:** sample RACF commands add transaction `CT02` to a resource profile and connect a user to a group ([RACFCMDS lines 19-30](../Old_Cobol_Code/samples/jcl/RACFCMDS.jcl#L19-L30)). They neither cover the complete transaction catalog nor override the supplied `RESSEC(NO)` definitions. They are deployment samples, not proof of an active security configuration.

### Optional store and broker trust

- **Observed — code:** the transaction-type Db2 CSD uses `AUTHTYPE(USERID)` for its DB2ENTRY ([CRDDEMOD.csd lines 45-60](../Old_Cobol_Code/app/app-transaction-type-db2/csd/CRDDEMOD.csd#L45-L60)). The SQL install script selects `SYSADM` as current SQL ID and grants database administration, tablespace use, and table DML to `PUBLIC` ([DB2CREAT lines 15-20](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L15-L20), [lines 52-59](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L52-L59), [lines 72-73](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L72-L73), [lines 103-105](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L103-L105)). No Db2 password is embedded in those COBOL calls; runtime identity is delegated to CICS/Db2.
- **Observed — code:** the authorization worker opens the queue named by trigger data, gets a delimited request, and uses request-supplied reply metadata; its MQGET and MQPUT are `NO_SYNCPOINT`, and the reply is nonpersistent ([COPAUA0C lines 230-283](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L230-L283), [lines 386-431](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L386-L431), [lines 738-779](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L738-L779)). No application-level caller signature or authorization field is present in the [authorization request contract](07-Optional-Modules-and-Integrations.md#authorization-mq-request).
- **Observed — code:** the VSAM/MQ account and date servers likewise take their input queue from MQ trigger data and do not authenticate an application caller in the message ([COACCT01 lines 178-218](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L178-L218), [lines 390-457](../Old_Cobol_Code/app/app-vsam-mq/cbl/COACCT01.cbl#L390-L457), [CODATE01 lines 127-167](../Old_Cobol_Code/app/app-vsam-mq/cbl/CODATE01.cbl#L127-L167)). Broker channel definitions, principals, authorization records, trigger/process definitions, and TLS settings are not supplied; see [authorization deployment](07-Optional-Modules-and-Integrations.md#authorization-deployment).
- **Observed — code:** the authorization PSB gives update access to the pending-authorization database ([PSBPAUTB lines 17-20](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ims/PSBPAUTB.psb#L17-L20)). The detail screen can toggle a fraud flag through a linked program without a separate role check in that operation ([COPAUS1C lines 230-266](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L230-L266)).

**Derived:** Db2, IMS, and MQ are trusted runtime resources in the legacy design. The code proves how it calls them; it does not prove network isolation, channel authentication, encryption in transit, external access-control lists, or production operator procedures.

## Authorization matrix

### Observed access paths

This matrix describes reachable application paths, not an endorsement of the enforcement mechanism.

| Actor/type | Legacy entry path | Functions exposed by supplied menus or workers | Enforcement proved in source |
|---|---|---|---|
| Unauthenticated terminal user | `CC00` / sign-on | Submit user ID and password | Nonblank checks, uppercasing, `USRSEC` read, exact plaintext comparison ([COSGN00C lines 108-140](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L108-L140), [lines 209-257](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L209-L257)) |
| Authenticated type `U` | Main menu | Account view/update; card list/view/update; transaction list/view/add; report request; bill payment; optional pending-authorization inquiry | All eleven menu entries are tagged `U`; the dispatcher checks only menu metadata against the mutable COMMAREA type ([COMEN02Y lines 25-90](../Old_Cobol_Code/app/cpy/COMEN02Y.cpy#L25-L90), [COMEN01C lines 136-158](../Old_Cobol_Code/app/cbl/COMEN01C.cbl#L136-L158)) |
| Authenticated type `A` | Admin menu | User list/add/update/delete; optional transaction-type list/update and maintenance | Sign-on routes type `A` to this menu; the menu itself dispatches without revalidating type ([COSGN00C lines 221-240](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L221-L240), [COADM02Y lines 19-53](../Old_Cobol_Code/app/cpy/COADM02Y.cpy#L19-L53), [COADM01C lines 119-158](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L119-L158)) |
| Type other than `A` | Main menu | Same route as a normal user | Sign-on has an `A`/else branch, while the copybook defines only `A` and `U` level-88 values ([COSGN00C lines 230-239](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L230-L239), [COCOM01Y lines 25-28](../Old_Cobol_Code/app/cpy/COCOM01Y.cpy#L25-L28)) |
| Pending-authorization screen user | Main-menu option 11, when installed | Browse pending authorizations and toggle fraud status | Option 11 is tagged `U`; the fraud operation has no additional application role test ([COMEN02Y lines 86-90](../Old_Cobol_Code/app/cpy/COMEN02Y.cpy#L86-L90), [COPAUS1C lines 230-266](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L230-L266)) |
| Batch job or MQ-triggered task | JCL/scheduler or MQ trigger | Imports, exports, posting, interest, reports, authorization processing, date/account services | Job, subsystem, dataset, queue, Db2, and IMS permissions are external trust boundaries; the repository does not supply a complete identity policy |

**Observed — code:** user maintenance accepts any nonblank one-character type. Add validates only required fields before writing the record ([COUSR01C lines 117-160](../Old_Cobol_Code/app/cbl/COUSR01C.cbl#L117-L160), [lines 238-273](../Old_Cobol_Code/app/cbl/COUSR01C.cbl#L238-L273)). Update reads and rewrites the password field directly ([COUSR02C lines 177-245](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L177-L245), [lines 320-389](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L320-L389)). Delete does not show a confirmation, self-delete restriction, or last-administrator guard in its delete path ([COUSR03C lines 174-192](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L174-L192), [lines 267-335](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L267-L335)).

### Target authorization policy

**Target recommendation:** authenticate once into a server-owned in-process session object, then authorize every application use case at its entry point. Screen routing, menu visibility, command-line mode, queue trigger, record keys, and deserialized legacy context must never be accepted as proof of permission.

| Target permission | Regular user | Administrator | Fraud analyst/operator | Batch/worker service | Notes |
|---|:---:|:---:|:---:|:---:|---|
| Sign in and sign out | Yes | Yes | Yes | No | Interactive principals only; service identities use adapter/runtime authentication. |
| Account/card/transaction inquiry | Yes | Decision required | As assigned | Service-specific | Legacy administrators are routed away from the main menu, so admin inheritance is a product decision, not a parity fact. |
| Account/card update, transaction add, bill payment, report request | Yes | Decision required | No by default | Only a named job use case | Preserve regular-user functions while enforcing record-scope policy. |
| User list/add/update/delete and role assignment | No | Yes | No | No | Require reauthorization on the use case, not merely an admin menu route. Prevent self-disable and loss of the last enabled administrator unless a controlled recovery path exists. |
| Transaction-type maintenance | No | Yes | No | Import/export service may have bounded access | Replace Db2 `PUBLIC` grants with minimum database permissions. |
| Pending authorization inquiry | Legacy: yes; target decision required | Decision required | Yes | Processing worker only | The legacy `U` route is recorded above; deployment must decide whether to retain it. |
| Mark/remove fraud | No by default | Decision required | Yes | Authorization/fraud service only | Treat as a separate high-impact permission and audit every attempt. |
| Batch import, posting, interest, refresh, statement generation | No | Operator command only if explicitly granted | No | Yes, one identity per workload class | Interactive login alone must not imply file-system, queue, or database administration. |
| Configuration, schema migration, credential bootstrap | No | No by default | No | Dedicated deploy/operator identity | Separate deploy-time privileges from runtime privileges. |

**Decision required:** approve the exact target roles and whether administrators also receive regular-user permissions. `Administrator`, `FraudAnalyst`, `BatchOperator`, and service identities in this table are proposed target roles; only legacy `A` and `U` are observed values.

Target enforcement requirements:

1. Build the authenticated principal only after credential verification. Store a stable user identifier, current status, normalized role set, authentication time, and opaque session ID outside any screen model.
2. Resolve permissions through one authorization service called by interactive, command, and worker use cases. A hidden menu entry is not authorization.
3. Re-read security-relevant user status and roles for privileged operations, or invalidate active sessions after role/password/status changes.
4. Validate entity scope independently of submitted account/card/customer keys. Card view/update currently reads the entered key rather than proving ownership ([COCRDSLC lines 736-775](../Old_Cobol_Code/app/cbl/COCRDSLC.cbl#L736-L775), [COCRDUPC lines 1376-1415](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L1376-L1415)).
5. Deny by default when the role, command, optional module, destination, or entity scope is unknown. Audit both denied and successful privileged operations.
6. Do not allow any compatibility profile to restore mutable-COMMAREA trust, plaintext credential storage, `PUBLIC` database grants, or unauthenticated queue access ([compatibility profiles](09-DotNet-Target-Architecture.md#compatibility-profiles)).

## Credential storage and migration

### Legacy credential facts

- **Observed — code:** password capacity is exactly eight characters in the fixed `USRSEC` record ([CSUSR01Y lines 17-23](../Old_Cobol_Code/app/cpy/CSUSR01Y.cpy#L17-L23)).
- **Observed — code:** sign-on uppercases the password before comparison, but user add/update move entered maintenance values to the record without the same normalization ([COSGN00C lines 132-136](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L132-L136), [COUSR01C lines 153-160](../Old_Cobol_Code/app/cbl/COUSR01C.cbl#L153-L160), [COUSR02C lines 215-234](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L215-L234)). **Derived:** a stored lowercase character can make an otherwise valid-looking legacy credential impossible to reproduce through sign-on.
- **Observed — code:** update loads the stored password into the BMS field before redisplay; the BMS `DARK` attribute masks presentation but does not protect the value at rest or in application memory ([COUSR02C lines 166-172](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L166-L172), [COUSR02 BMS lines 125-139](../Old_Cobol_Code/app/bms/COUSR02.bms#L125-L139)).
- **Observed — data:** fixed sample credentials are committed in JCL as described under [Legacy security model](#legacy-security-model). They are test fixtures to quarantine, not defaults to retain.

### Safe target credential model

**Target recommendation:** the target user store holds no recoverable password. Store a versioned output from a vetted password-hashing implementation with a unique random salt and centrally controlled work factor. Do not invent a custom cipher or hash. The implementation decision must select and record the algorithm, library, parameters, upgrade strategy, maximum input length, and denial-of-service limits before coding.

Required credential behavior:

- Preserve the eight-character uppercase comparison only inside a bounded legacy-import verifier, never as the steady-state password policy.
- Accept target passwords without silent uppercasing or truncation. Enforce the approved length/quality policy before hashing.
- Use constant-time verification through the selected library. On a successful verification of an older hash version, rehash with current parameters.
- Rate-limit failed attempts by account and terminal/process source, add progressive delay, and use a generic failure message. Record the attempted user identifier in normalized or tokenized form, never the submitted password.
- Make account enable/disable, credential reset, role change, and session invalidation explicit operations. Do not send or display an existing password.
- Generate reset/bootstrap secrets with a cryptographic random generator, deliver them out of band, make them single-use and short-lived, and require change at first sign-in.
- Keep hashing secrets, connection strings, MQ credentials, and recovery material out of source, fixed-width files, application logs, command history, and process arguments.

### Migration sequence

1. Inventory every `USRSEC` record and reject duplicate or malformed IDs into a restricted migration exception report. Never print password bytes.
2. Import name, normalized ID, enabled state, and a mapped `A`/`U` role. Quarantine other type values for an explicit mapping decision.
3. Prefer forced reset for every imported account. If continuity requires a one-time legacy verifier, store imported legacy material in a separately protected, time-bounded migration store and erase it immediately after successful upgrade or at the migration deadline.
4. Disable shipped sample accounts unless an owner explicitly provisions replacements. Bootstrap the first administrator through an operator-only command that refuses default/reused values and writes an audit event.
5. Verify counts without comparing or exporting cleartext credentials: source records read, target users created, quarantined records, disabled fixtures, resets completed, and legacy secrets destroyed.
6. Back up the new credential database using the same or stronger access restrictions as the live database; test restore without exposing secrets to test logs.

**Decision required:** select the password hasher and policy, retry/lockout behavior, session lifetime, administrator recovery procedure, migration deadline, and whether one-time legacy verification is permitted. These values cannot be inferred from COBOL.

### FTP/JES automation boundary

**Observed — code:** repository scripts connect `tnftp` to `localhost:2121`, upload artifacts, select JES file type, and submit jobs; credentials are not embedded in these shell-script bodies ([remote_compile lines 1-40](../Old_Cobol_Code/scripts/remote_compile.sh#L1-L40), [remote_submit lines 1-25](../Old_Cobol_Code/scripts/remote_submit.sh#L1-L25), [run_full_batch lines 1-70](../Old_Cobol_Code/scripts/run_full_batch.sh#L1-L70)). The FTP `pwd` command in those scripts means print working directory; it is not a password. Authentication and transport protection depend on the external local tunnel/client environment and are not specified by the repository. This absence of embedded secrets is scoped to the shell scripts only: the in-repository job `FTPJCL.JCL` does embed a plaintext host, user ID and password directly in its FTP `SYSIN` stream ([FTPJCL lines 30-41](../Old_Cobol_Code/app/jcl/FTPJCL.JCL#L30-L41); secrets not reproduced here), tracked as [DEF-SEC-004](14-Known-Defects-and-Open-Decisions.md#operations-and-source-estate-decisions).

**Target recommendation:** do not place FTP/JES upload automation inside the replacement runtime. Treat build/deploy submission as a separate operator tool. If retained during migration, require an authenticated, encrypted tunnel; store credentials in an OS or managed secret provider; pin the permitted host; restrict submitted datasets/jobs; and prevent secrets from appearing in arguments, transcripts, or generated control files.

## Input and data protection

### Sensitive data inventory

The classifications below are technical handling categories for this product, not legal classifications.

| Data | Evidence | Required target handling |
|---|---|---|
| Password and reset/bootstrap material | Plaintext legacy password field ([CSUSR01Y lines 17-23](../Old_Cobol_Code/app/cpy/CSUSR01Y.cpy#L17-L23)) | Hash passwords; never log/display/export secrets; tightly restrict migration material. |
| Card number and CVV | Card record carries 16-digit card number and CVV ([CVACT02Y lines 4-11](../Old_Cobol_Code/app/cpy/CVACT02Y.cpy#L4-L11)) | Minimize access, mask terminal/log output, tokenize identifiers in telemetry, and do not copy CVV into audit history. |
| Customer identity | Customer record includes names, addresses, phones, SSN, government ID, date of birth, EFT account, and FICO score ([CVCUS01Y lines 4-23](../Old_Cobol_Code/app/cpy/CVCUS01Y.cpy#L4-L23)) | Authorize by use case and record scope; redact output; restrict data files/backups; define retention/deletion with the data owner. |
| Account financial data | Account record includes balances, credit/cash limits, dates, status, group, and ZIP ([CVACT01Y lines 4-17](../Old_Cobol_Code/app/cpy/CVACT01Y.cpy#L4-L17)) | Treat as confidential business data; expose only required fields and protect persisted files/backups. |
| Transaction/merchant data | Transaction record includes card number, amount, merchant identity, address, and timestamps ([CVTRA05Y lines 4-18](../Old_Cobol_Code/app/cpy/CVTRA05Y.cpy#L4-L18)) | Bound free text, encode reports safely, and avoid full record dumps. |
| Authorization request/history | MQ request includes card/account/amount/merchant fields; Db2 fraud history persists decision data ([CCPAURQY lines 19-36](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CCPAURQY.cpy#L19-L36), [AUTHFRDS DDL lines 1-28](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/AUTHFRDS.ddl#L1-L28)) | Authenticate producer, validate the complete message, limit destinations, redact payloads, and separate domain history from security audit. |
| Pending authorization detail | IMS segment contains card, account, amount, merchant, response, reason, and fraud state ([CIPAUDTY lines 19-54](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cpy/CIPAUDTY.cpy#L19-L54)) | Restrict inquiry/toggle permissions, mask fields, and audit fraud-state changes. |

**Observed — code:** account view sends SSN, government ID, and EFT account values to the terminal once its account/customer records are selected ([COACTVWC lines 493-523](../Old_Cobol_Code/app/cbl/COACTVWC.cbl#L493-L523)). This is the legacy display contract, not justification for unrestricted target output.

### Validation and injection boundaries

- **Observed — code:** BMS maps and fixed COBOL fields bound many interactive values, and individual programs apply numeric/date/status checks. This is length control, not a general sanitization layer. Complete field rules remain in [field and workflow specifications](04-Online-Screens-and-Navigation.md#field-and-workflow-specifications).
- **Observed — code:** report request constructs JCL text and writes it line by line to transient-data queue `JOBS` ([CORPT00C lines 81-127](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L81-L127), [lines 462-535](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L462-L535)). User influence is restricted to a validated selector and parsed date components moved into fixed symbolic lines ([CORPT00C lines 258-436](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L258-L436)); `JOBS` is defined as an extra-partition internal reader ([CARDDEMO.CSD lines 499-505](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L499-L505)). **Derived:** this exact screen does not expose a free-form JCL field, but it crosses a privileged job-submission boundary and can produce partial submissions.
- **Observed — code:** optional Db2 screens use static embedded SQL with host variables rather than constructing SQL text; for example, transaction-type maintenance statements are fixed SQL ([COTRTUPC lines 1473-1664](../Old_Cobol_Code/app/app-transaction-type-db2/cbl/COTRTUPC.cbl#L1473-L1664)). Static SQL reduces SQL-text injection but does not replace authorization, validation, or least-privilege database grants.
- **Observed — code:** the authorization worker splits a comma-delimited MQ payload and converts values without a complete token-count, escaping, length, numeric, or date validation contract ([COPAUA0C lines 351-379](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L351-L379), [authorization compatibility defects](07-Optional-Modules-and-Integrations.md#authorization-compatibility-defects)).
- **Observed — code:** transaction-add merchant text becomes record data rather than executable SQL or commands, but report renderers still require output encoding and fixed-width overflow handling.

**Target recommendation:** parse every terminal, command-line, file, environment, database, IMS, and MQ input into a typed request before invoking domain logic. Enforce character, length, numeric scale, date, enum, cross-field, record-scope, maximum-record-count, and maximum-message-size rules. Reject trailing/unconsumed data where the contract does not permit it.

The .NET report command must create a typed report request and invoke the report service directly. It must not construct JCL, shell commands, SQL text, or an FTP/JES transcript. File arguments must resolve beneath configured roots, reject traversal and device paths, use deliberate overwrite rules, and write through a temporary file plus atomic replacement when supported. Encode HTML, delimited output, and terminal control characters for their destination.

**Decision required:** choose deployment-appropriate at-rest protection. SQLite and ordinary flat files do not become encrypted merely by using .NET. At minimum apply OS ownership and restrictive file permissions to databases, imports, exports, reports, backups, dead-letter payloads, and temporary files; decide whether volume/database encryption and application-level field protection are required for the actual threat model.

## Transaction and recovery controls

### Observed consistency behavior

| Flow | Observed control or gap | Security/integrity implication |
|---|---|---|
| User update/delete | Uses `READ UPDATE` before `REWRITE`/`DELETE` ([COUSR02C lines 320-389](../Old_Cobol_Code/app/cbl/COUSR02C.cbl#L320-L389), [COUSR03C lines 267-335](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L267-L335)). | Record locking protects the individual operation, but business guards such as last-admin protection are absent. |
| Account update | Rewrites multiple customer/account/cross-reference records in a fixed order; only a later customer failure has an explicit rollback path ([COACTUPC lines 3888-4193](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L3888-L4193)). | Failure can expose partial state under the supplied nonrecoverable file definitions. |
| Card update | Re-reads and compares before rewrite ([COCRDUPC lines 1420-1521](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L1420-L1521)). | This is an optimistic conflict check worth preserving, but authorization must precede it. |
| Bill payment | Performs multiple writes and continues through some error handling paths ([COBIL00C lines 208-235](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L208-L235)). | A failed step can leave a partial business operation. |
| Transaction add | Finds the highest sequence and adds one ([COTRN02C lines 442-466](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L442-L466)). | Concurrent writers can select the same next key. |
| Report submission | Writes JCL one TDQ line at a time ([CORPT00C lines 462-535](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L462-L535)). | A mid-stream error can leave a partial privileged request. |
| Authorization MQ worker | MQGET/MQPUT are outside syncpoint and the reply precedes a later IMS write in the legacy flow ([authorization compatibility defects](07-Optional-Modules-and-Integrations.md#authorization-compatibility-defects)). | Loss, duplicate handling, reply/store disagreement, and retry semantics are not made atomic. |
| VSAM/MQ servers | Account/date workers use syncpoint MQ operations ([VSAM/MQ servers](07-Optional-Modules-and-Integrations.md#vsammq-account-and-date-servers)). | This is stronger queue recovery behavior than the authorization worker, but caller authorization and duplicate policy remain separate concerns. |

### Target controls

**Target recommendation:** use one application transaction/unit of work for each business operation whose effects must agree. The required [units-of-work](09-DotNet-Target-Architecture.md#units-of-work) and [concurrency controls](09-DotNet-Target-Architecture.md#concurrency) apply to both interactive and batch callers.

- Begin authorization and validation before opening a write transaction; re-check security-sensitive state inside the transaction when it can change concurrently.
- Atomically commit all local rows for account update, card update, transaction add, bill payment, user/role change, authorization decision, and fraud toggle. On failure, commit none and return a stable error/correlation ID.
- Use database-generated keys or a concurrency-safe allocator, unique constraints, and optimistic version columns where competing changes must be detected.
- Use an idempotency key for retryable commands and inbound queue messages. Persist inbox state, domain effects, audit event, and outbound message in the same local transaction; dispatch the outbox after commit ([safe atomic behavior](07-Optional-Modules-and-Integrations.md#safe-atomic-behavior)).
- Authenticate and validate MQ producers at the broker/adapter boundary; allowlist input and reply destinations, preserve correlation, use persistent messages where loss is unacceptable, cap attempts, and dead-letter irrecoverable input without logging the raw sensitive payload.
- Do not claim distributed atomicity across SQLite, MQ, Db2, IMS, or files. Define compensating/reconciliation behavior and expose an operator-visible failed state.
- Make backups consistent, access-controlled, and restorable. A successful backup command is not sufficient; test recovery and record the result.

## Audit events

### Observed audit coverage

**Observed — code:** core interactive programs update business records but do not write a separate actor-attributed security audit record for sign-on, user maintenance, account/card changes, transaction add, bill payment, or report request. Some programs `DISPLAY` CICS response/reason codes and show terminal errors; those are diagnostic messages, not a durable business audit trail.

**Observed — code:** `AUTHFRDS` stores authorization/fraud domain history, but its DDL has no authenticated actor/session columns ([AUTHFRDS DDL lines 1-28](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/ddl/AUTHFRDS.ddl#L1-L28)). CICS `TRACE(YES)`/`DUMP(YES)`, JES output, scheduler history, and queue diagnostics may be operational evidence in a deployment, but the repository does not prove their retention, integrity, access policy, or linkage to a business actor.

### Required target event catalog

| Event family | Events that must be recorded | Sensitive-value rule |
|---|---|---|
| Authentication/session | sign-in success/failure, throttling/lockout, sign-out, session expiry/invalidation, bootstrap/reset success/failure | No password, reset token, hash, salt, raw user record, or full source input |
| User and role administration | user create/enable/disable/update/delete, credential reset, role grant/revoke, rejected self/last-admin operation | Record target user ID and changed field names; never record credential material |
| Business mutations | account update, card update, transaction add, bill payment | Record entity tokens, operation, outcome, and approved non-sensitive change summary; no full before/after record |
| Reports and files | report requested/generated/failed, import/export started/completed/failed, output replaced/refused | Record normalized report type, date range, record counts, and controlled path identifier; no report body |
| Optional authorization | request accepted/rejected, authorization decision, fraud mark/remove, expiration/purge, replay/duplicate, dead-letter/reconcile | Tokenize card/account/customer IDs and omit raw MQ/IMS/Db2 payloads |
| Batch/worker operations | posting, interest, refresh, statement, migration, queue-worker lifecycle and checkpoint | Record workload identity, input manifest/hash where safe, counts, outcome, and correlation; no sensitive record dump |
| Security/configuration | permission denied, secret/config load failure, schema migration, backup/restore test, redaction failure, destination/path rejection | Record the control and outcome, not secret values or unrestricted environment/config snapshots |

Each audit event must contain: immutable event ID; UTC timestamp; actor or service identity; authentication/session or job ID; correlation/causation ID; action; target entity type and tokenized identifier; outcome; stable reason/error code; source mode/command; and application version. Audit writes for successful business changes belong in the same local unit of work. A failure to persist a mandatory privileged-event audit must fail the privileged operation rather than silently continue.

**Target recommendation:** append audit rows through one interface with a schema distinct from diagnostic logs. Restrict read/export/delete permissions, make tampering detectable through append-only database permissions or an external append-only sink, monitor gaps and clock anomalies, and include audit data in restore tests.

**Decision required:** approve audit retention, archival, deletion, export access, tamper-evidence mechanism, incident review ownership, and treatment of failed read-only inquiries. No retention duration is present in the COBOL estate.

## Logging and redaction

### Legacy diagnostic behavior

The legacy programs commonly display `RESP`/`RESP2`, file status, SQL, IMS, or MQ return information on exceptional paths. For example, bill-payment error handling displays response and reason codes ([COBIL00C lines 356-403](../Old_Cobol_Code/app/cbl/COBIL00C.cbl#L356-L403)). The shipped CSD enables trace and dump on transactions, while its confidentiality-data flag is `NO` in the cited definitions. These facts require a migration review of diagnostic exposure; they do not establish the contents of a particular dump or the security of an actual spool.

### Target log policy

Use structured `ILogger` events with stable event IDs, levels, correlation IDs, and exception categories as specified by [logging, audit and observability](09-DotNet-Target-Architecture.md#logging-audit-and-observability). Redaction must happen before values reach the logger, formatter, trace scope, metrics label, exception message, or audit adapter.

| Value | Permitted diagnostic representation |
|---|---|
| Password, reset/bootstrap token, hash, salt, connection string secret, MQ credential | Never log; use only “present/missing/valid/invalid” where needed |
| CVV, SSN, government ID, full EFT account | Never log; no partial value unless an approved operations requirement documents it |
| Card number | Tokenized stable identifier or last four digits only; never a full number |
| Account/customer/user ID | Prefer a keyed token in shared logs; full value only in a separately approved restricted audit use case |
| Customer/card/account/transaction/auth record or MQ body | Never serialize the complete object; log schema/version, byte count, safe message ID, and validation result |
| Merchant description/address and report text | Omit or sanitize control characters and cap length; do not use as a metric label |
| File path | Log a configured-root-relative logical name; do not expose home directories, traversal input, or secret-bearing names |
| Exception | Stable error code and safe message; include stack trace only in restricted diagnostics and ensure object `ToString()` cannot emit records/secrets |

Additional controls:

- Terminal errors expose a concise action message plus correlation ID. Detailed exceptions go only to restricted diagnostics.
- Do not log command-line arguments or full environment/configuration snapshots; either can contain secrets.
- Cap log field lengths and escape terminal control characters to prevent log forging and terminal manipulation.
- Keep authentication failure messages generic while recording a safe internal reason code.
- Apply access control, rotation, retention, integrity monitoring, and secure deletion to logs. Do not let a verbose/debug switch disable redaction.
- Test redaction with representative fixed-width records, MQ messages, exceptions, validation failures, and batch rejects before release.

## Configuration and secret management

### Observed environment coupling

**Observed — code:** source artifacts contain dataset names, queue names, Db2 plans/schemas, CICS transaction/program names, IMS PSB/database names, local tunnel endpoints, and report/job control text. Most are environment configuration rather than secrets, but hard-coding them couples the product to a privileged runtime. The optional Db2 installation grants broad `PUBLIC` privileges as documented under [Optional store and broker trust](#optional-store-and-broker-trust).

**Observed — repository boundary:** the shell scripts rely on an externally configured `localhost:2121` tunnel and `tnftp`; no complete authentication, host-verification, or TLS contract is supplied. MQ queue-manager/channel security definitions and database credentials are likewise outside the repository. Absence from source is not evidence that a deployment had no credentials; it means their management cannot be reconstructed from these artifacts.

### Target configuration contract

Follow the single console application's [configuration contract](09-DotNet-Target-Architecture.md#configuration-contract): typed, startup-validated sections for data, files, compatibility, terminal, batch, authorization, MQ, and security. Non-secret values may come from committed defaults plus environment-specific files and `CARDDEMO_` environment variables. Secret values must come from a dedicated provider.

Required controls:

1. Commit no passwords, password hashes intended for deployment, reset tokens, connection-string credentials, MQ credentials/certificates/private keys, encryption keys, or production fixture records.
2. Permit .NET user-secrets only for local development. Production obtains secrets from an OS credential store, protected mounted secret, or approved managed secret service; the exact provider is a deployment decision.
3. Do not accept secrets as ordinary command-line arguments. Validate required secret presence without echoing values, source locations, or exception payloads.
4. Give the runtime identity read access only to needed secrets and data roots. Use separate deploy/schema-migration, interactive runtime, batch, authorization-worker, and backup identities where their privileges differ.
5. Replace Db2 `PUBLIC` grants with explicit minimum privileges. Separate schema ownership/migration rights from runtime `SELECT`/DML rights.
6. For MQ, configure authenticated and encrypted channels, queue-level authorization, allowlisted queue names/reply destinations, maximum message size, persistence, retry/dead-letter rules, and certificate/credential rotation. Broker authorization is required even when the application validates a message.
7. For IMS/Db2 adapters retained during transition, bind each service identity to only the PSB/plan/tables used by that workload. Do not reuse an interactive administrator identity.
8. Restrict database, legacy imports, reports, dead letters, backups, logs, temp files, and configuration files with OS permissions. Fail startup if a sensitive path is world-writable or resolves outside an approved root where the platform can determine that safely.
9. Validate configuration once at startup, report missing/invalid keys by name only, and expose a redacted effective-configuration diagnostic for operators.
10. Rotate credentials and keys without source changes. Define restart/reload semantics, overlap period, revocation, backup-key treatment, and recovery testing.

**Target recommendation:** keep development sample data behind an explicit fixture command that refuses a production environment marker and generates random credentials. No normal startup path may silently create a default administrator.

## Threat scenarios and required mitigations

| Threat scenario | Evidence/exposure | Required .NET mitigation |
|---|---|---|
| Credential disclosure and reuse | Plaintext eight-character passwords and committed fixed sample values ([CSUSR01Y lines 17-23](../Old_Cobol_Code/app/cpy/CSUSR01Y.cpy#L17-L23), [DUSRSECJ lines 29-49](../Old_Cobol_Code/app/jcl/DUSRSECJ.jcl#L29-L49)) | Hash with a vetted versioned scheme; force migration reset; quarantine/destroy legacy secrets; prohibit defaults and secret logging. |
| User enumeration and brute force | Distinct “not found” and “wrong password” results with no source-observed limiter ([COSGN00C lines 241-256](../Old_Cobol_Code/app/cbl/COSGN00C.cbl#L241-L256)) | Generic terminal response, rate limits, progressive delay/lockout policy, alerts, and audit without password input. |
| Role or route forgery | Role is a mutable COMMAREA byte; admin dispatcher does not re-check it ([COCOM01Y lines 19-44](../Old_Cobol_Code/app/cpy/COCOM01Y.cpy#L19-L44), [COADM01C lines 75-158](../Old_Cobol_Code/app/cbl/COADM01C.cbl#L75-L158)) | Server-owned principal, centralized per-use-case policy, deny by default, and session invalidation after security changes. |
| Unauthorized user/role administration | Add accepts any nonblank type; delete lacks source-observed self/last-admin guard ([COUSR01C lines 117-160](../Old_Cobol_Code/app/cbl/COUSR01C.cbl#L117-L160), [COUSR03C lines 267-335](../Old_Cobol_Code/app/cbl/COUSR03C.cbl#L267-L335)) | Validate role enum; require administrator permission and recent authentication; prevent last-admin loss; audit every attempt. |
| Excessive database privilege | Install SQL grants DBADM/tablespace/DML to `PUBLIC` ([DB2CREAT lines 52-59](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L52-L59), [lines 72-73](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L72-L73), [lines 103-105](../Old_Cobol_Code/app/app-transaction-type-db2/ctl/DB2CREAT.ctl#L103-L105)) | Named least-privilege principals; separate schema/runtime roles; automated grant review and negative permission tests. |
| Sensitive terminal/data-file exposure | Account display includes identity/EFT fields and records contain card/CVV/customer data ([COACTVWC lines 493-523](../Old_Cobol_Code/app/cbl/COACTVWC.cbl#L493-L523), [sensitive data inventory](#sensitive-data-inventory)) | Record-scope authorization, field minimization/masking, restricted files/backups, inactivity timeout, and safe terminal clearing where supported. |
| Object-scope bypass | Card selection/update accepts submitted account/card keys without an ownership policy ([COCRDSLC lines 736-775](../Old_Cobol_Code/app/cbl/COCRDSLC.cbl#L736-L775), [COCRDUPC lines 1376-1415](../Old_Cobol_Code/app/cbl/COCRDUPC.cbl#L1376-L1415)) | Resolve permitted entities from the authenticated principal and re-check the relationship on every read/write. |
| Fraud-state abuse | Pending-auth route is tagged `U`; detail operation toggles fraud without another role test ([COMEN02Y lines 86-90](../Old_Cobol_Code/app/cpy/COMEN02Y.cpy#L86-L90), [COPAUS1C lines 230-266](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUS1C.cbl#L230-L266)) | Separate fraud permission, optional dual control if approved, concurrency check, mandatory audit, and alerts on unusual toggles. |
| Malformed or spoofed MQ request | Delimited parser lacks complete validation and message carries reply routing ([COPAUA0C lines 351-379](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L351-L379), [lines 738-779](../Old_Cobol_Code/app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl#L738-L779)) | Broker identity/TLS/ACLs, strict versioned parser, size/token/type checks, destination allowlist, poison-message isolation, no raw-payload logging. |
| Replay, duplicate, loss, or split-brain authorization | Authorization MQ operations are `NO_SYNCPOINT`; reply/store ordering is non-atomic ([authorization compatibility defects](07-Optional-Modules-and-Integrations.md#authorization-compatibility-defects)) | Inbox/outbox, idempotency key, persistent delivery where required, atomic local effects, bounded retry, dead letter, and reconciliation. |
| Report/JCL privilege crossing | Interactive request writes constructed JCL to an internal-reader TDQ ([CORPT00C lines 462-535](../Old_Cobol_Code/app/cbl/CORPT00C.cbl#L462-L535), [CARDDEMO.CSD lines 499-505](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L499-L505)) | Replace with typed in-process report service; never execute submitted JCL/shell text; authorize report and output path; audit request/result. |
| FTP/JES tunnel misuse and committed FTP credentials | Scripts target local FTP tunnel and submit jobs with an external authentication/transport contract ([remote_submit lines 1-25](../Old_Cobol_Code/scripts/remote_submit.sh#L1-L25)); the in-repo job `FTPJCL.JCL` additionally embeds a plaintext host/user/password in its `SYSIN` ([FTPJCL lines 30-41](../Old_Cobol_Code/app/jcl/FTPJCL.JCL#L30-L41), [DEF-SEC-004](14-Known-Defects-and-Open-Decisions.md#operations-and-source-estate-decisions)) | Keep out of runtime; authenticated encrypted operator channel, pinned endpoint, least-privilege submit identity, restricted job templates, protected transcript; remove committed credentials and never reproduce them. |
| Path traversal or output overwrite | Console target necessarily accepts configured input/output roots; legacy jobs use named datasets/files ([configuration contract](09-DotNet-Target-Architecture.md#configuration-contract)) | Canonicalize beneath allowlisted roots, reject traversal/device paths/symlink escape where detectable, deliberate overwrite policy, safe temp and permissions. |
| Race or partial commit | Highest-plus-one key allocation and multi-record nonrecoverable write flows ([COTRN02C lines 442-466](../Old_Cobol_Code/app/cbl/COTRN02C.cbl#L442-L466), [COACTUPC lines 3888-4193](../Old_Cobol_Code/app/cbl/COACTUPC.cbl#L3888-L4193)) | Database transactions, generated keys/unique constraints, optimistic versions, idempotency, rollback and reconciliation tests. |
| Malformed fixed-width/packed input or resource exhaustion | Batch and adapter boundaries consume external record/message streams ([batch workload catalog](05-Batch-Processing.md#batch-workload-catalog), [integration data contracts](07-Optional-Modules-and-Integrations.md#integration-data-contracts)) | Strict codec, bounded files/records/messages, checked numeric conversion, cancellation/timeouts, quarantine with safe counts/hashes, fuzz/property tests. |
| Log/dump leakage | Legacy enables trace/dump and holds sensitive records in memory ([CARDDEMO.CSD lines 306-316](../Old_Cobol_Code/app/csd/CARDDEMO.CSD#L306-L316)) | Central redaction, restricted diagnostics, no object/payload dumps, secure log retention, crash-dump policy, redaction regression tests. |
| Secret/configuration leakage | External credentials and security definitions are absent while environment identifiers are hard-coded | Dedicated secret provider, no CLI/source secrets, least-privilege identities, redacted startup validation, rotation and repository secret scanning. |
| Backup or migration-copy exposure | Plaintext legacy user and business files must be read during migration | Isolated migration host, restricted staging, encrypted/protected transport and backup, checksum/count reconciliation, documented destruction and access audit. |

## Security acceptance criteria

The replacement is not security-acceptable until all applicable checks below pass with evidence. “Not installed” is acceptable only for an explicitly excluded optional module documented in the deployment profile.

### Authentication and identity

- [ ] No target database, fixture, configuration file, repository artifact, report, log, or backup sample contains a recoverable production password or reset token.
- [ ] Sign-in verifies a versioned salted password hash, does not uppercase/truncate target passwords, returns one generic failure message, and enforces the approved retry/lockout policy.
- [ ] Password reset/bootstrap is random, single-use, time-bounded, out-of-band, audited, and invalidates affected sessions.
- [ ] A migration reconciliation report accounts for every legacy user without emitting password bytes; non-`A`/`U`, duplicate, and malformed records are quarantined.
- [ ] Shipped sample accounts and fixed sample credentials are absent or disabled in every non-fixture deployment.

### Authorization

- [ ] Every interactive, batch, file, and worker use case calls the central authorization policy; direct navigation, command invocation, message delivery, or fabricated screen/session fields cannot bypass it.
- [ ] Negative tests prove a regular user cannot manage users/roles, administer transaction types, invoke operator commands, or toggle fraud unless the approved matrix explicitly grants that permission.
- [ ] Entity-scope tests prove that submitting another account/card/customer key does not bypass authorization.
- [ ] User type/role input is an allowlisted enum. Self-disable/delete and removal of the last enabled administrator follow the approved guarded recovery policy.
- [ ] Role/status/password changes invalidate or refresh existing sessions according to the approved session policy.
- [ ] No compatibility mode restores plaintext passwords, mutable-session authorization, `PUBLIC` database privilege, unauthenticated MQ, or raw JCL/shell execution.

### Data and input protection

- [ ] Terminal, reports, exports, errors, logs, metrics, traces, audits, dead letters, and exceptions pass automated redaction tests for password, token, hash/salt, CVV, SSN, government ID, EFT account, full card number, connection secret, and raw authorization payload.
- [ ] Every external parser has enforced maximum size/count, exact field/token consumption, numeric/date/enum/range/cross-field validation, and deterministic malformed-input behavior.
- [ ] Report requests invoke typed application code only; tests prove user input cannot create JCL, shell commands, SQL text, arbitrary queue destinations, or terminal control output.
- [ ] Input/output paths are confined to configured roots; traversal, device path, symlink escape where detectable, unauthorized overwrite, and unsafe temporary-file tests fail closed.
- [ ] Database, import/export, report, backup, log, dead-letter, temp, and secret files have verified restrictive ownership/permissions for the deployment platform.

### Transactions and integrations

- [ ] User/role changes, account/card changes, transaction add, bill payment, fraud toggle, and authorization persistence are atomic in the target store; injected failure at every write boundary leaves either the approved complete state or no change.
- [ ] Concurrent transaction/card/account tests prove unique key generation and conflict detection; retry does not duplicate a business effect.
- [ ] MQ adapters authenticate and encrypt channels, authorize queues, cap message size, validate schema, allowlist replies, preserve correlation, and implement idempotent inbox/outbox, bounded retry, dead letter, and reconciliation.
- [ ] Db2/IMS/MQ/runtime and schema/deploy identities have documented minimum privileges. Automated checks fail if `PUBLIC` or an interactive user receives administrative/runtime access outside the approved matrix.
- [ ] Backup and restore tests recover consistent data plus audit state without exposing secrets; results are recorded as security events.

### Audit, logging, configuration, and operations

- [ ] Required audit events contain event/time/actor/session-or-job/correlation/action/entity-token/outcome/reason/version fields and contain none of the prohibited sensitive values.
- [ ] Successful privileged mutations and their audit events commit atomically; a mandatory-audit failure prevents the privileged mutation.
- [ ] Audit access, append/tamper protection, retention, archival, deletion, restore, and review ownership are approved and tested.
- [ ] Structured logging uses stable event IDs and correlation; debug mode, exception paths, codec failures, and object formatting cannot bypass redaction.
- [ ] Production starts only when typed configuration is valid and required secrets are available from the approved provider. It logs key names and safe status, never secret values or full arguments/environment.
- [ ] Repository and release scans find no committed secrets or production sensitive fixtures; dependency and runtime patch policy is approved and build artifacts are reproducible from pinned inputs.
- [ ] Security tests cover interactive TTY and redirected I/O, every non-interactive command, optional-module enabled/disabled profiles, malformed legacy records, concurrency, cancellation, process crash, and restart/replay.
- [ ] The deployment runbook documents first-admin bootstrap, credential/key rotation, account recovery, permission review, incident log/audit access, backup restore, queue poison handling, and migration-secret destruction.

**Decision required:** unresolved policy choices on this page must be closed before their dependent acceptance test can pass. Closing them means recording an owner-approved value and test, not inferring a value from common practice.

[&larr; Optional modules](07-Optional-Modules-and-Integrations.md) | [Home](Home.md) | [.NET target architecture &rarr;](09-DotNet-Target-Architecture.md)
