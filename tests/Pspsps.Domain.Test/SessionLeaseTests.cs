namespace Pspsps.Domain.Test;

public sealed class SessionLeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ALease_RunsOutOnlyOnceStrictlyOlderThanItsDuration()
    {
        Assert.That(SessionLease.HasExpired(Now, Now + SessionLease.Duration), Is.False);
        Assert.That(SessionLease.HasExpired(Now, Now + SessionLease.Duration + TimeSpan.FromTicks(1)), Is.True);
    }

    [Test]
    public void ALease_SurvivesTwoMissedRenewals_EvenWhenTheThirdIsLate()
    {
        var thirdRenewalSomeSecondsLate = Now + (SessionLease.RenewEvery * 3) + TimeSpan.FromSeconds(5);

        Assert.That(SessionLease.HasExpired(Now, thirdRenewalSomeSecondsLate), Is.False);
    }
}
