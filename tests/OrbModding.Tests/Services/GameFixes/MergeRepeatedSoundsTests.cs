using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using OrbAutomata;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests.Services.GameFixes;

[Collection(GameFixesCollection.Name)]
public sealed class MergeRepeatedSoundsTests : IDisposable
{
    private readonly MergeRepeatedSounds _merge = new();
    private readonly SoundManager? _previousManager = SoundManager.instance;

    public MergeRepeatedSoundsTests()
    {
        SoundManager.instance = new SoundManager { audioElement = new AudioElement { name = "Audio Element" } };
        Time.frameCount = 100;
        _merge.Apply(true);
    }

    public void Dispose()
    {
        _merge.Apply(false);
        SoundManager.instance = _previousManager;
    }

    [Fact]
    public void ARepeatedClipInOneFrameGetsTheSilentOverflowElementAndIsCounted()
    {
        var clip = new AudioClip();
        var before = _merge.Fired;

        Assert.True(Play(clip, out _));
        Assert.False(Play(clip, out var merged));
        Assert.False(Play(clip, out var mergedAgain));

        Assert.NotNull(merged);
        Assert.Same(merged, mergedAgain);
        Assert.True(SoundOverflowElement.IsOverflow(merged!));
        Assert.Equal(before + 2, _merge.Fired);
    }

    [Fact]
    public void ADifferentClipOrTheNextFramePlays()
    {
        var clip = new AudioClip();

        Assert.True(Play(clip, out _));
        Assert.True(Play(new AudioClip(), out _));
        Time.frameCount++;
        Assert.True(Play(clip, out _));
    }

    [Fact]
    public void SwitchingTheMergeOffAndOnForgetsTheFrame()
    {
        var clip = new AudioClip();
        Assert.True(Play(clip, out _));

        _merge.Apply(false);
        Assert.True(Play(clip, out _));
        _merge.Apply(true);

        Assert.True(Play(clip, out _));
    }

    [Fact]
    public void AMissingClipOrSoundManagerIsLeftToTheGame()
    {
        Assert.True(Play(null, out _));
        Assert.True(Play(null, out _));
        var clip = new AudioClip();
        Assert.True(Play(clip, out _));

        SoundManager.instance = null;

        Assert.True(Play(clip, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, 10, 3)]
    [InlineData(true, 2, 2)]
    [InlineData(false, 10, 10)]
    public void ThePowerBuildLoopPlaysAtMostTheCapWhileTheMergeIsOn(bool active, int fires, int expectedSounds)
    {
        _merge.Apply(active);
        var sfx = new AudioInstance();
        var previous = GlobalVariables.PowerBuildSfx;
        GlobalVariables.PowerBuildSfx = sfx;
        try
        {
            PowerBuildLoop(PowerBuildSoundCap.Transpile)(fires);
        }
        finally
        {
            GlobalVariables.PowerBuildSfx = previous;
        }

        Assert.Equal(expectedSounds, sfx.PlayCalls);
    }

    [Fact]
    public void APowerBuildLoopOfAnotherShapeIsRefused()
    {
        var shapes = new Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>[]
        {
            instructions => Without(instructions, OpCodes.Call),
            instructions => Replaced(instructions, OpCodes.Br, OpCodes.Nop),
            instructions => Replaced(instructions, OpCodes.Ldloc_1, OpCodes.Ldarg_0),
            instructions => Replaced(instructions, OpCodes.Blt, OpCodes.Bne_Un),
        };

        foreach (var shape in shapes)
        {
            var refusal = Assert.Throws<InvalidOperationException>(
                () => PowerBuildLoop(instructions => PowerBuildSoundCap.Transpile(shape(instructions))));
            Assert.StartsWith(PowerBuildSoundCap.Target, refusal.Message);
        }
    }

    private static bool Play(AudioClip? clip, out AudioElement? result)
    {
        AudioElement element = null!;
        var played = MergeRepeatedSounds.PlayOncePerFrame(clip!, ref element);
        result = element;
        return played;
    }

    /// <summary>
    /// The game's power-build loop, <c>for (i = 0; i &lt; local1; i++) GetPowerBuildSfx().Play();</c>,
    /// in the audited shape, run through a transpiler and compiled.
    /// </summary>
    private static Action<int> PowerBuildLoop(
        Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpile)
    {
        var method = new DynamicMethod("CompleteQueuedQuantity", typeof(void), new[] { typeof(int) },
            typeof(MergeRepeatedSoundsTests).Module);
        var il = method.GetILGenerator();
        il.DeclareLocal(typeof(int));
        il.DeclareLocal(typeof(int));
        var body = il.DefineLabel();
        var compare = il.DefineLabel();
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Stloc_1),
            new(OpCodes.Ldc_I4_0),
            new(OpCodes.Stloc_0),
            new(OpCodes.Br, compare),
            new(OpCodes.Call, PowerBuildSoundCap.PowerBuildSfx),
            new(OpCodes.Callvirt, typeof(AudioInstance).GetMethod(nameof(AudioInstance.Play))),
            new(OpCodes.Pop),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldc_I4_1),
            new(OpCodes.Add),
            new(OpCodes.Stloc_0),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldloc_1),
            new(OpCodes.Blt, body),
            new(OpCodes.Ret),
        };
        var marks = new Dictionary<CodeInstruction, Label> { [instructions[5]] = body, [instructions[12]] = compare };
        foreach (var instruction in transpile(instructions))
        {
            if (marks.TryGetValue(instruction, out var label)) il.MarkLabel(label);
            switch (instruction.operand)
            {
                case null: il.Emit(instruction.opcode); break;
                case Label target: il.Emit(instruction.opcode, target); break;
                case System.Reflection.MethodInfo called: il.Emit(instruction.opcode, called); break;
                default: throw new InvalidOperationException("Unexpected operand " + instruction.operand);
            }
        }
        return (Action<int>)method.CreateDelegate(typeof(Action<int>));
    }

    private static IEnumerable<CodeInstruction> Without(IEnumerable<CodeInstruction> instructions, OpCode opcode)
    {
        foreach (var instruction in instructions)
            if (instruction.opcode != opcode) yield return instruction;
    }

    private static IEnumerable<CodeInstruction> Replaced(IEnumerable<CodeInstruction> instructions, OpCode from, OpCode to)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == from)
            {
                instruction.opcode = to;
                if (to == OpCodes.Nop) instruction.operand = null;
            }
            yield return instruction;
        }
    }
}
