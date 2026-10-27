/// <summary>Assertions for the console test runner: each failure names what broke.</summary>
static class Check
{
    internal static void That(bool condition, string failure)
    {
        if (!condition) throw new Exception(failure);
    }

    internal static void Throws<T>(Action action, string failure) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(failure);
    }
}
