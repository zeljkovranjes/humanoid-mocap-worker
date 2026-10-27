using System.Buffers.Binary;
using HumanoidMocapWorker;
using Sandbox;

static class AttributeTests
{
    internal static Task Run()
    {
        var block = new ConstantBlock(0, 12, [new("Rows", 0, "uint"), new("Epsilon", 4, "float"), new("Scale", 8, "float")]);
        var attributes = new RenderAttributes();
        attributes.Set("Rows", 7); attributes.Set("Epsilon", 1e-5f); attributes.Set("Scale", 3);
        var bytes = new byte[12]; Array.Fill(bytes, (byte)0xff);
        attributes.WriteConstants(block, bytes, "test");
        Check.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 7, "A uint attribute must keep its bits");
        Check.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4)) == 1e-5f, "A float attribute must keep its bits");
        Check.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(8)) == 3f, "An int must set a float member by conversion");
        attributes.Set("Rows", -1);
        attributes.WriteConstants(block, bytes, "test");
        Check.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes) == uint.MaxValue, "Later values must replace earlier ones");
        var unset = new byte[12]; Array.Fill(unset, (byte)0xff);
        new RenderAttributes().WriteConstants(block, unset, "test");
        Check.That(unset.All(value => value == 0), "Unset scalars must read as zero, as in the engine");
        var wrong = new RenderAttributes(); wrong.Set("Rows", 1.5f);
        Check.Throws<InvalidOperationException>(() => wrong.WriteConstants(block, new byte[12], "test"), "A float must not set a uint member");
        Check.Throws<InvalidOperationException>(() => new RenderAttributes().Buffer("Input", "test"), "A missing buffer must fail the dispatch");
        return Task.CompletedTask;
    }
}
