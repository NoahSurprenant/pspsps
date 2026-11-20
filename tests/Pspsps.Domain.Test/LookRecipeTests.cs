using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class LookRecipeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly PaintStep[] Herringbone =
    [
        new("fill", Colour: "main"),
        new("rect", Colour: "accent", X: 0, Y: 0, Width: 0.5, Height: 0.2, Outline: true, Repeat: new PaintRepeat(0.5, 0.2, Shift: 0.25)),
        new("checker", Colour: "main", Accent: "accent", Shade: 0.1, Cell: 0.5),
    ];

    private static readonly PropPart[] Bird = [new("ball", [0, 0, 0], "#336699", Diameter: 0.2)];

    [Test]
    public void AGoodPainterRecipe_HasNoProblems_AndIsKept()
    {
        var entry = PainterRecipeEntry.Of("herringbone", PaintSurfaces.Floor, Herringbone, Now);

        Assert.Multiple(() =>
        {
            Assert.That(LookRecipes.PainterProblems("herringbone", PaintSurfaces.Wall, Herringbone), Is.Empty);
            Assert.That((entry.Id, entry.Surface, entry.Steps), Is.EqualTo(("herringbone", "floor", (IReadOnlyList<PaintStep>)Herringbone)));
        });
    }

    [Test]
    public void ABadPainterRecipe_SaysWhatIsWrong()
    {
        var problems = LookRecipes.PainterProblems("planks", "ceiling", [
            new PaintStep("splat"),
            new PaintStep("rect", Colour: "red", X: 0, Y: 0, Width: 0, Height: 1),
            new PaintStep("rect", Colour: "main", X: 0, Y: 0, Width: 1, Height: 1, Repeat: new PaintRepeat(0.01, 0)),
            new PaintStep("checker", Colour: "main", Cell: 0.01),
        ]);

        Assert.That(problems, Has.Some.Contains("paints a floor or wall")
            .And.Some.Contains("'splat' is not a step")
            .And.Some.Contains("step 2: its colour")
            .And.Some.Contains("step 2: its width")
            .And.Some.Contains("step 3: its repeat across")
            .And.Some.Contains("step 4: its cell"));
    }

    [Test]
    public void ARecipe_NeedsStepsButNotTooMany()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LookRecipes.PainterProblems("empty", PaintSurfaces.Wall, []), Has.Some.Contains("1 to 64 steps"));
            Assert.That(LookRecipes.PainterProblems("long", PaintSurfaces.Wall, [.. Enumerable.Repeat(new PaintStep("fill", Colour: "main"), 65)]), Has.Some.Contains("1 to 64 steps"));
            Assert.That(LookRecipes.PainterProblems("planks", PaintSurfaces.Floor, Herringbone), Has.Some.Contains("a painter the browser already has"));
            Assert.That(() => PainterRecipeEntry.Of("Bad Name", PaintSurfaces.Wall, Herringbone, Now), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void AGoodWildlifeRecipe_IsKept_AndABadOneSaysWhy()
    {
        var entry = WildlifeRecipeEntry.Of("bird", Bird, new WildlifeMotion("wander", 0.5, 1.2, Reach: 3), Now);
        var problems = LookRecipes.WildlifeProblems("fish", [], new WildlifeMotion("hop", 0, 9, Prop: "sofa"));
        var shuttle = LookRecipes.WildlifeProblems("bee", Bird, new WildlifeMotion("shuttle", 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Motion.Kind, Is.EqualTo("wander"));
            Assert.That(problems, Has.Some.Contains("a creature the browser already has")
                .And.Some.Contains("1 to 64 parts")
                .And.Some.Contains("'hop' is not a motion")
                .And.Some.Contains("its speed"));
            Assert.That(shuttle, Has.Some.Contains("between two of a kind of prop"));
            Assert.That(LookRecipes.WildlifeProblems("bee", Bird, new WildlifeMotion("shuttle", 1, 1, Prop: PropKinds.FlowerBed)), Is.Empty);
        });
    }

    [Test]
    public void ALook_MayNameARecipe_OnlyWhereTheNamesAreKnown()
    {
        var look = new RoomLook("#bfe3f2", "#9fd38a", "#cfc2a8", new LookBackdrop([])) { Floor = new LookPaint("herringbone"), Wildlife = "bird" };
        var names = LookNames.BuiltIn.With([(PaintSurfaces.Floor, "herringbone")], ["bird"]);

        Assert.Multiple(() =>
        {
            Assert.That(look.Problems("loft", names), Is.Empty);
            Assert.That(look.Problems("loft"), Has.Count.EqualTo(2));
            Assert.That(look.Problems("loft", LookNames.BuiltIn.With([(PaintSurfaces.Wall, "herringbone")], ["bird"])), Has.Some.Contains("not a floor painter"));
            Assert.That(() => RoomLookEntry.Of("loft", look, Now), Throws.TypeOf<DomainException>());
            Assert.That(RoomLookEntry.Of("loft", look, Now, names).Look, Is.EqualTo(look));
        });
    }
}
