using MDTPro.Data;
using MDTPro.Setup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rage;
using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;


namespace MDTPro.Utility {
    internal class Helper {
        private static int _logWritesForTrimCheck;
        internal static T ReadFromJsonFile<T>(string filePath) where T : new() {
            return File.Exists(filePath) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath)) : default;
        }
        internal static void WriteToJsonFile<T>(string filePath, T objectToWrite) where T : new() {
            File.WriteAllText(filePath, JsonConvert.SerializeObject(objectToWrite, Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>Read a value from an INI file. Returns null if file/section/key missing.</summary>
        internal static string ReadIniValue(string filePath, string section, string key) {
            if (!File.Exists(filePath)) return null;
            string[] lines = File.ReadAllLines(filePath);
            bool inSection = false;
            section = "[" + section.TrimStart('[').TrimEnd(']') + "]";
            key = key?.Trim();
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) {
                    inSection = string.Equals(line, section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        /// <summary>Write a line to MDTPro/MDTPro.log in the GTA V directory. Ensures the MDTPro folder exists. Also optionally sends to RAGE in-game log.</summary>
        internal static void Log(string message, bool logInGame = false, LogSeverity severity = LogSeverity.Info) {
            if (logInGame) Game.LogTrivial($"MDT Pro: [{severity}] {message}");
            try {
                string logPath = Setup.SetupController.LogFilePath;
                string logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                string line = $"[{DateTime.Now:O}] [{severity}] {message}\n";
                File.AppendAllText(logPath, line);
                MaybeTrimLogFile(logPath);
            } catch (Exception ex) {
                try { Game.LogTrivial($"MDT Pro: Log write failed: {ex.Message}"); } catch { }
            }
        }

        /// <summary>Writes to MDTPro.log only when <see cref="Setup.Config.verboseArrestCourtLogging"/> is true.</summary>
        internal static void LogArrestCourtVerbose(string message) {
            try {
                if (!Setup.SetupController.GetConfig().verboseArrestCourtLogging) return;
            } catch {
                return;
            }
            Log($"[ArrestCourt] {message}", false, LogSeverity.Info);
        }

        /// <summary>If logFileMaxSizeKb is set, shorten the file when it grows past the limit (keeps the newest half).</summary>
        private static void MaybeTrimLogFile(string logPath) {
            if (++_logWritesForTrimCheck % 32 != 0) return;
            int maxKb = 0;
            try {
                maxKb = Setup.SetupController.GetConfig().logFileMaxSizeKb;
            } catch {
                maxKb = 5120;
            }
            if (maxKb <= 0) return;
            long maxBytes = (long)maxKb * 1024L;
            try {
                var fi = new FileInfo(logPath);
                if (!fi.Exists || fi.Length <= maxBytes) return;
                int keep = (int)Math.Min(int.MaxValue / 4, Math.Max(4096, maxBytes / 2));
                string tail = ReadUtf8TailFromFile(logPath, keep);
                string header = $"[{DateTime.Now:O}] [{LogSeverity.Warning}] MDT Pro log shortened (file exceeded {maxKb} KB). Older lines removed.\n";
                File.WriteAllText(logPath, header + tail);
            } catch {
                // never break gameplay for log maintenance
            }
        }

        private static string ReadUtf8TailFromFile(string path, int maxBytesToRead) {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                long len = fs.Length;
                if (len <= maxBytesToRead) {
                    using (var sr = new StreamReader(fs))
                        return sr.ReadToEnd();
                }
                long start = Math.Max(0, len - maxBytesToRead);
                fs.Position = start;
                if (start > 0) {
                    int b;
                    while ((b = fs.ReadByte()) >= 0 && b != '\n' && fs.Position < len) { }
                }
                using (var sr = new StreamReader(fs))
                    return sr.ReadToEnd();
            }
        }

        internal enum LogSeverity {
            Info, Warning, Error
        }

        /// <summary>Reset the log file to a single initial line. Ensures the MDTPro folder exists.</summary>
        internal static void ClearLog() {
            try {
                string logPath = Setup.SetupController.LogFilePath;
                string logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                File.WriteAllText(logPath, $"[{DateTime.Now:O}] [{LogSeverity.Info}] MDT Pro log initialized\n");
            } catch (Exception ex) {
                try { Game.LogTrivial($"MDT Pro: ClearLog failed: {ex.Message}"); } catch { }
            }
        }

        internal static string GetRequestPostData(HttpListenerRequest req) {
            if (!req.HasEntityBody) return null;
            using Stream body = req.InputStream;
            // HttpListenerRequest.ContentEncoding often defaults to a legacy code page when clients omit charset;
            // browsers and native MDT send UTF-8 JSON — always decode as UTF-8 to avoid corrupt JSON / parse failures.
            using StreamReader reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>Extract string from request body. Handles both JSON string ("...") and plain text for Content-Type: application/json.</summary>
        internal static string GetRequestBodyAsString(HttpListenerRequest req) {
            string body = GetRequestPostData(req);
            if (string.IsNullOrEmpty(body)) return "";
            body = body.Trim();
            if (body.Length >= 2 && body[0] == '"' && body[body.Length - 1] == '"') {
                try { return JsonConvert.DeserializeObject<string>(body) ?? ""; } catch {
                    // Avoid searching for a literal "Name" including quote chars if JSON unescape fails.
                    return body.Substring(1, body.Length - 2);
                }
            }
            return body;
        }

        /// <summary>Parses POST body as a positive int: plain <c>10</c>, JSON number, or JSON string <c>"10"</c> (native MDT may send any of these).</summary>
        internal static int ParsePostBodyAsPositiveInt(string body, int defaultValue) {
            if (string.IsNullOrWhiteSpace(body)) return defaultValue;
            string t = body.Trim();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int direct))
                return direct;
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') {
                try {
                    string inner = JsonConvert.DeserializeObject<string>(t)?.Trim();
                    if (!string.IsNullOrEmpty(inner) && int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out int q))
                        return q;
                } catch { }
            }
            try {
                var tok = JToken.Parse(t);
                if (tok.Type == JTokenType.Integer) return tok.Value<int>();
                if (tok.Type == JTokenType.String) {
                    string s = tok.Value<string>()?.Trim();
                    if (!string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sVal))
                        return sVal;
                }
            } catch { }
            return defaultValue;
        }

        internal static string GetAgencyNameFromScriptName(string scriptName) {
            XmlDocument xmlDoc = new XmlDocument();
            if (!File.Exists("lspdfr/data/agency.xml")) {
                Log("Agency XML file not found, returning null.", true, LogSeverity.Warning);
                return null;
            }

            xmlDoc.Load("lspdfr/data/agency.xml");

            XmlNodeList agencies = xmlDoc.SelectNodes("/Agencies/Agency");

            foreach (XmlNode agency in agencies) {
                XmlNode scriptNameNode = agency.SelectSingleNode("ScriptName");
                if (scriptNameNode != null && scriptNameNode.InnerText.Equals(scriptName, StringComparison.OrdinalIgnoreCase)) {
                    XmlNode nameNode = agency.SelectSingleNode("Name");
                    if (nameNode != null) {
                        return nameNode.InnerText;
                    }
                }
            }

            return null; 
        }


        internal static string GetCallSignFromIPTCommon() {
#pragma warning disable CS0618 // Type or member is obsolete - use StatusHandler.Instance when IPT.Common provides it
            return IPT.Common.Handlers.PlayerHandler.GetCallsign();
#pragma warning restore CS0618
        }

        internal static string GenerateUniqueId(int length) {
            if (length <= 0) return string.Empty;

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            Random random = new Random();
            char[] id = new char[length];

            for (int i = 0; i < length; i++) {
                id[i] = chars[random.Next(chars.Length)];
            }

            return new string(id);
        }

        internal static string GetLocalIPAddress() {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties ipProps = ni.GetIPProperties();

                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses) {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address) &&
                        !addr.Address.ToString().StartsWith("169.254")) {
                        return addr.Address.ToString();
                    }
                }
            }

            return "";
        }

        internal static string GetCourtCaseNumber() {
            return DataController.AllocateCourtCaseNumber();
        }


        private static readonly Random random = new Random();
        internal static int GetRandomInt(int min, int max) {
            if (max < min) (min, max) = (max, min);
            return random.Next(min, max + 1);
        }

        internal static bool UrlAclExists(string url) {
            Process process = new Process();
            process.StartInfo.FileName = "netsh";
            process.StartInfo.Arguments = "http show urlacl";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Contains(url);
        }

        public static bool AddUrlAcl(string url) {
            Process process = new Process();
            process.StartInfo.FileName = "netsh";
            process.StartInfo.Arguments = $"http add urlacl url={url} user=\"{System.Security.Principal.WindowsIdentity.GetCurrent().Name}\"";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.Verb = "runas";

            process.Start();
            process.WaitForExit();

            return process.ExitCode == 0;
        }

        /// <summary>Strip local machine path prefixes from text (e.g. stack traces) before writing logs users may share.</summary>
        internal static string RedactMachineAbsolutePaths(string text) {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"[A-Za-z]:\\(?:[^\\\r\n]+\\)+", "<...>/");
            text = Regex.Replace(text, @"[A-Za-z]:\\([^:\r\n]+\.cs)\b", "<...>/$1");
            text = Regex.Replace(text, @"/Users/[^/\r\n]+/", "<...>/");
            text = Regex.Replace(text, @"/home/[^/\r\n]+/", "<...>/");
            return text;
        }

        internal static string SanitizeExceptionForLog(Exception ex) {
            return ex == null ? string.Empty : RedactMachineAbsolutePaths(ex.ToString());
        }

        /// <summary>LSPDFR <c>County</c> enum <c>ToString()</c> is PascalCase without spaces (e.g. LosSantos). Inserts spaces before in-word capitals for display.</summary>
        internal static string SpacedWordsFromPascalIdentifier(string s) {
            if (string.IsNullOrWhiteSpace(s)) return s == null ? "" : s.Trim();
            string t = s.Trim();
            if (t.IndexOf(' ') >= 0) return t;
            return Regex.Replace(t, "([a-z])([A-Z])", "$1 $2");
        }
    }
}
