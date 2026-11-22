using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly RoomLayout Layout = RoomLayout.Of(RoomCatalog.Find("shop")!);

    [Test]
    public void ADraft_CanBeEditedAndThenPublished_AndThenOnlyReplaced()
    {
        var draft = RoomVersion.Draft("shop", 2, Layout, Guid.NewGuid(), Now);
        var edited = Layout with { Name = "The Shop" };

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
        Assert.That(() => RoomVersion.Draft("shop", 1, Layout, null, Now).Archive(), Throws.TypeOf<DomainException>());

    [Test]
    public void GoingBack_IsANewDraftWithAnOldLayout()
    {
        var old = RoomVersion.Published("shop", Layout, Now);
        old.Archive();

        var back = old.DraftFrom(3, null, Now.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(back.Id, Is.Not.EqualTo(old.Id));
            Assert.That((back.RoomId, back.Number, back.State, back.Layout), Is.EqualTo(("shop", 3, RoomVersionState.Draft, Layout)));
            Assert.That(old.State, Is.EqualTo(RoomVersionState.Archived), "the old version stays as it was");
        });
    }

    [Test]
    public void ADraft_CountsItsEdits_SoASaveOverSomeoneElsesCanBeSpotted()
    {
        var draft = RoomVersion.Draft("shop", 2, Layout, null, Now);
        Assert.That(draft.Revision, Is.EqualTo(1));

        draft.Edit(Layout with { Name = "A" });
        draft.Edit(Layout with { Name = "B" });

        Assert.That(draft.Revision, Is.EqualTo(3));
    }

    [Test]
    public void AnAuditEntry_NeedsAnActionAndATarget()
    {
        var entry = RoomAuditEntry.Of(Guid.NewGuid(), "published", "shop", "version 2", Now);

        Assert.Multiple(() =>
        {
            Assert.That((entry.Action, entry.Target, entry.Detail, entry.At), Is.EqualTo(("published", "shop", "version 2", Now)));
            Assert.That(() => RoomAuditEntry.Of(Guid.NewGuid(), " ", "shop", "", Now), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => RoomAuditEntry.Of(Guid.NewGuid(), "published", " ", "", Now), Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void ALook_TravelsWithTheDraft_IsKeptByALayoutOnlyEdit_AndCarriedToTheNextDraft()
    {
        var look = new RoomLook("#112233", "#445566", "#778899", new LookBackdrop([]));
        var other = look with { Sky = "#abcdef" };
        var draft = RoomVersion.Draft("shop", 2, Layout, null, Now, look);

        draft.Edit(Layout with { Name = "Edited" });
        var kept = draft.Look;
        draft.Edit(Layout, other);
        var replaced = draft.Look;
        draft.Publish(Now);
        var next = draft.DraftFrom(3, null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(look), "an edit with no look leaves it");
            Assert.That(replaced, Is.EqualTo(other));
            Assert.That(next.Look, Is.EqualTo(other), "the next draft starts from the published look");
            Assert.That(RoomVersion.Draft("shop", 4, Layout, null, Now).Look, Is.Null);
        });
    }

    [Test]
    public void ADraft_ScheduledForLater_IsTakenOffByEditingItOrPublishingIt()
    {
        var admin = Guid.NewGuid();
        var draft = RoomVersion.Draft("shop", 2, Layout, admin, Now);

        draft.Schedule(Now.AddDays(1), admin, Now);
        Assert.That((draft.GoLiveAt, draft.ScheduledBy), Is.EqualTo((Now.AddDays(1), (Guid?)admin)));

        draft.Edit(Layout with { Name = "Changed" });
        Assert.That((draft.GoLiveAt, draft.ScheduledBy), Is.EqualTo(((DateTimeOffset?)null, (Guid?)null)), "an edit takes it off");

        draft.Schedule(Now.AddDays(1), admin, Now);
        draft.Publish(Now.AddDays(1));
        Assert.That(draft.GoLiveAt, Is.Null, "published, so nothing is waiting");
    }

    [Test]
    public void OnlyADraft_CanBeScheduled_AndOnlyForALaterTime()
    {
        var admin = Guid.NewGuid();
        var draft = RoomVersion.Draft("shop", 2, Layout, admin, Now);
        var published = RoomVersion.Published("shop", Layout, Now);

        Assert.Multiple(() =>
        {
            Assert.That(() => draft.Schedule(Now, admin, Now), Throws.TypeOf<DomainException>());
            Assert.That(() => draft.Schedule(Now.AddMinutes(-1), admin, Now), Throws.TypeOf<DomainException>());
            Assert.That(() => published.Schedule(Now.AddDays(1), admin, Now), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void AVersion_NeedsARoomAndANumberFromOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => RoomVersion.Draft(" ", 1, Layout, null, Now), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => RoomVersion.Draft("shop", 0, Layout, null, Now), Throws.TypeOf<DomainException>());
        });
    }
}
