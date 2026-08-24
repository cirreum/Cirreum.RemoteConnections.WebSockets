namespace Cirreum.RemoteConnections.WebSockets.Tests;

public class ReconnectDelayScheduleTests {

	private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

	private static TimeSpan DelayFor(int attempt, TimeSpan? maxDelay = null) =>
		new ReconnectDelaySchedule(maxDelay ?? MaxDelay).NextDelay(attempt);

	[Fact]
	public void FirstAttempt_IsImmediate() {
		DelayFor(0).Should().Be(TimeSpan.Zero);
	}

	[Theory]
	[InlineData(1, 2)]
	[InlineData(2, 5)]
	[InlineData(3, 10)]
	public void ScheduledAttempts_BackOffWithinJitterBounds(int attempt, int expectedSeconds) {
		DelayFor(attempt).TotalSeconds.Should().BeInRange(expectedSeconds * 0.8, expectedSeconds * 1.2);
	}

	[Theory]
	[InlineData(4)]
	[InlineData(50)]
	public void BeyondTheSchedule_SitsAtTheCeiling(int attempt) {
		DelayFor(attempt).TotalSeconds
			.Should().BeInRange(MaxDelay.TotalSeconds * 0.8, MaxDelay.TotalSeconds * 1.2);
	}

	[Fact]
	public void ACeilingBelowTheSchedule_ClampsTheScheduledDelays() {
		var ceiling = TimeSpan.FromSeconds(1);

		foreach (var attempt in new[] { 1, 2, 3, 9 }) {
			DelayFor(attempt, ceiling).TotalSeconds
				.Should().BeLessThanOrEqualTo(ceiling.TotalSeconds * 1.2);
		}
	}

	[Fact]
	public void Jitter_VariesTheDelayBetweenAttempts() {
		var schedule = new ReconnectDelaySchedule(MaxDelay);

		var delays = Enumerable.Range(0, 25).Select(_ => schedule.NextDelay(5)).Distinct().ToList();

		delays.Should().HaveCountGreaterThan(1, "identical delays would reconnect clients in lockstep");
	}

	[Fact]
	public void TheScheduleNeverEnds() {
		// Raw WebSockets have no reconnection of their own; the loop relies on always
		// receiving a delay rather than a signal to stop.
		DelayFor(int.MaxValue - 1).Should().BeGreaterThan(TimeSpan.Zero);
	}

}
