using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Utils.Build.Editor
{
    /// <summary>
    /// Writes <c>Assets/StreamingAssets/BuildVersion.txt</c> with the current
    /// git commit hash (short) plus a <c>-dirty</c> suffix if the working tree
    /// has uncommitted changes. Runs on every editor domain reload and before
    /// every player build so the shipped binary always has a correct id. The
    /// file is gitignored; <see cref="BuildInfo"/> reads it at runtime.
    /// </summary>
    public static class GitVersionGenerator
    {
        private const string RelativePath = "Assets/StreamingAssets/" + BuildInfo.FileName;

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // Defer a frame so Unity's asset database is ready for ImportAsset.
            EditorApplication.delayCall += () => Regenerate(log: false);
        }

        public class BuildPreprocessor : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                Regenerate(log: true);
            }
        }

        [MenuItem("Tools/Build/Regenerate BuildVersion.txt")]
        private static void RegenerateMenu() => Regenerate(log: true);

        private static void Regenerate(bool log)
        {
            string id = TryComputeBuildId(out string reason);
            string absolute = Path.Combine(Directory.GetCurrentDirectory(), RelativePath);

            try
            {
                string dir = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string existing = File.Exists(absolute) ? File.ReadAllText(absolute).Trim() : null;
                if (existing == id)
                {
                    if (log)
                        Debug.Log($"[GitVersion] BuildVersion.txt up to date: {id}");
                    return;
                }

                File.WriteAllText(absolute, id);
                AssetDatabase.ImportAsset(RelativePath, ImportAssetOptions.ForceUpdate);

                if (log)
                    Debug.Log($"[GitVersion] Wrote BuildVersion.txt: {id} ({reason})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GitVersion] Failed to write {RelativePath}: {ex.Message}");
            }
        }

        private static string TryComputeBuildId(out string reason)
        {
            if (!TryRunGit("rev-parse --short=8 HEAD", out string hash, out string hashErr))
            {
                reason = $"git rev-parse failed: {hashErr}";
                return BuildInfo.Fallback;
            }

            hash = hash.Trim();
            if (string.IsNullOrEmpty(hash))
            {
                reason = "git rev-parse returned empty";
                return BuildInfo.Fallback;
            }

            if (!TryRunGit("status --porcelain", out string status, out string _))
            {
                reason = "git status failed; treating as dirty";
                return hash + "-dirty";
            }

            bool dirty = !string.IsNullOrWhiteSpace(status);
            reason = dirty ? "working tree dirty" : "clean";
            return dirty ? hash + "-dirty" : hash;
        }

        private static bool TryRunGit(string args, out string stdout, out string stderr)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null)
                    return false;
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return false;
            }
        }
    }
}
