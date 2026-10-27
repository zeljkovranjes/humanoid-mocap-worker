using HumanoidMocapWorker;
using HumanoidMocapX;

static class OptionsTests
{
    internal static Task Run()
    {
        var capture = Options.Parse(["capture", "in.mp4", "out.hmotion", "--start", "1.5", "--end", "4", "--model", "base",
            "--camera-interval", "30", "--lanes", "2"]);
        Check.That(capture.Command == "capture" && capture.Positional.SequenceEqual(["in.mp4", "out.hmotion"]), "Positional arguments were lost");
        Check.That(capture.Start == 1.5 && capture.End == 4 && capture.Model == FlowModel.Base && capture.CameraInterval == 30
            && capture.Lanes == 2, "Named values were misread");
        var defaults = Options.Parse(["capture", "a", "b"]);
        Check.That(defaults.Start == 0 && defaults.End is null && defaults.Model == FlowModel.Latest && defaults.CameraInterval == 10
            && defaults.Lanes == CaptureLanes.Auto, "Defaults must match the editor's capture defaults");
        Check.That(Options.Parse(["probe", "in.mp4"]).Positional.SequenceEqual(["in.mp4"]), "probe takes one video");
        Check.Throws<ArgumentException>(() => Options.Parse([]), "A missing command must be rejected");
        Check.Throws<ArgumentException>(() => Options.Parse(["capture", "a"]), "A missing argument must be rejected");
        Check.Throws<ArgumentException>(() => Options.Parse(["capture", "a", "b", "--start"]), "An option without a value must be rejected");
        Check.Throws<ArgumentException>(() => Options.Parse(["capture", "a", "b", "--start", "1", "--start", "2"]), "Repeated options must be rejected");
        Check.Throws<ArgumentException>(() => _ = Options.Parse(["capture", "a", "b", "--start", "soon"]).Start, "Non-numeric times must be rejected");
        Check.Throws<ArgumentException>(() => _ = Options.Parse(["capture", "a", "b", "--end", "NaN"]).End, "Non-finite times must be rejected");
        Check.Throws<ArgumentException>(() => _ = Options.Parse(["capture", "a", "b", "--model", "huge"]).Model, "Unknown models must be rejected");
        Check.Throws<ArgumentException>(() => Options.Parse(["fixture", "a", "b", "--lanes", "2"]).Allow(), "Options a command does not take must be rejected");
        return Task.CompletedTask;
    }
}
