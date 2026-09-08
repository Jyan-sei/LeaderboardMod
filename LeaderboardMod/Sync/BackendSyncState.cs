using System;
using Newtonsoft.Json;

namespace LeaderboardMod.Sync;

internal sealed class BackendSyncState
{
	[JsonProperty("lastAcknowledgedSnapshotSeq")]
	public int LastAcknowledgedSnapshotSeq;

	[JsonProperty("latestPendingSnapshotSeq")]
	public int LatestPendingSnapshotSeq;

	[JsonProperty("latestPendingTimestampUtc")]
	public string LatestPendingTimestampUtc;

	[JsonProperty("lastError")]
	public string LastError;

	[JsonProperty("lastErrorUtc")]
	public string LastErrorUtc;

	[JsonProperty("lastSuccessUtc")]
	public string LastSuccessUtc;

	[JsonProperty("isInvalidated")]
	public bool IsInvalidated;

	[JsonProperty("invalidationReason")]
	public string InvalidationReason;

	[JsonProperty("invalidationDetail")]
	public string InvalidationDetail;

	[JsonProperty("invalidatedUtc")]
	public string InvalidatedUtc;

	public bool IsBehind => !IsInvalidated && LatestPendingSnapshotSeq > LastAcknowledgedSnapshotSeq;
}
