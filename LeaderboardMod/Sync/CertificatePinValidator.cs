using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LeaderboardMod.Sync;

internal static class CertificatePinValidator
{
	// sha256 (base64) of the leaf cert der, not the spki. has to match what we pin.
	// prod: fch-toolkit.com (lets encrypt via nginx). redo this if the cert rotates.
	internal const string DefaultProductionCertPinBase64 = "YIwFQ4SNkpqD7/IKN5LoJGZb6HobWSthZujV5nuXeJY=";

	// local dev https (node generate-dev-certs.js). override in cfg if UploadUrl is 127.0.0.1
	internal const string DefaultDevCertPinBase64 = "1yIHbcQORVKiXKfPX4+6Y4dZqkgRTHwO+U3Bzk1nje8=";

	private static bool _enabled;
	private static string _expectedPinBase64;
	private static bool _callbackRegistered;

	internal static void Configure(bool enabled, string certPinSha256Base64)
	{
		_enabled = enabled;
		_expectedPinBase64 = string.IsNullOrWhiteSpace(certPinSha256Base64)
			? DefaultProductionCertPinBase64
			: certPinSha256Base64.Trim();

		if (!_callbackRegistered)
		{
			ServicePointManager.ServerCertificateValidationCallback += ValidateServerCertificate;
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			_callbackRegistered = true;
		}
	}

	private static bool ValidateServerCertificate(
		object sender,
		X509Certificate certificate,
		X509Chain chain,
		SslPolicyErrors sslPolicyErrors)
	{
		if (!_enabled)
			return sslPolicyErrors == SslPolicyErrors.None;

		if (certificate == null)
			return false;

		try
		{
			var cert = new X509Certificate2(certificate);
			using var sha = SHA256.Create();
			string pin = Convert.ToBase64String(sha.ComputeHash(cert.RawData));
			if (pin == _expectedPinBase64)
				return true;
		}
		catch
		{
			return false;
		}

		return false;
	}
}
