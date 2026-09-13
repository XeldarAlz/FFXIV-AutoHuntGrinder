using System.Buffers;
using System.Text;
using System.Text.Json;

namespace AutoHuntGrinder.Core.Ipc;

// AutoTarget stays Aggressive with every category off and Retarget NoTarget. The combat plugin ranks a mob that is not
// fighting the party below zero and Aggressive only picks from zero up, so it only ever takes an attacker, and only while
// the character has no target: the targeted mark keeps focus, and whatever is still attacking takes over once it is dead.
// Every rotation uses the player's target and a single-target rotation with no multi-target actions, so an area attack
// cannot pull a mob standing next to the mark. FATE targeting and FATE sync stay off; a FATE-bound hunt syncs itself.
// Pathfind movement lets the combat plugin close to range and dodge once the fight is on.
internal static class HuntCombatPreset
{
    public const string Name = "Auto Hunt Grinder";

    public const int Revision = 1;

    private const string AutoTargetModule = "BossMod.Autorotation.MiscAI.AutoTarget";
    private const string NormalMovementModule = "BossMod.Autorotation.MiscAI.NormalMovement";
    private const string WarriorModule = "BossMod.Autorotation.VeynWAR";
    private const string RotationModulePrefix = "BossMod.Autorotation.xan.";

    private static readonly string[] rotationJobs =
    [
        "AST", "BLM", "BRD", "BST", "DNC", "DRG", "DRK", "GNB", "MCH", "MNK", "NIN",
        "PCT", "PLD", "RDM", "RPR", "SAM", "SCH", "SGE", "SMN", "VPR", "WHM",
    ];

    private static readonly string[] roleModules = ["Caster", "HealerAI", "MeleeAI", "RangedAI", "TankAI"];

    private static readonly PresetTrack[] autoTargetTracks =
    [
        new("General", "Aggressive"),
        new("Retarget", "NoTarget"),
        new("FATE", "Disabled"),
    ];

    private static readonly PresetTrack[] movementTracks = [new("Destination", "Pathfind")];
    private static readonly PresetTrack[] warriorTracks = [new("AOE", "SingleTarget")];
    private static readonly PresetTrack[] rotationTracks = [new("Targeting", "Manual"), new("AOE", "ForceST")];

    private static string? serialized;

    public static string GetSerialized() => serialized ??= Serialize();

    private static string Serialize()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("Name", Name);
            writer.WriteStartObject("Modules");
            WriteModule(writer, AutoTargetModule, autoTargetTracks);
            WriteModule(writer, NormalMovementModule, movementTracks);
            WriteModule(writer, WarriorModule, warriorTracks);
            for (var jobIndex = 0; jobIndex < rotationJobs.Length; jobIndex++)
            {
                WriteModule(writer, RotationModulePrefix + rotationJobs[jobIndex], rotationTracks);
            }

            for (var roleIndex = 0; roleIndex < roleModules.Length; roleIndex++)
            {
                WriteModule(writer, RotationModulePrefix + roleModules[roleIndex], []);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteModule(Utf8JsonWriter writer, string module, ReadOnlySpan<PresetTrack> tracks)
    {
        writer.WriteStartArray(module);
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            writer.WriteStartObject();
            writer.WriteString("Track", tracks[trackIndex].Track);
            writer.WriteString("Option", tracks[trackIndex].Option);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private readonly record struct PresetTrack(string Track, string Option);
}
