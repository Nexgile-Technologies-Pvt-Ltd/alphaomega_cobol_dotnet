# MQ Shim Spec (optional CardDemo modules)

In-process **request/response shim** that replaces IBM MQ + CICS triggering for the two optional
MQ modules of CardDemo. This is the authoritative contract the `CardDemo.Mq` project (see
`_design/ARCHITECTURE.md` line 28: *"optional MQ request/response shim (in-proc queue)"*) must
honor. It captures **every queue name, message structure, and the request→response flow** observed
in the COBOL source so a .NET implementation reproduces the same externally-visible behavior
without a real queue manager.

> Scope: this spec describes the **transport + message contract** only. The *business* behavior of
> each program (VSAM/IMS/DB2 reads, the auth decision, fraud writes) belongs to the program's own
> port (COACCT01 / CODATE01 / COPAUA0C) and the IMS/DB2 specs. Here we pin the MQ envelope those
> programs sit behind.

## Source programs

| Source file | Program | CICS TXN | Started by | Role on the queue |
| --- | --- | --- | --- | --- |
| `app/app-vsam-mq/cbl/CODATE01.cbl` | **CODATE01** | CDRD | MQ trigger (CICS `RETRIEVE` of `MQTM`) | Server: GET request, reply with system date/time |
| `app/app-vsam-mq/cbl/COACCT01.cbl` | **COACCT01** | CDRA | MQ trigger (CICS `RETRIEVE` of `MQTM`) | Server: GET request (`INQA`+acct id), read VSAM `ACCTDAT`, reply with account snapshot |
| `app/app-authorization-ims-db2-mq/cbl/COPAUA0C.cbl` | **COPAUA0C** | CP00 | MQ trigger (CICS `RETRIEVE` of `MQTM`) | Server: GET CSV auth request, decide, reply with CSV auth response, persist to IMS |

All three are **server-side, MQ-triggered CICS transactions** (the "consumer" / responder). The
*requesters* (POS emulator, any MQ client, the README's "Cloud client") are **not** in the repo —
the shim must supply that requester side as a programmatic API so .NET callers can drive the flow.
There is **no MQCONN/MQCONNX** call in any program: under CICS the handle comes from the
CICS-MQ adapter, so all three pass an **already-connected** `HCONN` to `MQOPEN/MQGET/MQPUT`.

---

## 1. Queues

Queue names are taken **verbatim** from the source (literals + README + CSD). The mainframe object
names are kept as the shim's logical queue keys (case-sensitive, trailing spaces trimmed — the
COBOL fields are `PIC X(48)` space-padded; the shim keys on the trimmed name).

| Logical queue (shim key) | Direction (vs. server pgm) | Used by | Origin in source |
| --- | --- | --- | --- |
| `CARD.DEMO.REQUEST.ACCT` *(account request)* | inbound to COACCT01 | COACCT01 | Trigger-supplied via `MQTM-QNAME` (CSD `CARDREQ`/README `CARDDEMO.REQUEST.QUEUE`). Server opens whatever `MQTM-QNAME` it is handed. |
| `CARD.DEMO.REPLY.ACCT` | outbound from COACCT01 | COACCT01 | **Hard-coded literal**: `MOVE 'CARD.DEMO.REPLY.ACCT' TO REPLY-QUEUE-NAME` (COACCT01 line 198). |
| `CARD.DEMO.REQUEST.DATE` *(date request)* | inbound to CODATE01 | CODATE01 | Trigger-supplied via `MQTM-QNAME`. |
| `CARD.DEMO.REPLY.DATE` | outbound from CODATE01 | CODATE01 | **Hard-coded literal**: `MOVE 'CARD.DEMO.REPLY.DATE' TO REPLY-QUEUE-NAME` (CODATE01 line 147). |
| `CARD.DEMO.ERROR` | outbound (errors) from CODATE01 & COACCT01 | both VSAM-MQ pgms | **Hard-coded literal**: `MOVE 'CARD.DEMO.ERROR' TO ERROR-QUEUE-NAME`. Diagnostic dead-letter for MQ/CICS failures. |
| `AWS.M2.CARDDEMO.PAUTH.REQUEST` | inbound to COPAUA0C | COPAUA0C | README "MQ Configuration" input queue; runtime name comes from `MQTM-QNAME` on the trigger. |
| `AWS.M2.CARDDEMO.PAUTH.REPLY` | outbound from COPAUA0C | COPAUA0C | README reply queue; **runtime name comes from `MQMD-REPLYTOQ` of the request msg**, not a literal. |

### Important queue-name behaviors the shim MUST honor

1. **Input queue name is trigger-driven, not hard-coded.** Every server opens
   `MQOD-OBJECTNAME = MQTM-QNAME` (the queue that "triggered" it). The shim's trigger record
   therefore carries the request-queue name; the table above lists the conventional names but the
   server takes the name it is given.
2. **Reply-queue resolution differs between the two modules — preserve this difference:**
   - **VSAM-MQ (CODATE01, COACCT01):** reply queue is a **fixed literal** (`CARD.DEMO.REPLY.DATE`
     / `CARD.DEMO.REPLY.ACCT`). The server *does* read `MQMD-REPLYTOQ` from the request into
     `MQ-QUEUE-REPLY`/`SAVE-REPLY2Q` (COACCT01 line 366 / CODATE01 line 315) **but never uses it for
     the PUT** — the PUT targets the pre-opened literal reply queue. *(Faithful bug: the request's
     ReplyToQ is captured and ignored.)*
   - **Authorization (COPAUA0C):** reply queue is **dynamic** — taken from
     `MQMD-REPLYTOQ OF MQM-MD-REQUEST → WS-REPLY-QNAME` (line 413) and used as the `MQPUT1` target.
3. **`CARD.DEMO.ERROR`** is only relevant to the VSAM-MQ pair. COPAUA0C does **not** use an MQ error
   queue; it logs errors to a CICS TD queue `CSSL` (`EXEC CICS WRITEQ TD QUEUE('CSSL')`) using the
   `ERROR-LOG-RECORD` layout (copybook `CCPAUERY`) — that is a log sink, not an MQ queue, and is out
   of MQ scope (model it as an injected `IErrorLog`, not a shim queue).

---

## 2. The MQ trigger record (how a server transaction starts)

Under CICS, MQ "triggers" the transaction and the program reads the **MQ Trigger Message** via
`EXEC CICS RETRIEVE INTO(MQTM)`. `MQTM` is the IBM-supplied `CMQTML` copybook (not in the repo;
it is the standard IBM `MQTM` / `MQTMC2` structure). Only two fields are consumed:

| MQTM field | COBOL use | Shim equivalent |
| --- | --- | --- |
| `MQTM-QNAME` (`PIC X(48)`) | → `INPUT-QUEUE-NAME` / `WS-REQUEST-QNAME` — the queue to GET from | `TriggerMessage.QueueName` |
| `MQTM-TRIGGERDATA` (`PIC X(64)`) | → `WS-TRIGGER-DATA` (COPAUA0C only; captured, not otherwise used) | `TriggerMessage.TriggerData` (optional, ignored) |

**Shim contract:** the server entry point is invoked with a `TriggerMessage { QueueName, TriggerData }`.
There is no real CICS trigger monitor; the shim's dispatcher calls the server when a message arrives
on the named request queue (trigger-on-first-message semantics — see §6).

---

## 3. MQ message descriptor (MQMD) fields the programs actually touch

The full MQMD (`CMQMDV`) is present but only these fields participate in the contract. The shim's
message envelope must carry them:

| MQMD field | Set/Read by | Meaning in the flow |
| --- | --- | --- |
| `MQMD-MSGID` (24 bytes) | GET reads it (saved to `SAVE-MSGID`); VSAM-MQ PUT copies `SAVE-MSGID` back into reply `MQMD-MSGID`. COPAUA0C sets reply `MQMD-MSGID = MQMI-NONE`. | Correlation token (VSAM-MQ echoes request MsgId into the reply). |
| `MQMD-CORRELID` (24 bytes) | GET reads it (saved to `SAVE-CORELID`/`WS-SAVE-CORRELID`); reply PUT sets `MQMD-CORRELID` = saved value. | **Primary request/response correlation key** (both modules echo the request's CorrelId into the reply's CorrelId). |
| `MQMD-REPLYTOQ` (48 bytes) | GET reads it into `MQ-QUEUE-REPLY` (VSAM-MQ: ignored) / `WS-REPLY-QNAME` (auth: used as PUT target). | Where the requester wants the reply. |
| `MQMD-REPLYTOQMGR` | COPAUA0C reply sets it to SPACES. | Reply QMgr (blank = local). |
| `MQMD-FORMAT` | All replies set `MQFMT-STRING`. | Payload is character data (drives MQ data conversion). |
| `MQMD-CODEDCHARSETID` | VSAM-MQ replies set `= MQCCSI-Q-MGR`. | Reply charset = queue-manager default. |
| `MQMD-MSGTYPE` | COPAUA0C reply sets `MQMT-REPLY`. | Marks the message as a reply. |
| `MQMD-PERSISTENCE` | COPAUA0C reply sets `MQPER-NOT-PERSISTENT`. | Reply is non-persistent. |
| `MQMD-EXPIRY` | COPAUA0C reply sets `50` (= 5.0 s, MQ expiry is in 1/10 s). | Reply auto-expires after 5 s if not consumed. |

**Correlation contract the shim MUST enforce:** on reply, **`reply.CorrelId = request.CorrelId`**
(both modules). VSAM-MQ additionally sets **`reply.MsgId = request.MsgId`**; COPAUA0C sets
`reply.MsgId = MQMI-NONE` (a fresh id). A requester correlates by matching `reply.CorrelId` to the
`CorrelId` it sent (commonly the requester puts its own MsgId as the CorrelId).

---

## 4. GET / PUT option semantics to reproduce

| Behavior | CODATE01 / COACCT01 | COPAUA0C |
| --- | --- | --- |
| GET options | `MQGMO-SYNCPOINT + MQGMO-FAIL-IF-QUIESCING + MQGMO-CONVERT + MQGMO-WAIT` | `MQGMO-NO-SYNCPOINT + MQGMO-WAIT + MQGMO-CONVERT + MQGMO-FAIL-IF-QUIESCING` |
| GET wait interval | `MQGMO-WAITINTERVAL = 5000` ms (5 s) | `WS-WAIT-INTERVAL = 5000` ms (5 s) |
| GET buffer length | `1000` (`MQ-BUFFER PIC X(1000)`) | `LENGTH OF W01-GET-BUFFER` = `500` (`W01-GET-BUFFER PIC X(500)`) |
| GET match | `MQMD-MSGID = MQMI-NONE`, `MQMD-CORRELID = MQCI-NONE` (take any next msg) | same (`MQMI-NONE` / `MQCI-NONE`) |
| Empty-queue handling | `MQRC-NO-MSG-AVAILABLE` → set `NO-MORE-MSGS`, terminate cleanly | `MQRC-NO-MSG-AVAILABLE` → `NO-MORE-MSG-AVAILABLE`, end loop |
| PUT call | `MQPUT` to a **pre-opened** reply-queue handle (`OUTPUT-QUEUE-HANDLE`) | `MQPUT1` to reply queue **by name** (`WS-REPLY-QNAME`, opened+put+closed in one call) |
| PUT options | `MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING` | `MQPMO-NO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT` |
| PUT buffer length | `1000` (full padded buffer) | `WS-RESP-LENGTH` = exact built length (`STRING ... WITH POINTER`) |
| Unit of work | reply PUT + (account read) inside CICS **SYNCPOINT** per message; `4000-MAIN-PROCESS` issues `EXEC CICS SYNCPOINT` then GETs next | reply PUT is **NO-SYNCPOINT** (committed immediately, before IMS write); IMS write + counters committed by `EXEC CICS SYNCPOINT` after each message |
| Open options (request Q) | `MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING` | `MQOO-INPUT-SHARED` |
| Open options (reply/err Q) | `MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING` | n/a (MQPUT1) |
| Close | explicit `MQCLOSE` of input/reply/error queues with `MQCO-NONE` | `MQCLOSE` request queue with `MQCO-NONE`; reply via MQPUT1 needs no close |

**Loop / batch semantics:** each server **drains** its request queue in a loop (`PERFORM ... UNTIL
NO-MORE-MSGS`). It GETs with a 5 s wait; when the wait expires with no message it stops. COPAUA0C
additionally caps the loop at `WS-REQSTS-PROCESS-LIMIT = 500` messages per invocation, then ends.
The shim must let the server process **0..N messages per trigger**, stopping on empty-after-wait
(and, for auth, after 500).

### Faithful bugs to preserve (do NOT "fix")

- **VSAM-MQ wrong HCONN:** `MQGET`/`MQPUT` are called with `MQ-HCONN` (a separate field left at its
  `VALUE 0` initializer), while `MQOPEN`/`MQCLOSE` use `QMGR-HANDLE-CONN` (also 0). Both happen to be
  0 so it works under the CICS adapter; the shim should treat the connection as a single ambient
  in-proc context and **not** rely on these being the same variable.
- **VSAM-MQ ReplyToQ ignored** for the PUT (see §1.2) — reply always goes to the literal queue.
- **`MQ-MSG-COUNT` increment** in the VSAM-MQ GET path is dead bookkeeping (never read out); ignore.
- COACCT01 error string `'CICS RETREIVE'` and CODATE01 `'CICS RETRIEVE'` differ in spelling — purely
  cosmetic; not part of the message contract.

---

## 5. Message structures (payloads)

All payloads are **character strings** (`MQFMT-STRING`), space/format as the COBOL builds them. The
shim treats the body as a string; the field breakdown below is the contract each side parses/builds.

### 5.1 Date module (CODATE01)

**Request** — CODATE01 GETs a 1000-byte buffer into `REQUEST-MSG-COPY` but **uses none of the request
content** (it always answers with the current date/time). The README documents an intended
`DATE`/`REQUEST-ID` request, but the program ignores the body. Shim request for the date flow may be
**empty/any**; only the MQMD correlation fields matter.

| README request layout (informational) | PIC | Notes |
| --- | --- | --- |
| `REQUEST-TYPE` | `X(4)` (`'DATE'`) | Not parsed by CODATE01. |
| `REQUEST-ID` | `X(8)` | Not parsed by CODATE01. |

**Response** — built by `STRING` into `REPLY-MESSAGE` (then padded to 1000 in `MQ-BUFFER`):

```
'SYSTEM DATE : ' + WS-MMDDYYYY(10) + 'SYSTEM TIME : ' + WS-TIME(8)
```
- `WS-MMDDYYYY` = CICS `FORMATTIME MMDDYYYY` with `DATESEP('-')` → `MM-DD-YYYY`.
- `WS-TIME` = CICS `FORMATTIME TIME` with `TIMESEP` → `HH:MM:SS`.
- Result is a **free-text string**, padded with trailing spaces to 1000 bytes on the wire.
- Reply `MQMD`: `MsgId = request MsgId`, `CorrelId = request CorrelId`, `Format = MQFMT-STRING`,
  `CodedCharSetId = MQCCSI-Q-MGR`.

> The README's structured `DATE-RESPONSE-MSG` (`RESPONSE-TYPE X(4)`, `RESPONSE-ID X(8)`,
> `SYSTEM-DATE X(10)`) is **not** what the code emits — the code emits the free-text form above. The
> shim must match the **code**, not the README. (Record this divergence in `faithful-bugs.md`.)

### 5.2 Account module (COACCT01)

**Request** — GET into `MQ-BUFFER`(1000) → `REQUEST-MSG-COPY`:

| Field | PIC | Meaning |
| --- | --- | --- |
| `WS-FUNC` | `X(04)` | Function code; must be **`INQA`** to do an account inquiry. |
| `WS-KEY` | `9(11)` | Account id (must be `> 0`). |
| `WS-FILLER` | `X(985)` | Unused padding (total request = 1000). |

Logic: if `WS-FUNC = 'INQA' AND WS-KEY > 0` → read VSAM `ACCTDAT` by `WS-KEY` (11-digit acct id).
Else → reply with an `INVALID REQUEST PARAMETERS ...` text message.

> README's documented account request (`REQUEST-TYPE 'ACCT'`, `REQUEST-ID X(8)`,
> `ACCOUNT-NUMBER X(11)`) again does **not** match the code, which keys on `INQA` + an 11-digit
> numeric. Honor the **code** layout.

**Response** — on a successful read, `WS-ACCT-RESPONSE` is moved to `REPLY-MESSAGE` (padded to 1000).
It is a **labeled fixed-layout text block** built from the account record (`CVACT01Y`):

| Segment (label + value) | Source field | Value PIC |
| --- | --- | --- |
| `'ACCOUNT ID : '` + id | `ACCT-ID` | `9(11)` |
| `'ACCOUNT STATUS : '` + status | `ACCT-ACTIVE-STATUS` | `X(01)` |
| `'BALANCE : '` + bal | `ACCT-CURR-BAL` | `S9(10)V99` |
| `'CREDIT LIMIT : '` + lim | `ACCT-CREDIT-LIMIT` | `S9(10)V99` |
| `'CASH LIMIT : '` + cashlim | `ACCT-CASH-CREDIT-LIMIT` | `S9(10)V99` |
| `'OPEN DATE : '` + date | `ACCT-OPEN-DATE` | `X(10)` |
| `'EXPR DATE : '` + date | `ACCT-EXPIRAION-DATE` | `X(10)` *(sic spelling)* |
| `'REIS DATE : '` + date | `ACCT-REISSUE-DATE` | `X(10)` |
| `'CREDIT BAL : '` + amt | `ACCT-CURR-CYC-CREDIT` | `S9(10)V99` |
| `'DEBIT BAL : '` + amt | `ACCT-CURR-CYC-DEBIT` | `S9(10)V99` |
| `'GROUP ID : '` + grp | `ACCT-GROUP-ID` | `X(10)` |

- Account **not found** (`DFHRESP(NOTFND)`) → reply text:
  `'INVALID REQUEST PARAMETERS ACCT ID : ' + WS-KEY`.
- Bad function/key → reply text:
  `'INVALID REQUEST PARAMETERS ACCT ID : ' + WS-KEY + 'FUNCTION : ' + WS-FUNC`.
- Other VSAM error → write `MQ-ERR-DISPLAY` to `CARD.DEMO.ERROR` (see §5.4) and terminate.
- Reply `MQMD`: same correlation echo as the date module (`MsgId`+`CorrelId` from request).

### 5.3 Authorization module (COPAUA0C) — CSV in / CSV out

This module uses **comma-delimited CSV** payloads (not fixed columns).

**Request** — `UNSTRING W01-GET-BUFFER(1:W01-DATALEN) DELIMITED BY ','` into the `CCPAURQY`
fields, in this exact order (18 comma-separated fields):

| # | Field (`CCPAURQY`) | PIC | Notes |
| --- | --- | --- | --- |
| 1 | `PA-RQ-AUTH-DATE` | `X(06)` | |
| 2 | `PA-RQ-AUTH-TIME` | `X(06)` | |
| 3 | `PA-RQ-CARD-NUM` | `X(16)` | card key into XREF |
| 4 | `PA-RQ-AUTH-TYPE` | `X(04)` | |
| 5 | `PA-RQ-CARD-EXPIRY-DATE` | `X(04)` | |
| 6 | `PA-RQ-MESSAGE-TYPE` | `X(06)` | |
| 7 | `PA-RQ-MESSAGE-SOURCE` | `X(06)` | |
| 8 | `PA-RQ-PROCESSING-CODE` | `9(06)` | |
| 9 | `WS-TRANSACTION-AMT-AN` → `PA-RQ-TRANSACTION-AMT` | edited `+9(10).99`, parsed via `FUNCTION NUMVAL` | amount as text, then numeric |
| 10 | `PA-RQ-MERCHANT-CATAGORY-CODE` | `X(04)` | *(sic spelling 'CATAGORY')* |
| 11 | `PA-RQ-ACQR-COUNTRY-CODE` | `X(03)` | |
| 12 | `PA-RQ-POS-ENTRY-MODE` | `9(02)` | |
| 13 | `PA-RQ-MERCHANT-ID` | `X(15)` | |
| 14 | `PA-RQ-MERCHANT-NAME` | `X(22)` | |
| 15 | `PA-RQ-MERCHANT-CITY` | `X(13)` | |
| 16 | `PA-RQ-MERCHANT-STATE` | `X(02)` | |
| 17 | `PA-RQ-MERCHANT-ZIP` | `X(09)` | |
| 18 | `PA-RQ-TRANSACTION-ID` | `X(15)` | |

(Matches the README "Input Request Message Format" order exactly. Buffer max 500 bytes.)

**Response** — `STRING`-built CSV into `W02-PUT-BUFFER` (max 200 bytes), 6 fields from `CCPAURLY`,
each followed by a comma (trailing comma included), length = `WS-RESP-LENGTH`:

| # | Field (`CCPAURLY`) | PIC | Value |
| --- | --- | --- | --- |
| 1 | `PA-RL-CARD-NUM` | `X(16)` | echo of request card num |
| 2 | `PA-RL-TRANSACTION-ID` | `X(15)` | echo of request tran id |
| 3 | `PA-RL-AUTH-ID-CODE` | `X(06)` | = request `AUTH-TIME` |
| 4 | `PA-RL-AUTH-RESP-CODE` | `X(02)` | `'00'` approved / `'05'` declined |
| 5 | `PA-RL-AUTH-RESP-REASON` | `X(04)` | reason (see table) |
| 6 | `WS-APPROVED-AMT-DIS` | edited `-zzzzzzzzz9.99` | approved amount (0 if declined) |

Output is `field1,field2,field3,field4,field5,amt,` — note the **trailing comma** after the amount
(the `STRING` appends a `','` after every field including the last). Preserve it.

Auth-response-reason codes (`PA-RL-AUTH-RESP-REASON`, set only when declined):

| Reason | Condition |
| --- | --- |
| `0000` | approved (default) |
| `3100` | card/acct/cust not found in XREF/master |
| `4100` | insufficient funds (amount > available credit) |
| `4200` | card not active *(flag exists; not set in this version)* |
| `4300` | account closed *(flag exists; not set in this version)* |
| `5100` | card fraud *(flag exists; not set in this version)* |
| `5200` | merchant fraud *(flag exists; not set in this version)* |
| `9000` | other/unspecified decline |

Decision summary (business detail lives in the COPAUA0C port; given here to make the response
deterministic): approve unless `transaction-amt > available credit` (available = credit limit −
credit balance, using the IMS auth-summary if present, else the account master), or the card was not
found. The shim does not implement this — it just carries request→response.

### 5.4 Error message (CODATE01 / COACCT01 → `CARD.DEMO.ERROR`)

On an MQ/CICS failure the VSAM-MQ programs PUT the 80-ish-byte `MQ-ERR-DISPLAY` block (padded to
1000) to `CARD.DEMO.ERROR`:

| Field | PIC | Content |
| --- | --- | --- |
| `MQ-ERROR-PARA` | `X(25)` | paragraph/op label (e.g. `'CICS RETRIEVE'`) |
| filler | `X(02)` | spaces |
| `MQ-APPL-RETURN-MESSAGE` | `X(25)` | short error text (e.g. `'INP MQGET ERR:'`) |
| filler | `X(02)` | spaces |
| `MQ-APPL-CONDITION-CODE` | `9(02)` | MQ completion / CICS RESP1 |
| filler | `X(02)` | spaces |
| `MQ-APPL-REASON-CODE` | `9(05)` | MQ reason / CICS RESP2 |
| filler | `X(02)` | spaces |
| `MQ-APPL-QUEUE-NAME` | `X(48)` | offending queue name |

Format = `MQFMT-STRING`, `CodedCharSetId = MQCCSI-Q-MGR`. The shim should expose this as an error
sink for the VSAM-MQ servers (separate from the normal reply path).

---

## 6. In-process shim contract a .NET implementation must honor

The `CardDemo.Mq` project replaces the queue manager + CICS trigger monitor with an **in-process,
single-threaded request/response broker**. Required surface (names illustrative; behavior binding):

### 6.1 Message + queue model
- A **message** carries: `byte[]`/`string Body`, `MsgId` (24), `CorrelId` (24), `ReplyToQueue`,
  `ReplyToQMgr`, `Format` (default `MQSTR`), `MsgType`, `Persistence`, `Expiry`, `CodedCharSetId`.
- A **queue** is an in-proc FIFO keyed by its trimmed mainframe name (see §1). Queues are created on
  first use; names are the literals in §1.
- **GET semantics:** non-destructive match is NOT used (both modules GET "any next" with
  `MsgId/CorrelId = NONE`). GET removes the head message. GET **waits up to 5000 ms**; if the queue
  is still empty, GET returns an *empty/no-message* result mapping to `MQRC-NO-MSG-AVAILABLE` — the
  server then stops its drain loop. (In a synchronous in-proc shim "wait" can be immediate: if empty,
  return no-message right away; the 5 s is only a real-MQ blocking detail. Keep it configurable.)
- **PUT / PUT1 semantics:** append to the named queue. `MQPUT` uses a pre-opened handle; `MQPUT1`
  opens-puts-closes by name in one step. Both reduce to "enqueue on the named queue" in-proc.
- **Expiry/persistence** may be modeled as metadata only (no real broker), but the shim must not
  hand back an expired auth reply if a test simulates the 5 s expiry.

### 6.2 Server (responder) contract — one per program
Each server is a handler invoked by the dispatcher with a `TriggerMessage { QueueName, TriggerData }`:
1. Open/attach to the request queue named by `TriggerMessage.QueueName` (input-shared).
2. Loop: GET next request (wait 5 s).
   - On a message: parse per §5, run the program's business logic, build the reply per §5, **echo
     correlation** per §3, PUT the reply (CODATE01/COACCT01 → literal reply queue;
     COPAUA0C → `request.ReplyToQueue`).
   - On no-message-after-wait: exit the loop.
   - COPAUA0C: also exit after 500 messages.
3. Close the request queue (and reply/error queues for VSAM-MQ).
4. Per-message **unit of work**: VSAM-MQ commits GET+reply+account-read together (SYNCPOINT);
   COPAUA0C PUTs the reply **before** committing the IMS write (reply is NO-SYNCPOINT). The shim must
   expose a syncpoint boundary so this ordering is reproducible.

### 6.3 Requester (client) contract — supplied by the shim (no real client in repo)
Because the requesters are external, the shim provides a programmatic requester so .NET code/tests
can drive a full round-trip:
```
reply = mq.Request(
    requestQueue:  <one of the §1 request queues>,
    body:          <payload per §5>,
    replyToQueue:  <a reply queue>,     // honored only by COPAUA0C; VSAM-MQ uses its literal
    correlId:      <token>,             // echoed back on reply.CorrelId
    timeout:       <= server wait)
```
- `Request` PUTs the request (setting `ReplyToQueue`, `MsgId`, `CorrelId`), causes the matching
  server handler to run (trigger), then GETs from the reply queue **filtering by `CorrelId`** and
  returns the reply body. (A real client browses the reply queue for its CorrelId; the shim must
  match the reply whose `CorrelId == request.CorrelId`.)
- For the date flow the request body may be empty.
- For the account flow the body is the fixed `INQA` + 11-digit acct id (right layout, not README's).
- For the auth flow the body is the 18-field CSV.

### 6.4 Triggering
- There is **no** background trigger monitor. The shim's dispatcher maps **request queue name →
  server handler** and invokes the handler (passing the `TriggerMessage`) when a request is enqueued
  (synchronously inside `Request`, or via an explicit `mq.Drain(queue)` in tests). Mapping:

| Request queue | Handler |
| --- | --- |
| `CARD.DEMO.REQUEST.DATE` | CODATE01 server |
| `CARD.DEMO.REQUEST.ACCT` | COACCT01 server |
| `AWS.M2.CARDDEMO.PAUTH.REQUEST` | COPAUA0C server |

### 6.5 Invariants the shim MUST guarantee (checklist for tests)
1. `reply.CorrelId == request.CorrelId` for all three flows.
2. VSAM-MQ: `reply.MsgId == request.MsgId`; reply lands on the **literal** reply queue regardless of
   `request.ReplyToQueue`. COPAUA0C: reply lands on `request.ReplyToQueue`; `reply.MsgId` is a fresh
   `MQMI-NONE`.
3. Empty request queue after the wait ⇒ server returns having processed its messages, no error.
4. COPAUA0C stops after at most 500 messages per trigger.
5. Payloads are byte-faithful to §5 (free-text for date, labeled block for account, trailing-comma
   CSV for auth) including the README/code divergences noted as faithful bugs.
6. Reply `Format == MQSTR (MQFMT-STRING)`.

---

## 7. Cross-references
- DB2 side of the auth module (fraud table `AUTHFRDS`): `_design/specs/optional/DB2_SCHEMA.md`.
- IMS segments written by COPAUA0C (`PAUTSUM0`/`CIPAUSMY`, `PAUTDTL1`/`CIPAUDTY`): IMS module spec
  under `_design/specs` (the `CardDemo.Ims` project).
- Architecture placement: `_design/ARCHITECTURE.md` line 28 (`CardDemo.Mq` = in-proc MQ shim).
- Faithful bugs (README-vs-code payload divergence, ignored ReplyToQ, wrong HCONN, trailing CSV
  comma): record each with a pinning test in `_design/faithful-bugs.md`.
