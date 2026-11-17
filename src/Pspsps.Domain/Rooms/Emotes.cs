namespace Pspsps.Domain.Rooms;

/// <summary>The emotes a cat can do, by the names the browser sends, and which of them are poses held until it walks.</summary>
public static class Emotes
{
    public static IReadOnlyList<string> All { get; } = ["sit", "meow", "wave", "sleep"];

    private static readonly Dictionary<string, CatPose> Poses = new(StringComparer.Ordinal)
    {
        ["sit"] = CatPose.Sitting,
        ["sleep"] = CatPose.Sleeping,
    };

    public static bool IsKnown(string? emote) => emote is not null && All.Contains(emote, StringComparer.Ordinal);

    public static bool IsPose(string emote) => Poses.ContainsKey(emote);

    /// <summary>The pose <paramref name="emote"/> leaves the cat in, or null when it doesn't change the pose.</summary>
    public static CatPose? PoseOf(string emote) => Poses.TryGetValue(emote, out var pose) ? pose : null;

    /// <summary>The emote that holds <paramref name="pose"/>, or null for standing.</summary>
    public static string? NameOf(CatPose pose) => Poses.FirstOrDefault(p => p.Value == pose).Key;
}
