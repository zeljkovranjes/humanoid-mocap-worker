// Runs every test group; GPU and FFmpeg groups report SKIP when the machine lacks them.
var groups = new (string Name, Func<Task> Run)[]
{
    ("options", OptionsTests.Run),
    ("attributes", AttributeTests.Run),
    ("shaders", ShaderTests.Run),
    ("fixture", FixtureTests.Run),
    ("video", VideoTests.RunAsync),
    ("gpu", GpuTests.Run),
};
var failed = 0;
foreach (var (name, run) in groups)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (SkipException skip) { Console.WriteLine($"SKIP {name}: {skip.Message}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
}
return failed == 0 ? 0 : 1;

sealed class SkipException(string reason) : Exception(reason);
