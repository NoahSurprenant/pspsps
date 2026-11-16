namespace Pspsps.Domain;

/// <summary>A change the domain refuses: the message is safe to show to the player.</summary>
public sealed class DomainException(string message) : Exception(message);
