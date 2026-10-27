using System.Globalization;
using HumanoidMocapX;

namespace HumanoidMocapWorker;

/// <summary>Parsed command line: a command, its positional arguments and --name value options.</summary>
internal sealed class Options
{
    internal const string Usage = """
        usage:
          humanoid-mocap-worker capture <video> <output.hmotion> [--start S] [--end S] [--model base|latest]
                                [--camera-interval 1|10|30] [--lanes 0-6] [--models DIR] [--cache DIR]
          humanoid-mocap-worker fixture <fixture.json> <output-directory>
          humanoid-mocap-worker decode <video> <output.rgba> [--start S] [--end S] [--cache DIR]
          humanoid-mocap-worker probe <video>
          humanoid-mocap-worker models [--models DIR]
        """;
    static readonly string[] Commands = ["capture", "fixture", "decode", "probe", "models"];
    static readonly Dictionary<string, int> Arity = new() { ["capture"] = 2, ["fixture"] = 2, ["decode"] = 2, ["probe"] = 1, ["models"] = 0 };
    readonly Dictionary<string, string> named;

    internal string Command { get; }
    internal string[] Positional { get; }

    Options(string command, string[] positional, Dictionary<string, string> named)
    {
        Command = command; Positional = positional; this.named = named;
    }

    /// <summary>Throws ArgumentException describing the first malformed argument.</summary>
    internal static Options Parse(string[] args)
    {
        if (args.Length == 0 || !Commands.Contains(args[0])) throw new ArgumentException("Expected a command.");
        var positional = new List<string>(); var named = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) { positional.Add(args[index]); continue; }
            if (index + 1 >= args.Length) throw new ArgumentException($"Option {args[index]} needs a value.");
            if (!named.TryAdd(args[index][2..], args[++index])) throw new ArgumentException($"Option {args[index - 1]} is repeated.");
        }
        if (positional.Count != Arity[args[0]]) throw new ArgumentException($"'{args[0]}' takes {Arity[args[0]]} arguments.");
        return new Options(args[0], positional.ToArray(), named);
    }

    internal double Start => Number("start") ?? 0;
    internal double? End => Number("end");
    internal int CameraInterval => Integer("camera-interval") ?? 10;
    internal int Lanes => Integer("lanes") ?? CaptureLanes.Auto;
    internal string ModelDirectory => named.GetValueOrDefault("models")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HumanoidMocapX", "models");
    internal string CacheDirectory => named.GetValueOrDefault("cache")
        ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache ? cache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "humanoid-mocap-worker");

    internal FlowModel Model => named.GetValueOrDefault("model") switch
    {
        null or "latest" => FlowModel.Latest,
        "base" => FlowModel.Base,
        var other => throw new ArgumentException($"Unknown model '{other}'; use base or latest."),
    };

    /// <summary>Rejects options the command does not take.</summary>
    internal void Allow(params string[] names)
    {
        foreach (var name in named.Keys)
            if (!names.Contains(name)) throw new ArgumentException($"'{Command}' does not take --{name}.");
    }

    double? Number(string name) => named.TryGetValue(name, out var text)
        ? double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value
            : throw new ArgumentException($"--{name} must be a number of seconds.")
        : null;

    int? Integer(string name) => named.TryGetValue(name, out var text)
        ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value
            : throw new ArgumentException($"--{name} must be an integer.")
        : null;
}
