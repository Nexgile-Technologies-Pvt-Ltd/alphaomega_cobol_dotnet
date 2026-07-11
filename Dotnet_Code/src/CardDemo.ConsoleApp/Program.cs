using CardDemo.ConsoleApp;

// Entry point for the CardDemo console host. CICS would START transaction CC00 (the sign-on) at the
// terminal; here the host primes the pseudo-conversational dispatcher with that TRANSID and a cold-start
// (null) COMMAREA, then runs the SEND/RECEIVE loop until a handler issues a terminal RETURN (logoff) or
// the operator disconnects. The start transaction can be overridden by the first command-line argument.

string startTransId = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0].Trim() : "CC00";

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch
{
    // Some redirected/dumb terminals reject encoding changes; ignore and carry on.
}

using var host = new CardDemoConsoleHost();
host.Run(startTransId);

// Restore a usable terminal on exit (the host hides/colours the cursor during rendering).
try
{
    Console.ResetColor();
    if (!Console.IsOutputRedirected) Console.CursorVisible = true;
    Console.WriteLine();
}
catch
{
    // best-effort cleanup
}
