using System;
using System.IO;
using System.Text;
using LeaderboardMod.Capture;
using LeaderboardMod.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace LeaderboardMod.Sync;

internal sealed class EcKeyFile
{
	public byte[] D;
	public byte[] Qx;
	public byte[] Qy;
}

internal static class ClientSigningKeyStore
{
	private const string KeyFileName = "signing.ec.json";
	private static AsymmetricKeyParameter _privateKey;
	private static ECPublicKeyParameters _publicKey;

	internal static void Initialize()
	{
		string dir = Path.Combine(LeaderboardPaths.GetPluginDirectory(), "client_keys");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, KeyFileName);

		if (File.Exists(path))
		{
			try
			{
				var stored = JsonConvert.DeserializeObject<EcKeyFile>(File.ReadAllText(path));
				if (stored?.D != null && stored.Qx != null && stored.Qy != null)
				{
					LoadFromBytes(stored.D, stored.Qx, stored.Qy);
					LbLog.Step("Backend:Keys", "Loaded client signing key (P-256, BouncyCastle)");
					return;
				}
			}
			catch (Exception ex)
			{
				LbLog.Warn("Backend:Keys", $"Failed loading signing key - generating new: {ex.Message}");
			}
		}

		GenerateAndPersist(path);
	}

	internal static string GetPublicKeyHeaderValue()
	{
		EnsureInitialized();
		var jwk = new JObject
		{
			["kty"] = "EC",
			["crv"] = "P-256",
			["x"] = Base64Url.Encode(CoordinateTo32Bytes(_publicKey.Q.XCoord.ToBigInteger())),
			["y"] = Base64Url.Encode(CoordinateTo32Bytes(_publicKey.Q.YCoord.ToBigInteger()))
		};
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(jwk.ToString(Formatting.None)));
	}

	internal static string SignCanonical(string canonical)
	{
		EnsureInitialized();
		byte[] data = Encoding.UTF8.GetBytes(canonical ?? "");
		var signer = SignerUtilities.GetSigner("SHA256withECDSA");
		signer.Init(true, _privateKey);
		signer.BlockUpdate(data, 0, data.Length);
		return Convert.ToBase64String(signer.GenerateSignature());
	}

	private static void EnsureInitialized()
	{
		if (_privateKey == null || _publicKey == null)
			Initialize();
	}

	private static void GenerateAndPersist(string path)
	{
		var domain = SecNamedCurves.GetByName("secp256r1");
		var ecParams = new ECDomainParameters(domain.Curve, domain.G, domain.N, domain.H, domain.GetSeed());
		var gen = new ECKeyPairGenerator();
		gen.Init(new ECKeyGenerationParameters(ecParams, new SecureRandom()));
		var keyPair = gen.GenerateKeyPair();

		_publicKey = (ECPublicKeyParameters)keyPair.Public;
		_privateKey = keyPair.Private;

		var priv = (ECPrivateKeyParameters)_privateKey;
		var file = new EcKeyFile
		{
			D = priv.D.ToByteArrayUnsigned(),
			Qx = CoordinateTo32Bytes(_publicKey.Q.XCoord.ToBigInteger()),
			Qy = CoordinateTo32Bytes(_publicKey.Q.YCoord.ToBigInteger())
		};
		File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
		LbLog.Step("Backend:Keys", "Generated new client signing key (P-256, BouncyCastle)");
	}

	private static void LoadFromBytes(byte[] d, byte[] qx, byte[] qy)
	{
		var domain = SecNamedCurves.GetByName("secp256r1");
		var ecParams = new ECDomainParameters(domain.Curve, domain.G, domain.N, domain.H, domain.GetSeed());
		var q = domain.Curve.CreatePoint(new BigInteger(1, qx), new BigInteger(1, qy));
		_publicKey = new ECPublicKeyParameters(q, ecParams);
		_privateKey = new ECPrivateKeyParameters(new BigInteger(1, d), ecParams);
	}

	private static byte[] CoordinateTo32Bytes(BigInteger value)
	{
		byte[] raw = value.ToByteArrayUnsigned();
		if (raw.Length == 32)
			return raw;
		if (raw.Length > 32)
			throw new InvalidOperationException("P-256 coordinate longer than 32 bytes");

		var padded = new byte[32];
		Buffer.BlockCopy(raw, 0, padded, 32 - raw.Length, raw.Length);
		return padded;
	}
}
