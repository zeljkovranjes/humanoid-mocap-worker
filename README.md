Humanoid Mocap Worker
=====================

[<img alt="docs" src="https://img.shields.io/badge/docs-design-66c2a5?style=for-the-badge" height="20">][design]

[design]: docs/design.md

This program provides [`humanoid-mocap-worker`], a standalone Linux capture
worker for [Humanoid Mocap X] that runs the full FlowHMR video pipeline on
Vulkan without the s&box editor.

[`humanoid-mocap-worker`]: src/main.cs
[Humanoid Mocap X]: ../../sbox/libraries/humanoid-mocap-x

```sh
dotnet build -c Release
./bin/Release/net10.0/humanoid-mocap-worker models
./bin/Release/net10.0/humanoid-mocap-worker capture clip.mp4 clip.hmotion
```

The build compiles the Humanoid Mocap X and Tensor Inference sources from
their workspace checkouts; neither the build nor a capture needs s&box. Install
the .NET 10 SDK and FFmpeg (`ffmpeg` and `ffprobe` on `PATH`).

<br>

## Example

```console
$ humanoid-mocap-worker capture clip.mp4 clip.hmotion --start 2 --end 7
{"message":"Verifying installed flowhmr_latest.ckpt…","fraction":null}
{"message":"Tracking person · 1/150","fraction":0}
...
{"message":"Motion is ready","fraction":1}
```

Progress goes to standard output as one JSON object per line, and diagnostics
to standard error. The exit code is 0 when the motion was written, 1 on
failure, 2 for a bad command line and 130 after SIGINT or SIGTERM. The output
is written atomically; a failed capture leaves no file.

```text
capture <video> <output.hmotion> [--start S] [--end S] [--model base|latest]
        [--camera-interval 1|10|30] [--lanes 0-6] [--models DIR] [--cache DIR]
fixture <fixture.json> <output-directory>
decode  <video> <output.rgba> [--start S] [--end S] [--cache DIR]
probe   <video>
models  [--models DIR]
```

`models` downloads and verifies the checkpoints the editor's **Local models**
buttons install, into the same directory
(`~/.local/share/HumanoidMocapX/models`), so a machine that already has them
needs nothing more.

<br>

## Comparison to the in-editor host

The [in-editor host] keeps the s&box editor running on the processing
machine and decodes uploads with Windows Media Foundation. The worker needs
neither the editor nor Windows, and loads its models per job.

[in-editor host]: ../../sbox/libraries/humanoid-mocap-x/docs/editor-host.md

<br>

#### License

<sup>
The worker's own code is licensed under the <a href="LICENSE-MIT">MIT
license</a>. The build includes Humanoid Mocap X model ports with their own
terms (see its THIRD_PARTY_NOTICES.md). FFmpeg is run as a separate program
under its LGPL or GPL build license and is not distributed with the worker.
Model checkpoints keep their publishers' licenses.
</sup>
