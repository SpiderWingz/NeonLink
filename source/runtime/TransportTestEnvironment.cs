// Compile only into RuntimeHarness.exe. DirectTests exercises the real invite
// and mapping planner code with a fake router. Any accidental access to the
// launcher's real settings or persisted router mapping must fail the fixture.
using System;
namespace NeonLink
{
    static class Core
    {
        public static string DataRoot { get { throw new InvalidOperationException("Native fixtures cannot access launcher settings."); } }
        public static T ReadXml<T>(string path) { throw new InvalidOperationException("Native fixtures cannot read persisted launcher state."); }
    }
}
