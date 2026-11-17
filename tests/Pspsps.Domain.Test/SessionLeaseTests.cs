namespace Pspsps.Domain.Test;

public sealed class SessionLeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly SessionLease Lease = SessionLease.Start("tab", Now);

    [Test]
    public void ALease_RunsOutOnlyOnceStrictlyOlderThanItsDuration()
    {
        Assert.That(Lease.HasExpired(Now + SessionLease.Duration), Is.False);
        Assert.That(Lease.HasExpired(Now + SessionLease.Duration + TimeSpan.FromTicks(1)), Is.True);
    }

    [Test]
    public void ALease_SurvivesTwoMissedRenewals_EvenWhenTheThirdIsLate()
    {
        var thirdRenewalSomeSecondsLate = Now + (SessionLease.RenewEvery * 3) + TimeSpan.FromSeconds(5);

        Assert.That(Lease.HasExpired(thirdRenewalSomeSecondsLate), Is.False);
    }

    [Test]
    public void Renew_StartsTheDurationAgain()
    {
        var renewed = Lease.Renew(Now + SessionLease.RenewEvery);

        Assert.That(renewed.Session, Is.EqualTo("tab"));
        Assert.That(renewed.HasExpired(Now + SessionLease.RenewEvery + SessionLease.Duration), Is.False);
        Assert.That(renewed.HasExpired(Now + SessionLease.RenewEvery + SessionLease.Duration + TimeSpan.FromTicks(1)), Is.True);
    }
}
