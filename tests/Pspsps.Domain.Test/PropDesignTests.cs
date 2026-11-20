using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class PropDesignTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static PropPart Box() => new("box", [0, 0.5, 0], "#c98f5a", Width: 1, Height: 1, Depth: 1);

    [Test]
    public void AGoodDesign_IsKept_AndCanBeReplaced()
    {
        var design = PropDesign.Of("park-bench", 1, [Box()], Now);

        design.Replace(2, [Box(), new PropPart("ball", [0, 1, 0], "#ffffff", Diameter: 0.4)], Now.AddDays(1));

        Assert.That((design.Kind, design.Size, design.Parts.Count, design.UpdatedAt), Is.EqualTo(("park-bench", 2.0, 2, Now.AddDays(1))));
    }

    [Test]
    public void ADesign_NeedsAKindASizeAndParts_ThatCanBeDrawn()
    {
        var problems = PropDesign.Problems("Park Bench", 0, [
            Box() with { Colour = "red" },
            new PropPart("pyramid", [0, 0, 0], "#ffffff"),
            new PropPart("cylinder", [0, 0], "#ffffff", Diameter: 1),
            new PropPart("ball", [0, 0, 0], "#ffffff", Diameter: -1),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("lower-case", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("size is not above nothing", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("part 1: its colour", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("part 2: 'pyramid' is not a shape", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("part 3: its place needs three numbers", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("part 3: a cylinder needs its sizes", StringComparison.Ordinal)));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("part 4: a ball needs its sizes", StringComparison.Ordinal)));
            Assert.That(PropDesign.Problems("empty", 1, []), Has.Some.EqualTo("empty: has no parts."));
            Assert.That(() => PropDesign.Of("Bad", 1, [Box()], Now), Throws.TypeOf<DomainException>());
        });
    }
}
