using System;
using System.Threading;
class CrashFixture
{
    static void Main(string[] args)
    {
        Thread.Sleep(2500);
        // Simulate a native crash's exit status without crashing a game or
        // triggering Windows Error Reporting UI during the integration test.
        Environment.Exit(args.Length > 0 && args[0] == "crash" ? unchecked((int)0xC0000005) : 0);
    }
}
