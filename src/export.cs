using System.Text;
using System.Text.Json;
using HumanoidMocapX;

namespace HumanoidMocapWorker;

/// <summary>Writes a captured motion the way the editor's clip check does: the .hmotion
/// document, the raw FlowHMR motion and Human/Citizen FBX retargets.</summary>
internal static class MotionExport
{
    /// <summary>Writes capture.hmotion, flow-motion.json, human.fbx and citizen.fbx into
    /// directory. Throws when retargeting fails or an FBX does not round-trip.</summary>
    internal static async Task WriteAsync(FlowMotion motion, string sourceVideo, string rigDirectory, string directory,
        CancellationToken cancellation)
    {
        motion.Video = sourceVideo; motion.Name = Path.GetFileNameWithoutExtension(sourceVideo);
        var bytes = Encoding.UTF8.GetBytes(motion.ToDocument().ToJson());
        await File.WriteAllBytesAsync(Path.Combine(directory, "capture.hmotion"), bytes, cancellation);
        await File.WriteAllTextAsync(Path.Combine(directory, "flow-motion.json"), JsonSerializer.Serialize(motion), cancellation);
        foreach (var citizen in new[] { false, true })
        {
            cancellation.ThrowIfCancellationRequested();
            var rig = await File.ReadAllTextAsync(Path.Combine(rigDirectory, citizen ? "target_rig_sbox_citizen.json" : "target_rig_sbox.json"), cancellation);
            var fbx = Retarget(bytes, citizen ? RetargetTargetSpec.SboxCitizen(rig) : RetargetTargetSpec.SboxDefault(rig), motion.Name);
            await File.WriteAllBytesAsync(Path.Combine(directory, citizen ? "citizen.fbx" : "human.fbx"), fbx, cancellation);
        }
    }

    static byte[] Retarget(byte[] document, RetargetTargetSpec target, string name)
    {
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = document,
            SourceFileName = "capture.hmotion",
            FootPlantCleanup = true,
            MocapCorrections = new TargetCorrectionSettings { FirstPerson = false, StabilizeFeet = true },
        }, target);
        if (!result.Success) throw new InvalidDataException("Retarget failed: " + string.Join("; ", result.Errors.Concat(result.Clips.Select(c => c.Error))));
        var clip = result.Clips.Single();
        foreach (var poses in clip.SolvedFrames)
            foreach (var pose in poses)
                if (!float.IsFinite(pose.Pos.Length()) || !float.IsFinite(pose.Rot.Length())) throw new InvalidDataException("Retarget produced a non-finite pose.");
        var times = Enumerable.Range(0, clip.SolvedFrames.Count).Select(i => i / (double)clip.Fps).ToArray();
        var exported = ModelDocFbxExport.Write(target.Rig.Skeleton, name, clip.SolvedFrames, times, clip.Fps, target.UpAxis, "WorldRelative");
        var roundTrip = FbxImporter.Import(exported);
        if (roundTrip.Clips.Count != 1 || roundTrip.Clips[0].Frames.Count != clip.SolvedFrames.Count)
            throw new InvalidDataException("The exported FBX lost animation frames.");
        return exported;
    }
}
