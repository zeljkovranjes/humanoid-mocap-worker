Worker design
=============

## Same results as the editor

The worker compiles the libraries' C# files
unchanged, against [`src/sandbox`], a small implementation of the s&box
API they use (`ComputeShader`, `GpuBuffer`, `Graphics`, `GameTask`,
`TensorBatch`). It dispatches the exact SPIR-V the editor runs: s&box
compiles shaders with Slang into `.shader_c` files, and [`codegen`]
extracts those modules into [`src/gen`]. Buffers and constants are bound by
the same declaration-order slots the engine uses. On the five-second
validation clip, all eight stage outputs (boxes, camera rotations and
intrinsics, body features, FlowHMR motion, `.hmotion`, Human and Citizen
FBX) are byte-identical to the editor's.

```console
$ humanoid-mocap-worker fixture fixture.json out
{
  "Gpu": "AMD Radeon RX 9070 (RADV GFX1201)",
  "TotalSeconds": 152.3848299,
  "StageSeconds": { "Detection": 7.22, "Camera": 14.46, "Features": 124.47, "Generation": 4.54, "Export": 1.30 },
  ...
}
```

[`src/sandbox`]: ../src/sandbox
[`codegen`]: ../codegen/main.cs
[`src/gen`]: ../src/gen

## Shader updates

The build fails when a Tensor Inference `.shader` no
longer matches the source hash recorded in `src/gen/sources.sha256`. Open the
library in the s&box editor once so it recompiles the `.shader_c` files, then
regenerate. This needs an s&box installation; running the worker does not.

```sh
dotnet run --project codegen/ShaderCodegen.csproj -- \
    "$FACEPUNCH_ENGINE" ../../sbox/libraries/humanoid-mocap-x/Assets src/gen
```

## Video decoding

FFmpeg decodes every frame with its exact timestamp,
and the editor's own timeline code keeps the frame displayed at each 30 fps
sample. On the validation clip this reproduces the predecoded fixture
(`fps=30:round=up`) byte for byte. Decoded frames are cached under
`--cache` (default `~/.cache/humanoid-mocap-worker`) for the duration of a
capture; a five-second 1080p range needs 1.2 GB.

## Requirements

Linux with a Vulkan 1.2 GPU that supports
`VK_KHR_push_descriptor`; the first discrete GPU is used. The validation clip
peaked at 5.8 GB of process memory. Identical results were verified against
the Linux editor on the same GPU, driver and .NET runtime. The driver compiles
the SPIR-V and the C runtime evaluates CPU math, so another GPU, driver or
operating system gives close but not identical motion, for the editor too.

## Other checkouts

Every library source the worker compiles is listed in
one item group of `HumanoidMocapWorker.csproj`. Pass `-p:WorkspaceRoot=DIR`
to build against another tree with the same `sbox/` layout.

## Hosting

The worker is a separate process per job, so a driver fault
or a job's peak memory cannot take down the HTTP host, and all memory is
returned when the job ends. A host `IBodyCapture` adapter runs `capture`,
forwards each progress line and kills the process on cancellation; `probe`
prints `{"duration":seconds}` so the host can cap a requested range to the
video before admitting the job.
