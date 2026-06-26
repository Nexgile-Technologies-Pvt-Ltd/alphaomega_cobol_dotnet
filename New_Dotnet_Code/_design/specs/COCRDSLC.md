# PORT SPEC — COCRDSLC (Credit Card Detail / View Card, online/CICS)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COCRDSLC.cbl`
BMS map source: `Old_Cobol_Code/.../app/bms/COCRDSL.bms` (mapset `COCRDSL`, map `CCRDSLA`)
BMS symbolic copybook: `Old_Cobol_Code/.../app/cpy-bms/COCRDSL.CPY` (`CCRDSLAI` input / `CCRDSLAO` output)
Logic copybooks: `CVCRD01Y.cpy` (CC-WORK-AREA + AID flags + filter fields), `COCOM01Y.cpy` (CARDDEMO-COMMAREA), `CVACT02Y.cpy` (CARD-RECORD), `CSSTRPFY.cpy` (`YYYY-STORE-PFKEY`), `CSDAT01Y.cpy` (date/time), `CSMSG01Y`/`CSMSG02Y` (messages/abend), `COTTL01Y.cpy` (titles), `CSUSR01Y.cpy` (signed-on user), `CVCUS01Y.cpy` (customer — COPYed but unused here).
Target spec consumer: `src/CardDemo.Online` (transaction handler `CCDL`) + `src/CardDemo.Data` (CARD repository) per ARCHITECTURE.md.

All line citations use the form `// source: COCRDSLC.cbl:NNN` (or the named copybook).

---

## 1. Purpose & Invocation

**Purpose.** COCRDSLC is the CICS pseudo-conversational **"View Credit Card Detail"** transaction. The operator supplies an **11-digit account number** and a **16-digit card number** on a 24×80 BMS screen; the program validates both filters, reads the CARD master by primary key (card number) and, on a hit, displays the card's **embossed name, active status (Y/N), and expiry month + year**. It is the detail-view counterpart of the card-list program COCRDLIC, and is most often reached *from* that list (selection criteria pre-validated). It is read-only — it never writes/updates/deletes any file. `// source: COCRDSLC.cbl:1-5`

**Invocation.**
- CICS TRANSID **`CCDL`** (`LIT-THISTRANID`). `// source: COCRDSLC.cbl:165-166`
- Program id **`COCRDSLC`** (`LIT-THISPGM`). `// source: COCRDSLC.cbl:163-164`
- Mapset **`COCRDSL`** (`LIT-THISMAPSET`, note trailing space → 8 chars `'COCRDSL '`) / map **`CCRDSLA`** (`LIT-THISMAP`). `// source: COCRDSLC.cbl:167-170`
- Pseudo-conversational: re-drives itself via `EXEC CICS RETURN TRANSID('CCDL') COMMAREA(WS-COMMAREA)`. `// source: COCRDSLC.cbl:402-406`
- Typically reached by `EXEC CICS XCTL` from the card-list program **`COCRDLIC`/`CCLI`** (`LIT-CCLISTPGM`/`LIT-CCLISTTRANID`); when `CDEMO-PGM-ENTER AND CDEMO-FROM-PROGRAM = 'COCRDLIC'` the filters in COMMAREA are already validated, so the program reads immediately. `// source: COCRDSLC.cbl:339-348`
- Also reachable cold (EIBCALEN=0) or from the main menu **`COMEN01C`/`CM00`** (`LIT-MENUPGM`/`LIT-MENUTRANID`). `// source: COCRDSLC.cbl:179-186`
- It is **not** a called subroutine; all flow is via COMMAREA + XCTL/RETURN.
- **XCTL target out:** on PF3 → `CDEMO-TO-PROGRAM`, which is `CDEMO-FROM-PROGRAM` if set, else `COMEN01C` (menu). `// source: COCRDSLC.cbl:305-334`

---

## 2. FILE / TABLE ACCESS

Only **one** file is accessed: the CARD master, by **primary-key READ** (card number). No write/rewrite/delete and **no browse**. An alternate-index path literal `CARDAIX` (`LIT-CARDFILENAME-ACCT-PATH`) is declared and a paragraph `9150-GETCARD-BYACCT` exists that would READ via that path, **but that paragraph is never PERFORMed** (dead code — see Faithful Bugs §7). Only the primary-key READ in `9100-GETCARD-BYACCTCARD` runs.

| COBOL DATASET (DDNAME literal) | Logical file | ARCH table | Key (RIDFLD) | CICS op | SQL equivalent |
|---|---|---|---|---|---|
| `CARDDAT` (`LIT-CARDFILENAME`) | Card master | **CARD** | `WS-CARD-RID-CARDNUM` X(16) | READ (key, equal) | `SELECT * FROM CARD WHERE card_num = @cardNum` |
| `CARDAIX` (`LIT-CARDFILENAME-ACCT-PATH`) | Card master alt-index by acct | **CARD** (idx acct_id) | `WS-CARD-RID-ACCT-ID` 9(11) | READ (alt key) **— DEAD CODE, never invoked** | (not implemented) |

Citations: primary READ `// source: COCRDSLC.cbl:742-750`; RESP evaluation `// source: COCRDSLC.cbl:752-772`. Dead alt-index READ `// source: COCRDSLC.cbl:783-808`.

**Repository contract notes (per ARCHITECTURE.md §VSAM→SQL).**
- `READ FILE('CARDDAT') RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16)` = keyed equal read on PK `card_num`. The RIDFLD is the X(16) card-number image. `// source: COCRDSLC.cbl:742-745`
- RESP mapping (`EVALUATE WS-RESP-CD`): `DFHRESP(NORMAL)` → record found (FileStatus '00'); `DFHRESP(NOTFND)` → not found (FileStatus '23'); `WHEN OTHER` → hard error → build file-error message. `// source: COCRDSLC.cbl:752-772`
- Repository returns the full **150-byte CARD-RECORD** (`INTO(CARD-RECORD)`), but only `CARD-EMBOSSED-NAME`, `CARD-EXPIRAION-DATE`, `CARD-ACTIVE-STATUS` are consumed for display (see 1200-SETUP-SCREEN-VARS). `CARD-ACCT-ID`/`CARD-CVV-CD` are read into the buffer but not displayed. `// source: COCRDSLC.cbl:746-747, 474-485`
- **Important faithful semantic:** the READ keys **only on card number** — the account number, though validated and required, is **not** used to constrain the read (the `MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID` line is commented out). So the displayed card may belong to a *different* account than the one entered; the account filter is validated for format only. See Faithful Bugs §7. `// source: COCRDSLC.cbl:739-740`

---

## 3. DATA STRUCTURES USED

- **CARD-RECORD** (`CVACT02Y`, RECLN 150): `CARD-NUM X(16)`, `CARD-ACCT-ID 9(11)`, `CARD-CVV-CD 9(3)`, `CARD-EMBOSSED-NAME X(50)`, `CARD-EXPIRAION-DATE X(10)`, `CARD-ACTIVE-STATUS X(1)`, FILLER X(59). Only EMBOSSED-NAME / EXPIRAION-DATE / ACTIVE-STATUS used for display. `// source: CVACT02Y.cpy:4-11`
- **CC-WORK-AREA** (`CVCRD01Y`): AID flags (`CCARD-AID` X5, 88s ENTER/CLEAR/PA1-2/PFK01-12), `CCARD-NEXT-PROG X8`, `CCARD-NEXT-MAPSET X7`, `CCARD-NEXT-MAP X7`, `CCARD-ERROR-MSG X75`, `CCARD-RETURN-MSG X75`, filter inputs `CC-ACCT-ID X11` (redef `CC-ACCT-ID-N 9(11)`), `CC-CARD-NUM X16` (redef `CC-CARD-NUM-N 9(16)`), `CC-CUST-ID X9` (redef `CC-CUST-ID-N 9(9)`). `// source: CVCRD01Y.cpy:1-42`
- **CARDDEMO-COMMAREA** (`COCOM01Y`, the shared inter-program commarea): `CDEMO-FROM-TRANID X4`, `CDEMO-FROM-PROGRAM X8`, `CDEMO-TO-TRANID X4`, `CDEMO-TO-PROGRAM X8`, `CDEMO-USER-ID X8`, `CDEMO-USER-TYPE X1` (88 ADMIN='A'/USER='U'), `CDEMO-PGM-CONTEXT 9(1)` (88 CDEMO-PGM-ENTER=0 / CDEMO-PGM-REENTER=1), `CDEMO-CUST-ID 9(9)` (+ fname/mname/lname), `CDEMO-ACCT-ID 9(11)`, `CDEMO-ACCT-STATUS X1`, `CDEMO-CARD-NUM 9(16)`, `CDEMO-LAST-MAP X7`, `CDEMO-LAST-MAPSET X7`. `// source: COCOM01Y.cpy:19-44`
- **WS-THIS-PROGCOMMAREA** (program-private commarea tail, appended after CARDDEMO-COMMAREA): `CA-CALL-CONTEXT` = `CA-FROM-PROGRAM X8` + `CA-FROM-TRANID X4` (12 bytes). `// source: COCRDSLC.cbl:200-203`
- **WS-COMMAREA X(2000)** — the full RETURN commarea buffer; CARDDEMO-COMMAREA is copied into bytes 1..len, WS-THIS-PROGCOMMAREA into bytes len+1..len+12. `// source: COCRDSLC.cbl:205, 397-400`
- **WS-CARD-RID**: `WS-CARD-RID-CARDNUM X16`, `WS-CARD-RID-ACCT-ID 9(11)` (redef `-ACCT-ID-X X11`). The RIDFLD holder. `// source: COCRDSLC.cbl:97-101`
- **CICS-OUTPUT-EDIT-VARS** (display/edit scratch with REDEFINES): `CARD-ACCT-ID-X X11`/`-N 9(11)`, `CARD-CVV-CD-X X3`/`-N 9(3)`, `CARD-CARD-NUM-X X16`/`-N 9(16)`, `CARD-NAME-EMBOSSED-X X50`, `CARD-STATUS-X X1`, `CARD-EXPIRAION-DATE-X X10` redefined into `CARD-EXPIRY-YEAR X4` + FILLER X1 + `CARD-EXPIRY-MONTH X2` + FILLER X1 + `CARD-EXPIRY-DAY X2` (i.e. expiry stored as `YYYY-MM-DD`), and `CARD-EXPIRAION-DATE-N 9(10)`. `// source: COCRDSLC.cbl:72-92`
- **WS-INPUT-FLAG X1** (88 INPUT-OK='0', INPUT-ERROR='1', INPUT-PENDING=LOW-VALUES). `// source: COCRDSLC.cbl:51-54`
- **WS-EDIT-ACCT-FLAG X1** (88 FLG-ACCTFILTER-NOT-OK='0', FLG-ACCTFILTER-ISVALID='1', FLG-ACCTFILTER-BLANK=' '). `// source: COCRDSLC.cbl:55-58`
- **WS-EDIT-CARD-FLAG X1** (88 FLG-CARDFILTER-NOT-OK='0', FLG-CARDFILTER-ISVALID='1', FLG-CARDFILTER-BLANK=' '). `// source: COCRDSLC.cbl:59-62`
- **WS-RETURN-FLAG X1** (88 OFF=LOW-VALUES, ON='1'). `// source: COCRDSLC.cbl:63-65`
- **WS-PFK-FLAG X1** (88 PFK-VALID='0', PFK-INVALID='1'). `// source: COCRDSLC.cbl:66-68`
- **WS-INFO-MSG X40** (88 WS-NO-INFO-MESSAGE=SPACES/LOW-VALUES, FOUND-CARDS-FOR-ACCOUNT='   Displaying requested details', WS-PROMPT-FOR-INPUT='Please enter Account and Card Number'). `// source: COCRDSLC.cbl:126-132`
- **WS-RETURN-MSG X75** with 88-level message constants (88 WS-RETURN-MSG-OFF=SPACES, plus the message literals enumerated in §6). `// source: COCRDSLC.cbl:134-158`
- **WS-FILE-ERROR-MESSAGE** group: `'File Error: '` + ERROR-OPNAME X8 + `' on '` + ERROR-FILE X9 + `' returned RESP '` + ERROR-RESP X10 + `,RESP2 ` + ERROR-RESP2 X10 + 5 spaces. `// source: COCRDSLC.cbl:102-121`

---

## 4. BMS MAP — `COCRDSL` / `CCRDSLA` (24×80)

Symbolic copybook `COCRDSL.CPY` provides `CCRDSLAI` (input map) and `CCRDSLAO` (output map, REDEFINES of input). For each named field there is `<f>L` length (S9(4) COMP), `<f>F` flag / `<f>A` attribute (X), `<f>I` input value, and on output `<f>C` color / `<f>P` / `<f>H` / `<f>V` / `<f>O` output value. `// source: COCRDSL.CPY:17-201`

| Field | Pos | Len | Role | Read by program? | Written by program? |
|---|---|---|---|---|---|
| `TRNNAME` | (1,7) | 4 | Tran id label | no | yes — `LIT-THISTRANID` `// source: COCRDSLC.cbl:434` |
| `TITLE01` | (1,21) | 40 | Title line 1 | no | yes — `CCDA-TITLE01` `// source: COCRDSLC.cbl:432` |
| `CURDATE` | (1,71) | 8 | Date `mm/dd/yy` | no | yes `// source: COCRDSLC.cbl:443` |
| `PGMNAME` | (2,7) | 8 | Program name | no | yes — `LIT-THISPGM` `// source: COCRDSLC.cbl:435` |
| `TITLE02` | (2,21) | 40 | Title line 2 | no | yes — `CCDA-TITLE02` `// source: COCRDSLC.cbl:433` |
| `CURTIME` | (2,71) | 8 | Time `hh:mm:ss` | no | yes `// source: COCRDSLC.cbl:449` |
| **`ACCTSID`** | (7,45) | 11 | **Account Number input** | **yes** (`ACCTSIDI`) `// source: COCRDSLC.cbl:615-619` | yes (`ACCTSIDO`, value/attr/color) |
| **`CARDSID`** | (8,45) | 16 | **Card Number input** | **yes** (`CARDSIDI`) `// source: COCRDSLC.cbl:622-626` | yes (`CARDSIDO`, value/attr/color) |
| `CRDNAME` | (11,25) | 50 | Name on card (output) | no | yes — `CARD-EMBOSSED-NAME` `// source: COCRDSLC.cbl:475-476` |
| `CRDSTCD` | (13,25) | 1 | Card Active Y/N (output) | no | yes — `CARD-ACTIVE-STATUS` `// source: COCRDSLC.cbl:484` |
| `EXPMON` | (15,25) | 2 | Expiry month (output) | no | yes — `CARD-EXPIRY-MONTH` `// source: COCRDSLC.cbl:480` |
| `EXPYEAR` | (15,30) | 4 | Expiry year (output) | no | yes — `CARD-EXPIRY-YEAR` `// source: COCRDSLC.cbl:482` |
| `INFOMSG` | (20,25) | 40 | Info message | no | yes — `WS-INFO-MSG` `// source: COCRDSLC.cbl:496` |
| `ERRMSG` | (23,1) | 80 | Error message | no | yes — `WS-RETURN-MSG` via `CCARD-ERROR-MSG` `// source: COCRDSLC.cbl:494` |
| `FKEYS` | (24,1) | 75 | F-key legend | no | (BMS INITIAL `'ENTER=Search Cards  F3=Exit'`) |

BMS notes (`COCRDSL.bms`): mapset `COCRDSL` `DFHMSD MODE=INOUT, TIOAPFX=YES`; map `CCRDSLA DFHMDI CTRL=(FREEKB) SIZE=(24,80)`. `ACCTSID` is `ATTRB=(FSET,IC,NORM,UNPROT)` (initial cursor / IC); `CARDSID` is `ATTRB=(FSET,NORM,UNPROT)`. Output-only fields (`CRDNAME`,`CRDSTCD`,`EXPMON`,`EXPYEAR`) are protected/ASKIP. `ERRMSG ATTRB=(ASKIP,BRT,FSET) COLOR=RED`. `// source: COCRDSL.bms:20-152`

---

## 5. PSEUDO-CONVERSATIONAL FLOW & COMMAREA

### 5.1 Commarea contract
On entry the LINKAGE `DFHCOMMAREA` is split: first `LENGTH OF CARDDEMO-COMMAREA` bytes → `CARDDEMO-COMMAREA`; next `LENGTH OF WS-THIS-PROGCOMMAREA` (12) bytes → `WS-THIS-PROGCOMMAREA`. If `EIBCALEN = 0`, **or** (`CDEMO-FROM-PROGRAM = 'COMEN01C' AND NOT CDEMO-PGM-REENTER`), both commareas are re-INITIALIZEd (fresh start). `// source: COCRDSLC.cbl:268-279`
On RETURN, the two areas are re-concatenated back into `WS-COMMAREA` and passed via `RETURN TRANSID('CCDL')`. `// source: COCRDSLC.cbl:397-406`

### 5.2 AID / PFKey handling
1. `PERFORM YYYY-STORE-PFKEY` (from `CSSTRPFY.cpy`): maps `EIBAID` → one of `CCARD-AID-*` flags. PF13–24 fold onto PFK01–12 respectively. `// source: COCRDSLC.cbl:284-285; CSSTRPFY.cpy:21-78`
2. `SET PFK-INVALID TO TRUE`; then if `CCARD-AID-ENTER OR CCARD-AID-PFK03` → `SET PFK-VALID`. `// source: COCRDSLC.cbl:291-295`
3. If still `PFK-INVALID` → `SET CCARD-AID-ENTER TO TRUE` (any unsupported key is **silently coerced to ENTER**). `// source: COCRDSLC.cbl:297-299`

So only **ENTER** and **PF3** are honored; everything else (including CLEAR, PA1/2, PF1-2,4-12) is treated as ENTER. `// source: COCRDSLC.cbl:291-299`

### 5.3 Main dispatch (`EVALUATE TRUE`) — `// source: COCRDSLC.cbl:304-381`
- **WHEN `CCARD-AID-PFK03`** (PF3 exit): compute `CDEMO-TO-TRANID` (= `CDEMO-FROM-TRANID` if set, else `CM00`), `CDEMO-TO-PROGRAM` (= `CDEMO-FROM-PROGRAM` if set, else `COMEN01C`); set FROM-TRANID/FROM-PROGRAM to this program, `CDEMO-USRTYP-USER`, `CDEMO-PGM-ENTER`, LAST-MAPSET/MAP; `EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA)`. `// source: COCRDSLC.cbl:305-334`
- **WHEN `CDEMO-PGM-ENTER AND CDEMO-FROM-PROGRAM = 'COCRDLIC'`** (came from card list, criteria already validated): `SET INPUT-OK`, move `CDEMO-ACCT-ID → CC-ACCT-ID-N`, `CDEMO-CARD-NUM → CC-CARD-NUM-N`, `PERFORM 9000-READ-DATA`, `PERFORM 1000-SEND-MAP`, `GO TO COMMON-RETURN`. `// source: COCRDSLC.cbl:339-348`
- **WHEN `CDEMO-PGM-ENTER`** (any other first-entry context): `PERFORM 1000-SEND-MAP`, `GO TO COMMON-RETURN` (just paint the prompt screen). `// source: COCRDSLC.cbl:349-356`
- **WHEN `CDEMO-PGM-REENTER`** (user pressed a key on our screen): `PERFORM 2000-PROCESS-INPUTS`; if `INPUT-ERROR` → SEND-MAP + COMMON-RETURN; else `PERFORM 9000-READ-DATA`, SEND-MAP, COMMON-RETURN. `// source: COCRDSLC.cbl:357-371`
- **WHEN OTHER** (unexpected): set ABEND-CULPRIT/CODE/REASON, `WS-RETURN-MSG = 'UNEXPECTED DATA SCENARIO'`, `PERFORM SEND-PLAIN-TEXT` (SEND TEXT + RETURN, ends transaction). `// source: COCRDSLC.cbl:373-380`

After the EVALUATE, a fallthrough guard: if `INPUT-ERROR` still set, move `WS-RETURN-MSG → CCARD-ERROR-MSG`, SEND-MAP, COMMON-RETURN. `// source: COCRDSLC.cbl:386-391`

---

## 6. PARAGRAPH-BY-PARAGRAPH OUTLINE

> Each PROCEDURE-DIVISION paragraph maps to one method. Preserve statement order and PERFORM/GO-TO flow. `THRU …-EXIT` pairs collapse to a method returning at the EXIT label.

1. **0000-MAIN** `// source: COCRDSLC.cbl:248-407`
   - `EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE)`; `INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA`; `MOVE LIT-THISTRANID → WS-TRANID`; `SET WS-RETURN-MSG-OFF`.
   - Commarea split / fresh-start logic (see §5.1).
   - `PERFORM YYYY-STORE-PFKEY`; AID validation/coercion (see §5.2).
   - Main `EVALUATE TRUE` dispatch (see §5.3).
   - Fallthrough INPUT-ERROR guard → SEND-MAP + GO TO COMMON-RETURN.
2. **COMMON-RETURN** `// source: COCRDSLC.cbl:394-407`
   - `MOVE WS-RETURN-MSG → CCARD-ERROR-MSG`; rebuild `WS-COMMAREA` from CARDDEMO-COMMAREA + WS-THIS-PROGCOMMAREA; `EXEC CICS RETURN TRANSID('CCDL') COMMAREA(WS-COMMAREA) LENGTH(2000)`.
3. **0000-MAIN-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:408-410`
4. **1000-SEND-MAP** `// source: COCRDSLC.cbl:412-425`
   - PERFORM 1100-SCREEN-INIT, 1200-SETUP-SCREEN-VARS, 1300-SETUP-SCREEN-ATTRS, 1400-SEND-SCREEN (in that order).
5. **1100-SCREEN-INIT** `// source: COCRDSLC.cbl:427-455`
   - `MOVE LOW-VALUES TO CCRDSLAO`; load `FUNCTION CURRENT-DATE → WS-CURDATE-DATA`; move titles `CCDA-TITLE01/02`, `LIT-THISTRANID → TRNNAMEO`, `LIT-THISPGM → PGMNAMEO`; reload CURRENT-DATE; build `WS-CURDATE-MM/DD/YY` (year = `WS-CURDATE-YEAR(3:2)`, 2-digit) → `CURDATEO` as `mm-dd-yy`; build `WS-CURTIME-HH/MM/SS` → `CURTIMEO` as `hh-mm-ss`.
6. **1100-SCREEN-INIT-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:453-455`
7. **1200-SETUP-SCREEN-VARS** `// source: COCRDSLC.cbl:457-501`
   - If `EIBCALEN = 0` → `SET WS-PROMPT-FOR-INPUT`. Else: if `CDEMO-ACCT-ID = 0` → `MOVE LOW-VALUES → ACCTSIDO` else `MOVE CC-ACCT-ID → ACCTSIDO`; same for card (`CDEMO-CARD-NUM = 0` ? LOW-VALUES : `CC-CARD-NUM`) → `CARDSIDO`.
   - If `FOUND-CARDS-FOR-ACCOUNT` (i.e. a card was just read): move `CARD-EMBOSSED-NAME → CRDNAMEO`; move `CARD-EXPIRAION-DATE → CARD-EXPIRAION-DATE-X` (parse YYYY-MM-DD via REDEFINES); `CARD-EXPIRY-MONTH → EXPMONO`; `CARD-EXPIRY-YEAR → EXPYEARO`; `CARD-ACTIVE-STATUS → CRDSTCDO`.
   - Message setup: if `WS-NO-INFO-MESSAGE` → `SET WS-PROMPT-FOR-INPUT`; `MOVE WS-RETURN-MSG → ERRMSGO`; `MOVE WS-INFO-MSG → INFOMSGO`.
8. **1200-SETUP-SCREEN-VARS-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:499-501`
9. **1300-SETUP-SCREEN-ATTRS** `// source: COCRDSLC.cbl:502-560`
   - Protect/unprotect: if `CDEMO-LAST-MAPSET = 'COCRDLI' AND CDEMO-FROM-PROGRAM = 'COCRDLIC'` → set both `ACCTSIDA`/`CARDSIDA = DFHBMPRF` (protect); else `= DFHBMFSE` (unprotect, modified-data-tag forced).
   - Cursor (`EVALUATE TRUE`): if acct NOT-OK or BLANK → `MOVE -1 TO ACCTSIDL`; elif card NOT-OK or BLANK → `MOVE -1 TO CARDSIDL`; else (OTHER) → `MOVE -1 TO ACCTSIDL`.
   - Color: if from-list (same condition as protect) → `ACCTSIDC/CARDSIDC = DFHDFCOL` (default). If `FLG-ACCTFILTER-NOT-OK` → `ACCTSIDC = DFHRED`. If `FLG-CARDFILTER-NOT-OK` → `CARDSIDC = DFHRED`.
   - If `FLG-ACCTFILTER-BLANK AND CDEMO-PGM-REENTER` → `MOVE '*' TO ACCTSIDO`, `ACCTSIDC = DFHRED`. Same for card-blank → `'*' TO CARDSIDO`, `CARDSIDC = DFHRED`.
   - Info color: if `WS-NO-INFO-MESSAGE` → `INFOMSGC = DFHBMDAR` (dark) else `= DFHNEUTR`.
10. **1300-SETUP-SCREEN-ATTRS-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:559-560`
11. **1400-SEND-SCREEN** `// source: COCRDSLC.cbl:563-580`
    - `MOVE LIT-THISMAPSET → CCARD-NEXT-MAPSET`; `MOVE LIT-THISMAP → CCARD-NEXT-MAP`; `SET CDEMO-PGM-REENTER TO TRUE`; `EXEC CICS SEND MAP(CCARD-NEXT-MAP) MAPSET(CCARD-NEXT-MAPSET) FROM(CCRDSLAO) CURSOR ERASE FREEKB RESP(WS-RESP-CD)`.
12. **1400-SEND-SCREEN-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:578-580`
13. **2000-PROCESS-INPUTS** `// source: COCRDSLC.cbl:582-595`
    - PERFORM 2100-RECEIVE-MAP, 2200-EDIT-MAP-INPUTS; then `MOVE WS-RETURN-MSG → CCARD-ERROR-MSG`, `LIT-THISPGM → CCARD-NEXT-PROG`, `LIT-THISMAPSET → CCARD-NEXT-MAPSET`, `LIT-THISMAP → CCARD-NEXT-MAP`.
14. **2000-PROCESS-INPUTS-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:593-595`
15. **2100-RECEIVE-MAP** `// source: COCRDSLC.cbl:596-607`
    - `EXEC CICS RECEIVE MAP('CCRDSLA') MAPSET('COCRDSL ') INTO(CCRDSLAI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD)`. (No RESP check after — see Faithful Bugs §7.)
16. **2100-RECEIVE-MAP-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:605-607`
17. **2200-EDIT-MAP-INPUTS** `// source: COCRDSLC.cbl:608-645`
    - `SET INPUT-OK`, `SET FLG-CARDFILTER-ISVALID`, `SET FLG-ACCTFILTER-ISVALID` (optimistic defaults).
    - Normalize acct: if `ACCTSIDI = '*'` or `= SPACES` → `MOVE LOW-VALUES TO CC-ACCT-ID` else `MOVE ACCTSIDI TO CC-ACCT-ID`. Same normalize for `CARDSIDI → CC-CARD-NUM`.
    - PERFORM 2210-EDIT-ACCOUNT, 2220-EDIT-CARD.
    - Cross-field: if `FLG-ACCTFILTER-BLANK AND FLG-CARDFILTER-BLANK` → `SET NO-SEARCH-CRITERIA-RECEIVED` (message `'No input received'`).
18. **2200-EDIT-MAP-INPUTS-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:643-645`
19. **2210-EDIT-ACCOUNT** `// source: COCRDSLC.cbl:647-683`
    - `SET FLG-ACCTFILTER-NOT-OK` (default fail).
    - Not supplied: if `CC-ACCT-ID = LOW-VALUES` OR `= SPACES` OR `CC-ACCT-ID-N = ZEROS` → `SET INPUT-ERROR`, `SET FLG-ACCTFILTER-BLANK`; if `WS-RETURN-MSG-OFF` → `SET WS-PROMPT-FOR-ACCT` (`'Account number not provided'`); `MOVE ZEROES → CDEMO-ACCT-ID`; `GO TO 2210-EDIT-ACCOUNT-EXIT`.
    - Not numeric: if `CC-ACCT-ID IS NOT NUMERIC` → `SET INPUT-ERROR`, `SET FLG-ACCTFILTER-NOT-OK`; if `WS-RETURN-MSG-OFF` → `MOVE 'ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER' → WS-RETURN-MSG`; `MOVE ZERO → CDEMO-ACCT-ID`; `GO TO …EXIT`. Else `MOVE CC-ACCT-ID → CDEMO-ACCT-ID`, `SET FLG-ACCTFILTER-ISVALID`.
20. **2210-EDIT-ACCOUNT-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:681-683`
21. **2220-EDIT-CARD** `// source: COCRDSLC.cbl:685-724`
    - `SET FLG-CARDFILTER-NOT-OK` (default fail).
    - Not supplied: if `CC-CARD-NUM = LOW-VALUES` OR `= SPACES` OR `CC-CARD-NUM-N = ZEROS` → `SET INPUT-ERROR`, `SET FLG-CARDFILTER-BLANK`; if `WS-RETURN-MSG-OFF` → `SET WS-PROMPT-FOR-CARD` (`'Card number not provided'`); `MOVE ZEROES → CDEMO-CARD-NUM`; `GO TO 2220-EDIT-CARD-EXIT`.
    - Not numeric: if `CC-CARD-NUM IS NOT NUMERIC` → `SET INPUT-ERROR`, `SET FLG-CARDFILTER-NOT-OK`; if `WS-RETURN-MSG-OFF` → `MOVE 'CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER' → WS-RETURN-MSG`; `MOVE ZERO → CDEMO-CARD-NUM`; `GO TO …EXIT`. Else `MOVE CC-CARD-NUM-N → CDEMO-CARD-NUM`, `SET FLG-CARDFILTER-ISVALID`.
22. **2220-EDIT-CARD-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:722-724`
23. **9000-READ-DATA** `// source: COCRDSLC.cbl:726-734`
    - PERFORM 9100-GETCARD-BYACCTCARD.
24. **9000-READ-DATA-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:732-734`
25. **9100-GETCARD-BYACCTCARD** `// source: COCRDSLC.cbl:736-777`
    - (commented) acct-id move skipped; `MOVE CC-CARD-NUM → WS-CARD-RID-CARDNUM`; `EXEC CICS READ FILE('CARDDAT') RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) INTO(CARD-RECORD) LENGTH(150) RESP/RESP2`.
    - `EVALUATE WS-RESP-CD`: NORMAL → `SET FOUND-CARDS-FOR-ACCOUNT`; NOTFND → `SET INPUT-ERROR`, `SET FLG-ACCTFILTER-NOT-OK`, `SET FLG-CARDFILTER-NOT-OK`, if `WS-RETURN-MSG-OFF` → `SET DID-NOT-FIND-ACCTCARD-COMBO` (`'Did not find cards for this search condition'`); OTHER → `SET INPUT-ERROR`, if `WS-RETURN-MSG-OFF` → `SET FLG-ACCTFILTER-NOT-OK`, build `WS-FILE-ERROR-MESSAGE` (op `'READ'`, file `CARDDAT`, RESP/RESP2) → `WS-RETURN-MSG`.
26. **9100-GETCARD-BYACCTCARD-EXIT** — `EXIT`. `// source: COCRDSLC.cbl:775-777`
27. **9150-GETCARD-BYACCT** — **DEAD CODE, never PERFORMed.** Alt-index READ on `CARDAIX` by `WS-CARD-RID-ACCT-ID`; RESP eval sets FOUND / `DID-NOT-FIND-ACCT-IN-CARDXREF` (`'Did not find this account in cards database'`) / file-error. Do not port as live code (document only). `// source: COCRDSLC.cbl:779-812`
28. **SEND-LONG-TEXT** — debug only: `SEND TEXT FROM(WS-LONG-MSG) … ERASE FREEKB` + `RETURN`. Not referenced. `// source: COCRDSLC.cbl:820-833`
29. **SEND-PLAIN-TEXT** — `SEND TEXT FROM(WS-RETURN-MSG) … ERASE FREEKB` + `RETURN`. Invoked only by the WHEN OTHER branch of the main dispatch. `// source: COCRDSLC.cbl:838-851`
30. **YYYY-STORE-PFKEY** (from `CSSTRPFY.cpy`) — maps `EIBAID` → `CCARD-AID-*` (PF13-24 fold to PFK01-12). `// source: CSSTRPFY.cpy:17-82`
31. **ABEND-ROUTINE** (HANDLE ABEND label) — if `ABEND-MSG = LOW-VALUES` → set default text; `MOVE LIT-THISPGM → ABEND-CULPRIT`; `EXEC CICS SEND FROM(ABEND-DATA) NOHANDLE`; `EXEC CICS HANDLE ABEND CANCEL`; `EXEC CICS ABEND ABCODE('9999')`. `// source: COCRDSLC.cbl:857-878`

---

## 7. VALIDATION RULES & EXACT LITERAL MESSAGES

Field validations (in `2200/2210/2220`):

| Rule | Condition | Flag set | Exact message text |
|---|---|---|---|
| Account required | `CC-ACCT-ID` = LOW-VALUES / SPACES / numeric ZEROS | `FLG-ACCTFILTER-BLANK`, `INPUT-ERROR` | `Account number not provided` (88 WS-PROMPT-FOR-ACCT) `// source: COCRDSLC.cbl:138-139, 651-661` |
| Account numeric | `CC-ACCT-ID IS NOT NUMERIC` | `FLG-ACCTFILTER-NOT-OK`, `INPUT-ERROR` | `ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER` (literal MOVE) `// source: COCRDSLC.cbl:665-672` |
| Card required | `CC-CARD-NUM` = LOW-VALUES / SPACES / numeric ZEROS | `FLG-CARDFILTER-BLANK`, `INPUT-ERROR` | `Card number not provided` (88 WS-PROMPT-FOR-CARD) `// source: COCRDSLC.cbl:140-141, 691-702` |
| Card numeric | `CC-CARD-NUM IS NOT NUMERIC` | `FLG-CARDFILTER-NOT-OK`, `INPUT-ERROR` | `CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER` (literal MOVE) `// source: COCRDSLC.cbl:706-713` |
| Both blank | `FLG-ACCTFILTER-BLANK AND FLG-CARDFILTER-BLANK` | `NO-SEARCH-CRITERIA-RECEIVED` | `No input received` `// source: COCRDSLC.cbl:142-143, 637-640` |
| Card not found | READ RESP = NOTFND | acct+card NOT-OK, `INPUT-ERROR` | `Did not find cards for this search condition` (88 DID-NOT-FIND-ACCTCARD-COMBO) `// source: COCRDSLC.cbl:153-154, 755-761` |
| Read hard error | READ RESP = OTHER | `INPUT-ERROR` | `File Error: READ      on CARDDAT   returned RESP nnnn ,RESP2 nnnn` (WS-FILE-ERROR-MESSAGE) `// source: COCRDSLC.cbl:762-771` |

Other message 88-constants defined but **set elsewhere / dead in this program**: `SEARCHED-ACCT-ZEROES`/`SEARCHED-ACCT-NOT-NUMERIC` = `'Account number must be a non zero 11 digit number'`; `SEARCHED-CARD-NOT-NUMERIC` = `'Card number if supplied must be a 16 digit number'`; `DID-NOT-FIND-ACCT-IN-CARDXREF` = `'Did not find this account in cards database'` (only set in dead 9150); `XREF-READ-ERROR` = `'Error reading Card Data File'`; `CODING-TO-BE-DONE` = `'Looks Good.... so far'`. These literals are never SET on the live path — keep the constants but do not emit them. `// source: COCRDSLC.cbl:144-158`

Info-message 88s: `FOUND-CARDS-FOR-ACCOUNT` = `'   Displaying requested details'` (3 leading spaces — preserve), `WS-PROMPT-FOR-INPUT` = `'Please enter Account and Card Number'`. `// source: COCRDSLC.cbl:129-132`

Exit message: `WS-EXIT-MESSAGE` = `'PF03 pressed.Exiting              '` is defined but **not set on the live path** (PF3 just XCTLs without setting it). `// source: COCRDSLC.cbl:136-137`

**The `WS-RETURN-MSG-OFF` guard pattern:** every message MOVE/SET is guarded by `IF WS-RETURN-MSG-OFF` so the **first** error to fire wins and later validations do not overwrite it. Preserve this "first-error-wins" ordering: account-required → account-numeric → card-required → card-numeric → both-blank → read result. `// source: COCRDSLC.cbl:656, 668, 696, 709, 759, 764`

---

## 8. NUMERIC / ARITHMETIC NOTES

This program does **no COMPUTE / arithmetic** at all — no money math, no counters. All "numeric" handling is:
- **`IS NUMERIC` class tests** on `CC-ACCT-ID` (X11) and `CC-CARD-NUM` (X16): true only if every character is a digit `0-9` (PIC X tested for numeric → all-digits). Port as a regex/all-digit check on the exact 11/16-char field including any embedded spaces. `// source: COCRDSLC.cbl:665, 706`
- **REDEFINES X↔9 comparisons**: `CC-ACCT-ID-N EQUAL ZEROS` and `CC-CARD-NUM-N EQUAL ZEROS` reinterpret the X field as a zoned-decimal 9 field and test all-zero. Port: treat as "field string is all `'0'` digits". `// source: COCRDSLC.cbl:653, 693`
- **Expiry date parsing** via REDEFINES of `CARD-EXPIRAION-DATE X(10)` as `YYYY`(4) `-`(1) `MM`(2) `-`(1) `DD`(2): so `CARD-EXPIRY-YEAR` = chars 1-4, `CARD-EXPIRY-MONTH` = chars 6-7, `CARD-EXPIRY-DAY` = chars 9-10. Slice the stored `expiration_date` TEXT (`CCYY-MM-DD`) accordingly; no validation of the date content is performed. `// source: COCRDSLC.cbl:84-92, 477-482`
- **2-digit screen date/time** built from `FUNCTION CURRENT-DATE`: year shown as last 2 digits (`WS-CURDATE-YEAR(3:2)`), assembled `mm-dd-yy` / `hh-mm-ss`. `// source: COCRDSLC.cbl:439-449`

No sign handling, no truncation, no edited PIC output fields. `decimal` / `CobolDecimal` from Runtime are **not** needed for this program.

---

## 9. FAITHFUL BUGS (reproduce verbatim — do not fix)

1. **Account number is validated but never used in the read.** The read keys only on card number; the line `MOVE CC-ACCT-ID-N TO WS-CARD-RID-ACCT-ID` is commented out. A user can type any (format-valid) account number with a valid card number and the program will display that card even if it belongs to a different account. Reproduce: read by card number alone, ignore the account value beyond format validation. `// source: COCRDSLC.cbl:739-740, 742-745`
2. **Dead alt-index path.** `9150-GETCARD-BYACCT` (alt-index `CARDAIX` read by account) and its message `DID-NOT-FIND-ACCT-IN-CARDXREF` are unreachable — `9150` is never PERFORMed. Keep as documented dead code; do not wire it up. `// source: COCRDSLC.cbl:779-812`
3. **PFKey coercion to ENTER.** Any AID other than ENTER/PF3 (CLEAR, PA1/PA2, PF1-2, PF4-12) is silently turned into ENTER, so e.g. CLEAR re-validates instead of clearing/exiting. Reproduce the coercion exactly. `// source: COCRDSLC.cbl:297-299`
4. **`RECEIVE MAP` RESP captured but never checked.** `2100-RECEIVE-MAP` stores RESP/RESP2 into `WS-RESP-CD`/`WS-REAS-CD` but never tests them; a MAPFAIL (e.g. blank screen / CLEAR-after-coercion) is not handled distinctly here. Do not add a check. `// source: COCRDSLC.cbl:596-603`
5. **`SET FLG-ACCTFILTER-NOT-OK` on NOTFND mislabels which filter failed.** On card-not-found the program reds **both** account and card filters even though only the (card) lookup failed. Preserve. `// source: COCRDSLC.cbl:755-758`
6. **`9100` OTHER branch only sets `FLG-ACCTFILTER-NOT-OK` when `WS-RETURN-MSG-OFF`**, so on a hard read error following a prior message the acct-NOT-OK red highlight may not be applied. Preserve the guard placement. `// source: COCRDSLC.cbl:762-766`
7. **Cursor fallback always lands on `ACCTSIDL` in the `WHEN OTHER`** branch of the cursor EVALUATE even after a successful card display, re-positioning cursor on the account field. Preserve. `// source: COCRDSLC.cbl:522-523`
8. **Title source quirk:** `LIT-THISMAPSET` is `'COCRDSL '` (8 chars incl. trailing space) but `CCARD-NEXT-MAPSET` / `CDEMO-LAST-MAPSET` are X(7); the trailing space is truncated on MOVE. Comparisons against `LIT-CCLISTMAPSET = 'COCRDLI'` (7) work; just preserve the literal widths. `// source: COCRDSLC.cbl:167-168, 175-176, 505, 565`

All of the above must be entered in `_design/faithful-bugs.md` with a pinning test each.

---

## 10. PORT NOTES (relational-access translation plan)

- **Transaction handler** in `src/CardDemo.Online` registered for TRANSID `CCDL`, program `COCRDSLC`. Implement the pseudo-conversational turn: receive AID + map input → run dispatch → produce next BMS screen model (`CCRDSLAO`) or XCTL, persist COMMAREA (CARDDEMO-COMMAREA 11+ fields + 12-byte program tail) for the next turn. Mirror the commarea split/concat exactly (offsets: CARDDEMO len, then +12). `// source: COCRDSLC.cbl:268-279, 397-406`
- **CARD repository read** (`src/CardDemo.Data`): single method `ReadByCardNumber(string cardNum16)` → `SELECT * FROM CARD WHERE card_num = @cardNum` returning the row + FileStatus (`'00'` found / `'23'` not found / other = error). Map RESP NORMAL→'00', NOTFND→'23', else hard error. Use the **exact 16-char** card-number string as the key (right-padded as the X(16) image would be; the input field is `CARDSIDI X(16)` so it is already 16 wide). Do **not** filter by account (faithful bug §9.1). `// source: COCRDSLC.cbl:742-772; ARCHITECTURE.md §VSAM→SQL READ key`
- **Field normalization:** treat input `'*'` or all-spaces as "blank" (→ LOW-VALUES) before validation. Account/card "numeric" = all-ASCII-digits over the full fixed width; "blank/zeros" = all spaces, low-values, or all `'0'`. Keep `CDEMO-ACCT-ID`/`CDEMO-CARD-NUM` updated to `0` on blank/non-numeric, to the parsed value on valid. `// source: COCRDSLC.cbl:614-627, 647-720`
- **Expiry parse:** slice the `CARD.expiration_date` TEXT as `[0:4]` year, `[5:7]` month (the `CCYY-MM-DD` layout) for the EXPYEAR/EXPMON output fields. No reformatting beyond the slice. `// source: COCRDSLC.cbl:84-90, 477-482`
- **Screen attributes** (`1300`): model BMS attribute bytes as a per-field attribute object (protect/unprotect = `DFHBMPRF`/`DFHBMFSE`; color `DFHRED`/`DFHDFCOL`/`DFHNEUTR`/`DFHBMDAR`; cursor via `-1` length). The "came from list" branch (`CDEMO-LAST-MAPSET='COCRDLI' AND CDEMO-FROM-PROGRAM='COCRDLIC'`) protects + default-colors the two filter fields and the `'*'` markers go on blank fields only on REENTER. Reproduce attribute logic for screen-parity tests. `// source: COCRDSLC.cbl:502-558`
- **`INITIALIZE` semantics:** `INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA` sets group alphanumeric subfields to SPACES and numeric to ZERO per COBOL rules; `MOVE LOW-VALUES TO CCRDSLAO` clears the output map to binary zeros (suppresses fields). The .NET screen model should default unset output fields to "not sent" (low-values) and explicitly set only the fields the program moves into. `// source: COCRDSLC.cbl:254-256, 428`
- **REDEFINES** on `CC-ACCT-ID`/`CC-CARD-NUM` (X↔9) and `CARD-EXPIRAION-DATE` (X10 ↔ Y/M/D slices) → in C# keep one string field and expose typed accessors/slices; do not store both.
- **No arithmetic / no money:** `CobolDecimal` not required here. Date/time come from `IClock` (Runtime) formatted `mm/dd/yy` and `hh:mm:ss`. `// source: COCRDSLC.cbl:430-449`
- **HANDLE ABEND / ABEND-ROUTINE:** map to a try/catch around the turn that renders `ABEND-DATA` text and ends the transaction with abend code `9999`. `// source: COCRDSLC.cbl:250-252, 857-878`

---

## 11. OPEN QUESTIONS / RISKS

- **CARD key padding/collation:** the card-number key is an exact 16-char digit string in all observed paths (`CARDSIDI X(16)` and `CDEMO-CARD-NUM 9(16)` → `CC-CARD-NUM-N`). Confirm the seeded `CARD.card_num` is stored as the same 16-char form so the equality read matches (per ARCHITECTURE.md store EXACT n chars). Low risk (equality, not range).
- **`'COCRDSL '` vs `'COCRDLI'` mapset literals:** the list-program mapset literal in this program is `LIT-CCLISTMAPSET = 'COCRDLI'` (7), while COCRDLIC's own `LIT-THISMAPSET` is also `'COCRDLI'`; the "came from list" attribute branch relies on COCRDLIC having set `CDEMO-LAST-MAPSET='COCRDLI'`. Verify cross-program against the COCRDLIC spec (it sets LAST-MAPSET on XCTL). `// source: COCRDSLC.cbl:175-176, 505, 527`
- **Whether the "ignore account on read" behavior is intended** is unknowable from source; treated as a faithful bug (§9.1) and pinned, not fixed.
- No DB2/IMS/MQ involvement; no JCL step (online only).
