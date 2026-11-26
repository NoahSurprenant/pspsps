using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class PitchBallTests
{
    private static readonly Position Middle = new(100, 50);

    private static PitchBall Ball(bool ice = false) => new(Middle, 10.8, 6.8, ice, Middle);

    [Test]
    public void AStepThroughTheBall_KicksItTheWayTheCatWalks()
    {
        var ball = Ball();

        var kicked = ball.Kick(new Position(98.5, 50), new Position(99.2, 50));

        Assert.Multiple(() =>
        {
            Assert.That(kicked, Is.True);
            Assert.That(ball.VelocityX, Is.EqualTo(ball.KickSpeed).Within(1e-9));
            Assert.That(ball.VelocityZ, Is.Zero);
            Assert.That(ball.Moving, Is.True);
        });
    }

    [Test]
    public void AStepFarFromTheBall_OrAwayFromIt_DoesNotKickIt()
    {
        var ball = Ball();

        Assert.Multiple(() =>
        {
            Assert.That(ball.Kick(new Position(90, 45), new Position(91, 45)), Is.False, "far");
            Assert.That(ball.Kick(new Position(101, 50), new Position(102, 50)), Is.False, "walking away, the ball behind");
            Assert.That(ball.Kick(new Position(99, 50), new Position(99, 50)), Is.False, "standing still");
            Assert.That(ball.Moving, Is.False);
        });
    }

    [Test]
    public void ARollingBall_SlowsToAStopOnGrass_AndLongerOnIce()
    {
        var grass = Ball();
        var ice = Ball(ice: true);
        grass.Kick(new Position(99, 50), new Position(99.5, 50));
        ice.Kick(new Position(99, 50), new Position(99.5, 50));

        for (var step = 0; step < 30; step++)
        {
            grass.Step(0.1);
            ice.Step(0.1);
        }

        Assert.Multiple(() =>
        {
            Assert.That(grass.Moving, Is.False, "stopped within three seconds on grass");
            Assert.That(ice.Moving, Is.True, "still gliding on ice");
            Assert.That(Math.Abs(ice.VelocityX), Is.GreaterThan(1), "at a good pace");
        });
    }

    [Test]
    public void ABallHittingTheBoards_BouncesBackSlower_AndStaysOnThePitch()
    {
        var ball = Ball(ice: true);
        ball.Kick(new Position(99, 50), new Position(99.5, 50));

        for (var step = 0; step < 40; step++)
        {
            ball.Step(0.1);
            Assert.That(Math.Abs(ball.Position.X - Middle.X), Is.LessThanOrEqualTo(10.8 + 1e-9));
        }

        Assert.That(ball.VelocityX, Is.LessThan(0), "it bounced off the far board");
    }

    [Test]
    public void AStillBall_StaysWhereItIs()
    {
        var ball = Ball();

        ball.Step(1);

        Assert.That(ball.Position, Is.EqualTo(Middle));
    }
}
