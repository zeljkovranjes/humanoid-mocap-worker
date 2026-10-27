using System.Runtime.InteropServices;
using HumanoidMocapWorker;

// Exit codes: 0 done, 1 failed, 2 bad command line, 130 cancelled by SIGINT/SIGTERM.
Options options;
try { options = Options.Parse(args); }
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(Options.Usage);
    return 2;
}
using var cancellation = new CancellationTokenSource();
using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancel);
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Cancel);
try { return MainLoop.Run(() => Cmd.RunAsync(options, cancellation.Token)); }
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine("error: " + error);
    return 1;
}

void Cancel(PosixSignalContext context)
{
    context.Cancel = true;
    cancellation.Cancel();
}
