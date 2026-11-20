using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly RoomLayout Layout = RoomLayout.Of(RoomCatalog.Find("garden")!);

    [Test]
    public void ADraft_CanBeEditedAndThenPublished_AndThenOnlyReplaced()
    {
        var draft = RoomVersion.Draft("garden", 2, Layout, Guid.NewGuid(), Now);
        var edited = Layout with { Name = "The Garden" };

        draft.Edit(edited);
        draft.Publish(Now.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(draft.State, Is.EqualTo(RoomVersionState.Published));
            Assert.That(draft.Layout, Is.EqualTo(edited));
            Assert.That(draft.PublishedAt, Is.EqualTo(Now.AddHours(1)));
            Assert.That(() => draft.Edit(Layout), Throws.TypeOf<DomainException>());
            Assert.That(() => draft.Publish(Now), Throws.TypeOf<DomainException>());
        });

        draft.Archive();

        Assert.Multiple(() =>
        {
            Assert.That(draft.State, Is.EqualTo(RoomVersionState.Archived));
            Assert.That(() => draft.Archive(), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void ADraftThatWasNeverPublished_CannotBeArchived() =>
        Assert.That(() => RoomVersion.Draft("garden", 1, Layout, null, Now).Archive(), Throws.TypeOf<DomainException>());

    [Test]
    public void GoingBack_IsANewDraftWithAnOldLayout()
    {
        var old = RoomVersion.Published("garden", Layout, Now);
        old.Archive();

        var back = old.DraftFrom(3, null, Now.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(back.Id, Is.Not.EqualTo(old.Id));
            Assert.That((back.RoomId, back.Number, back.State, back.Layout), Is.EqualTo(("garden", 3, RoomVersionState.Draft, Layout)));
            Assert.That(old.State, Is.EqualTo(RoomVersionState.Archived), "the old version stays as it was");
        });
    }

    [Test]
    public void ADraft_CountsItsEdits_SoASaveOverSomeoneElsesCanBeSpotted()
    {
        var draft = RoomVersion.Draft("garden", 2, Layout, null, Now);
        Assert.That(draft.Revision, Is.EqualTo(1));

        draft.Edit(Layout with { Name = "A" });
        draft.Edit(Layout with { Name = "B" });

        Assert.That(draft.Revision, Is.EqualTo(3));
    }

    [Test]
    public void AnAuditEntry_NeedsAnActionAndATarget()
    {
        var entry = RoomAuditEntry.Of(Guid.NewGuid(), "published", "garden", "version 2", Now);

        Assert.Multiple(() =>
        {
            Assert.That((entry.Action, entry.Target, entry.Detail, entry.At), Is.EqualTo(("published", "garden", "version 2", Now)));
            Assert.That(() => RoomAuditEntry.Of(Guid.NewGuid(), " ", "garden", "", Now), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => RoomAuditEntry.Of(Guid.NewGuid(), "published", " ", "", Now), Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void AVersion_NeedsARoomAndANumberFromOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => RoomVersion.Draft(" ", 1, Layout, null, Now), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => RoomVersion.Draft("garden", 0, Layout, null, Now), Throws.TypeOf<DomainException>());
        });
    }
}
