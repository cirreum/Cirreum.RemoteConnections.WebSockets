namespace Cirreum.RemoteServices;

/// <summary>
/// The delay between reconnect attempts: a fixed schedule backing off to a configured ceiling,
/// with jitter applied to each value.
/// </summary>
/// <remarks>
/// <para>
/// The schedule is 0s, 2s, 5s, 10s, then the ceiling for every subsequent attempt, each varied
/// by up to twenty percent so that clients dropped together do not reconnect in lockstep.
/// </para>
/// <para>
/// Raw WebSockets have no reconnection of their own, so the connection drives this schedule
/// itself. The values match the SignalR transport's policy; the two are stated separately
/// because neither package may reference the other.
/// </para>
/// </remarks>
internal sealed class ReconnectDelaySchedule(TimeSpan maxDelay) {

	private static readonly TimeSpan[] Schedule = [
		TimeSpan.Zero,
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10)
	];

	private const double JitterFactor = 0.2;

	/// <summary>The delay before the attempt following <paramref name="previousAttempts"/> failures.</summary>
	internal TimeSpan NextDelay(int previousAttempts) {

		var baseDelay = previousAttempts < Schedule.Length
			? Schedule[previousAttempts]
			: maxDelay;

		if (baseDelay > maxDelay) {
			baseDelay = maxDelay;
		}

		if (baseDelay <= TimeSpan.Zero) {
			return TimeSpan.Zero;
		}

		var factor = 1 + ((Random.Shared.NextDouble() * 2 - 1) * JitterFactor);
		return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * factor);
	}

}
