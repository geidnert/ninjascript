#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public sealed class AutoEdgeLicenseGate
    {
        private const string ApiBaseUrl = "https://solidparts.se";
        private const string CheckEndpoint = "/api/nt8/license/check";
        internal const int DefaultNextCheckSeconds = 21600;
        internal const int DefaultGracePeriodSeconds = 259200;
        internal const int MaxClientNextCheckSeconds = 60;
        private const string StorageFolderName = "AutoEdge";
        private const string LicensingFolderName = "Licensing";
        private const int CryptProtectUiForbidden = 0x1;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoEdge.NT8.Licensing.v1");
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly HashSet<string> ExplicitBlockingStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unknown_customer",
            "unlicensed",
            "unlicensed_strategy",
            "expired",
            "revoked",
            "suspended",
            "device_blocked",
            "device_limit_exceeded",
            "invalid_request",
            "rate_limited"
        };

        private readonly string strategyKey;
        private readonly string providedLicenseKey;

        private AutoEdgeLicenseGate(string strategyKey, string licenseKey)
        {
            this.strategyKey = NormalizeStrategyKey(strategyKey);
            providedLicenseKey = licenseKey ?? string.Empty;
        }

        public static AutoEdgeLicenseGate ForStrategy(string strategyKey, string licenseKey)
        {
            return new AutoEdgeLicenseGate(strategyKey, licenseKey);
        }

        public AutoEdgeLicenseResult EnsureLicensed()
        {
            return EnsureLicensed(false, null);
        }

        public AutoEdgeLicenseResult EnsureLicensed(bool forceNetwork)
        {
            return EnsureLicensed(forceNetwork, null);
        }

        public AutoEdgeLicenseResult EnsureLicensed(bool forceNetwork, Action<string> log)
        {
            string licenseKey = ResolveLicenseKey();
            AutoEdgeLicenseResult cached = LoadCachedResult();
            DateTime nowUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(licenseKey))
                return AutoEdgeLicenseResult.Blocked(strategyKey, "invalid_request", "AutoEdge license key is missing.", false, nowUtc);

            if (!forceNetwork && cached != null && cached.CanTrade && nowUtc < cached.NextCheckUtc)
                return cached.WithMessage("License active from local cache.");

            try
            {
                AutoEdgeLicenseResult serverResult = CheckServer(licenseKey);
                if (serverResult.CanTrade)
                {
                    SaveLicenseKey(licenseKey);
                    SaveCachedResult(serverResult);
                    return serverResult;
                }

                if (serverResult.IsExplicitBlock)
                    DeleteCache();

                return serverResult;
            }
            catch (Exception ex)
            {
                string message = "AutoEdge licensing server unavailable: " + ex.Message;
                if (log != null)
                    log(message);

                if (cached != null && cached.CanTrade && nowUtc <= cached.GraceUntilUtc)
                    return cached.AsGrace(message, nowUtc);

                return AutoEdgeLicenseResult.Blocked(strategyKey, "network_unavailable", message + " No valid grace is available.", false, nowUtc);
            }
        }

        public void SaveLicenseKey(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return;

            WriteProtectedText(GetLicenseKeyPath(), licenseKey.Trim());
        }

        public string LoadStoredLicenseKey()
        {
            return ReadProtectedText(GetLicenseKeyPath());
        }

        public AutoEdgeLicenseResult LoadCachedResult()
        {
            string json = ReadProtectedText(GetCachePath());
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                Dictionary<string, object> data = Serializer.Deserialize<Dictionary<string, object>>(json);
                return AutoEdgeLicenseResult.FromCache(strategyKey, data);
            }
            catch
            {
                return null;
            }
        }

        public string GetMachineFingerprint()
        {
            return BuildMachineFingerprint();
        }

        private AutoEdgeLicenseResult CheckServer(string licenseKey)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["license_key"] = licenseKey.Trim();
            string machineFingerprint = BuildMachineFingerprint();
            payload["machine_fingerprint"] = machineFingerprint;
            payload["nt8_version"] = ResolveNinjaTraderVersion();
            payload["strategy"] = strategyKey;

            string responseJson = PostJson(ApiBaseUrl + CheckEndpoint, Serializer.Serialize(payload));
            Dictionary<string, object> response = ParseJsonObject(responseJson);
            return AutoEdgeLicenseResult.FromServer(strategyKey, response, DateTime.UtcNow, machineFingerprint);
        }

        private string ResolveLicenseKey()
        {
            if (!string.IsNullOrWhiteSpace(providedLicenseKey))
                return providedLicenseKey.Trim();

            return LoadStoredLicenseKey();
        }

        private static string PostJson(string url, string json)
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            byte[] body = Encoding.UTF8.GetBytes(json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.UserAgent = "AutoEdge-NT8-Licensing/1.0";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.ContentLength = body.Length;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (Stream requestStream = request.GetRequestStream())
                requestStream.Write(body, 0, body.Length);

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                if (ex.Response == null)
                    throw;

                using (HttpWebResponse response = (HttpWebResponse)ex.Response)
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(responseText))
                        return responseText;
                }

                throw;
            }
        }

        private static Dictionary<string, object> ParseJsonObject(string responseJson)
        {
            string normalized = NormalizeResponseText(responseJson);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("License server returned an empty response.");

            if (!normalized.StartsWith("{", StringComparison.Ordinal))
                throw new InvalidOperationException("License server returned non-JSON response: " + BuildResponseSnippet(normalized));

            try
            {
                Dictionary<string, object> response = Serializer.Deserialize<Dictionary<string, object>>(normalized);
                if (response == null)
                    throw new InvalidOperationException("License server returned JSON that was not an object.");

                return response;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("License server returned invalid JSON: " + ex.Message + " Response: " + BuildResponseSnippet(normalized));
            }
        }

        private static string NormalizeResponseText(string value)
        {
            if (value == null)
                return string.Empty;

            return value.TrimStart('\uFEFF').Trim();
        }

        private static string BuildResponseSnippet(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<empty>";

            string sanitized = Regex.Replace(value, "\\s+", " ").Trim();
            return sanitized.Length <= 240 ? sanitized : sanitized.Substring(0, 240) + "...";
        }

        private void SaveCachedResult(AutoEdgeLicenseResult result)
        {
            if (result == null || !result.CanTrade)
                return;

            Dictionary<string, object> data = result.ToCacheDictionary();
            WriteProtectedText(GetCachePath(), Serializer.Serialize(data));
        }

        private void DeleteCache()
        {
            try
            {
                string path = GetCachePath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private string GetLicenseKeyPath()
        {
            return Path.Combine(GetStorageDirectory(), SafeFileName(strategyKey) + ".key");
        }

        private string GetCachePath()
        {
            return Path.Combine(GetStorageDirectory(), SafeFileName(strategyKey) + ".cache");
        }

        private static string GetStorageDirectory()
        {
            string root = NinjaTrader.Core.Globals.UserDataDir;
            string dir = Path.Combine(root, StorageFolderName, LicensingFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteProtectedText(string path, string text)
        {
            byte[] clear = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] encrypted = ProtectBytes(clear);
            File.WriteAllBytes(path, encrypted);
        }

        private static string ReadProtectedText(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return string.Empty;

                byte[] encrypted = File.ReadAllBytes(path);
                byte[] clear = UnprotectBytes(encrypted);
                return Encoding.UTF8.GetString(clear);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] ProtectBytes(byte[] clear)
        {
            DataBlob input = CreateDataBlob(clear);
            DataBlob entropy = CreateDataBlob(Entropy);
            DataBlob output = new DataBlob();

            try
            {
                if (!CryptProtectData(ref input, "AutoEdge NT8 licensing", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                    throw new InvalidOperationException("Windows DPAPI protect failed: " + Marshal.GetLastWin32Error());

                return CopyBlob(output);
            }
            finally
            {
                FreeHGlobalBlob(input);
                FreeHGlobalBlob(entropy);
                FreeLocalBlob(output);
            }
        }

        private static byte[] UnprotectBytes(byte[] encrypted)
        {
            DataBlob input = CreateDataBlob(encrypted);
            DataBlob entropy = CreateDataBlob(Entropy);
            DataBlob output = new DataBlob();

            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                    throw new InvalidOperationException("Windows DPAPI unprotect failed: " + Marshal.GetLastWin32Error());

                return CopyBlob(output);
            }
            finally
            {
                FreeHGlobalBlob(input);
                FreeHGlobalBlob(entropy);
                FreeLocalBlob(output);
            }
        }

        private static DataBlob CreateDataBlob(byte[] data)
        {
            DataBlob blob = new DataBlob();
            blob.cbData = data != null ? data.Length : 0;
            blob.pbData = IntPtr.Zero;

            if (blob.cbData > 0)
            {
                blob.pbData = Marshal.AllocHGlobal(blob.cbData);
                Marshal.Copy(data, 0, blob.pbData, blob.cbData);
            }

            return blob;
        }

        private static byte[] CopyBlob(DataBlob blob)
        {
            if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
                return new byte[0];

            byte[] data = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }

        private static void FreeHGlobalBlob(DataBlob blob)
        {
            if (blob.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(blob.pbData);
        }

        private static void FreeLocalBlob(DataBlob blob)
        {
            if (blob.pbData != IntPtr.Zero)
                LocalFree(blob.pbData);
        }

        private static string BuildMachineFingerprint()
        {
            List<string> parts = new List<string>();
            parts.Add("nt8");
            parts.Add(GetNinjaTraderMachineId());
            parts.Add(Environment.MachineName ?? string.Empty);
            parts.Add(Environment.UserName ?? string.Empty);
            parts.Add(Environment.OSVersion.VersionString ?? string.Empty);
            parts.Add(NinjaTrader.Core.Globals.UserDataDir ?? string.Empty);

            string normalized = string.Join("|", parts.ToArray()).Trim().ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                return ToHex(hash);
            }
        }

        private static string GetNinjaTraderMachineId()
        {
            try
            {
                Type type = Type.GetType("NinjaTrader.Cbi.Globals, NinjaTrader.Core");
                if (type == null)
                    return string.Empty;

                PropertyInfo property = type.GetProperty("MachineId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null)
                {
                    object value = property.GetValue(null, null);
                    return value != null ? value.ToString() : string.Empty;
                }

                FieldInfo field = type.GetField("MachineId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    return value != null ? value.ToString() : string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ResolveNinjaTraderVersion()
        {
            try
            {
                Version version = typeof(Strategy).Assembly.GetName().Version;
                if (version != null)
                    return version.ToString();
            }
            catch
            {
            }

            return "unknown";
        }

        private static string NormalizeStrategyKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static string SafeFileName(string value)
        {
            string safe = NormalizeStrategyKey(value);
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return string.IsNullOrWhiteSpace(safe) ? "UNKNOWN" : safe;
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal static bool IsExplicitBlockingStatus(string status)
        {
            return ExplicitBlockingStatuses.Contains(status ?? string.Empty);
        }

        internal static bool ContainsStrategyKey(object value, string strategyKey)
        {
            string normalized = NormalizeStrategyKey(strategyKey);
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), normalized, StringComparison.OrdinalIgnoreCase);

            foreach (object item in enumerable)
            {
                if (string.Equals(Convert.ToString(item, CultureInfo.InvariantCulture), normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            IntPtr ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }

    public sealed class AutoEdgeLicenseResult
    {
        private AutoEdgeLicenseResult()
        {
        }

        public string StrategyKey { get; private set; }
        public string Status { get; private set; }
        public string Message { get; private set; }
        public bool Licensed { get; private set; }
        public bool HasStrategy { get; private set; }
        public bool IsGrace { get; private set; }
        public bool IsExplicitBlock { get; private set; }
        public DateTime CheckedUtc { get; private set; }
        public DateTime NextCheckUtc { get; private set; }
        public DateTime GraceUntilUtc { get; private set; }
        public int NextCheckSeconds { get; private set; }
        public int GracePeriodSeconds { get; private set; }
        public string LeaseToken { get; private set; }
        public string MachineFingerprint { get; private set; }

        public bool CanTrade
        {
            get { return Licensed && HasStrategy && string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase); }
        }

        public static AutoEdgeLicenseResult FromServer(string strategyKey, Dictionary<string, object> response, DateTime checkedUtc)
        {
            return FromServer(strategyKey, response, checkedUtc, string.Empty);
        }

        public static AutoEdgeLicenseResult FromServer(string strategyKey, Dictionary<string, object> response, DateTime checkedUtc, string machineFingerprint)
        {
            string status = GetString(response, "status");
            bool licensed = GetBool(response, "licensed");
            bool hasStrategy = AutoEdgeLicenseGate.ContainsStrategyKey(GetValue(response, "strategy_keys"), strategyKey);
            string message = GetString(response, "message");

            if (string.IsNullOrWhiteSpace(status))
                status = "invalid_response";
            if (string.IsNullOrWhiteSpace(message))
                message = status;
            if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) && !hasStrategy)
                status = "unlicensed_strategy";

            int nextCheckSeconds = GetInt(response, "next_check_seconds", AutoEdgeLicenseGate.DefaultNextCheckSeconds);
            int gracePeriodSeconds = GetInt(response, "grace_period_seconds", AutoEdgeLicenseGate.DefaultGracePeriodSeconds);
            string leaseToken = GetNestedString(response, "lease", "token");

            AutoEdgeLicenseResult result = new AutoEdgeLicenseResult();
            result.StrategyKey = strategyKey;
            result.Status = status;
            result.Message = message;
            result.Licensed = licensed;
            result.HasStrategy = hasStrategy;
            result.IsGrace = false;
            result.IsExplicitBlock = AutoEdgeLicenseGate.IsExplicitBlockingStatus(status);
            result.CheckedUtc = checkedUtc;
            result.NextCheckSeconds = Math.Max(1, Math.Min(nextCheckSeconds, AutoEdgeLicenseGate.MaxClientNextCheckSeconds));
            result.GracePeriodSeconds = Math.Max(0, gracePeriodSeconds);
            result.NextCheckUtc = checkedUtc.AddSeconds(result.NextCheckSeconds);
            result.GraceUntilUtc = checkedUtc.AddSeconds(result.GracePeriodSeconds);
            result.LeaseToken = leaseToken;
            result.MachineFingerprint = machineFingerprint ?? string.Empty;
            return result;
        }

        public static AutoEdgeLicenseResult FromCache(string strategyKey, Dictionary<string, object> data)
        {
            AutoEdgeLicenseResult result = new AutoEdgeLicenseResult();
            result.StrategyKey = strategyKey;
            result.Status = GetString(data, "status");
            result.Message = GetString(data, "message");
            result.Licensed = GetBool(data, "licensed");
            result.HasStrategy = GetBool(data, "has_strategy");
            result.IsGrace = false;
            result.IsExplicitBlock = AutoEdgeLicenseGate.IsExplicitBlockingStatus(result.Status);
            result.CheckedUtc = GetDateTime(data, "checked_utc", DateTime.MinValue);
            result.NextCheckUtc = GetDateTime(data, "next_check_utc", DateTime.MinValue);
            result.GraceUntilUtc = GetDateTime(data, "grace_until_utc", DateTime.MinValue);
            result.NextCheckSeconds = GetInt(data, "next_check_seconds", AutoEdgeLicenseGate.DefaultNextCheckSeconds);
            result.GracePeriodSeconds = GetInt(data, "grace_period_seconds", AutoEdgeLicenseGate.DefaultGracePeriodSeconds);
            result.LeaseToken = GetString(data, "lease_token");
            result.MachineFingerprint = GetString(data, "machine_fingerprint");
            return result;
        }

        public static AutoEdgeLicenseResult Blocked(string strategyKey, string status, string message, bool explicitBlock, DateTime checkedUtc)
        {
            AutoEdgeLicenseResult result = new AutoEdgeLicenseResult();
            result.StrategyKey = strategyKey;
            result.Status = status;
            result.Message = message;
            result.Licensed = false;
            result.HasStrategy = false;
            result.IsGrace = false;
            result.IsExplicitBlock = explicitBlock || AutoEdgeLicenseGate.IsExplicitBlockingStatus(status);
            result.CheckedUtc = checkedUtc;
            result.NextCheckUtc = checkedUtc.AddMinutes(5);
            result.GraceUntilUtc = checkedUtc;
            result.NextCheckSeconds = 300;
            result.GracePeriodSeconds = 0;
            result.LeaseToken = string.Empty;
            result.MachineFingerprint = string.Empty;
            return result;
        }

        public AutoEdgeLicenseResult WithMessage(string message)
        {
            AutoEdgeLicenseResult copy = Copy();
            copy.Message = string.IsNullOrWhiteSpace(message) ? copy.Message : message;
            return copy;
        }

        public AutoEdgeLicenseResult AsGrace(string message, DateTime nowUtc)
        {
            AutoEdgeLicenseResult copy = Copy();
            copy.IsGrace = true;
            copy.Message = message;
            copy.CheckedUtc = nowUtc;
            copy.NextCheckUtc = nowUtc.AddMinutes(5);
            return copy;
        }

        public Dictionary<string, object> ToCacheDictionary()
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["strategy_key"] = StrategyKey ?? string.Empty;
            data["status"] = Status ?? string.Empty;
            data["message"] = Message ?? string.Empty;
            data["licensed"] = Licensed;
            data["has_strategy"] = HasStrategy;
            data["checked_utc"] = CheckedUtc.ToString("o", CultureInfo.InvariantCulture);
            data["next_check_utc"] = NextCheckUtc.ToString("o", CultureInfo.InvariantCulture);
            data["grace_until_utc"] = GraceUntilUtc.ToString("o", CultureInfo.InvariantCulture);
            data["next_check_seconds"] = NextCheckSeconds;
            data["grace_period_seconds"] = GracePeriodSeconds;
            data["lease_token"] = LeaseToken ?? string.Empty;
            data["machine_fingerprint"] = MachineFingerprint ?? string.Empty;
            return data;
        }

        private AutoEdgeLicenseResult Copy()
        {
            AutoEdgeLicenseResult copy = new AutoEdgeLicenseResult();
            copy.StrategyKey = StrategyKey;
            copy.Status = Status;
            copy.Message = Message;
            copy.Licensed = Licensed;
            copy.HasStrategy = HasStrategy;
            copy.IsGrace = IsGrace;
            copy.IsExplicitBlock = IsExplicitBlock;
            copy.CheckedUtc = CheckedUtc;
            copy.NextCheckUtc = NextCheckUtc;
            copy.GraceUntilUtc = GraceUntilUtc;
            copy.NextCheckSeconds = NextCheckSeconds;
            copy.GracePeriodSeconds = GracePeriodSeconds;
            copy.LeaseToken = LeaseToken;
            copy.MachineFingerprint = MachineFingerprint;
            return copy;
        }

        private static object GetValue(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            object value = GetValue(data, key);
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool GetBool(Dictionary<string, object> data, string key)
        {
            object value = GetValue(data, key);
            if (value == null)
                return false;
            if (value is bool)
                return (bool)value;

            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

        private static int GetInt(Dictionary<string, object> data, string key, int fallback)
        {
            object value = GetValue(data, key);
            if (value == null)
                return fallback;
            if (value is int)
                return (int)value;
            if (value is long)
                return (int)(long)value;
            if (value is decimal)
                return (int)(decimal)value;

            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static DateTime GetDateTime(Dictionary<string, object> data, string key, DateTime fallback)
        {
            string value = GetString(data, key);
            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed.ToUniversalTime()
                : fallback;
        }

        private static string GetNestedString(Dictionary<string, object> data, string objectKey, string valueKey)
        {
            object nested = GetValue(data, objectKey);
            Dictionary<string, object> nestedDictionary = nested as Dictionary<string, object>;
            if (nestedDictionary == null)
                return string.Empty;

            return GetString(nestedDictionary, valueKey);
        }
    }
}
