namespace Pspsps.Domain.Rooms;

/// <summary>
/// The ball (or, on the ice, the puck) a pitch's cats kick about, in world metres. A cat's step kicks it when the step
/// passes close behind it; it then rolls on, slowing at the surface's rate, and bounces off the boards.
/// </summary>
public sealed class PitchBall(Position centre, double halfWidth, double halfDepth, bool onIce, Position start)
{
    /// <summary>How near a cat's step must pass to kick, in metres.</summary>
    public const double Reach = 0.9;

    /// <summary>The speed a kick gives: the puck flies faster than a ball, in metres a second.</summary>
    public double KickSpeed => onIce ? 10 : 6.5;

    /// <summary>How fast it slows, per second: a ball drags on grass, a puck hardly slows on ice.</summary>
    public double Drag => onIce ? 0.3 : 2.2;

    /// <summary>What a board gives back of a bounce.</summary>
    public const double Bounce = 0.7;

    private const double RestBelow = 0.05;

    public Position Position { get; private set; } = start;

    public double VelocityX { get; private set; }

    public double VelocityZ { get; private set; }

    public bool Moving => Math.Abs(VelocityX) > 0 || Math.Abs(VelocityZ) > 0;

    /// <summary>Kicks it when the step from <paramref name="from"/> to <paramref name="to"/> passes in reach in front of it.</summary>
    public bool Kick(Position from, Position to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        var length = Math.Sqrt((dx * dx) + (dz * dz));
        if (length < 1e-6)
        {
            return false;
        }

        var along = (((Position.X - from.X) * dx) + ((Position.Z - from.Z) * dz)) / length;
        if (along < 0)
        {
            return false;
        }

        var at = Math.Min(along, length);
        var nearest = new Position(from.X + (dx / length * at), from.Z + (dz / length * at));
        if (nearest.DistanceTo(Position) > Reach)
        {
            return false;
        }

        VelocityX = dx / length * KickSpeed;
        VelocityZ = dz / length * KickSpeed;
        return true;
    }

    /// <summary>Lets <paramref name="seconds"/> go by: it rolls, bounces off the boards, and slows.</summary>
    public void Step(double seconds)
    {
        if (!Moving)
        {
            return;
        }

        var x = Position.X + (VelocityX * seconds);
        var z = Position.Z + (VelocityZ * seconds);
        var vx = VelocityX;
        var vz = VelocityZ;
        if (Math.Abs(x - centre.X) > halfWidth)
        {
            x = centre.X + (Math.Sign(x - centre.X) * halfWidth);
            vx = -vx * Bounce;
        }

        if (Math.Abs(z - centre.Z) > halfDepth)
        {
            z = centre.Z + (Math.Sign(z - centre.Z) * halfDepth);
            vz = -vz * Bounce;
        }

        var slow = Math.Exp(-Drag * seconds);
        vx *= slow;
        vz *= slow;
        if (Math.Sqrt((vx * vx) + (vz * vz)) < RestBelow)
        {
            vx = 0;
            vz = 0;
        }

        Position = new Position(x, z);
        VelocityX = vx;
        VelocityZ = vz;
    }
}
