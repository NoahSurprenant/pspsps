namespace Pspsps.Domain.Safety;

/// <summary>Why a player reports another: a short, fixed list (the report's note says more).</summary>
public enum ReportReason
{
    /// <summary>"Mean or hurtful chat".</summary>
    MeanChat,

    /// <summary>"Inappropriate name".</summary>
    InappropriateName,

    /// <summary>"Spam".</summary>
    Spam,

    /// <summary>"Something else".</summary>
    SomethingElse,
}
