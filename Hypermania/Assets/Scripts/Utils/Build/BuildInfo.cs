using System.IO;
using UnityEngine;

namespace Utils.Build
{
    /// <summary>
    /// Exposes the build's identity (git commit hash + dirty flag) for netcode
    /// handshakes. The string is written by <c>GitVersionGenerator</c> into
    /// <c>StreamingAssets/BuildVersion.txt</c> on editor load and before every
    /// player build. This file is gitignored; if it is missing (fresh clone
    /// before the generator has run, or a broken build) we fall back to
    /// <c>"DEV-dirty"</c>, which never matches any peer's id — safe default.
    /// </summary>
    public static class BuildInfo
    {
        public const string FileName = "BuildVersion.txt";
        public const string Fallback = "DEV-dirty";

        private static string _buildId;

        public static string BuildId
        {
            get
            {
                if (_buildId != null)
                    return _buildId;
                _buildId = LoadBuildId();
                return _buildId;
            }
        }

        private static string LoadBuildId()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, FileName);
                if (!File.Exists(path))
                    return Fallback;
                string text = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(text) ? Fallback : text;
            }
            catch
            {
                return Fallback;
            }
        }
    }
}
