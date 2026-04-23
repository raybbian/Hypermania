using System;
using System.Text;

namespace Netcode.P2P
{
    public enum LobbyChatOpcode
    {
        Back,
        CsLaunchReq,
        CsLaunch,
        Start,
        CsInput,
    }

    /// <summary>
    /// Wire format for messages sent via <c>SteamMatchmaking.SendLobbyChatMsg</c>.
    /// Layout: <c>HM1|&lt;opcode&gt;|&lt;arg0&gt;|&lt;arg1&gt;|...</c>. The version
    /// prefix is independent of <see cref="Utils.Build.BuildInfo.BuildId"/> and
    /// should be bumped whenever this wire format changes. Build-hash gating at
    /// lobby-join time already guarantees same-commit peers, but a protocol
    /// tag still acts as defense in depth against stray/spoofed traffic.
    /// </summary>
    public static class LobbyChatProtocol
    {
        public const string Version = "HM1";
        private const char Separator = '|';

        public static byte[] Encode(LobbyChatOpcode op, params string[] args)
        {
            var sb = new StringBuilder();
            sb.Append(Version);
            sb.Append(Separator);
            sb.Append(op.ToString());
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i] ?? string.Empty;
                    if (a.IndexOf(Separator) >= 0)
                        throw new ArgumentException($"Argument {i} contains reserved separator '{Separator}': {a}");
                    sb.Append(Separator);
                    sb.Append(a);
                }
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static bool TryDecode(string raw, out LobbyChatOpcode op, out string[] args)
        {
            op = default;
            args = Array.Empty<string>();

            if (string.IsNullOrEmpty(raw))
                return false;

            string[] parts = raw.Split(Separator);
            if (parts.Length < 2)
                return false;
            if (parts[0] != Version)
                return false;
            if (!Enum.TryParse(parts[1], ignoreCase: false, out LobbyChatOpcode parsed))
                return false;

            op = parsed;
            if (parts.Length == 2)
                return true;

            args = new string[parts.Length - 2];
            Array.Copy(parts, 2, args, 0, args.Length);
            return true;
        }
    }
}
