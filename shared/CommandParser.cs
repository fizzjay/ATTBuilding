using System;
using System.Globalization;

namespace ATTBuilding
{
    public enum CommandKind { None, Help, Spawn, ServerPing, Nick, DelNick, Nicks, BuildMode, Snapping, SnapAngle, Grid, LivePositionSnap, List, Spawned, Clear, Unknown }

    public sealed class ParsedCommand
    {
        public CommandKind Kind;
        public string Text = "";      // prefab name / list filter / prefab ref (Nick) / nickname (DelNick)
        public string Text2 = "";     // nickname (for Nick)
        public float Number;          // count, angle, grid
        public bool Flag;             // on/off value
        public string Error = "";     // set when Kind == Unknown because of bad syntax
    }

    // Grammar (typed into the game's RuntimeConsole, opened from the new
    // quick-access bubble). A leading "build" is optional for the verbs the
    // user asked for verbatim:
    //   spawn <prefab name> [count]        build spawn <prefab name> [count]
    //   build mode on|off                  buildmode on|off
    //   snapping true|false                build snapping true|false
    //   build snapangle <deg>              build grid <metres>
    //   build livesnap true|false          build list [filter]
    //   build spawned                      build clear        build help
    // Pure string logic only -- unit-testable and identical on PC and Quest.
    public static class CommandParser
    {
        public static ParsedCommand Parse(string raw)
        {
            var result = new ParsedCommand();
            if (string.IsNullOrWhiteSpace(raw)) { result.Kind = CommandKind.None; return result; }
            string[] words = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            if (Is(words[0], "build") || Is(words[0], "attb")) i = 1;
            if (i >= words.Length) { result.Kind = CommandKind.Help; return result; }

            string verb = words[i].ToLowerInvariant();

            if (verb == "mode" || verb == "buildmode")
            {
                // Accept "build mode on", "buildmode on" and a bare toggle.
                result.Kind = CommandKind.BuildMode;
                result.Flag = i + 1 >= words.Length ? !BuildState.BuildMode
                    : SnapMath.ParseBool(words[i + 1], !BuildState.BuildMode);
                return result;
            }
            if (verb == "spawn")
            {
                result.Kind = CommandKind.Spawn;
                int last = words.Length - 1;
                float count;
                if (last > i && float.TryParse(words[last], NumberStyles.Float, CultureInfo.InvariantCulture, out count) && count >= 1f && count <= 20f)
                { result.Number = count; result.Text = Join(words, i + 1, last); }
                else { result.Number = 1; result.Text = Join(words, i + 1, words.Length); }
                if (result.Text.Length == 0) { result.Kind = CommandKind.Unknown; result.Error = "Usage: spawn <prefab name> [count]"; }
                return result;
            }
            if (verb == "snapping" || verb == "snap")
            {
                result.Kind = CommandKind.Snapping;
                result.Flag = i + 1 >= words.Length ? true : SnapMath.ParseBool(words[i + 1], true);
                return result;
            }
            if (verb == "snapangle" || verb == "angle")
            {
                result.Kind = CommandKind.SnapAngle;
                float deg;
                if (i + 1 >= words.Length || !float.TryParse(words[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out deg) || deg < 0f || deg > 180f)
                { result.Kind = CommandKind.Unknown; result.Error = "Usage: build snapangle <0-180, e.g. 15 or 45>"; return result; }
                result.Number = deg; return result;
            }
            if (verb == "grid")
            {
                result.Kind = CommandKind.Grid;
                float metres;
                if (i + 1 >= words.Length || !float.TryParse(words[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out metres) || metres < 0f || metres > 10f)
                { result.Kind = CommandKind.Unknown; result.Error = "Usage: build grid <0-10 metres, e.g. 0.25>"; return result; }
                result.Number = metres; return result;
            }
            if (verb == "livesnap") { result.Kind = CommandKind.LivePositionSnap; result.Flag = i + 1 >= words.Length ? true : SnapMath.ParseBool(words[i + 1], true); return result; }
            if (verb == "list") { result.Kind = CommandKind.List; result.Text = Join(words, i + 1, words.Length); return result; }
            if (verb == "spawned") { result.Kind = CommandKind.Spawned; return result; }
            if (verb == "nick")
            {
                // build nick <prefab name|id> <nickname> -- the nickname is the
                // LAST word; the prefab name may be several words, e.g.
                // build nick weapon rack wall rack   /   build nick anvil anv
                result.Kind = CommandKind.Nick;
                if (i + 2 >= words.Length) { result.Kind = CommandKind.Unknown; result.Error = "Usage: build nick <prefab name|id> <nickname>  e.g. build nick anvil anv"; return result; }
                result.Text = Join(words, i + 1, words.Length - 1);
                result.Text2 = words[words.Length - 1];
                return result;
            }
            if (verb == "nicks") { result.Kind = CommandKind.Nicks; return result; }
            if (verb == "delnick" || (verb == "del" && i + 1 < words.Length && Is(words[i + 1], "nick")))
            {
                // build del nick <nickname>   (also: build delnick <nickname>)
                result.Kind = CommandKind.DelNick;
                int from = verb == "del" ? i + 2 : i + 1;
                result.Text = Join(words, from, words.Length);
                if (result.Text.Length == 0) { result.Kind = CommandKind.Unknown; result.Error = "Usage: build del nick <nickname>"; }
                return result;
            }
            if (verb == "ping") { result.Kind = CommandKind.ServerPing; return result; }
            if (verb == "clear" || verb == "despawn") { result.Kind = CommandKind.Clear; return result; }
            if (verb == "help" || verb == "?") { result.Kind = CommandKind.Help; return result; }

            result.Kind = CommandKind.Unknown;
            result.Error = "Unknown build command '" + verb + "'. Try: build help";
            return result;
        }

        public static readonly string[] HelpLines =
        {
            "build spawn <prefab name> [count] - spawn a prefab in front of you, e.g. build spawn wooden plank",
            "build mode on|off - build mode: open the Prefabulizer bubble on your wrist wheel for the build tablet; point the finger ray at a spawned piece and pull the trigger to control it (no grabbing)",
            "snapping true|false - tablet moves/rotates keep the piece at set angles (straight) and on the position grid",
            "build snapangle <deg> - angle increment for snapping (default 15, use 45 for coarse)",
            "build grid <metres> - position grid applied to tablet moves when snapping is on (default 0.25, 0 = off)",
            "build livesnap true|false - legacy toggle: tablet moves always snap live, so this no longer changes anything",
            "build list [filter] - loaded prefab names you can spawn",
            "build spawned - how many console-spawned objects are tracked for build mode",
            "build ping - ask the server whether ATTBuilding is installed/registered there (reply shows its version)",
            "build nick <prefab name|id> <nickname> - save a short nickname for a prefab, e.g. build nick anvil anv (then: spawn anv)",
            "build nicks - list your saved prefab nicknames (saved in nicks.txt on this machine)",
            "build del nick <nickname> - delete a saved prefab nickname",
            "build clear - remove every console-spawned object",
        };

        private static string Join(string[] words, int from, int toExclusive)
        {
            if (from >= toExclusive) return "";
            return string.Join(" ", words, from, toExclusive - from);
        }
        private static bool Is(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
