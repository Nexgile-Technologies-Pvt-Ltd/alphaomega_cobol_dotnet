namespace CardDemo.Parity.Tests;

/// <summary>
/// Small GnuCOBOL helper programs used only by the oracle: loaders that build indexed (BDB) files from
/// fixed-length sequential input (the IDCAMS REPRO equivalent), and a driver that calls CBACT04C with
/// the JCL PARM. They reference only byte-range keys (PIC X), so ordering matches the real program's
/// keys without depending on the production copybooks.
/// </summary>
internal static class OracleCobolFixtures
{
    // Each loader reads ASSIGN LDIN (sequential) and writes an INDEXED file under its DD name.
    public const string LoadTcat = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. LOADTCAT.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO LDIN
                  ORGANIZATION SEQUENTIAL.
           SELECT OUT-F ASSIGN TO TCATBALF
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS O-KEY.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC PIC X(50).
       FD OUT-F.
       01 OUT-REC.
          05 O-KEY  PIC X(17).
          05 FILLER PIC X(33).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    public const string LoadXref = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. LOADXREF.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO LDIN
                  ORGANIZATION SEQUENTIAL.
           SELECT OUT-F ASSIGN TO XREFFILE
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS O-PK
                  ALTERNATE RECORD KEY IS O-AK WITH DUPLICATES.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC PIC X(50).
       FD OUT-F.
       01 OUT-REC.
          05 O-PK    PIC X(16).
          05 FILLER  PIC X(09).
          05 O-AK    PIC X(11).
          05 FILLER  PIC X(14).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    public const string LoadAcct = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. LOADACCT.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO LDIN
                  ORGANIZATION SEQUENTIAL.
           SELECT OUT-F ASSIGN TO ACCTFILE
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS O-KEY.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC PIC X(300).
       FD OUT-F.
       01 OUT-REC.
          05 O-KEY  PIC X(11).
          05 FILLER PIC X(289).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    public const string LoadDisc = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. LOADDISC.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO LDIN
                  ORGANIZATION SEQUENTIAL.
           SELECT OUT-F ASSIGN TO DISCGRP
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS O-KEY.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC PIC X(50).
       FD OUT-F.
       01 OUT-REC.
          05 O-KEY  PIC X(16).
          05 FILLER PIC X(34).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    public const string LoadCard = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. LOADCARD.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO LDIN
                  ORGANIZATION SEQUENTIAL.
           SELECT OUT-F ASSIGN TO CARDFILE
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS O-KEY.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC PIC X(150).
       FD OUT-F.
       01 OUT-REC.
          05 O-KEY  PIC X(16).
          05 FILLER PIC X(134).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    // Unloads the INDEXED TRANSACT file to a fixed sequential file in key order (for comparison).
    public const string UnloadTran = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. UNLDTRAN.
       ENVIRONMENT DIVISION.
       INPUT-OUTPUT SECTION.
       FILE-CONTROL.
           SELECT IN-F  ASSIGN TO TRANFILE
                  ORGANIZATION INDEXED ACCESS SEQUENTIAL
                  RECORD KEY IS R-KEY.
           SELECT OUT-F ASSIGN TO UNLOAD
                  ORGANIZATION SEQUENTIAL.
       DATA DIVISION.
       FILE SECTION.
       FD IN-F.
       01 IN-REC.
          05 R-KEY  PIC X(16).
          05 FILLER PIC X(334).
       FD OUT-F.
       01 OUT-REC PIC X(350).
       WORKING-STORAGE SECTION.
       01 WS-EOF PIC X VALUE 'N'.
       PROCEDURE DIVISION.
           OPEN INPUT IN-F
           OPEN OUTPUT OUT-F
           PERFORM UNTIL WS-EOF = 'Y'
              READ IN-F NEXT AT END MOVE 'Y' TO WS-EOF
                NOT AT END MOVE IN-REC TO OUT-REC WRITE OUT-REC
              END-READ
           END-PERFORM
           CLOSE IN-F OUT-F
           STOP RUN.
";

    // Driver: invokes CBACT04C exactly as JCL EXEC PGM=CBACT04C,PARM='2022071800' would.
    public const string RunB04 = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. RUNB04.
       DATA DIVISION.
       WORKING-STORAGE SECTION.
       01 PARMS.
          05 P-LEN  PIC S9(4) COMP VALUE 10.
          05 P-DATE PIC X(10) VALUE '2022071800'.
       PROCEDURE DIVISION.
           CALL 'CBACT04C' USING PARMS
           STOP RUN.
";
}
