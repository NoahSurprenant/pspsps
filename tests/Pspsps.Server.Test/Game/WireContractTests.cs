using System.Reflection;
using Orleans;
using Orleans.CodeGeneration;
using Pspsps.Server.Game;

namespace Pspsps.Server.Test.Game;

/// <summary>
/// What pods say to each other, against wire-contracts.txt, the record of every version of it so far: during a rolling
/// update old and new pods run side by side, and a grain call or a message one makes, the other must read
/// (docs/architecture.md, "Changing what pods say to each other").
/// <list type="bullet">
/// <item>A grain interface's methods are exactly those recorded for its [Version]: a change without a new version fails.</item>
/// <item>A new version keeps every method of the last one (marking one [Obsolete] first, removing it a version later).</item>
/// <item>A [GenerateSerializer] type's Id numbers keep their member's type and name for good, even once removed.</item>
/// </list>
/// A failure prints the record as it would be with today's code: after a deliberate change, copy the new lines in.
/// </summary>
public sealed class WireContractTests
{
    private const string GrainPrefix = "grain ";
    private const string TypePrefix = "type ";

    private static readonly Assembly Server = typeof(IRoomGrain).Assembly;

    private static readonly string RecordPath = Path.Combine(
        AppContext.BaseDirectory, "Game", "wire-contracts.txt");

    [Test]
    public void EveryGrainInterface_MatchesTheMethodsRecordedForItsVersion()
    {
        var recorded = Recorded();
        var problems = new List<string>();
        foreach (var grain in GrainInterfaces())
        {
            var version = VersionOf(grain);
            var methods = Methods(grain);
            var key = $"{grain.Name} {version}";
            if (!recorded.Grains.TryGetValue(key, out var known))
            {
                problems.Add($"{grain.Name} version {version} isn't recorded: add its lines, keeping the earlier versions'.");
                continue;
            }

            if (!known.SetEquals(methods))
            {
                problems.Add($"{grain.Name} changed without a new [Version] (it is {version}): raise it and record the new " +
                    $"version. Recorded: [{string.Join("; ", known.Order(StringComparer.Ordinal))}]; now: " +
                    $"[{string.Join("; ", methods.Order(StringComparer.Ordinal))}].");
            }
        }

        Assert.That(problems, Is.Empty, Advice(recorded));
    }

    [Test]
    public void ANewGrainInterfaceVersion_KeepsEveryMethodOfTheLastOne_ThatWasntObsolete()
    {
        var recorded = Recorded();
        var problems = new List<string>();
        foreach (var grain in GrainInterfaces())
        {
            var version = VersionOf(grain);
            var earlier = recorded.Grains.Keys
                .Select(key => key.Split(' '))
                .Where(parts => parts[0] == grain.Name && int.Parse(parts[1]) < version)
                .Select(parts => int.Parse(parts[1]))
                .DefaultIfEmpty(-1)
                .Max();
            if (earlier < 0)
            {
                continue;
            }

            var now = Methods(grain).Select(WithoutObsolete).ToHashSet();
            foreach (var method in recorded.Grains[$"{grain.Name} {earlier}"].Where(m => !m.StartsWith("[Obsolete] ", StringComparison.Ordinal)))
            {
                if (!now.Contains(method))
                {
                    problems.Add($"{grain.Name} version {version} dropped or changed '{method}' of version {earlier}: " +
                        "keep it (a pod still on the old version calls it), mark it [Obsolete], and remove it a version later.");
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void EverySerializedMember_KeepsTheTypeAndNameRecordedForItsId()
    {
        var recorded = Recorded();
        var problems = new List<string>();
        foreach (var (type, id, member) in SerializedMembers())
        {
            if (!recorded.Types.TryGetValue($"{type} {id}", out var known))
            {
                problems.Add($"{type} Id({id}) ({member}) isn't recorded: add its line.");
            }
            else if (known != member)
            {
                problems.Add($"{type} Id({id}) was '{known}' and is '{member}' now: an Id is never reused. Give the member a " +
                    "new Id, and keep the old line.");
            }
        }

        Assert.That(problems, Is.Empty, Advice(recorded));
    }

    [Test]
    public void TheRecord_KnowsTheGrainInterfaces()
    {
        // The reflection finds what the record is about: a test that checks nothing passes for the wrong reason.
        Assert.Multiple(() =>
        {
            Assert.That(GrainInterfaces().Select(g => g.Name), Is.SupersetOf(new[] { "IRoomGrain", "IWorldGrain", "IPlayerSessionGrain" }));
            Assert.That(SerializedMembers().Select(m => m.Type), Does.Contain("PlayerCard"));
        });
    }

    [Test]
    public void ASignature_ShowsWhatTheWireCarries()
    {
        var join = typeof(IRoomGrain).GetMethod(nameof(IRoomGrain.Join))!;
        var renew = typeof(IRoomGrain).GetMethod(nameof(IRoomGrain.Renew))!;

        Assert.Multiple(() =>
        {
            Assert.That(Signature(join), Is.EqualTo("Task<RoomSnapshot> Join(PlayerCard player, String session, String throughDoor)"));
            Assert.That(Signature(renew), Is.EqualTo("Task<IReadOnlyList<String>> Renew(IReadOnlyList<PlayerSession> sessions)"));
        });
    }

    private static List<Type> GrainInterfaces() =>
        [.. Server.GetTypes().Where(t => t.IsInterface && t.IsPublic && typeof(IGrain).IsAssignableFrom(t)).OrderBy(t => t.Name, StringComparer.Ordinal)];

    private static int VersionOf(Type grain) => grain.GetCustomAttribute<VersionAttribute>()?.Version ?? 0;

    private static HashSet<string> Methods(Type grain) => [.. grain.GetMethods().Select(Signature)];

    private static string Signature(MethodInfo method) =>
        (method.GetCustomAttribute<ObsoleteAttribute>() is null ? "" : "[Obsolete] ") +
        $"{NameOf(method.ReturnType)} {method.Name}(" +
        string.Join(", ", method.GetParameters().Select(p => $"{NameOf(p.ParameterType)} {p.Name}")) + ")";

    private static string WithoutObsolete(string signature) =>
        signature.StartsWith("[Obsolete] ", StringComparison.Ordinal) ? signature["[Obsolete] ".Length..] : signature;

    private static string NameOf(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return NameOf(inner) + "?";
        }

        if (type.IsArray)
        {
            return NameOf(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(NameOf))}>";
    }

    private static List<(string Type, int Id, string Member)> SerializedMembers()
    {
        var members = new List<(string, int, string)>();
        foreach (var type in Server.GetTypes().Where(t => t.GetCustomAttribute<GenerateSerializerAttribute>() is not null))
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var property in type.GetProperties(all))
            {
                if (property.GetCustomAttribute<IdAttribute>() is { } id)
                {
                    members.Add((type.Name, (int)id.Id, $"{NameOf(property.PropertyType)} {property.Name}"));
                }
            }

            foreach (var field in type.GetFields(all))
            {
                if (field.GetCustomAttribute<IdAttribute>() is { } id)
                {
                    members.Add((type.Name, (int)id.Id, $"{NameOf(field.FieldType)} {field.Name}"));
                }
            }
        }

        return members;
    }

    // The record: "grain <Interface> <version>: <signature>" and "type <Type> <id>: <member type> <name>" lines.
    private static Record Recorded()
    {
        var grains = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(RecordPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            Assert.That(colon, Is.GreaterThan(0), $"wire-contracts.txt: '{line}' has no ': '");
            var key = line[..colon];
            var value = line[(colon + 2)..];
            if (key.StartsWith(GrainPrefix, StringComparison.Ordinal))
            {
                var grain = key[GrainPrefix.Length..];
                if (!grains.TryGetValue(grain, out var methods))
                {
                    grains[grain] = methods = new HashSet<string>(StringComparer.Ordinal);
                }

                methods.Add(value);
            }
            else if (key.StartsWith(TypePrefix, StringComparison.Ordinal))
            {
                Assert.That(types.TryAdd(key[TypePrefix.Length..], value), Is.True, $"wire-contracts.txt: '{key}' twice");
            }
            else
            {
                Assert.Fail($"wire-contracts.txt: '{line}' is neither a grain nor a type line");
            }
        }

        return new Record(grains, types);
    }

    // The record with today's code added (nothing removed: old versions and retired Ids stay), for copying in.
    private static string Advice(Record recorded)
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (grain, methods) in recorded.Grains)
        {
            lines.UnionWith(methods.Select(m => $"{GrainPrefix}{grain}: {m}"));
        }

        foreach (var (key, member) in recorded.Types)
        {
            lines.Add($"{TypePrefix}{key}: {member}");
        }

        foreach (var grain in GrainInterfaces())
        {
            lines.UnionWith(Methods(grain).Select(m => $"{GrainPrefix}{grain.Name} {VersionOf(grain)}: {m}"));
        }

        foreach (var (type, id, member) in SerializedMembers())
        {
            lines.Add($"{TypePrefix}{type} {id}: {member}");
        }

        return "After a deliberate, compatible change, tests/Pspsps.Server.Test/Game/wire-contracts.txt would read " +
            "(remove a line only where it replaces one for the same key):\n" + string.Join('\n', lines);
    }

    private sealed record Record(Dictionary<string, HashSet<string>> Grains, Dictionary<string, string> Types);
}
