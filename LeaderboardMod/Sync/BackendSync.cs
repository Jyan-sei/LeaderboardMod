using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using LeaderboardMod.Identity;
using LeaderboardMod.Logging;
using LeaderboardMod.Session;
using LeaderboardMod.Storage;
using Newtonsoft.Json.Linq;

namespace LeaderboardMod.Sync;

internal sealed class BackendUploadResponse
{
	internal bool Ok;
	internal bool NeedsToken;
	internal string Token;
	internal string Error;
	internal bool InvalidateRun;
	internal string InvalidationReason;
	internal string ForbiddenCommand;
	internal bool OutOfOrder;
	internal int ExpectedSnapshotSeq;
	internal int Bytes;

	internal bool HasExpectedSeq => ExpectedSnapshotSeq > 0;

	internal static BackendUploadResponse Parse(string json)
	{
		var result = new BackendUploadResponse();
		if (string.IsNullOrWhiteSpace(json))
		{
			result.Error = "Empty response body";
			return result;
		}

		var jo = JObject.Parse(json);
		result.Ok = jo.Value<bool?>("ok") == true;
		result.NeedsToken = jo.Value<bool?>("needsToken") == true;
		result.InvalidateRun = jo.Value<bool?>("invalidateRun") == true;
		result.OutOfOrder = jo.Value<bool?>("outOfOrder") == true;
		result.Token = jo.Value<string>("token");
		result.Error = jo.Value<string>("error");
		result.InvalidationReason = jo.Value<string>("invalidationReason");
		result.ForbiddenCommand = jo.Value<string>("forbiddenCommand");
		result.ExpectedSnapshotSeq = jo.Value<int?>("expectedSnapshotSeq") ?? 0;
		result.Bytes = jo.Value<int?>("bytes") ?? 0;

		if (result.ExpectedSnapshotSeq <= 0 && !string.IsNullOrEmpty(result.Error))
		{
			var match = System.Text.RegularExpressions.Regex.Match(
				result.Error, @"expected snapshot_seq (\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed) && parsed > 0)
				result.ExpectedSnapshotSeq = parsed;
		}
		if (!result.OutOfOrder && !string.IsNullOrEmpty(result.Error)
			&& result.Error.IndexOf("out of order", StringComparison.OrdinalIgnoreCase) >= 0)
			result.OutOfOrder = true;

		return result;
	}

	internal static BackendUploadResponse FromException(Exception ex)
	{
		return new BackendUploadResponse { Error = ex.Message };
	}
}

internal static class BackendSync
{
	private const int BacklogIntervalMs = 1000;
	private const int UploadTimeoutMs = 120000;
	private const int StatusTimeoutMs = 10000;
	private const int TimeoutBackoffStartMs = 5000;
	private const int TimeoutBackoffMaxMs = 30000;
	// snapshots every 60s. if we're behind, send a bit faster (~32s) so we dont trip rate limits
	private const int CatchupIntervalMs = 32000;
	private const int MinRetriesBeforeAlign = 5;
	private static int _timeoutStreak;
	private static int _retrySeq;
	private static int _retryCount;
	private static DateTime _lastPostUtc = DateTime.MinValue;
	private static DateTime _lastStatusProbeUtc = DateTime.MinValue;

	private static string _uploadUrl = "https://fch-toolkit.com/api/cu/sqlite";
	private static bool _enabled = true;
	private static int _uploadDepth;
	private static Thread _worker;
	private static readonly object Gate = new object();

	private static string _pendingSaveId;
	private static int _pendingSnapshotSeq;
	private static string _pendingTimestampUtc;
	private static bool _pendingQueued;
	private static int _rewindSnapshotSeq;
	private static string _rewindSaveId;
	private static volatile bool _shutdownRequested;
	private static readonly ManualResetEventSlim _wakeWorker = new ManualResetEventSlim(false);

	internal static void Configure(bool enabled, string uploadUrl, string fingerprintSecret = null,
		bool certPinningEnabled = true, string certPinSha256Base64 = null)
	{
		_shutdownRequested = false;
		_enabled = enabled;
		if (!string.IsNullOrWhiteSpace(uploadUrl))
			_uploadUrl = uploadUrl.Trim();
		PayloadFingerprint.Configure(fingerprintSecret);
		CertificatePinValidator.Configure(certPinningEnabled, certPinSha256Base64);
		ClientSigningKeyStore.Initialize();
		SessionTokenStore.Initialize();
		BackendSyncStateStore.Initialize();
		LbLog.Step("Backend:Init",
			$"enabled={_enabled} url={_uploadUrl} security=HMAC+ECDSA+JWT+TLS-pin");
	}

	internal static void Shutdown()
	{
		_shutdownRequested = true;
		_wakeWorker.Set();
		lock (Gate)
		{
			_pendingQueued = false;
		}

		Thread worker;
		lock (Gate)
		{
			worker = _worker;
		}

		if (worker != null && worker.IsAlive)
			worker.Join(3000);
	}

	internal static void ResumeBacklogIfNeeded(string saveId)
	{
		if (!_enabled || string.IsNullOrEmpty(saveId))
			return;

		var state = BackendSyncStateStore.Load(saveId);
		if (state.IsInvalidated)
			return;

		if (!CanSyncSave(saveId))
			return;

		if (!state.IsBehind)
			return;

		int resumeSeq = state.LastAcknowledgedSnapshotSeq + 1;
		lock (Gate)
		{
			if (_worker != null && _worker.IsAlive)
				return;

			if (string.IsNullOrEmpty(_pendingSaveId))
			{
				_pendingSaveId = saveId;
				_pendingSnapshotSeq = resumeSeq;
				_pendingTimestampUtc = CaptureDatabase.TryGetSnapshotTimestamp(saveId, resumeSeq)
					?? state.LatestPendingTimestampUtc;
				_pendingQueued = true;
			}
		}

		LbLog.Step("Backend:Backlog",
			$"Resuming backlog save={saveId} ack={state.LastAcknowledgedSnapshotSeq} pending={state.LatestPendingSnapshotSeq} next={resumeSeq}");
		TryAlignWithServer(saveId, resumeSeq);
		EnsureWorkerRunning();
	}

	/// <summary>
	/// fresh StartRun - dump pending uploads, clear jwt, stop retrying old backlog
	/// </summary>
	internal static void PrepareFreshRun(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		BackendSyncStateStore.Reset(saveId);
		SessionTokenStore.ClearToken(saveId);

		lock (Gate)
		{
			if (_pendingSaveId == saveId)
			{
				_pendingQueued = false;
				_pendingSaveId = null;
				_pendingSnapshotSeq = 0;
				_pendingTimestampUtc = null;
			}
		}

		_wakeWorker.Set();
		ClearRewind(saveId);
		ClearRetryAttempts();

		LbLog.Step("Backend:FreshRun", $"Cleared upload queue and session token for save={saveId}");
	}

	/// <summary>
	/// stop in-memory retries but leave persisted sync state (partial / load-run)
	/// </summary>
	internal static void CancelQueuedUploads(string saveId)
	{
		if (string.IsNullOrEmpty(saveId))
			return;

		lock (Gate)
		{
			if (_pendingSaveId == saveId)
			{
				_pendingQueued = false;
				_pendingSaveId = null;
				_pendingSnapshotSeq = 0;
				_pendingTimestampUtc = null;
			}
		}

		_wakeWorker.Set();
		ClearRewind(saveId);
	}

	internal static void QueueUpload(string saveId, int snapshotSeq, string timestampUtc)
	{
		if (!_enabled || string.IsNullOrWhiteSpace(_uploadUrl))
			return;
		if (!CaptureDatabase.IsInitialized)
			return;
		if (BackendSyncStateStore.Load(saveId).IsInvalidated)
			return;
		if (!CanSyncSave(saveId))
			return;

		BackendSyncStateStore.RecordPending(saveId, snapshotSeq, timestampUtc);

		lock (Gate)
		{
			_pendingSaveId = saveId;
			_pendingSnapshotSeq = snapshotSeq;
			_pendingTimestampUtc = timestampUtc;
			_pendingQueued = true;
			_wakeWorker.Set();
			EnsureWorkerRunningLocked();
		}
	}

	private static void EnsureWorkerRunning()
	{
		lock (Gate)
		{
			EnsureWorkerRunningLocked();
		}
	}

	private static void EnsureWorkerRunningLocked()
	{
		if (_worker != null && _worker.IsAlive)
			return;

		_worker = new Thread(RunUploadWorker)
		{
			IsBackground = true,
			Name = "LeaderboardMod.BackendSync"
		};
		_worker.Start();
	}

	private static void RunUploadWorker()
	{
		while (!_shutdownRequested)
		{
			string saveId;
			int snapshotSeq;
			string timestampUtc;
			bool hadImmediatePending;
			lock (Gate)
			{
				saveId = _pendingSaveId;
				snapshotSeq = _pendingSnapshotSeq;
				timestampUtc = _pendingTimestampUtc;
				hadImmediatePending = _pendingQueued;
				_pendingQueued = false;
			}

			var state = !string.IsNullOrEmpty(saveId)
				? BackendSyncStateStore.Load(saveId)
				: new BackendSyncState();

			if (!string.IsNullOrEmpty(saveId))
			{
				state = BackendSyncStateStore.Load(saveId);
				snapshotSeq = ResolveUploadSeq(saveId, state, snapshotSeq);
				timestampUtc = CaptureDatabase.TryGetSnapshotTimestamp(saveId, snapshotSeq)
					?? timestampUtc
					?? state.LatestPendingTimestampUtc;
			}

			if (_shutdownRequested || !_enabled || string.IsNullOrEmpty(saveId))
			{
				lock (Gate)
				{
					_worker = null;
				}
				return;
			}

			if (state.IsInvalidated)
			{
				CancelQueuedUploads(saveId);
				lock (Gate)
				{
					_worker = null;
				}
				return;
			}

			if (!state.IsBehind && !hadImmediatePending)
			{
				lock (Gate)
				{
					if (!_pendingQueued)
					{
						_worker = null;
						return;
					}
				}
				continue;
			}

			WaitForCatchupSlot(saveId, snapshotSeq);
			if (_shutdownRequested)
				continue;

			NoteUploadAttempt(snapshotSeq);

			BackendUploadResponse response = null;
			string error = null;
			try
			{
				_lastPostUtc = DateTime.UtcNow;
				response = UploadDatabaseSnapshot(saveId, snapshotSeq, timestampUtc);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				LbLog.Warn("Backend:Upload", ex.Message);
			}

			bool success = response != null && response.Ok;
			state = BackendSyncStateStore.Load(saveId);
			if (success)
			{
				_timeoutStreak = 0;
				ClearRetryAttempts();
				ClearRewind(saveId);
				BackendSyncStateStore.RecordSuccess(saveId, snapshotSeq);
				state = BackendSyncStateStore.Load(saveId);
				LbLog.Step("Backend:Upload",
					$"accepted seq={snapshotSeq} save={saveId} ack={state.LastAcknowledgedSnapshotSeq}/{state.LatestPendingSnapshotSeq}");
			}
			else
			{
				state = BackendSyncStateStore.Load(saveId);
				if (state.IsInvalidated)
				{
					ClearRewind(saveId);
					LbLog.Warn("Backend:Upload",
						$"halted save={saveId} - run invalidated ({state.InvalidationReason})");
					CancelQueuedUploads(saveId);
					lock (Gate)
					{
						_worker = null;
					}
					return;
				}

				if (response != null)
					TryRewindToExpected(saveId, snapshotSeq, response);

				error = error ?? response?.Error ?? "Upload failed";
				if (IsTimeoutError(error))
					_timeoutStreak++;
				else
					_timeoutStreak = 0;

				if (IsTransientUploadError(error) && _retryCount >= MinRetriesBeforeAlign)
					TryAlignWithServer(saveId, snapshotSeq);

				BackendSyncStateStore.RecordFailure(saveId, error);
				state = BackendSyncStateStore.Load(saveId);
				int retryMs = RetryDelayMs(error);
				LbLog.Warn("Backend:Backlog",
					$"retry {_retryCount} save={saveId} seq={snapshotSeq} ack={state.LastAcknowledgedSnapshotSeq} pending={state.LatestPendingSnapshotSeq} next in {retryMs}ms err={error}");
				_wakeWorker.Wait(retryMs);
				_wakeWorker.Reset();
			}

			lock (Gate)
			{
				state = BackendSyncStateStore.Load(saveId);
				if (!state.IsBehind && !_pendingQueued)
				{
					_worker = null;
					return;
				}
			}
		}
	}

	private static int ResolveUploadSeq(string saveId, BackendSyncState state, int queuedSeq)
	{
		if (!string.IsNullOrEmpty(_rewindSaveId)
			&& string.Equals(_rewindSaveId, saveId, StringComparison.Ordinal)
			&& _rewindSnapshotSeq > 0)
			return _rewindSnapshotSeq;

		if (state.IsBehind)
			return state.LastAcknowledgedSnapshotSeq + 1;

		if (state.LatestPendingSnapshotSeq > queuedSeq)
			return state.LatestPendingSnapshotSeq;

		return queuedSeq > 0 ? queuedSeq : state.LatestPendingSnapshotSeq;
	}

	private static void TryRewindToExpected(string saveId, int attemptedSeq, BackendUploadResponse response)
	{
		if (response == null || (!response.OutOfOrder && !response.HasExpectedSeq))
			return;

		int expected = response.ExpectedSnapshotSeq;
		if (expected < 1)
			return;
		if (expected == attemptedSeq)
			return;

		if (expected > 1)
			BackendSyncStateStore.RecordSuccess(saveId, expected - 1);

		if (!CaptureDatabase.HasSnapshotSeq(saveId, expected))
		{
			LbLog.Warn("Backend:Upload",
				$"out of order: server expected seq={expected} but local sqlite has no such snapshot");
			return;
		}

		_rewindSaveId = saveId;
		_rewindSnapshotSeq = expected;
		LbLog.Step("Backend:Upload",
			$"out of order seq={attemptedSeq} - retrying from expected seq={expected}");
	}

	private static void WaitForCatchupSlot(string saveId, int snapshotSeq)
	{
		if (_lastPostUtc == DateTime.MinValue)
			return;

		double elapsedMs = (DateTime.UtcNow - _lastPostUtc).TotalMilliseconds;
		int remainMs = CatchupIntervalMs - (int)elapsedMs;
		if (remainMs <= 0)
			return;

		LbLog.Step("Backend:Backlog",
			$"waiting {remainMs}ms before seq={snapshotSeq} save={saveId}");
		_wakeWorker.Wait(remainMs);
		_wakeWorker.Reset();
	}

	private static void TryAlignWithServer(string saveId, int attemptedSeq)
	{
		if (string.IsNullOrEmpty(saveId))
			return;
		if (_lastStatusProbeUtc != DateTime.MinValue
			&& (DateTime.UtcNow - _lastStatusProbeUtc).TotalMilliseconds < CatchupIntervalMs)
			return;

		_lastStatusProbeUtc = DateTime.UtcNow;
		var probe = ProbeExpectedSeq(saveId);
		if (probe == null || !probe.HasExpectedSeq)
			return;
		TryRewindToExpected(saveId, attemptedSeq, probe);
	}

	private static void NoteUploadAttempt(int snapshotSeq)
	{
		if (snapshotSeq != _retrySeq)
		{
			_retrySeq = snapshotSeq;
			_retryCount = 0;
		}
		_retryCount++;
	}

	private static void ClearRetryAttempts()
	{
		_retrySeq = 0;
		_retryCount = 0;
	}

	private static bool IsTimeoutError(string error)
	{
		return !string.IsNullOrEmpty(error)
			&& error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsTransientUploadError(string error)
	{
		if (string.IsNullOrEmpty(error))
			return false;
		if (IsTimeoutError(error))
			return true;
		return error.IndexOf("unable to connect", StringComparison.OrdinalIgnoreCase) >= 0
			|| error.IndexOf("connection was closed", StringComparison.OrdinalIgnoreCase) >= 0
			|| error.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0
			|| error.IndexOf("name resolution", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static int RetryDelayMs(string error)
	{
		if (!IsTimeoutError(error) || _timeoutStreak <= 0)
			return BacklogIntervalMs;
		int delay = TimeoutBackoffStartMs;
		for (int i = 1; i < _timeoutStreak; i++)
		{
			if (delay >= TimeoutBackoffMaxMs)
				return TimeoutBackoffMaxMs;
			delay *= 2;
		}
		return Math.Min(delay, TimeoutBackoffMaxMs);
	}

	private static void ClearRewind(string saveId)
	{
		if (!string.IsNullOrEmpty(saveId)
			&& !string.IsNullOrEmpty(_rewindSaveId)
			&& !string.Equals(_rewindSaveId, saveId, StringComparison.Ordinal))
			return;

		_rewindSaveId = null;
		_rewindSnapshotSeq = 0;
	}

	private static BackendUploadResponse UploadDatabaseSnapshot(string saveId, int snapshotSeq, string timestampUtc)
	{
		if (!CanSyncSave(saveId))
			return new BackendUploadResponse { Error = "Capture not allowed for this session" };

		if (_uploadDepth > 0)
			return new BackendUploadResponse { Error = "Upload already in progress" };

		string dbPath = CaptureDatabase.DatabasePath;
		if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
		{
			BackendSyncStateStore.RecordFailure(saveId, "Database file missing");
			LbLog.Warn("Backend:Upload", "Database file missing");
			return new BackendUploadResponse { Error = "Database file missing" };
		}

		string tempPath = Path.Combine(Path.GetTempPath(), "LB_upload_" + Guid.NewGuid().ToString("N") + ".db");
		_uploadDepth++;
		try
		{
			CaptureDatabase.CopyDatabaseTo(tempPath);
			byte[] payload = ReadFileWithRetry(tempPath);
			return TryUploadWithSessionToken(payload, saveId, snapshotSeq, timestampUtc);
		}
		finally
		{
			_uploadDepth--;
			try
			{
				if (File.Exists(tempPath))
					File.Delete(tempPath);
			}
			catch
			{
				// ignore
			}
		}
	}

	private static BackendUploadResponse TryUploadWithSessionToken(
		byte[] payload, string saveId, int snapshotSeq, string timestampUtc)
	{
		string token = SessionTokenStore.GetToken(saveId);
		BackendUploadResponse response;
		try
		{
			response = PostSqlite(payload, saveId, snapshotSeq, timestampUtc, token);
		}
		catch (Exception ex)
		{
			BackendSyncStateStore.RecordFailure(saveId, ex.Message);
			return BackendUploadResponse.FromException(ex);
		}

		if (response.NeedsToken && !string.IsNullOrEmpty(response.Token))
		{
			LbLog.Step("Backend:Upload", $"registration pass - JWT issued for save {saveId}; retrying");
			SessionTokenStore.SaveToken(saveId, response.Token);
			try
			{
				response = PostSqlite(payload, saveId, snapshotSeq, timestampUtc, response.Token);
			}
			catch (Exception ex)
			{
				BackendSyncStateStore.RecordFailure(saveId, ex.Message);
				return BackendUploadResponse.FromException(ex);
			}
		}

		if (response.Ok)
		{
			if (!string.IsNullOrEmpty(response.Token))
				SessionTokenStore.SaveToken(saveId, response.Token);
			return response;
		}

		if (response.InvalidateRun)
		{
			string detail = !string.IsNullOrEmpty(response.ForbiddenCommand)
				? response.ForbiddenCommand
				: response.Error;
			RunInvalidation.ApplyServerInvalidation(
				saveId,
				response.InvalidationReason ?? RunInvalidation.ReasonForbiddenConsole,
				detail);
			return response;
		}

		if (!string.IsNullOrEmpty(response.Error))
		{
			LbLog.Warn("Backend:Upload", $"rejected save={saveId}: {response.Error}");
			if (token != null && !response.NeedsToken && !response.OutOfOrder && !response.HasExpectedSeq)
				SessionTokenStore.ClearToken(saveId);
			BackendSyncStateStore.RecordFailure(saveId, response.Error);
		}

		return response;
	}

	private static byte[] ReadFileWithRetry(string path)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			try
			{
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using var ms = new MemoryStream();
				fs.CopyTo(ms);
				return ms.ToArray();
			}
			catch (IOException) when (attempt < 7)
			{
				Thread.Sleep(50);
			}
		}

		return File.ReadAllBytes(path);
	}

	private static BackendUploadResponse ProbeExpectedSeq(string saveId)
	{
		string token = SessionTokenStore.GetToken(saveId);
		if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_uploadUrl))
			return null;

		string url = _uploadUrl.TrimEnd('/');
		try
		{
			var request = (HttpWebRequest)WebRequest.Create(url);
			request.Method = "GET";
			request.Timeout = StatusTimeoutMs;
			request.ReadWriteTimeout = StatusTimeoutMs;
			request.Headers["X-Steam-Id"] = PlayerIdentity.GetSteamId() ?? "";
			request.Headers["X-Save-Id"] = saveId ?? "";
			request.Headers["Authorization"] = "Bearer " + token;

			using var response = (HttpWebResponse)request.GetResponse();
			using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
			var parsed = BackendUploadResponse.Parse(reader.ReadToEnd());
			if (parsed.HasExpectedSeq)
				LbLog.Step("Backend:Status", $"server expected seq={parsed.ExpectedSnapshotSeq} save={saveId}");
			return parsed;
		}
		catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
		{
			using (errorResponse)
			using (var reader = new StreamReader(errorResponse.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
			{
				string body = reader.ReadToEnd();
				if (!string.IsNullOrWhiteSpace(body))
					return BackendUploadResponse.Parse(body);
			}
			return null;
		}
		catch (Exception ex)
		{
			LbLog.Warn("Backend:Status", ex.Message);
			return null;
		}
	}

	private static BackendUploadResponse PostSqlite(
		byte[] payload, string saveId, int snapshotSeq, string timestampUtc, string sessionToken)
	{
		try
		{
			return PostSqliteInternal(payload, saveId, snapshotSeq, timestampUtc, sessionToken);
		}
		catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
		{
			using (errorResponse)
			using (var reader = new StreamReader(errorResponse.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
			{
				string body = reader.ReadToEnd();
				if (!string.IsNullOrWhiteSpace(body))
					return BackendUploadResponse.Parse(body);
			}

			return BackendUploadResponse.FromException(ex);
		}
	}

	private static BackendUploadResponse PostSqliteInternal(
		byte[] payload, string saveId, int snapshotSeq, string timestampUtc, string sessionToken)
	{
		string steamId = PlayerIdentity.GetSteamId();
		var modList = ModListProvider.GetHeaderWithFingerprint();
		string canonical = RequestCanonical.Build(
			steamId, saveId, snapshotSeq, timestampUtc, modList.Sha256Hex, PluginInfo.Version, payload);

		var request = (HttpWebRequest)WebRequest.Create(_uploadUrl);
		request.Method = "POST";
		request.ContentType = "application/octet-stream";
		request.Timeout = UploadTimeoutMs;
		request.ReadWriteTimeout = UploadTimeoutMs;
		request.Headers["X-Steam-Id"] = steamId;
		request.Headers["X-Save-Id"] = saveId ?? "";
		request.Headers["X-Snapshot-Seq"] = snapshotSeq.ToString();
		request.Headers["X-Machine-Id"] = MachineIdProvider.ShortPrefix ?? "";
		request.Headers["X-Machine-Id-Hash"] = MachineIdProvider.FullHash ?? "";
		request.Headers["X-Timestamp-Utc"] = timestampUtc ?? "";
		request.Headers["X-Mod-List"] = modList.Json;
		request.Headers["X-LeaderboardMod-Version"] = PluginInfo.Version;
		request.Headers["X-Payload-Fingerprint"] = PayloadFingerprint.Compute(
			payload, steamId, saveId, snapshotSeq, timestampUtc, modList.Sha256Hex, PluginInfo.Version);
		request.Headers["X-Client-Public-Key"] = ClientSigningKeyStore.GetPublicKeyHeaderValue();
		request.Headers["X-Request-Signature"] = ClientSigningKeyStore.SignCanonical(canonical);
		if (!string.IsNullOrEmpty(sessionToken))
			request.Headers["Authorization"] = "Bearer " + sessionToken;

		using (var stream = request.GetRequestStream())
			stream.Write(payload, 0, payload.Length);

		using var response = (HttpWebResponse)request.GetResponse();
		using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
		string body = reader.ReadToEnd();
		return BackendUploadResponse.Parse(body);
	}

	private static bool CanSyncSave(string saveId)
	{
		if (!RunSessionManager.ShouldCapture())
			return false;

		var session = RunSessionManager.Current;
		if (session == null || !session.Active)
			return false;
		if (!session.TrackingEnabled)
			return false;
		if (!string.Equals(session.SaveId, saveId, StringComparison.Ordinal))
			return false;

		return true;
	}
}
