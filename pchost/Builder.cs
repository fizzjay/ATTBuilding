
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using ATT.Character.QuickAccessMenu;
using TabletUILib;
using UnityEngine;

namespace ATTBuilding
{
    // Build mode, tablet edition (0.5.0). The whole grab pipeline is GONE --
    // no Pickups are added, no hands are polled for dwell-grabs, nothing is
    // carried. Instead:
    //   * the wrist wheel gains a "Prefabulizer" bubble (TabletUILib) which
    //     summons the game's Social Tablet showing our control pages;
    //   * a toggle on the tablet turns on a ray from the local player's
    //     pointer finger; hovering one of OUR spawned prefabs (and only
    //     those -- TrackedRegistry) frames it with a wire box outline;
    //   * pulling the trigger (XR trigger via reflection; dwell-select as a
    //     fallback) makes that prefab the thing the tablet controls;
    //   * MOVE / ROTATE / SCALE pages then nudge the selected prefab.
    // Per-frame cost while building: the ray/dot visuals only. Targeting
    // runs at 10 Hz and is pure AABB float math over cached per-piece
    // probes -- NO physics raycast of any kind, NO material work, NO file
    // IO. (Cloned-material glow + Collider.Raycast + hover logging all
    // turned out to be frame killers on Quest; see BUILD.md 0.5.4.)
    internal static class Builder
    {
        // ---- tablet wiring ------------------------------------------------
        private static bool _registered;
        private static readonly Func<Page> ControlHomeFunc = new Func<Page>(ControlHomePage);
        private static readonly Func<Page> SettingsHomeFunc = new Func<Page>(SettingsHomePage);
        private static bool _wasShowing;

        internal static void RegisterTablet()
        {
            if (_registered) return;
            _registered = true;
            // Two tablets, one state: CONTROL (top-level wheel bubble) drives
            // the selected piece; SETTINGS (under the wheel's APPS bubble)
            // holds the amounts/behaviour. ATT has exactly one physical
            // Social Tablet, so they share its screen -- but they are
            // separate home stacks, cross-linked, and neither resets the
            // other: switching is one tap and selection/settings persist.
            TabletBubble bubble = TabletUI.AddBubble("Prefabulizer", "PFB");
            bubble.App("Prefabulizer", "PFB", ControlHomeFunc);
            TabletUI.AddApp("Prefab Settings", "SET", SettingsHomeFunc);
            try { TriggerInput.Warm(); } catch { } // resolve XR types now, at load -- never during a hover
            Diag.Log("builder: Prefabulizer (control) + Prefab Settings tablets registered on the wrist wheel.");
        }

        internal static void SetBuildMode(bool on)
        {
            BuildState.BuildMode = on;
            RayOn = on; // entering build mode arms the finger ray immediately
            if (!on) { HideOutline(); HideFx(); _hover = null; }
            Diag.Log("builder: build mode " + (on ? "ON (finger ray armed; Prefabulizer tablet controls the selected piece)" : "off") + ".");
        }

        // ---- state ---------------------------------------------------------
        internal static bool RayOn;
        private static GameObject _selected;
        private static int _selectedId;
        private static Vector3 _baseScale = Vector3.one;
        private static float _scaleFactor = 1f;
        private static bool _frozen = true;

        private static GameObject _hover;
        private static string _note = "";
        private static float _noteUntil = -1f;

        private static readonly string[] StepLabels = { "0.01 m", "0.05 m", "0.10 m", "0.25 m", "0.50 m", "1.00 m" };
        private static readonly float[] StepValues = { 0.01f, 0.05f, 0.10f, 0.25f, 0.50f, 1.00f };

        // Driven from the Settings tablet; persisted via MelonPreferences
        // (Main.Settings). Rotate amount reuses BuildState.AngleStepDegrees.
        internal static int RayHand;            // 0 = right (default), 1 = left
        internal static float RayLength = 20f;  // metres
        internal static bool NewUi;             // test D-pad home (Settings toggle)
        private static float _moveStep = 0.1f;
        private static float _scaleStep = 1.25f;
        internal static float MoveStep { get { return _moveStep; } set { _moveStep = Mathf.Clamp(value, 0.005f, 2f); } }
        internal static float ScaleStep { get { return _scaleStep; } set { _scaleStep = Mathf.Clamp(value, 1.02f, 2f); } }

        private static int NearestStepIdx()
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < StepValues.Length; i++) { float d = Mathf.Abs(StepValues[i] - _moveStep); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        private static void Note(string msg)
        {
            _note = msg;
            _noteUntil = Time.unscaledTime + 8f;
            Diag.Log("builder: " + msg);
        }

        private static string StatusText()
        {
            if (Time.unscaledTime < _noteUntil && _note.Length > 0) return _note;
            return "Point the finger ray at a piece you spawned and pull the trigger -- the tablet controls it.";
        }

        private static string CleanName(GameObject go)
        {
            if (go == null) return "(gone)";
            return go.name.Replace("(Clone)", "").Trim();
        }

        private static string SelectedLabel()
        {
            return _selected == null ? "nothing yet" : CleanName(_selected);
        }

        // ---- pages -----------------------------------------------------------
        private static Page ControlHomePage()
        {
            if (NewUi) return NewUiHomePage();
            var p = new Page("PREFABULIZER", StatusText());
            p.Toggle("Build mode", () => BuildState.BuildMode, v => SetBuildMode(v));
            p.Toggle("Finger ray", () => RayOn, v => { RayOn = v; if (!v) { _hover = null; HideFx(); } });
            p.Link("Open SETTINGS tablet", "amounts / ray / snapping", SettingsHomePage);
            p.Text("Controlling", SelectedLabel());
            p.Link("Move piece", F(_moveStep) + " m steps", MovePage);
            p.Link("Rotate piece", RotateStep() + " deg steps", RotatePage);
            p.Link("Scale piece", "x" + F(_scaleStep) + " steps", ScalePage);
            p.Link("Pieces", TrackedRegistry.Count + " spawned", PiecesPage);
            if (_selected != null) p.Link("DELETE selected piece", CleanName(_selected), DeleteConfirmPage);
            p.Link("How to use", "60-second guide", HelpPage);
            p.Button("Close tablet", delegate { try { TabletUI.Close(); } catch { } });
            p.Footer = "ATTBuilding " + Mod.Version + " -- point, trigger, build.";
            return p;
        }

        // New UI (test): the control home BECOMES the D-pad -- four
        // direction buttons around a centre CLONE, raise/lower beneath,
        // everything else one tap away under MORE. (The tablet renders
        // rows, so the "quarter-circle" pad is a row layout evoking it.)
        private static Page NewUiHomePage()
        {
            var p = new Page("PREFABULIZER", StatusText());
            p.Toggle("Build mode", () => BuildState.BuildMode, v => SetBuildMode(v));
            p.Toggle("Finger ray", () => RayOn, v => { RayOn = v; if (!v) { _hover = null; HideFx(); } });
            p.Link("Open SETTINGS tablet", "amounts / ray / snapping", SettingsHomePage);
            p.Text("Controlling", SelectedLabel());
            if (_selected == null)
            {
                p.Text("Select a piece", "point the ray at it + pull the trigger");
            }
            else
            {
                string step = F(_moveStep) + " m";
                p.Button("^ FORWARD", step, delegate { Nudge(HeadRight(), HeadFwd(), 0f, 1f); });
                p.Button("< LEFT", step, delegate { Nudge(HeadRight(), HeadFwd(), -1f, 0f); });
                p.Button("CLONE (copy)", "duplicate this piece", delegate { CloneSelected(); });
                p.Button("RIGHT >", step, delegate { Nudge(HeadRight(), HeadFwd(), 1f, 0f); });
                p.Button("v BACK", step, delegate { Nudge(HeadRight(), HeadFwd(), 0f, -1f); });
                p.Button("RAISE", "+" + step, delegate { NudgeWorld(Vector3.up); });
                p.Button("LOWER", "-" + step, delegate { NudgeWorld(Vector3.down); });
            }
            p.Link("More (rotate / scale / pieces)", MorePage);
            if (_selected != null) p.Link("DELETE selected piece", CleanName(_selected), DeleteConfirmPage);
            p.Button("Close tablet", delegate { try { TabletUI.Close(); } catch { } });
            p.Footer = "ATTBuilding " + Mod.Version + " -- new UI (test). Classic pages live under Settings > New UI off.";
            return p;
        }

        private static Page MorePage()
        {
            var p = new Page("MORE", StatusText());
            p.Link("Rotate", RotateStep() + " deg steps", RotatePage);
            p.Link("Scale", "x" + F(_scaleStep) + " steps", ScalePage);
            p.Link("Pieces", TrackedRegistry.Count + " spawned", PiecesPage);
            p.Link("Classic move page", F(_moveStep) + " m steps", MovePage);
            p.Link("How to use", "60-second guide", HelpPage);
            return p;
        }

        // Duplicate the selected piece through the normal spawn path (lands
        // in front of you like a fresh spawn, fully tracked + remembered).
        private static void CloneSelected()
        {
            if (_selected == null) { Note("Nothing selected to clone."); return; }
            string what = null;
            try { var np = _selected.GetComponent<NetworkPrefab>(); if (np != null) what = np.Hash.ToString(); } catch { }
            if (string.IsNullOrEmpty(what)) what = CleanName(_selected);
            string name = CleanName(_selected);
            Spawner.SpawnByName(what, 1, delegate (string s) { Note(s); });
            Note("Cloning '" + name + "' -- the copy lands in front of you; point + trigger it to control it.");
        }

        // The second tablet: everything about HOW MUCH things move, plus
        // snapping and ray behaviour. Wheel: APPS -> Prefab Settings (also
        // linked from Control). Shares all state with the Control tablet.
        private static Page SettingsHomePage()
        {
            var p = new Page("PREFAB SETTINGS", "Amounts and behaviour -- saved automatically.");
            p.Link("Open CONTROL tablet", "drive the selected piece", ControlHomePage);
            p.Toggle("New UI (test D-pad home)", () => NewUi, v => { NewUi = v; Settings.Save(); try { TabletUI.Refresh(); } catch { } });
            p.Slider("Move amount (m)", () => _moveStep, v => { MoveStep = v; Settings.Save(); }, 0.01f, 1f, 0.01f);
            p.Choice("Move amount presets", StepLabels, () => NearestStepIdx(), v => { MoveStep = StepValues[Mathf.Clamp(v, 0, StepValues.Length - 1)]; Settings.Save(); });
            p.Slider("Rotate amount (deg)", () => BuildState.AngleStepDegrees, v => { BuildState.AngleStepDegrees = Mathf.Clamp(v, 1f, 90f); Settings.Save(); }, 1f, 90f, 1f);
            p.Slider("Scale amount (x)", () => _scaleStep, v => { ScaleStep = v; Settings.Save(); }, 1.05f, 1.5f, 0.05f);
            p.Toggle("Snapping (straight + grid)", () => BuildState.Snapping, v => { BuildState.Snapping = v; Settings.Save(); });
            p.Slider("Snap grid (m)", () => BuildState.GridMetres, v => { BuildState.GridMetres = Mathf.Clamp(v, 0f, 1f); Settings.Save(); }, 0f, 1f, 0.05f);
            p.Choice("Ray hand", new[] { "Right", "Left" }, () => RayHand, v => { RayHand = Mathf.Clamp(v, 0, 1); Settings.Save(); });
            p.Slider("Ray length (m)", () => RayLength, v => { RayLength = Mathf.Clamp(v, 5f, 40f); Settings.Save(); }, 5f, 40f, 1f);
            p.Text("Trigger input", TriggerInput.StatusText());
            p.Footer = "ATTBuilding " + Mod.Version + " -- settings save automatically.";
            return p;
        }

        private static Page DeleteConfirmPage()
        {
            var p = new Page("DELETE PIECE?", "This despawns it for everyone on the server.");
            p.Text("Piece", SelectedLabel());
            p.Button("YES -- delete it", delegate { DeleteSelected(); try { TabletUI.GoHome(); } catch { } });
            p.Text("Keep it", "use the back button");
            return p;
        }

        private static Page MovePage()
        {
            var p = new Page("MOVE", StatusText());
            p.Text("Controlling", SelectedLabel());
            if (_selected == null) { p.Text("Nothing selected", "finger-ray a spawned piece + trigger"); return p; }
            p.Text("Step", F(_moveStep) + " m (Settings tablet)");
            string d = "+/- " + F(_moveStep) + " m";
            p.Button("FORWARD", d, delegate { Nudge(HeadRight(), HeadFwd(), 0f, 1f); });
            p.Button("BACK", d, delegate { Nudge(HeadRight(), HeadFwd(), 0f, -1f); });
            p.Button("LEFT", d, delegate { Nudge(HeadRight(), HeadFwd(), -1f, 0f); });
            p.Button("RIGHT", d, delegate { Nudge(HeadRight(), HeadFwd(), 1f, 0f); });
            p.Button("UP", d, delegate { NudgeWorld(Vector3.up); });
            p.Button("DOWN", d, delegate { NudgeWorld(Vector3.down); });
            p.Toggle("Frozen (can't fall)", () => _frozen, v => SetFrozenSelected(v));
            Vector3 pos = _selected.transform.position;
            p.Text("Position", F(pos.x) + ", " + F(pos.y) + ", " + F(pos.z));
            return p;
        }

        private static Page RotatePage()
        {
            var p = new Page("ROTATE", StatusText());
            p.Text("Controlling", SelectedLabel());
            if (_selected == null) { p.Text("Nothing selected", "finger-ray a spawned piece + trigger"); return p; }
            string d = RotateStep() + " deg";
            p.Button("Yaw left", "-" + d, delegate { Rotate(Vector3.up, -RotateStep()); });
            p.Button("Yaw right", "+" + d, delegate { Rotate(Vector3.up, RotateStep()); });
            p.Button("Pitch up", "+" + d, delegate { Rotate(HeadRight(), -RotateStep()); });
            p.Button("Pitch down", "-" + d, delegate { Rotate(HeadRight(), RotateStep()); });
            p.Button("Roll left", "-" + d, delegate { Rotate(HeadFwdRaw(), -RotateStep()); });
            p.Button("Roll right", "+" + d, delegate { Rotate(HeadFwdRaw(), RotateStep()); });
            p.Button("Make straight (snap now)", "angles -> " + BuildState.AngleStepDegrees + " deg steps", delegate { SnapNow(); });
            p.Button("Level upright", "keep facing, flatten tilt", delegate { LevelNow(); });
            p.Toggle("Frozen (can't fall)", () => _frozen, v => SetFrozenSelected(v));
            return p;
        }

        private static Page ScalePage()
        {
            var p = new Page("SCALE", StatusText());
            p.Text("Controlling", SelectedLabel());
            if (_selected == null) { p.Text("Nothing selected", "finger-ray a spawned piece + trigger"); return p; }
            p.Text("Current scale", Percent(_scaleFactor) + " of spawned size");
            p.Button("Bigger", "x" + F(_scaleStep), delegate { SetScaleFactor(_scaleFactor * _scaleStep); });
            p.Button("Smaller", "/" + F(_scaleStep), delegate { SetScaleFactor(_scaleFactor / _scaleStep); });
            p.Slider("Scale (fine)", () => _scaleFactor, v => SetScaleFactor(v), 0.1f, 5f, 0.05f);
            p.Button("Reset scale", "x1.00", delegate { SetScaleFactor(1f); });
            p.Toggle("Frozen (can't fall)", () => _frozen, v => SetFrozenSelected(v));
            return p;
        }

        private static Page PiecesPage()
        {
            var p = new Page("PIECES", StatusText());
            p.Text("Spawned pieces", TrackedRegistry.Count + " tracked");
            p.Text("Build memory", SpawnLedger.Count + " remembered (all sessions)");
            p.Text("Controlling", SelectedLabel());
            p.Button("Select next piece", delegate { CycleSelection(1); });
            p.Button("Select previous piece", delegate { CycleSelection(-1); });
            if (!string.IsNullOrEmpty(Spawner.LastSpawnName))
                p.Button("Spawn another", Spawner.LastSpawnName, delegate { Spawner.SpawnByName(Spawner.LastSpawnName, 1, delegate (string s) { Note(s); }); });
            else
                p.Text("Spawn another", "spawn once from the console first");
            if (_selected != null)
                p.Button("Clone selected piece", CleanName(_selected), delegate { CloneSelected(); });
            if (_selected != null)
                p.Button("DELETE selected piece", CleanName(_selected), delegate { DeleteSelected(); });
            if (TrackedRegistry.Count > 0)
                p.Link("Clear ALL spawned pieces", "asks first", ConfirmClearPage);
            return p;
        }

        private static Page ConfirmClearPage()
        {
            var p = new Page("CLEAR ALL?", "This removes every piece ATTBuilding spawned.");
            p.Text("Pieces tracked", TrackedRegistry.Count + "");
            p.Button("YES -- clear all", delegate { Spawner.ClearSpawned(delegate (string s) { Note(s); }); ClearSelection(); });
            p.Text("No", "use the back button to keep building");
            return p;
        }

        private static Page HelpPage()
        {
            var p = new Page("HOW TO USE", "Prefabulizer in 60 seconds");
            p.Text("1. Spawn pieces", "console bubble, then: build spawn <name>");
            p.Text("2. Open this tablet", "wrist wheel > Prefabulizer (settings: APPS > Prefab Settings)");
            p.Text("3. Build mode + ray on", "toggles on the home page");
            p.Text("4. Point at a piece", "its whole mesh glows amber");
            p.Text("5. Pull the trigger", "now the tablet controls it (green)");
            p.Text("6. Switch any time", "point + trigger a different piece");
            p.Text("7. Move / Rotate / Scale", "pages on the home screen");
            p.Text("Staying straight", "snapping on + build snapangle / build grid in the console");
            p.Text("Only your spawns", "world items are never selected");
            p.Text("Forever yours", "spawned pieces are remembered -- rejoin later and they are editable again");
            p.Footer = "No grabbing anywhere -- nothing to fight the hand physics.";
            return p;
        }

        // ---- frame tick ------------------------------------------------------
        internal static void Tick()
        {
            // No TabletUI.Tick() here: it is internal to the TabletUILib DLL,
            // whose own mod entrypoint ticks it. We only run the ray + watch
            // for the tablet opening (rising edge -> build mode on).
            if (!_registered) return;

            bool showing = false;
            try { showing = TabletUI.IsShowing(ControlHomeFunc) || TabletUI.IsShowing(SettingsHomeFunc); } catch { }
            if (showing && !_wasShowing) SetBuildMode(true); // opening a tablet IS entering build mode
            _wasShowing = showing;

            ValidateSelection();
            if (!BuildState.BuildMode)
            {
                _hover = null; HideOutline(); HideFx(); _dwellId = 0;
                return;
            }
            if (!RayOn)
            {
                _hover = null; SyncOutline(); HideFx(); _dwellId = 0;
                return;
            }
            // Targeting at 10 Hz; the visible line/dot still follow the hand
            // every frame from the cached result, so aiming feels instant.
            // The tick is timed: a one-line perf report lands in debug.log
            // every 15 s so hover cost is measured, never guessed.
            if (Time.unscaledTime >= _nextRayTick)
            {
                _nextRayTick = Time.unscaledTime + 0.1f;
                _perfWatch.Restart();
                UpdateRaycast();
                SyncOutline();
                _perfWatch.Stop();
                double ms = _perfWatch.Elapsed.TotalMilliseconds;
                _perfTicks++; _perfMsTotal += ms; if (ms > _perfMsMax) _perfMsMax = ms;
            }
            if (Time.unscaledTime >= _perfNextReport)
            {
                _perfNextReport = Time.unscaledTime + 15f;
                if (_perfTicks > 0)
                    Diag.FileOnly("builder perf: raytick avg " + (_perfMsTotal / _perfTicks).ToString("0.00", CultureInfo.InvariantCulture)
                        + " ms, max " + _perfMsMax.ToString("0.00", CultureInfo.InvariantCulture)
                        + " ms over " + _perfTicks + " ticks; probes=" + _probes.Count
                        + ", hoverChanges=" + _perfHoverChanges + ", triggerFailStreak=" + TriggerInput.FailStreak);
                _perfTicks = 0; _perfMsTotal = 0; _perfMsMax = 0; _perfHoverChanges = 0;
            }
            UpdateRayVisual();
        }

        private static void ValidateSelection()
        {
            if (_selected == null) { _selectedId = 0; return; } // == is Unity's fake-null: also catches destroyed
            if (!TrackedRegistry.Contains(_selectedId)) ClearSelection();
        }

        private static void ClearSelection()
        {
            if (_outlineRoot == _selected) HideOutline();
            _selected = null; _selectedId = 0; _scaleFactor = 1f;
        }

        // ---- probes: raycast-free targeting over OUR pieces only -------------
        // A world Physics.Raycast ploughs through every collider near the ray
        // (towns + mods like QuestForest pack in thousands), which is why
        // hovering a prefab tanked the frame rate. Instead each tracked
        // piece gets a cached probe (world AABB + its colliders): per ray
        // tick we do float math against the boxes, then confirm against the
        // winning piece's own colliders only (Collider.Raycast).
        private sealed class Probe
        {
            public GameObject Root;
            public Collider[] Colliders; // cached for AfterEdit rebuilds only -- never touched by the ray tick
            public Bounds Box;
            public Vector3 Anchor; // root position when the box was computed
            public int RegistryId = -1;
        }
        private static readonly System.Collections.Generic.HashSet<int> _knownRegistryIds = new System.Collections.Generic.HashSet<int>();

        private static readonly System.Collections.Generic.Dictionary<int, Probe> _probes = new System.Collections.Generic.Dictionary<int, Probe>();
        internal static bool ProbesDirty = true; // set by Spawner whenever tracking changes

        private static Probe BuildProbe(GameObject go)
        {
            var cols = new System.Collections.Generic.List<Collider>();
            Bounds box = new Bounds(go.transform.position, new Vector3(0.25f, 0.25f, 0.25f));
            bool has = false;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                cols.Add(c);
                try { if (!has) { box = c.bounds; has = true; } else box.Encapsulate(c.bounds); } catch { }
            }
            return new Probe { Root = go, Colliders = cols.ToArray(), Box = box, Anchor = go.transform.position };
        }

        private static void RefreshProbe(Probe pr)
        {
            var fresh = BuildProbe(pr.Root);
            pr.Colliders = fresh.Colliders; pr.Box = fresh.Box; pr.Anchor = fresh.Anchor;
        }

        // Incremental: only NEW registry ids pay a scene lookup, once.
        // (The old version cleared the table and re-scanned the whole
        // scene -- FindObjectsOfType over every NetworkPrefab -- on every
        // tracking change.)
        private static void SyncProbes()
        {
            var ids = TrackedRegistry.Snapshot();
            var alive = new System.Collections.Generic.HashSet<int>(ids);
            var stale = new List<int>();
            foreach (int id in _knownRegistryIds) if (!alive.Contains(id)) stale.Add(id);
            if (stale.Count > 0)
            {
                foreach (int id in stale) _knownRegistryIds.Remove(id);
                var deadRoots = new List<int>();
                foreach (var kv in _probes) if (stale.Contains(kv.Value.RegistryId)) deadRoots.Add(kv.Key);
                foreach (int rid in deadRoots) _probes.Remove(rid);
            }
            foreach (int id in ids)
            {
                if (_knownRegistryIds.Contains(id)) continue;
                var go = Spawner.FindByInstanceId(id);
                if (go == null) continue; // not streamed in yet; retried next sync
                _knownRegistryIds.Add(id);
                int rid = go.GetInstanceID();
                if (!_probes.ContainsKey(rid)) { var pr = BuildProbe(go); pr.RegistryId = id; _probes[rid] = pr; }
            }
            ProbesDirty = false;
        }

        // ---- finger ray (right hand by default; Settings can flip it) --------
        private static Transform _handR, _handL, _tipR, _tipL;
        private static int _handOwnerId;
        private static float _nextRayTick;
        private static float _nextProbeSync;
        private static float _nextHandTry;
        private static readonly System.Diagnostics.Stopwatch _perfWatch = new System.Diagnostics.Stopwatch();
        private static int _perfTicks; private static double _perfMsTotal; private static double _perfMsMax;
        private static int _perfHoverChanges; private static float _perfNextReport;
        private static Vector3 _hitPoint;
        private static bool _hasHit;

        private static void AcquireHands()
        {
            try
            {
                var player = PlayerController.Current;
                if (player == null) { _handR = _handL = _tipR = _tipL = null; _handOwnerId = 0; return; }
                if (_handOwnerId == player.GetInstanceID() && (_handR != null || _handL != null)) return;
                _handR = _handL = _tipR = _tipL = null;
                Transform head = null;
                try { head = player.Head; } catch { }
                var unclaimed = new List<Transform>();
                foreach (var interactor in player.GetComponentsInChildren<Interactor>())
                {
                    if (interactor == null || !interactor.IsLocal) continue;
                    int side = ClassifyHand(interactor.transform, head); // 0 right, 1 left, -1 unknown
                    if (side == 0 && _handR == null) { _handR = interactor.transform; _tipR = FindTip(_handR); }
                    else if (side == 1 && _handL == null) { _handL = interactor.transform; _tipL = FindTip(_handL); }
                    else unclaimed.Add(interactor.transform);
                }
                foreach (var u in unclaimed)
                {
                    if (_handR == null) { _handR = u; _tipR = FindTip(u); }
                    else if (_handL == null) { _handL = u; _tipL = FindTip(u); }
                }
                _handOwnerId = player.GetInstanceID();
                Diag.FileOnly("builder: ray hands R='" + (_handR == null ? "-" : _handR.name) + "' L='" + (_handL == null ? "-" : _handL.name) + "' (ray on " + (RayHand == 0 ? "RIGHT" : "LEFT") + ").");
            }
            catch (Exception ex) { Mod.LogOnce("builder hands: " + ex.Message); }
        }

        // Which hand is this? Trust rig names first ("...Right..."), then
        // which side of the head it was on when first seen.
        private static int ClassifyHand(Transform hand, Transform head)
        {
            for (var t = hand; t != null; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("right")) return 0;
                if (n.Contains("left")) return 1;
            }
            if (head != null)
            {
                try { return head.InverseTransformPoint(hand.position).x >= 0f ? 0 : 1; } catch { }
            }
            return -1;
        }

        private static Transform RayHandTransform(out Transform tip)
        {
            Transform t = RayHand == 0 ? _handR : _handL;
            tip = RayHand == 0 ? _tipR : _tipL;
            if (t == null) { t = RayHand == 0 ? _handL : _handR; tip = RayHand == 0 ? _tipL : _tipR; }
            if (t == null && Time.unscaledTime >= _nextHandTry)
            {
                _nextHandTry = Time.unscaledTime + 1f;
                AcquireHands();
                t = RayHand == 0 ? _handR : _handL;
                tip = RayHand == 0 ? _tipR : _tipL;
                if (t == null) { t = RayHand == 0 ? _handL : _handR; tip = RayHand == 0 ? _tipL : _tipR; }
            }
            return t;
        }

        // The ray should leave from the pointer fingertip when the hand rig
        // exposes one ("IndexTip"/"Pointer"...); otherwise the hand origin.
        // Direction is always the hand's forward (pointing direction).
        private static Transform FindTip(Transform hand)
        {
            Transform fallback = null;
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == hand) continue;
                string n = t.name.ToLowerInvariant();
                bool tippy = n.Contains("tip") || n.Contains("pointer") || n.Contains("point");
                if (!tippy) continue;
                if (n.Contains("index") || n.Contains("pointer") || n.Contains("point")) return t;
                if (fallback == null) fallback = t;
            }
            return fallback;
        }

        private static GameObject TrackedRootOf(Transform t)
        {
            int depth = 0;
            while (t != null && depth++ < 24)
            {
                if (TrackedRegistry.Contains(t.gameObject.GetInstanceID())) return t.gameObject;
                t = t.parent;
            }
            return null;
        }

        // Runs at 10 Hz (see Tick). PURE MATH: a slab test against each
        // tracked piece's cached world AABB. No Physics.Raycast, no
        // Collider.Raycast, no scene queries, no allocations, no logging.
        // A piece moved by someone else just slides its cached box along
        // (rebuilds happen only on tablet edits, in AfterEdit).
        private static void UpdateRaycast()
        {
            Transform tip;
            var hand = RayHandTransform(out tip);
            if (hand == null) { SetHover(null); _hasHit = false; return; }
            if (ProbesDirty || Time.unscaledTime >= _nextProbeSync) { _nextProbeSync = Time.unscaledTime + 5f; SyncProbes(); }
            Vector3 origin = tip != null ? tip.position : hand.position;
            var ray = new Ray(origin, hand.forward);
            GameObject best = null; float bestDist = float.MaxValue;
            foreach (var pair in _probes)
            {
                var pr = pair.Value;
                if (pr.Root == null) { ProbesDirty = true; continue; }
                Vector3 delta = pr.Root.transform.position - pr.Anchor;
                if (delta.sqrMagnitude > 0.0004f) // slid >2 cm: translate the box, never rebuild here
                {
                    var b = pr.Box; b.center = b.center + delta; pr.Box = b; pr.Anchor = pr.Root.transform.position;
                    if (pr.Root == _outlineRoot) _outlineDirty = true;
                }
                float d = 0f;
                if (!pr.Box.Contains(origin) && !pr.Box.IntersectRay(ray, out d)) continue;
                if (d > RayLength) continue;
                if (d < bestDist) { bestDist = d; best = pr.Root; _hitPoint = origin + hand.forward * d; }
            }
            _hasHit = best != null;
            SetHover(best);
            // The trigger is polled once per tick whether or not the ray is
            // on a piece, so press/release edges are always tracked cleanly
            // (and the poll can never become hover-only work).
            bool edge = TriggerInput.PressedEdge(RayHand);
            if (best != null) PollSelect(best, edge);
        }

        private static void SetHover(GameObject root)
        {
            if (root == _hover) return;
            _hover = root;
            _dwellId = 0;
            _perfHoverChanges++; // counted for the perf report; NO logging here (file IO on hover flicker was a lag source)
        }

        // ---- selection trigger: XR trigger, dwell fallback ----------------------
        private static int _dwellId;
        private static float _dwellSince;

        private static void PollSelect(GameObject target, bool triggerEdge)
        {
            if (triggerEdge) { Select(target); return; }
            if (TriggerInput.Available) { _dwellId = 0; return; }
            // No XR trigger found on this rig: holding the ray steady on the
            // piece for ~1.25 s selects it instead (logged once at startup).
            int id = target.GetInstanceID();
            if (_dwellId != id) { _dwellId = id; _dwellSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _dwellSince >= 1.25f) { _dwellId = 0; Select(target); }
        }

        private static void Select(GameObject root)
        {
            if (root == null) return;
            if (_selected == root) return;
            _selected = root;
            _selectedId = root.GetInstanceID();
            _baseScale = root.transform.localScale;
            _scaleFactor = 1f;
            SetFrozenSelected(true); // a build piece must never drop mid-edit (the falling Map Board lesson)
            try { Spawner.LedgerUpdate(root); } catch { }
            Note("Controlling '" + CleanName(root) + "' -- MOVE / ROTATE / SCALE pages drive it. Point + trigger another piece to switch.");
            try { TabletUI.Refresh(); } catch { }
        }

        private static void CycleSelection(int dir)
        {
            var roots = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (int id in TrackedRegistry.Snapshot())
            {
                var go = Spawner.FindByInstanceId(id);
                if (go == null) continue;
                int rid = go.GetInstanceID();
                if (seen.Add(rid)) roots.Add(go);
            }
            if (roots.Count == 0) { Note("Nothing spawned yet -- console: build spawn <name>."); return; }
            int idx = _selected == null ? -1 : roots.IndexOf(_selected);
            idx = idx < 0 ? (dir > 0 ? 0 : roots.Count - 1) : (idx + dir + roots.Count) % roots.Count;
            Select(roots[idx]);
            if (_selected == roots[idx]) Note("Controlling '" + CleanName(roots[idx]) + "' (" + (idx + 1) + "/" + roots.Count + ").");
        }

        private static void DeleteSelected()
        {
            if (_selected == null) return;
            string name = CleanName(_selected);
            int id = _selectedId;
            ClearSelection();
            if (Spawner.DespawnOne(id, delegate (string s) { Note(s); }))
                Note("Deleted '" + name + "'.");
        }

        // ---- highlight: a wire box around the piece, zero material work ------
        // The old glow cloned every material on the prefab (shader-variant
        // compiles + sharedMaterials swaps on the Quest GPU -- a frame
        // killer on big prefabs). Now: ONE reusable LineRenderer tracing
        // the hovered/selected piece's cached bounds. Positions are only
        // rewritten when the target or its box actually changes.
        private static LineRenderer _outline;
        private static GameObject _outlineRoot;
        private static Color _outlineColor;
        private static bool _outlineDirty = true;
        // Cube-edge traversal (14 points, all 12 edges, two retraced):
        // corners 0-3 bottom (y-), 4-7 top (y+) in BuildOutlineBox.
        private static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 6, 7, 3, 2, 6, 5, 1 };

        private static void EnsureOutline()
        {
            if (_outline != null) return;
            try
            {
                var go = new GameObject("ATTBuildingOutline");
                go.layer = 2; // Ignore Raycast
                UnityEngine.Object.DontDestroyOnLoad(go);
                _outline = go.AddComponent<LineRenderer>();
                if (_outline != null)
                {
                    var shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    if (shader != null) _outline.material = new Material(shader);
                    _outline.startWidth = 0.004f; _outline.endWidth = 0.004f;
                    _outline.positionCount = BoxPath.Length;
                    _outline.enabled = false;
                }
            }
            catch (Exception ex) { Mod.LogOnce("builder outline: " + ex.Message); }
        }

        private static void SyncOutline()
        {
            GameObject want = BuildState.BuildMode ? (_hover != null ? _hover : _selected) : null;
            if (want == null) { HideOutline(); return; }
            EnsureOutline();
            if (_outline == null) return;
            Color col = _hover != null ? HoverColor : SelectedColor;
            try
            {
                if (want != _outlineRoot) { _outlineRoot = want; _outlineDirty = true; }
                if (!_outline.enabled) _outline.enabled = true;
                if (col != _outlineColor) { _outlineColor = col; _outline.startColor = col; _outline.endColor = col; }
                if (_outlineDirty)
                {
                    _outlineDirty = false;
                    Probe pr; Bounds box;
                    if (_probes.TryGetValue(want.GetInstanceID(), out pr) && pr != null && pr.Root == want) box = pr.Box;
                    else box = new Bounds(want.transform.position, new Vector3(0.3f, 0.3f, 0.3f));
                    SetOutlineBox(box);
                }
            }
            catch { }
        }

        private static void SetOutlineBox(Bounds box)
        {
            Vector3 c = box.center, e = box.extents;
            var corners = new Vector3[8];
            corners[0] = c + new Vector3(-e.x, -e.y, -e.z);
            corners[1] = c + new Vector3( e.x, -e.y, -e.z);
            corners[2] = c + new Vector3(-e.x,  e.y, -e.z);
            corners[3] = c + new Vector3( e.x,  e.y, -e.z);
            corners[4] = c + new Vector3(-e.x, -e.y,  e.z);
            corners[5] = c + new Vector3( e.x, -e.y,  e.z);
            corners[6] = c + new Vector3( e.x,  e.y,  e.z);
            corners[7] = c + new Vector3(-e.x,  e.y,  e.z);
            for (int i = 0; i < BoxPath.Length; i++) _outline.SetPosition(i, corners[BoxPath[i]]);
        }

        private static void HideOutline()
        {
            _outlineRoot = null;
            try { if (_outline != null && _outline.enabled) _outline.enabled = false; } catch { }
        }

        // ---- ray visual (line + impact dot; per-frame, from cached results) -----
        private static LineRenderer _line;
        private static GameObject _dot;
        private static bool _fxReady;
        private static bool _lineOnTarget;
        private static bool _dotShown;

        private static void EnsureFx()
        {
            if (_fxReady) return;
            _fxReady = true;
            try
            {
                var go = new GameObject("ATTBuildingFingerRay");
                go.layer = 2; // Ignore Raycast: never intersects our own raycast mask
                UnityEngine.Object.DontDestroyOnLoad(go);
                _line = go.AddComponent<LineRenderer>();
                if (_line != null)
                {
                    var shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    if (shader != null) _line.material = new Material(shader);
                    _line.startWidth = 0.0035f; _line.endWidth = 0.0012f;
                    _line.positionCount = 2;
                    _line.enabled = false;
                }
                _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _dot.name = "ATTBuildingHitDot";
                _dot.layer = 2;
                UnityEngine.Object.DontDestroyOnLoad(_dot);
                var col = _dot.GetComponent<Collider>();
                if (col != null) col.enabled = false; // the dot must never block the ray
                var rend = _dot.GetComponent<MeshRenderer>();
                if (rend != null)
                {
                    var sh = Shader.Find("Standard");
                    if (sh == null) sh = Shader.Find("Unlit/Color");
                    if (sh != null)
                    {
                        var m = new Material(sh);
                        m.color = HoverColor;
                        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", HoverColor);
                        rend.material = m;
                    }
                }
                _dot.transform.localScale = new Vector3(0.024f, 0.024f, 0.024f);
                _dot.SetActive(false);
            }
            catch (Exception ex) { Mod.LogOnce("builder ray fx: " + ex.Message); }
        }

        // Every frame: point the line from the fingertip; the endpoint is
        // the cached hit point (refreshed at 20 Hz) or full ray length.
        private static void UpdateRayVisual()
        {
            EnsureFx();
            Transform tip;
            var hand = RayHandTransform(out tip);
            if (hand == null) { HideFx(); return; }
            Vector3 origin = tip != null ? tip.position : hand.position;
            bool onTarget = _hover != null && _hasHit;
            Vector3 end = onTarget ? _hitPoint : origin + hand.forward * RayLength;
            try
            {
                if (_line != null)
                {
                    _line.enabled = true;
                    if (onTarget != _lineOnTarget)
                    {
                        _lineOnTarget = onTarget;
                        var c = onTarget ? SelectedColor : HoverColor;
                        _line.startColor = c; _line.endColor = c;
                    }
                    _line.SetPosition(0, origin);
                    _line.SetPosition(1, end);
                }
                if (_dot != null)
                {
                    if (onTarget != _dotShown) { _dotShown = onTarget; _dot.SetActive(onTarget); }
                    if (onTarget) _dot.transform.position = _hitPoint;
                }
            }
            catch { }
        }

        private static void HideFx()
        {
            try { if (_line != null) _line.enabled = false; } catch { }
            try { if (_dot != null && _dotShown) { _dotShown = false; _dot.SetActive(false); } } catch { }
        }

        // ---- tablet operations on the selected piece ------------------------------
        private static float RotateStep() { return BuildState.AngleStepDegrees > 0f ? BuildState.AngleStepDegrees : 15f; }

        private static Transform Head()
        {
            try { var pc = PlayerController.Current; return pc == null ? null : pc.Head; } catch { return null; }
        }

        private static Vector3 HeadFwd()
        {
            var h = Head();
            Vector3 f = h == null ? Vector3.forward : h.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
        }

        private static Vector3 HeadFwdRaw()
        {
            var h = Head();
            return h == null ? Vector3.forward : h.forward;
        }

        private static Vector3 HeadRight()
        {
            var h = Head();
            Vector3 r = h == null ? Vector3.right : h.right;
            r.y = 0f;
            return r.sqrMagnitude < 0.0001f ? Vector3.right : r.normalized;
        }

        private static void AfterEdit()
        {
            if (_selected == null) return;
            try
            {
                var body = _selected.GetComponent<Rigidbody>();
                if (body != null) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            catch { }
            try { Spawner.LedgerUpdate(_selected); } catch { }
            try { Probe pr; if (_probes.TryGetValue(_selectedId, out pr) && pr != null && pr.Root == _selected) { RefreshProbe(pr); if (_outlineRoot == _selected) _outlineDirty = true; } } catch { }
        }

        private static void Nudge(Vector3 right, Vector3 fwd, float sx, float sz)
        {
            if (_selected == null) return;
            _selected.transform.position += (right * sx + fwd * sz) * _moveStep;
            SnapPositionIfOn();
            AfterEdit();
        }

        private static void NudgeWorld(Vector3 dir)
        {
            if (_selected == null) return;
            _selected.transform.position += dir * _moveStep;
            SnapPositionIfOn();
            AfterEdit();
        }

        private static void SnapPositionIfOn()
        {
            if (!BuildState.Snapping || BuildState.GridMetres <= 0f || _selected == null) return;
            _selected.transform.position = VectorSnap.SnapPosition(_selected.transform.position, BuildState.GridMetres);
        }

        private static void Rotate(Vector3 worldAxis, float degrees)
        {
            if (_selected == null) return;
            _selected.transform.Rotate(worldAxis, degrees, Space.World);
            if (BuildState.Snapping) _selected.transform.rotation = VectorSnap.SnapRotation(_selected.transform.rotation, BuildState.AngleStepDegrees);
            AfterEdit();
        }

        private static void SnapNow()
        {
            if (_selected == null) return;
            _selected.transform.rotation = VectorSnap.SnapRotation(_selected.transform.rotation, BuildState.AngleStepDegrees);
            SnapPositionIfOn();
            AfterEdit();
            Note("Snapped '" + CleanName(_selected) + "' straight (" + BuildState.AngleStepDegrees + " deg / " + BuildState.GridMetres + " m grid).");
        }

        private static void LevelNow()
        {
            if (_selected == null) return;
            Vector3 e = _selected.transform.rotation.eulerAngles;
            _selected.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
            if (BuildState.Snapping) _selected.transform.rotation = VectorSnap.SnapRotation(_selected.transform.rotation, BuildState.AngleStepDegrees);
            AfterEdit();
        }

        private static void SetScaleFactor(float factor)
        {
            if (_selected == null) return;
            _scaleFactor = Mathf.Clamp(factor, 0.05f, 20f);
            Vector3 s = _baseScale * _scaleFactor;
            // never collapse a piece to (near) zero on any axis
            s = new Vector3(Mathf.Max(0.001f, s.x), Mathf.Max(0.001f, s.y), Mathf.Max(0.001f, s.z));
            _selected.transform.localScale = s;
            AfterEdit();
        }

        private static void SetFrozenSelected(bool frozen)
        {
            _frozen = frozen;
            if (_selected == null) return;
            try
            {
                foreach (var body in _selected.GetComponentsInChildren<Rigidbody>())
                {
                    if (body == null) continue;
                    body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                    body.isKinematic = frozen;
                    if (!frozen) body.WakeUp();
                }
            }
            catch (Exception ex) { Mod.LogOnce("builder freeze: " + ex.Message); }
        }

        private static string Percent(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }
        private static string F(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        // ---- XR trigger (pure reflection: no new assembly references) -------------
        // UnityEngine.XR lives in an assembly the mod projects do not reference,
        // and naming it would break the build wherever that DLL is absent.
        // Everything here is best-effort and logged once; when no trigger is
        // available, dwell-select takes over (PollSelect above).
        private static class TriggerInput
        {
            internal static bool Available { get { if (_dead) return false; Ensure(); return _ok; } }
            internal static int FailStreak { get { return _failStreak; } }
            private static int _failStreak;
            private static bool _dead;
            internal static void Warm() { Ensure(); }

            private static bool _ok;
            private static bool _tried;
            private static float _nextTry = -1f;

            internal static string StatusText()
            {
                if (_ok) return "XR trigger -- pull to select";
                return _tried ? "dwell-select (hold ray on a piece ~1.25 s)" : "checking...";
            }
            private static MethodInfo _getDevice;
            private static MethodInfo _tryGet;
            private static object _nodeLeft, _nodeRight, _usage;
            private static bool _prevL, _prevR;

            private static void Ensure()
            {
                if (_ok || _dead || Time.unscaledTime < _nextTry) return;
                _nextTry = Time.unscaledTime + 15f;
                _tried = true;
                try
                {
                    var tDevices = AccessTools.TypeByName("UnityEngine.XR.InputDevices");
                    var tNode = AccessTools.TypeByName("UnityEngine.XR.XRNode");
                    var tUsages = AccessTools.TypeByName("UnityEngine.XR.CommonUsages");
                    var tDevice = AccessTools.TypeByName("UnityEngine.XR.InputDevice");
                    if (tDevices == null || tNode == null || tUsages == null || tDevice == null)
                    {
                        Diag.FileOnly("builder xr: XR types not loaded yet (dwell-select active meanwhile).");
                        return;
                    }
                    _nodeLeft = Enum.Parse(tNode, "LeftHand");
                    _nodeRight = Enum.Parse(tNode, "RightHand");
                    var field = tUsages.GetField("triggerButton", BindingFlags.Public | BindingFlags.Static);
                    if (field == null) { Diag.FileOnly("builder xr: CommonUsages.triggerButton missing."); return; }
                    _usage = field.GetValue(null);
                    _getDevice = tDevices.GetMethod("GetDeviceAtXRNode", BindingFlags.Public | BindingFlags.Static);
                    if (_getDevice == null) return;
                    foreach (var m in tDevice.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name != "TryGetFeatureValue") continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 2 && ps[0].ParameterType == field.FieldType
                            && ps[1].ParameterType.IsByRef && ps[1].ParameterType.GetElementType() == typeof(bool))
                        { _tryGet = m; break; }
                    }
                    _ok = _usage != null && _tryGet != null;
                    Diag.Log(_ok
                        ? "builder: XR trigger input READY -- pull the trigger on a highlighted piece to control it."
                        : "builder: XR trigger not resolvable -- dwell-select active (hold the ray on a piece ~1.25 s).");
                }
                catch (Exception ex) { Diag.FileOnly("builder xr resolve: " + ex.Message); }
            }

            private static readonly object[] _getArgs = new object[1];
            private static object[] _tryArgs;

            // Only the ray hand's device is polled (selection happens there).
            internal static bool PressedEdge(int side)
            {
                if (!Available) return false;
                return side == 0 ? Read(_nodeRight, ref _prevR) : Read(_nodeLeft, ref _prevL);
            }

            private static bool Read(object node, ref bool prev)
            {
                try
                {
                    _getArgs[0] = node;
                    object device = _getDevice.Invoke(null, _getArgs);
                    if (device == null) { prev = false; return false; }
                    if (_tryArgs == null) _tryArgs = new object[2];
                    _tryArgs[0] = _usage; _tryArgs[1] = false;
                    object okObj = _tryGet.Invoke(device, _tryArgs);
                    bool down = okObj is bool && (bool)okObj && _tryArgs[1] is bool && (bool)_tryArgs[1];
                    bool edge = down && !prev;
                    prev = down;
                    _failStreak = 0;
                    return edge;
                }
                catch
                {
                    // Reflection across the IL2CPP boundary throwing every
                    // poll is a frame killer (exception marshalling). Three
                    // strikes and the trigger disables itself for the
                    // session; dwell-select takes over.
                    prev = false;
                    if (++_failStreak >= 3 && !_dead)
                    {
                        _dead = true; _ok = false;
                        Diag.Log("builder: XR trigger input failed repeatedly -- disabled for this session, dwell-select active (hold the ray on a piece ~1.25 s).");
                    }
                    return false;
                }
            }
        }
    }
}
