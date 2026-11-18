using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

public sealed class ConfiguredAdminsTests
{
    private static readonly Guid Admin = Guid.Parse("0196d5a8-6b1e-7c3a-9f00-1234567890ab");

    [Test]
    public void Parse_TakesPlayerIds_AndSkipsAnythingElse_SayingWhat()
    {
        var skipped = new List<string>();

        var admins = ConfiguredAdmins.Parse(
            [$" {Admin} ", "not-an-id", "", Guid.Empty.ToString(), Admin.ToString().ToUpperInvariant()], skipped.Add);

        Assert.That(admins, Is.EquivalentTo(new[] { Admin }));
        Assert.That(skipped, Is.EqualTo(new[] { "not-an-id", "", Guid.Empty.ToString() }));
    }

    [Test]
    public void Parse_NoList_IsNoAdmins() =>
        Assert.That(ConfiguredAdmins.Parse(null, _ => { }), Is.Empty);

    [Test]
    public void Current_FollowsTheConfiguration_AsItChanges()
    {
        var options = Substitute.For<IOptionsMonitor<ModerationOptions>>();
        options.CurrentValue.Returns(new ModerationOptions());
        var admins = new ConfiguredAdmins(options, NullLogger<ConfiguredAdmins>.Instance);

        Assert.That(admins.Current, Is.Empty, "none by default");

        options.CurrentValue.Returns(new ModerationOptions { AdminPlayerIds = [Admin.ToString()] });
        Assert.That(admins.Current, Is.EquivalentTo(new[] { Admin }));
    }
}
