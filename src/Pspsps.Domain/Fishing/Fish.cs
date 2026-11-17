namespace Pspsps.Domain.Fishing;

/// <summary>A kind of fish (or worse): what it's worth in coins and how often it bites, relative to the others.</summary>
public sealed record Fish(string Id, string Name, int Coins, int Weight);
