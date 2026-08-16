namespace PhotoAtomic;

/// <summary>
/// How many times to insist when it is the LINE that failed, not the answer.
///
/// The two failures look alike from a distance and could not be more
/// different: a model that answers badly will answer badly again, so asking
/// twice is a waste; a service that did not answer at all often answers on the
/// next breath. Measured the hard way — a unit failed three runs in a row with
/// "Service request failed" while the same sentence, typed by hand against the
/// same endpoint, came back immediately.
///
/// Delays double from the first one: an endpoint that is rate-limiting wants
/// to be left alone for longer each time, not pestered at a fixed pace.
/// </summary>
public sealed record TransportRetry(int Attempts, TimeSpan FirstDelay)
{
    public static readonly TransportRetry Default = new(4, TimeSpan.FromSeconds(2));

    /// <summary>One shot, no waiting: for tests, and for callers who do their own insisting.</summary>
    public static readonly TransportRetry None = new(1, TimeSpan.Zero);

    public TimeSpan DelayBefore(int attempt) =>
        attempt <= 1 ? TimeSpan.Zero : FirstDelay * Math.Pow(2, attempt - 2);
}
