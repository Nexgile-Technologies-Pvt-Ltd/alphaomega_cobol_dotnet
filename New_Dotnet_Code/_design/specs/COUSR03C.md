# PORT SPEC — COUSR03C (Delete User, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COUSR03C.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COUSR03.bms`
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COUSR03.CPY`
COMMAREA copybook: `Old_Cobol_Code/.../app/cpy/COCOM01Y.cpy` (+ CU03 extension block in WS, see §3/§5)
USRSEC record copybook: `Old_Cobol_Code/.../app/cpy/CSUSR01Y.cpy`
Title/date/message copybooks: `COTTL01Y.cpy`, `CSDAT01Y.cpy`, `CSMSG01Y.cpy`
Target spec consumer: `src/CardDemo.Online` (transaction handler) + `src/CardDemo.ConsoleApp` (24×80 renderer/dispatch) per ARCHITECTURE.md §"Target solution layout". USRSEC → relational `USER_SECURITY` table via `src/CardDemo.Data` repository.

All line citations use the form `// source: COUSR03C.cbl:NNN` (or the named copybook/BMS).

---

## 1. Purpose & Invocation

**Purpose.** COUSR03C is the CICS pseudo-conversational **Delete User** transaction (admin function). The operator types an 8-char User ID; on ENTER the program reads the matching `USRSEC` (USER_SECURITY) record (with UPDATE intent) and displays the user's First Name, Last Name and User Type (read-only). On **PF5** it deletes that user record from USRSEC and confirms with a green "has been deleted" message. It supports PF3 (back to caller / admin menu), PF4 (clear screen), PF12 (back to admin menu), and rejects any other key. It owns one keyed file (USRSEC) and performs `READ … UPDATE` followed by `DELETE`. `// source: COUSR03C.cbl:1-6, 36-39`

**Invocation.**
- CICS TRANSID **`CU03`** (`WS-TRANID`). `// source: COUSR03C.cbl:37`
- Program id **`COUSR03C`** (`WS-PGMNAME`). `// source: COUSR03C.cbl:36`
- Mapset **`COUSR03`** / map **`COUSR3A`**. `// source: COUSR03C.cbl:220-221`
- Reached by `EXEC CICS XCTL` from the **Admin Menu** (`COADM01C`, option 4) `// source: COADM02Y reference in COADM01C spec` and/or from the **User List** screen (`COUSR00C`) which pre-loads `CDEMO-CU03-USR-SELECTED` so this program auto-fetches the selected user on first entry. `// source: COUSR03C.cbl:99-104`
- Pseudo-conversational: re-drives itself via `EXEC CICS RETURN TRANSID('CU03') COMMAREA(CARDDEMO-COMMAREA)`. `// source: COUSR03C.cbl:134-137`
- It is **not** a called subroutine; all flow control is via COMMAREA + XCTL/RETURN.

---

## 2. FILE / TABLE ACCESS

One logical file: **USRSEC** (DDNAME literal `'USRSEC  '`, `WS-USRSEC-FILE` X(08)). `// source: COUSR03C.cbl:39` Record layout `SEC-USER-DATA` (CSUSR01Y), keyed on `SEC-USR-ID` X(08). `// source: CSUSR01Y.cpy:17-23` Per ARCHITECTURE.md §"Base-app relational schema", USRSEC maps to table **USER_SECURITY** (CSUSR01Y/80) PK `usr_id X8`.

| COBOL op (paragraph) | ARCH table | Operation | SQL equivalent | FileStatus / RESP mapping |
|---|---|---|---|---|
| `EXEC CICS READ DATASET('USRSEC') … RIDFLD(SEC-USR-ID) UPDATE` (READ-USER-SEC-FILE) `// source: COUSR03C.cbl:269-278` | USER_SECURITY | READ key (for UPDATE) | `SELECT usr_id, first_name, last_name, pwd, usr_type FROM USER_SECURITY WHERE usr_id = @id` (begin a tracked/locked read so the subsequent DELETE targets the same row) | RESP `NORMAL`→ found (FileStatus '00'); `NOTFND`→ '23' (not found); other→ error branch |
| `EXEC CICS DELETE DATASET('USRSEC') …` (DELETE-USER-SEC-FILE) `// source: COUSR03C.cbl:307-311` | USER_SECURITY | DELETE | `DELETE FROM USER_SECURITY WHERE usr_id = @id` | RESP `NORMAL`→ deleted; `NOTFND`→ '23'; other→ error |

**Important VSAM/CICS semantics to preserve (see §12 Faithful Bugs and §13 Port Notes):**
- The `READ … UPDATE` in READ-USER-SEC-FILE acquires update intent (a record lock under VSAM/RLS). The subsequent `EXEC CICS DELETE DATASET('USRSEC')` in DELETE-USER-SEC-FILE has **no RIDFLD** — it deletes the record currently held under the READ-for-UPDATE (the "delete the read-for-update record" idiom). `// source: COUSR03C.cbl:269-278, 307-311` The .NET port must therefore delete the **same** `usr_id` that the immediately preceding READ-UPDATE selected — model the READ-UPDATE as "remember the locked key" and the DELETE as "delete that key". Do **not** add a WHERE-key to the DELETE that diverges from the READ key; they are coupled.
- In the **PF5 / DELETE-USER-INFO** path the program performs READ-USER-SEC-FILE then DELETE-USER-SEC-FILE back-to-back. `// source: COUSR03C.cbl:188-192` See faithful bug #1: READ-USER-SEC-FILE on success **issues its own SEND screen and RETURNs**? No — it does not RETURN; it SENDs and falls through. The control-flow consequence is detailed in §12.

---

## 3. DATA STRUCTURES USED

- **WS-VARIABLES** (working storage). `// source: COUSR03C.cbl:35-47`
  - `WS-PGMNAME X(08)='COUSR03C'`, `WS-TRANID X(04)='CU03'`. `// source: COUSR03C.cbl:36-37`
  - `WS-MESSAGE X(80)` — error/info text staged into ERRMSGO. `// source: COUSR03C.cbl:38`
  - `WS-USRSEC-FILE X(08)='USRSEC  '` — USRSEC DDNAME (2 trailing spaces). `// source: COUSR03C.cbl:39`
  - `WS-ERR-FLG X(01)` with 88s `ERR-FLG-ON='Y'` / `ERR-FLG-OFF='N'`. `// source: COUSR03C.cbl:40-42`
  - `WS-RESP-CD S9(09) COMP`, `WS-REAS-CD S9(09) COMP` — RESP/RESP2 from CICS calls. `// source: COUSR03C.cbl:43-44`
  - `WS-USR-MODIFIED X(01)` with 88s `USR-MODIFIED-YES='Y'` / `USR-MODIFIED-NO='N'` — **declared, set, but never read** (dead flag, see §12). `// source: COUSR03C.cbl:45-47`
- **CARDDEMO-COMMAREA** (COCOM01Y) — see §5. `// source: COUSR03C.cbl:49`
- **CDEMO-CU03-INFO** — program-specific COMMAREA **extension** appended after COCOM01Y in WS (so it overlays the bytes of CARDDEMO-COMMAREA past the base when passed via DFHCOMMAREA): `// source: COUSR03C.cbl:50-58`
  - `CDEMO-CU03-USRID-FIRST X(08)`, `CDEMO-CU03-USRID-LAST X(08)` — paging anchors (used by COUSR00C list; this program only passes them through). `// source: COUSR03C.cbl:51-52`
  - `CDEMO-CU03-PAGE-NUM 9(08)`. `// source: COUSR03C.cbl:53`
  - `CDEMO-CU03-NEXT-PAGE-FLG X(01) VALUE 'N'` with 88s `NEXT-PAGE-YES='Y'` / `NEXT-PAGE-NO='N'`. `// source: COUSR03C.cbl:54-56`
  - `CDEMO-CU03-USR-SEL-FLG X(01)`. `// source: COUSR03C.cbl:57`
  - **`CDEMO-CU03-USR-SELECTED X(08)`** — the user-id the List screen selected for deletion; if non-blank on first entry, auto-fetch. `// source: COUSR03C.cbl:58, 99-103`
- **COUSR3AI / COUSR3AO** (COUSR03 symbolic map, COUSR03.CPY) — input/output map structures (`COUSR3AO REDEFINES COUSR3AI`) — see §4. `// source: COUSR03C.cbl:60; COUSR03.CPY:17-153`
- **CCDA-SCREEN-TITLE** (COTTL01Y): `CCDA-TITLE01='      AWS Mainframe Modernization       '`, `CCDA-TITLE02='              CardDemo                  '`. `// source: COUSR03C.cbl:62; COTTL01Y.cpy:18-22`
- **WS-DATE-TIME** (CSDAT01Y): current date/time fields incl. `WS-CURDATE-MM-DD-YY` and `WS-CURTIME-HH-MM-SS` edited groups (literal '/' and ':' separators). `// source: COUSR03C.cbl:63; CSDAT01Y.cpy:17-41`
- **CCDA-COMMON-MESSAGES** (CSMSG01Y): `CCDA-MSG-INVALID-KEY='Invalid key pressed. Please see below...         '` (X(50)). `// source: COUSR03C.cbl:64; CSMSG01Y.cpy:20-21`
- **SEC-USER-DATA** (CSUSR01Y): `SEC-USR-ID X8`, `SEC-USR-FNAME X20`, `SEC-USR-LNAME X20`, `SEC-USR-PWD X8`, `SEC-USR-TYPE X1`, `SEC-USR-FILLER X23` (total 80). `// source: COUSR03C.cbl:65; CSUSR01Y.cpy:17-23`
- **DFHAID / DFHBMSCA** — CICS AID keys (DFHENTER, DFHPF3/4/5/12) and BMS attribute constants (DFHNEUTR, DFHGREEN). `// source: COUSR03C.cbl:67-68`
- **DFHCOMMAREA / LK-COMMAREA** (linkage): `OCCURS 1 TO 32767 DEPENDING ON EIBCALEN` — variable-length passed commarea. `// source: COUSR03C.cbl:74-76`

---

## 4. SCREEN / BMS MAP (mapset COUSR03, map COUSR3A)

24×80 map, `SIZE=(24,80)`, `CTRL=(ALARM,FREEKB)`, `EXTATT=YES`, `MODE=INOUT`. `// source: COUSR03.bms:19-28`

Fields (POS = row,col):
| Field | Type/ATTRB | Len | POS | Read? | Written? | Source |
|---|---|---|---|---|---|---|
| (label) `'Tran:'` | ASKIP,NORM,BLUE | 5 | 1,1 | no | static | `// source: COUSR03.bms:29-33` |
| **TRNNAME** | ASKIP,FSET,NORM | 4 | 1,7 | no | yes (=`WS-TRANID`) | `// source: COUSR03.bms:34-37` |
| **TITLE01** | ASKIP,FSET,NORM,YELLOW | 40 | 1,21 | no | yes (=`CCDA-TITLE01`) | `// source: COUSR03.bms:38-41` |
| (label) `'Date:'` | ASKIP,NORM,BLUE | 5 | 1,65 | no | static | `// source: COUSR03.bms:42-46` |
| **CURDATE** | ASKIP,FSET,NORM | 8 | 1,71 | no | yes (mm/dd/yy) | `// source: COUSR03.bms:47-51` |
| (label) `'Prog:'` | ASKIP,NORM,BLUE | 5 | 2,1 | no | static | `// source: COUSR03.bms:52-56` |
| **PGMNAME** | ASKIP,FSET,NORM | 8 | 2,7 | no | yes (=`WS-PGMNAME`) | `// source: COUSR03.bms:57-60` |
| **TITLE02** | ASKIP,FSET,NORM,YELLOW | 40 | 2,21 | no | yes (=`CCDA-TITLE02`) | `// source: COUSR03.bms:61-64` |
| (label) `'Time:'` | ASKIP,NORM,BLUE | 5 | 2,65 | no | static | `// source: COUSR03.bms:65-69` |
| **CURTIME** | ASKIP,FSET,NORM | 8 | 2,71 | no | yes (hh:mm:ss) | `// source: COUSR03.bms:70-74` |
| (label) `'Delete User'` | ASKIP,BRT,NEUTRAL | 11 | 4,35 | no | static | `// source: COUSR03.bms:75-79` |
| (label) `'Enter User ID:'` | ASKIP,NORM,GREEN | 14 | 6,6 | no | static | `// source: COUSR03.bms:80-84` |
| **USRIDIN** | **UNPROT**,FSET,IC,NORM,GREEN,HILIGHT=UNDERLINE | 8 | 6,21 | **yes (input)** | yes (echo) | `// source: COUSR03.bms:85-89` |
| (stopper) | ASKIP,NORM,len 0 | 0 | 6,30 | no | no | `// source: COUSR03.bms:90-92` |
| (banner) `'****…'` | YELLOW,len 70 | 70 | 8,6 | no | static | `// source: COUSR03.bms:93-97` |
| (label) `'First Name:'` | ASKIP,NORM,TURQUOISE | 11 | 11,6 | no | static | `// source: COUSR03.bms:98-102` |
| **FNAME** | ASKIP,FSET,NORM,BLUE,HILIGHT=UNDERLINE | 20 | 11,18 | no (protected) | yes (=`SEC-USR-FNAME`) | `// source: COUSR03.bms:103-107` |
| (stopper) | ASKIP,NORM,len 0 | 0 | 11,39 | no | no | `// source: COUSR03.bms:108-110` |
| (label) `'Last Name:'` | ASKIP,NORM,TURQUOISE | 10 | 13,6 | no | static | `// source: COUSR03.bms:111-115` |
| **LNAME** | ASKIP,FSET,NORM,BLUE,HILIGHT=UNDERLINE | 20 | 13,18 | no (protected) | yes (=`SEC-USR-LNAME`) | `// source: COUSR03.bms:116-120` |
| (stopper) | ASKIP,NORM,GREEN,len 0 | 0 | 13,39 | no | no | `// source: COUSR03.bms:121-124` |
| (label) `'User Type: '` | ASKIP,NORM,TURQUOISE | 11 | 15,6 | no | static | `// source: COUSR03.bms:125-129` |
| **USRTYPE** | ASKIP,FSET,NORM,BLUE,HILIGHT=UNDERLINE | 1 | 15,17 | no (protected) | yes (=`SEC-USR-TYPE`) | `// source: COUSR03.bms:130-134` |
| (label) `'(A=Admin, U=User)'` | ASKIP,NORM,BLUE | 17 | 15,19 | no | static | `// source: COUSR03.bms:135-139` |
| **ERRMSG** | ASKIP,BRT,FSET,RED | 78 | 23,1 | no | yes (=`WS-MESSAGE`) | `// source: COUSR03.bms:140-143` |
| (label) `'ENTER=Fetch  F3=Back  F4=Clear  F5=Delete'` | ASKIP,NORM,YELLOW | 58 | 24,1 | no | static | `// source: COUSR03.bms:144-148` |

Key BMS behaviors to preserve:
- **USRIDIN is the only unprotected input field** (`UNPROT`); `IC` puts the cursor there on first paint. FNAME/LNAME/USRTYPE are `ASKIP` (protected, display-only here). `// source: COUSR03.bms:85-89, 103-134`
- Symbolic map: input struct **COUSR3AI**, output struct **COUSR3AO REDEFINES COUSR3AI**. Input fields end `…I` (e.g. `USRIDINI`, `FNAMEI`, `LNAMEI`, `USRTYPEI`), each with length `…L` (COMP S9(4)), flag `…F`. Output fields end `…O` (e.g. `ERRMSGO`, `TITLE01O`), each with color byte `…C`, etc. `// source: COUSR03.CPY:17-153`
- **`USRIDINL` (the length/attribute field) is set to `-1`** at many points (`MOVE -1 TO USRIDINL`). On SEND, a length of -1 is the BMS idiom for "place the cursor in this field" (used with `CURSOR` on the SEND). Port: -1 on a field length means "cursor here". `// source: COUSR03C.cbl:98, 149, 152, 181, 184, 291, 327, 351; COUSR03C.cbl:224 (CURSOR)`
- **`ERRMSGC OF COUSR3AO`** is the ERRMSG color byte; default map color is RED. The program overrides it to **`DFHNEUTR`** (neutral/white) for the "Press PF5 …" prompt `// source: COUSR03C.cbl:285` and to **`DFHGREEN`** for the "has been deleted" confirmation `// source: COUSR03C.cbl:317`. All error messages keep the default red.
- SEND uses `ERASE` (clears screen) and `CURSOR` (honor -1 length fields for cursor placement). `// source: COUSR03C.cbl:223-224`

---

## 5. COMMAREA FIELDS (CARDDEMO-COMMAREA, COCOM01Y + CU03 extension)

On RETURN the program passes back the **entire** `CARDDEMO-COMMAREA` (base COCOM01Y). The `CDEMO-CU03-INFO` block is declared in WORKING-STORAGE immediately after the COPY COCOM01Y `// source: COUSR03C.cbl:49-58` so it logically extends the commarea region; the program copies inbound bytes via `MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA` `// source: COUSR03C.cbl:94`. Note: the MOVE targets `CARDDEMO-COMMAREA` (base 01 only), so the CU03 extension is **populated from the inbound commarea only insofar as it is contiguous in storage with CARDDEMO-COMMAREA** — see §14 Open Questions (this is the standard CardDemo idiom; the CU03 block sits right after the base in the same 01 group region in callers like COUSR00C).

Fields actually read/written here:
- `CDEMO-PGM-REENTER` (88 on `CDEMO-PGM-CONTEXT`=1) — first-entry vs re-entry dispatch; SET to TRUE on first SEND. `// source: COUSR03C.cbl:95-96; COCOM01Y.cpy:29-31`
- `CDEMO-TO-PROGRAM` X8 — XCTL target; set to `'COSGN00C'` (EIBCALEN=0), `'COADM01C'` (PF3 with blank from-program, or PF12), or `CDEMO-FROM-PROGRAM` (PF3 with non-blank from-program). `// source: COUSR03C.cbl:91, 112-116, 124, 199-201; COCOM01Y.cpy:24`
- `CDEMO-FROM-PROGRAM` X8 — read on PF3 to decide the back target; set to `WS-PGMNAME`='COUSR03C' in RETURN-TO-PREV-SCREEN. `// source: COUSR03C.cbl:112-116, 203; COCOM01Y.cpy:22`
- `CDEMO-FROM-TRANID` X4 — set to `WS-TRANID`='CU03' in RETURN-TO-PREV-SCREEN. `// source: COUSR03C.cbl:202; COCOM01Y.cpy:21`
- `CDEMO-PGM-CONTEXT` 9(1) — set to ZEROS in RETURN-TO-PREV-SCREEN before XCTL. `// source: COUSR03C.cbl:204; COCOM01Y.cpy:29`
- `CDEMO-CU03-USR-SELECTED` X8 — read on first entry; if not SPACES/LOW-VALUES, copied into `USRIDINI` and PROCESS-ENTER-KEY is auto-performed. `// source: COUSR03C.cbl:99-103`

Notes:
- `EIBCALEN` governs the cold-start decision (0 ⇒ no commarea ⇒ XCTL to signon). `// source: COUSR03C.cbl:90-92`
- Other COCOM01Y fields (USER-ID, USER-TYPE, CUST/ACCT/CARD info, LAST-MAP/LAST-MAPSET) are **not** touched; pass through unchanged. `// source: COCOM01Y.cpy:25-44`
- Port: model COMMAREA as the typed CARDDEMO-COMMAREA object plus the CU03 extension fields; preserve them on RETURN.

---

## 6. PSEUDO-CONVERSATIONAL FLOW (overview)

Each invocation either **cold-starts** (no commarea → XCTL signon), **first-displays** the delete screen (commarea present, not re-enter — optionally auto-fetching a pre-selected user), or **processes** the operator's AID key (re-enter). Each non-transfer path ends with `EXEC CICS RETURN TRANSID('CU03') COMMAREA(CARDDEMO-COMMAREA)`. `// source: COUSR03C.cbl:134-137`

1. `EIBCALEN = 0` (cold): `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`, `PERFORM RETURN-TO-PREV-SCREEN` → XCTL signon. `// source: COUSR03C.cbl:90-92`
2. commarea present, **not** `CDEMO-PGM-REENTER`: set re-enter; `MOVE LOW-VALUES TO COUSR3AO`; `MOVE -1 TO USRIDINL`; if `CDEMO-CU03-USR-SELECTED` ≠ SPACES AND ≠ LOW-VALUES → copy it into `USRIDINI`, `PERFORM PROCESS-ENTER-KEY` (auto-fetch); then `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:95-105`
3. commarea present, `CDEMO-PGM-REENTER`: `PERFORM RECEIVE-USRDEL-SCREEN`; `EVALUATE EIBAID` (see §10). `// source: COUSR03C.cbl:106-130`

---

## 7. PARAGRAPH-BY-PARAGRAPH OUTLINE (each = one method)

### MAIN-PARA `// source: COUSR03C.cbl:82-137`
1. `SET ERR-FLG-OFF TO TRUE`; `SET USR-MODIFIED-NO TO TRUE`. `// source: COUSR03C.cbl:84-85`
2. `MOVE SPACES TO WS-MESSAGE, ERRMSGO OF COUSR3AO`. `// source: COUSR03C.cbl:87-88`
3. `IF EIBCALEN = 0` → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. `// source: COUSR03C.cbl:90-92`
4. ELSE `MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA`; if `NOT CDEMO-PGM-REENTER` → set re-enter, `MOVE LOW-VALUES TO COUSR3AO`, `MOVE -1 TO USRIDINL`, optional auto-fetch (see §6 step 2), `PERFORM SEND-USRDEL-SCREEN`; else `PERFORM RECEIVE-USRDEL-SCREEN` + `EVALUATE EIBAID`. `// source: COUSR03C.cbl:93-131`
5. `EXEC CICS RETURN TRANSID('CU03') COMMAREA(CARDDEMO-COMMAREA)`. `// source: COUSR03C.cbl:134-137`

EIBAID dispatch (`EVALUATE EIBAID`): `// source: COUSR03C.cbl:108-130`
- `DFHENTER` → `PERFORM PROCESS-ENTER-KEY`. `// source: COUSR03C.cbl:109-110`
- `DFHPF3` → if `CDEMO-FROM-PROGRAM = SPACES OR LOW-VALUES` then `MOVE 'COADM01C' TO CDEMO-TO-PROGRAM` else `MOVE CDEMO-FROM-PROGRAM TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. `// source: COUSR03C.cbl:111-118`
- `DFHPF4` → `PERFORM CLEAR-CURRENT-SCREEN`. `// source: COUSR03C.cbl:119-120`
- `DFHPF5` → `PERFORM DELETE-USER-INFO`. `// source: COUSR03C.cbl:121-122`
- `DFHPF12` → `MOVE 'COADM01C' TO CDEMO-TO-PROGRAM`; `PERFORM RETURN-TO-PREV-SCREEN`. `// source: COUSR03C.cbl:123-125`
- `WHEN OTHER` → `MOVE 'Y' TO WS-ERR-FLG`; `MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE`; `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:126-129`

### PROCESS-ENTER-KEY `// source: COUSR03C.cbl:142-169`
1. `EVALUATE TRUE`: `WHEN USRIDINI = SPACES OR LOW-VALUES` → set ERR flag, `MOVE 'User ID can NOT be empty...' TO WS-MESSAGE`, `MOVE -1 TO USRIDINL`, `PERFORM SEND-USRDEL-SCREEN`; `WHEN OTHER` → `MOVE -1 TO USRIDINL`, CONTINUE. `// source: COUSR03C.cbl:144-154`
2. `IF NOT ERR-FLG-ON`: `MOVE SPACES TO FNAMEI, LNAMEI, USRTYPEI`; `MOVE USRIDINI TO SEC-USR-ID`; `PERFORM READ-USER-SEC-FILE`. `// source: COUSR03C.cbl:156-162`
3. `IF NOT ERR-FLG-ON`: `MOVE SEC-USR-FNAME TO FNAMEI`; `MOVE SEC-USR-LNAME TO LNAMEI`; `MOVE SEC-USR-TYPE TO USRTYPEI`; `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:164-169`

Note: READ-USER-SEC-FILE itself issues a SEND on the NORMAL path (the "Press PF5 …" prompt). So on a successful fetch the screen is SENT twice — once inside READ (step 2) with the neutral prompt, then again at step 3 with the populated names. The **last** SEND wins on the terminal. See §12 faithful bug #2.

### DELETE-USER-INFO `// source: COUSR03C.cbl:174-192`  (PF5 path)
1. `EVALUATE TRUE`: `WHEN USRIDINI = SPACES OR LOW-VALUES` → set ERR flag, `MOVE 'User ID can NOT be empty...' TO WS-MESSAGE`, `MOVE -1 TO USRIDINL`, `PERFORM SEND-USRDEL-SCREEN`; `WHEN OTHER` → `MOVE -1 TO USRIDINL`, CONTINUE. `// source: COUSR03C.cbl:176-186`
2. `IF NOT ERR-FLG-ON`: `MOVE USRIDINI TO SEC-USR-ID`; `PERFORM READ-USER-SEC-FILE`; `PERFORM DELETE-USER-SEC-FILE`. `// source: COUSR03C.cbl:188-192`

Note: step 2 performs READ then DELETE unconditionally back-to-back. READ-USER-SEC-FILE on NORMAL sets the "Press PF5 …" message and SENDs; then DELETE-USER-SEC-FILE runs, deletes, and SENDs the green confirmation. If READ sets ERR-FLG-ON (NOTFND), DELETE-USER-SEC-FILE **still runs** (no `IF NOT ERR-FLG-ON` guard around DELETE) — see §12 faithful bug #1.

### RETURN-TO-PREV-SCREEN `// source: COUSR03C.cbl:197-208`
1. `IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES` → `MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM`. `// source: COUSR03C.cbl:199-201`
2. `MOVE WS-TRANID TO CDEMO-FROM-TRANID`; `MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM`; `MOVE ZEROS TO CDEMO-PGM-CONTEXT`. `// source: COUSR03C.cbl:202-204`
3. `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COUSR03C.cbl:205-208`

### SEND-USRDEL-SCREEN `// source: COUSR03C.cbl:213-225`
1. `PERFORM POPULATE-HEADER-INFO`. `// source: COUSR03C.cbl:215`
2. `MOVE WS-MESSAGE TO ERRMSGO OF COUSR3AO`. `// source: COUSR03C.cbl:217`
3. `EXEC CICS SEND MAP('COUSR3A') MAPSET('COUSR03') FROM(COUSR3AO) ERASE CURSOR`. `// source: COUSR03C.cbl:219-225`

Note: WS-MESSAGE is X(80), ERRMSGO is X(78) — silent 2-char truncation on the move. `// source: COUSR03C.cbl:38, 217; COUSR03.CPY:152`

### RECEIVE-USRDEL-SCREEN `// source: COUSR03C.cbl:230-238`
1. `EXEC CICS RECEIVE MAP('COUSR3A') MAPSET('COUSR03') INTO(COUSR3AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. `// source: COUSR03C.cbl:232-238`

Note: RESP/RESP2 captured but **never checked** (no MAPFAIL handling). `// source: COUSR03C.cbl:236-237` See §12 faithful bug #4.

### POPULATE-HEADER-INFO `// source: COUSR03C.cbl:243-262`
1. `MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA`. `// source: COUSR03C.cbl:245`
2. `CCDA-TITLE01→TITLE01O`; `CCDA-TITLE02→TITLE02O`; `WS-TRANID→TRNNAMEO`; `WS-PGMNAME→PGMNAMEO`. `// source: COUSR03C.cbl:247-250`
3. Build `mm/dd/yy`: `WS-CURDATE-MONTH→WS-CURDATE-MM`, `WS-CURDATE-DAY→WS-CURDATE-DD`, `WS-CURDATE-YEAR(3:2)→WS-CURDATE-YY`; `WS-CURDATE-MM-DD-YY→CURDATEO`. `// source: COUSR03C.cbl:252-256`
4. Build `hh:mm:ss`: `WS-CURTIME-HOURS→WS-CURTIME-HH`, `WS-CURTIME-MINUTE→WS-CURTIME-MM`, `WS-CURTIME-SECOND→WS-CURTIME-SS`; `WS-CURTIME-HH-MM-SS→CURTIMEO`. `// source: COUSR03C.cbl:258-262`

Note: `WS-CURDATE-YEAR(3:2)` = last 2 digits of the 4-digit year. Edited groups embed literal '/' and ':' (CSDAT01Y). Port via Runtime `IClock` so tests pin the timestamp. `// source: CSDAT01Y.cpy:30-41`

### READ-USER-SEC-FILE `// source: COUSR03C.cbl:267-300`
1. `EXEC CICS READ DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA) LENGTH(LENGTH OF SEC-USER-DATA) RIDFLD(SEC-USR-ID) KEYLENGTH(LENGTH OF SEC-USR-ID) UPDATE RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. `// source: COUSR03C.cbl:269-278`
2. `EVALUATE WS-RESP-CD`:
   - `DFHRESP(NORMAL)` → `CONTINUE`, then `MOVE 'Press PF5 key to delete this user ...' TO WS-MESSAGE`, `MOVE DFHNEUTR TO ERRMSGC`, `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:281-286`
   - `DFHRESP(NOTFND)` → set ERR flag, `MOVE 'User ID NOT found...' TO WS-MESSAGE`, `MOVE -1 TO USRIDINL`, `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:287-292`
   - `WHEN OTHER` → `DISPLAY 'RESP:' … 'REAS:' …`, set ERR flag, `MOVE 'Unable to lookup User...' TO WS-MESSAGE`, `MOVE -1 TO FNAMEL` (note: FNAMEL not USRIDINL), `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:293-299`

Note: the NORMAL branch begins with a no-op `CONTINUE` before the MOVEs. `// source: COUSR03C.cbl:282` And it SENDs the "Press PF5 …" prompt — overwritten by a later SEND in the ENTER path (see PROCESS-ENTER-KEY note).

### DELETE-USER-SEC-FILE `// source: COUSR03C.cbl:305-336`
1. `EXEC CICS DELETE DATASET(WS-USRSEC-FILE) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)` (no RIDFLD — deletes the READ-for-UPDATE record). `// source: COUSR03C.cbl:307-311`
2. `EVALUATE WS-RESP-CD`:
   - `DFHRESP(NORMAL)` → `PERFORM INITIALIZE-ALL-FIELDS`; `MOVE SPACES TO WS-MESSAGE`; `MOVE DFHGREEN TO ERRMSGC`; `STRING 'User ' (SIZE) SEC-USR-ID (SPACE) ' has been deleted ...' (SIZE) INTO WS-MESSAGE`; `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:314-322`
   - `DFHRESP(NOTFND)` → set ERR flag, `MOVE 'User ID NOT found...' TO WS-MESSAGE`, `MOVE -1 TO USRIDINL`, `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:323-328`
   - `WHEN OTHER` → `DISPLAY …`, set ERR flag, `MOVE 'Unable to Update User...' TO WS-MESSAGE`, `MOVE -1 TO FNAMEL`, `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:329-335`

Note: the STRING builds the confirmation: `'User '` (DELIMITED BY SIZE = full 5 chars) + `SEC-USR-ID` (DELIMITED BY SPACE = up to first space, i.e. trimmed user-id) + `' has been deleted ...'` (SIZE) → e.g. `"User USER0007 has been deleted ..."`. `// source: COUSR03C.cbl:318-321` `INITIALIZE-ALL-FIELDS` clears the screen fields **before** the STRING references `SEC-USR-ID` — but `SEC-USR-ID` lives in SEC-USER-DATA (not a screen field), so it survives. `// source: COUSR03C.cbl:315, 318-319, 349-356`

### CLEAR-CURRENT-SCREEN `// source: COUSR03C.cbl:341-344`
1. `PERFORM INITIALIZE-ALL-FIELDS`. `// source: COUSR03C.cbl:343`
2. `PERFORM SEND-USRDEL-SCREEN`. `// source: COUSR03C.cbl:344`

### INITIALIZE-ALL-FIELDS `// source: COUSR03C.cbl:349-356`
1. `MOVE -1 TO USRIDINL` (cursor to user-id). `// source: COUSR03C.cbl:351`
2. `MOVE SPACES TO USRIDINI, FNAMEI, LNAMEI, USRTYPEI, WS-MESSAGE`. `// source: COUSR03C.cbl:352-356`

---

## 8. ARITHMETIC / NON-TRIVIAL MOVES

This program performs **no COMPUTE / arithmetic**. The only non-trivial data manipulations:
- `MOVE -1 TO USRIDINL` / `MOVE -1 TO FNAMEL` — BMS length fields set to -1 for cursor placement (negative literal into COMP S9(4)). `// source: COUSR03C.cbl:98, 149, 152, 181, 184, 291, 298, 327, 334, 351`
- `WS-CURDATE-YEAR(3:2)` — reference modification, last 2 digits of year. `// source: COUSR03C.cbl:254`
- `MOVE USRIDINI TO SEC-USR-ID` — X(8)→X(8), the lookup key. `// source: COUSR03C.cbl:160, 189`
- `STRING … DELIMITED BY SIZE / SPACE … INTO WS-MESSAGE` — confirmation assembly (see DELETE-USER-SEC-FILE note). `// source: COUSR03C.cbl:318-321`
- `MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA` — variable-length commarea copy. `// source: COUSR03C.cbl:94`

No signed-zoned-display, COMP-3, OCCURS-table, or edited-PIC arithmetic is involved.

---

## 9. VALIDATION RULES & EXACT LITERAL MESSAGES

| Condition | Message text (exact) | Color | Where staged | Source |
|---|---|---|---|---|
| ENTER or PF5 with blank/low-values User ID | `User ID can NOT be empty...` | RED (default) | WS-MESSAGE | `// source: COUSR03C.cbl:147-148, 179-180` |
| READ-USER-SEC-FILE NORMAL (found) | `Press PF5 key to delete this user ...` | **NEUTRAL** (`DFHNEUTR`) | WS-MESSAGE + ERRMSGC | `// source: COUSR03C.cbl:283-285` |
| READ-USER-SEC-FILE NOTFND | `User ID NOT found...` | RED | WS-MESSAGE | `// source: COUSR03C.cbl:289` |
| READ-USER-SEC-FILE other RESP | `Unable to lookup User...` | RED | WS-MESSAGE | `// source: COUSR03C.cbl:296` |
| DELETE-USER-SEC-FILE NORMAL (deleted) | `User <id> has been deleted ...` (STRING) | **GREEN** (`DFHGREEN`) | WS-MESSAGE | `// source: COUSR03C.cbl:317-321` |
| DELETE-USER-SEC-FILE NOTFND | `User ID NOT found...` | RED | WS-MESSAGE | `// source: COUSR03C.cbl:325` |
| DELETE-USER-SEC-FILE other RESP | `Unable to Update User...` | RED | WS-MESSAGE | `// source: COUSR03C.cbl:332` |
| AID key not ENTER/PF3/PF4/PF5/PF12 | `Invalid key pressed. Please see below...         ` (X(50) `CCDA-MSG-INVALID-KEY`) | RED | WS-MESSAGE (truncated to 78) | `// source: COUSR03C.cbl:128; CSMSG01Y.cpy:20-21` |

Exact-text reproduction requirements:
- `'User ID can NOT be empty...'` — note capitalization "can NOT", three trailing dots. `// source: COUSR03C.cbl:147`
- `'Press PF5 key to delete this user ...'` — note the **space before** the three dots. `// source: COUSR03C.cbl:283`
- `'User ID NOT found...'` — "NOT" uppercase, three dots, no space before dots. `// source: COUSR03C.cbl:289, 325`
- `'Unable to lookup User...'` (READ) vs `'Unable to Update User...'` (DELETE) — **different wording**; DELETE's "Update" is a copy-paste artifact from the update sibling program but must be reproduced verbatim. `// source: COUSR03C.cbl:296, 332`
- Confirmation STRING → `"User " + <trimmed SEC-USR-ID> + " has been deleted ..."` (space before dots). `// source: COUSR03C.cbl:318-321`
- `CCDA-MSG-INVALID-KEY` is X(50) with its own trailing spaces; moved into X(80) WS-MESSAGE then truncated to 78 into ERRMSGO. `// source: CSMSG01Y.cpy:20-21`

Validation semantics:
- The **only** field-content validation is the empty User ID check (ENTER and PF5 paths). There is **no** format/range check on the user-id; any non-blank 8-char value is used as the VSAM key. `// source: COUSR03C.cbl:144-154, 176-186`
- There is **no** confirmation prompt step beyond the "Press PF5" message; PF5 deletes immediately (the program does not require the user to first ENTER-fetch — PF5 re-reads then deletes). `// source: COUSR03C.cbl:188-192`

---

## 10. EIBAID / PFKey HANDLING

| AID | Action | Source |
|---|---|---|
| `DFHENTER` | PROCESS-ENTER-KEY (fetch & display user) | `// source: COUSR03C.cbl:109-110` |
| `DFHPF3` | Back: to `CDEMO-FROM-PROGRAM` if set, else `COADM01C` (XCTL via RETURN-TO-PREV-SCREEN) | `// source: COUSR03C.cbl:111-118` |
| `DFHPF4` | CLEAR-CURRENT-SCREEN (blank fields + re-SEND) | `// source: COUSR03C.cbl:119-120` |
| `DFHPF5` | DELETE-USER-INFO (read then delete) | `// source: COUSR03C.cbl:121-122` |
| `DFHPF12` | Back to `COADM01C` (XCTL) | `// source: COUSR03C.cbl:123-125` |
| any other AID (PF1/2/6.../PA/CLEAR) | "Invalid key pressed…" + re-SEND | `// source: COUSR03C.cbl:126-129` |

AID copybook `DFHAID` provides the AID tokens; the .NET console handler maps console keys → these tokens. `// source: COUSR03C.cbl:67`

---

## 11. XCTL / LINK TARGETS

| Trigger | XCTL target | COMMAREA | Source |
|---|---|---|---|
| EIBCALEN=0 (cold) | `COSGN00C` (default, via RETURN-TO-PREV-SCREEN guard) | CARDDEMO-COMMAREA | `// source: COUSR03C.cbl:90-92, 199-208` |
| PF3, `CDEMO-FROM-PROGRAM` blank | `COADM01C` | CARDDEMO-COMMAREA | `// source: COUSR03C.cbl:112-113` |
| PF3, `CDEMO-FROM-PROGRAM` set | value of `CDEMO-FROM-PROGRAM` | CARDDEMO-COMMAREA | `// source: COUSR03C.cbl:114-116` |
| PF12 | `COADM01C` | CARDDEMO-COMMAREA | `// source: COUSR03C.cbl:123-125` |

All transfers go through RETURN-TO-PREV-SCREEN, which: defaults blank/low-values target to `COSGN00C`, stamps from-tranid='CU03', from-program='COUSR03C', context=0, then `XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COUSR03C.cbl:197-208` No `EXEC CICS LINK` is used. No `HANDLE CONDITION` is used (RESP-based error handling only).

Port: the dispatcher maps each PGMNAME to its registered transaction handler. The pseudo-conversational loop returns TRANSID `CU03` after every non-XCTL path. `// source: COUSR03C.cbl:134-137`

---

## 12. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **PF5 deletes even when the immediately-preceding READ fails (no ERR-FLG guard around DELETE).** In DELETE-USER-INFO the `IF NOT ERR-FLG-ON` block performs `READ-USER-SEC-FILE` and then `DELETE-USER-SEC-FILE` **unconditionally**, with no re-check of ERR-FLG between them. `// source: COUSR03C.cbl:188-192` If READ returns NOTFND it sets ERR-FLG-ON and SENDs "User ID NOT found...", but DELETE-USER-SEC-FILE still executes. The DELETE then also returns NOTFND and SENDs its own "User ID NOT found..." (the second SEND wins). Net effect on a missing user: the user sees "User ID NOT found..." (from DELETE), and two SENDs occurred. Reproduce: do NOT insert an `IF NOT ERR-FLG-ON` around the DELETE call. `// source: COUSR03C.cbl:188-192, 287-292, 323-328`

2. **Double SEND on a successful ENTER fetch.** In PROCESS-ENTER-KEY, READ-USER-SEC-FILE's NORMAL branch SENDs the "Press PF5 key to delete this user ..." screen (neutral) `// source: COUSR03C.cbl:283-286`, then control returns and the caller's step 3 (`IF NOT ERR-FLG-ON`) moves the fetched names into FNAMEI/LNAMEI/USRTYPEI and SENDs **again** `// source: COUSR03C.cbl:164-169`. So two SENDs happen per ENTER; the second (with names populated, default-colored ERRMSG carrying the same "Press PF5…" text via WS-MESSAGE? — actually WS-MESSAGE still holds "Press PF5…" but ERRMSGC was set to DFHNEUTR only on the first SEND; the second SEND re-derives ERRMSGO from WS-MESSAGE but does not reset ERRMSGC) — reproduce both SENDs and the color-byte carry-over behavior exactly; do not collapse into one SEND. `// source: COUSR03C.cbl:160-169, 281-286`

3. **`WS-USR-MODIFIED` flag is dead.** `WS-USR-MODIFIED` (88s USR-MODIFIED-YES/NO) is initialized to NO in MAIN-PARA `// source: COUSR03C.cbl:85` but is **never set to YES and never tested** anywhere in the program. It is vestigial (copied from the Add/Update siblings). Do not wire it to any behavior. `// source: COUSR03C.cbl:45-47, 85`

4. **RESP/RESP2 from RECEIVE never checked.** RECEIVE-USRDEL-SCREEN captures WS-RESP-CD/WS-REAS-CD but the program never inspects them (no MAPFAIL handling). On a MAPFAIL the input map retains its prior/low-value state. Reproduce: do not add RECEIVE error handling. `// source: COUSR03C.cbl:232-238`

5. **DELETE error branch message says "Update", not "Delete".** The `WHEN OTHER` branch of DELETE-USER-SEC-FILE shows `'Unable to Update User...'` `// source: COUSR03C.cbl:332` — wrong verb for a delete transaction (copy-paste from COUSR02C update). Keep verbatim.

6. **`MOVE -1 TO FNAMEL` on lookup/other-error paths puts cursor on a protected field.** The `WHEN OTHER` branches of both READ and DELETE set `FNAMEL = -1` (cursor to First Name) `// source: COUSR03C.cbl:298, 334`, but FNAME is an `ASKIP` (protected) field per the BMS `// source: COUSR03.bms:103-107`. Placing the cursor on a protected field is a no-op/odd-placement on real CICS. Reproduce the -1 on FNAMEL exactly (do not redirect to USRIDINL). `// source: COUSR03C.cbl:298, 334`

7. **WS-MESSAGE X(80) → ERRMSGO X(78) silent 2-char truncation.** Any message ≥79 chars loses its tail. None of the shipped literals exceed 78, so no visible effect today; do not widen ERRMSG. `// source: COUSR03C.cbl:38, 217; COUSR03.CPY:152`

8. **No re-validation that the displayed user equals the deleted user.** Between an ENTER-fetch and a PF5-delete the operator could change USRIDIN (it is unprotected). PF5 re-reads whatever is now in USRIDINI and deletes that, regardless of what names are still displayed on screen from the prior fetch. Reproduce: PF5 always uses the current USRIDINI, not the previously-fetched id. `// source: COUSR03C.cbl:188-191`

9. **Auto-fetch on first entry can leave the screen showing a found user with the "Press PF5…" prompt without the operator ever pressing ENTER.** When `CDEMO-CU03-USR-SELECTED` is pre-set (from the List screen), MAIN-PARA performs PROCESS-ENTER-KEY before the first SEND-USRDEL-SCREEN `// source: COUSR03C.cbl:99-105`, so a non-ENTER first turn still triggers a read+SEND, then MAIN-PARA SENDs **again** at line 105. Reproduce the extra SEND from the first-entry path. `// source: COUSR03C.cbl:103-105`

---

## 13. PORT NOTES (relational-access + tricky COBOL semantics)

- **USER_SECURITY repository.** Inject the `USER_SECURITY` repository (per ARCHITECTURE.md VSAM→SQL contract). READ-for-UPDATE → `SELECT … WHERE usr_id=@id` within a transaction/unit-of-work; DELETE (no RIDFLD) → `DELETE … WHERE usr_id=@id` using the **same** key just read. Map RESP: NORMAL↔FileStatus '00', NOTFND↔'23', anything else↔ error branch. `// source: COUSR03C.cbl:269-311; ARCHITECTURE.md §VSAM-semantics`
- **Read-for-update / delete-current coupling.** The DELETE has no RIDFLD; it relies on the held READ-UPDATE position. In .NET model this as: READ-USER-SEC-FILE stores the resolved key/entity (e.g. `_heldUserId = SEC-USR-ID`), and DELETE-USER-SEC-FILE deletes `_heldUserId`. Because faithful bug #1 lets DELETE run even after a NOTFND READ, the .NET DELETE must itself detect "not found" and return the '23'/NOTFND branch (it cannot assume a held record exists). `// source: COUSR03C.cbl:188-192, 307-323`
- **SEC-USER-DATA fields → columns:** `SEC-USR-ID→usr_id`, `SEC-USR-FNAME→first_name (X20)`, `SEC-USR-LNAME→last_name (X20)`, `SEC-USR-PWD→pwd (X8)`, `SEC-USR-TYPE→usr_type (X1)`; `SEC-USR-FILLER X23` is reconstructed as spaces only on fixed-width re-serialize (no column). All X(n) keep full width incl. trailing spaces. `// source: CSUSR01Y.cpy:17-23; ARCHITECTURE.md type map`
- **`MOVE LOW-VALUES TO COUSR3AO`** before first SEND clears all output fields to nulls (BMS "no data/leave default"). Port: reset the screen model to defaults before first paint. `// source: COUSR03C.cbl:97`
- **Cursor placement via `…L = -1`** is the BMS+`CURSOR` idiom; port "field length = -1" → "place cursor in this field" and honor it on SEND (note bug #6: -1 on a protected field). `// source: COUSR03C.cbl:98, 224, 291, 298`
- **Color override bytes** (`ERRMSGC`): default RED (from BMS), forced NEUTRAL for the "Press PF5…" prompt and GREEN for the deletion confirmation. Model ERRMSG color as a per-SEND attribute that the handler sets explicitly on those two paths and leaves at default (red) otherwise; note the carry-over in bug #2. `// source: COUSR03C.cbl:285, 317; COUSR03.bms:140-141`
- **STRING DELIMITED BY SIZE / SPACE** for the confirmation: `'User '` full 5 chars + `SEC-USR-ID` up to first space (right-trim) + `' has been deleted ...'` full. Reproduce trim-on-space semantics for the id. `// source: COUSR03C.cbl:318-321`
- **`MOVE DFHCOMMAREA(1:EIBCALEN)`** variable-length copy: model COMMAREA as fixed typed object sized to the base+extension; tolerate short inbound commareas (cold path handled separately by EIBCALEN=0). `// source: COUSR03C.cbl:94`
- **Date/time** via `FUNCTION CURRENT-DATE` → Runtime `IClock`; `mm/dd/yy` and `hh:mm:ss` via literal-separator edited groups; year = last 2 digits. `// source: COUSR03C.cbl:245-262; CSDAT01Y.cpy:30-41`
- **RETURN TRANSID('CU03')** keeps the pseudo-conversational loop on this screen; model as "next transaction = CU03, persist COMMAREA". `// source: COUSR03C.cbl:134-137`
- **No arithmetic / no COMP-3 / no OCCURS data** in this program — nothing to port for numeric truncation/sign. `// source: COUSR03C.cbl (whole)`

---

## 14. OPEN QUESTIONS / RISKS

1. **CU03 extension vs commarea length.** `CDEMO-CU03-INFO` is declared in WS after `COPY COCOM01Y` but the MOVE only targets `CARDDEMO-COMMAREA` (the base 01). `// source: COUSR03C.cbl:49-58, 94` The auto-fetch reads `CDEMO-CU03-USR-SELECTED` `// source: COUSR03C.cbl:99`, which is only meaningful if the caller (COUSR00C list) actually populates the bytes after the base and the inbound `EIBCALEN` covers them. Confirm against COUSR00C how the CU03 block is passed; the .NET port should carry the CU03 fields as part of the persisted COMMAREA so the List→Delete handoff works. Medium risk to the List-driven auto-fetch path.
2. **Held READ-UPDATE lock lifetime across pseudo-conversational turns.** The READ-for-UPDATE and DELETE happen within a single CICS task (same invocation, PF5 path) `// source: COUSR03C.cbl:188-192`, so the lock is intra-task — no cross-turn lock to model. Confirm the .NET unit-of-work spans only READ→DELETE within one handler call (it does). Low risk.
3. **Other-RESP "WHEN OTHER" branches** issue `DISPLAY` (transient-data/SYSOUT) `// source: COUSR03C.cbl:294, 330` — no functional effect on the screen; port as a log line (optional) or no-op. Low risk.
4. **Double-SEND fidelity in characterization tests.** Faithful bugs #1, #2, #9 each cause more than one `EXEC CICS SEND` per turn. The online screen-parity harness (ARCHITECTURE.md §Verification 4) must assert on the **final** screen state per turn while also recording the SEND count if parity is keyed on send sequence. Confirm the harness compares last-SEND state. Medium risk to test design, not to behavior.
