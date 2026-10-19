using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ImprovedHierarchy
{
    /// <summary>
    /// Tiny VCS status probe used by <see cref="ProjectFolderColorizer"/> to draw
    /// a "backed up?" badge alongside each folder icon. Designed to be CHEAP:
    ///
    /// 1. Runs <c>git status --porcelain</c> off the main thread, gated by a
    ///    TTL so the editor never spawns more than one git process at a time.
    /// 2. Maps every reported file path up its directory chain into a
    ///    folder-level dictionary, so the per-frame project-window draw only
    ///    does a single hash lookup per folder.
    /// 3. Self-disables permanently if git isn't on PATH or the project isn't a
    ///    git repo — no error spam, just a one-time toggle flip.
    ///
    /// Future improvements: hook UnityEditor.VersionControl.Provider for users
    /// on Plastic / Perforce. Today's implementation is git-only; the public
    /// API is provider-agnostic so we can layer that in later without touching
    /// the colorizer.
    /// </summary>
    [InitializeOnLoad]
    public static class VcsStatusProvider
    {
        public enum State
        {
            /// <summary>No information available (provider disabled or path not seen).</summary>
            Unknown,
            /// <summary>File / folder tracked AND committed — "backed up".</summary>
            Clean,
            /// <summary>Tracked with uncommitted edits, or a staged add.</summary>
            Modified,
            /// <summary>Not yet tracked by VCS at all.</summary>
            Untracked,
            /// <summary>Matches a .gitignore rule (Library/, Temp/, etc.).</summary>
            Ignored
        }

        private const string EnabledPref = "ImprovedHierarchy.VcsBadges.Enabled";
        private const string MenuPath = "Tools/Improved Hierarchy/VCS Folder Badges";

        // Refresh cadence. Most projects' `git status` runs in <100 ms, so 4 s
        // is a good balance between freshness and "feels instant".
        private const double RefreshIntervalSeconds = 4.0;

        // If git fails (not on PATH, not a repo), don't keep retrying forever
        // — but re-check occasionally so users can install git mid-session.
        private const double FailureRetrySeconds = 60.0;

        private static readonly object Lock = new object();
        private static Dictionary<string, State> _fileStates = new Dictionary<string, State>();
        private static Dictionary<string, State> _folderStates = new Dictionary<string, State>();

        private static double _lastRefreshAttempt;
        private static double _lastFailureAt = -1000.0;
        private static volatile bool _refreshInFlight;
        private static volatile bool _gitProbeFailed;

        private static string _projectRoot;

        static VcsStatusProvider()
        {
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            EditorApplication.update += Tick;
        }

        // -------------------------------------------------------------------------
        // Public API consumed by ProjectFolderColorizer
        // -------------------------------------------------------------------------

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set
            {
                if (Enabled == value) return;
                EditorPrefs.SetBool(EnabledPref, value);
                EditorApplication.RepaintProjectWindow();
                if (value) ScheduleRefresh();
            }
        }

        /// <summary>
        /// Aggregated VCS state for the folder at <paramref name="folderPath"/>
        /// (Unity-style "Assets/Foo" relative path). Folder state is the
        /// highest-severity state of any descendant file:
        /// Modified &gt; Untracked &gt; Ignored &gt; Clean.
        /// </summary>
        public static State GetFolderState(string folderPath)
        {
            if (!Enabled || _gitProbeFailed) return State.Unknown;
            if (string.IsNullOrEmpty(folderPath)) return State.Unknown;
            lock (Lock)
            {
                if (_folderStates.TryGetValue(folderPath, out var s)) return s;
            }
            // Folder not in the dirty/untracked map — by elimination it's
            // either tracked-and-clean or outside the repo. We only badge
            // non-clean states by default to keep the UI uncluttered, so
            // return Clean to let callers opt in if they want.
            return State.Clean;
        }

        // -------------------------------------------------------------------------
        // Refresh pump
        // -------------------------------------------------------------------------

        private static void Tick()
        {
            if (!Enabled || _refreshInFlight) return;

            double now = EditorApplication.timeSinceStartup;

            if (_gitProbeFailed)
            {
                if (now - _lastFailureAt < FailureRetrySeconds) return;
                _gitProbeFailed = false;
            }

            if (now - _lastRefreshAttempt < RefreshIntervalSeconds) return;
            ScheduleRefresh();
        }

        public static void ScheduleRefresh()
        {
            if (_refreshInFlight) return;
            _lastRefreshAttempt = EditorApplication.timeSinceStartup;
            _refreshInFlight = true;

            // Run the git invocation on the thread pool so the editor's main
            // thread never stalls waiting on a subprocess. Results are
            // assembled into local dictionaries then swapped into the public
            // ones under a lock — the project window draw path stays
            // lock-and-lookup only.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RunGitStatus();
                }
                catch (System.Exception e)
                {
                    MarkGitFailed("VcsStatusProvider — git invocation threw: " + e.Message);
                }
                finally
                {
                    _refreshInFlight = false;
                }
            });
        }

        private static void MarkGitFailed(string reason)
        {
            _gitProbeFailed = true;
            _lastFailureAt = EditorApplication.timeSinceStartup;
            // Use Debug.Log only at trace verbosity — most failures are
            // benign (e.g. project not a git repo). We don't want to spam
            // the console for users on Plastic / Perforce / nothing.
            if (Debug.isDebugBuild) Debug.Log(reason);
        }

        // -------------------------------------------------------------------------
        // Git invocation
        // -------------------------------------------------------------------------

        private static void RunGitStatus()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                // --porcelain: stable machine-readable format.
                // -z: NUL-terminated entries so paths with spaces/newlines
                //     don't corrupt the parse.
                // --no-renames: collapse "R orig -> new" into separate
                //     add/delete entries — easier per-file aggregation.
                // --ignored=traditional: report ignored files too, but only
                //     the top-level matches (don't recurse into Library/).
                Arguments = "status --porcelain=v1 -z --no-renames --ignored=traditional",
                WorkingDirectory = _projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            string output;
            int exit;
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        MarkGitFailed("VcsStatusProvider — failed to start git process.");
                        return;
                    }
                    output = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(); } catch { /* ignore */ }
                        MarkGitFailed("VcsStatusProvider — git timed out after 5s.");
                        return;
                    }
                    exit = proc.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MarkGitFailed("VcsStatusProvider — git not found on PATH; badges disabled.");
                return;
            }

            // Exit 128 = not a git repository. That's a permanent state for
            // this project, so disable until manual re-enable.
            if (exit != 0)
            {
                MarkGitFailed($"VcsStatusProvider — git exited with code {exit}.");
                return;
            }

            var files = ParseGitStatus(output);
            var folders = BuildFolderStates(files);

            lock (Lock)
            {
                _fileStates = files;
                _folderStates = folders;
            }

            // Schedule a project window repaint on the main thread.
            EditorApplication.delayCall += EditorApplication.RepaintProjectWindow;
        }

        private static Dictionary<string, State> ParseGitStatus(string output)
        {
            var result = new Dictionary<string, State>(System.StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(output)) return result;

            // -z output: each record is "XY <space> path\0".
            var entries = output.Split('\0');
            foreach (var raw in entries)
            {
                if (raw.Length < 4) continue;
                // First two chars are XY status code; char 2 is a space.
                string codes = raw.Substring(0, 2);
                string path = raw.Substring(3);
                path = path.Replace('\\', '/');

                State s = ClassifyStatusCode(codes);
                // Higher severity wins on duplicates (shouldn't happen with -z
                // / --no-renames, but defensive).
                if (!result.TryGetValue(path, out var existing) || Severity(s) > Severity(existing))
                    result[path] = s;
            }
            return result;
        }

        private static State ClassifyStatusCode(string codes)
        {
            if (codes == "??") return State.Untracked;
            if (codes == "!!") return State.Ignored;
            // Any other non-space character in either column = the file has
            // staged or unstaged changes.
            if (codes[0] != ' ' || codes[1] != ' ') return State.Modified;
            return State.Clean;
        }

        private static int Severity(State s)
        {
            switch (s)
            {
                case State.Modified: return 4;
                case State.Untracked: return 3;
                case State.Ignored: return 1; // ignored is benign noise
                case State.Clean: return 0;
                default: return 0;
            }
        }

        private static Dictionary<string, State> BuildFolderStates(Dictionary<string, State> fileStates)
        {
            var result = new Dictionary<string, State>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fileStates)
            {
                // Walk the directory chain upward, propagating max severity.
                string dir = GetParent(kv.Key);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (result.TryGetValue(dir, out var existing))
                    {
                        if (Severity(kv.Value) > Severity(existing))
                            result[dir] = kv.Value;
                    }
                    else
                    {
                        result[dir] = kv.Value;
                    }
                    dir = GetParent(dir);
                }
            }
            return result;
        }

        private static string GetParent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? null : path.Substring(0, slash);
        }

        // -------------------------------------------------------------------------
        // Menu items
        // -------------------------------------------------------------------------

        [MenuItem(MenuPath, priority = 24)]
        private static void Menu_Toggle()
        {
            Enabled = !Enabled;
            IHGui.Notify("VCS folder badges: " + (Enabled ? "on" : "off"));
        }

        [MenuItem(MenuPath, true)]
        private static bool Menu_Toggle_Validate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        [MenuItem("Tools/Improved Hierarchy/Refresh VCS Badges %&v", priority = 40)]
        private static void Menu_ForceRefresh()
        {
            _gitProbeFailed = false; // reset failure latch
            _lastRefreshAttempt = 0;
            ScheduleRefresh();
            IHGui.Notify("VCS badges: refreshing…");
        }
    }
}
