
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
    //     those -- TrackedRegistry) lights up its actual meshes;
    //   * pulling the trigger (XR trigger via reflection; dwell-select as a
    //     fallback) makes that prefab the thing the tablet controls;
    //   * MOVE / ROTATE / SCALE pages then nudge the selected prefab.
    // Per-frame cost while building: at most two raycasts plus transform
    // reads -- no scene scans, no collection walks.
    internal static class Builder
    {
        // ---- tablet wiring ------------------------------------------------
        private static bool _registered;
        private static readonly Func<Page> HomeFunc = new Func<Page>(HomePage);
        private static bool _wasShowing;

        internal static void RegisterTablet()
        {
            if (_registered) return;
            _registered = true;
            TabletBubble bubble = TabletUI.AddBubble("Prefabulizer", "PFB");
            bubble.App("Prefabulizer", "PFB", HomeFunc);
            Diag.Log("builder: Prefabulizer bubble registered (wrist wheel -> Prefabulizer -> tablet).");
        }

        internal static void SetBuildMode(bool on)
        {
            BuildState.BuildMode = on;
            RayOn = on; // entering build mode arms the finger ray immediately
            if (!on) { ClearLit(); HideLine(); _hover = null; }
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
        private static int _stepIdx = 2;

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
        private static Page HomePage()
        {
            var p = new Page("PREFABULIZER", StatusText());
            p.Toggle("Build mode", () => BuildState.BuildMode, v => SetBuildMode(v));
            p.Toggle("Finger ray", () => RayOn, v => { RayOn = v; if (!v) { _hover = null; HideLine(); } });
            p.Text("Controlling", SelectedLabel());
            p.Link("Move piece", StepLabels[_stepIdx] + " steps", MovePage);
            p.Link("Rotate piece", RotateStep() + " deg steps", RotatePage);
            p.Link("Scale piece", Percent(_scaleFactor), ScalePage);
            p.Link("Pieces", TrackedRegistry.Count + " spawned", PiecesPage);
            p.Link("How to use", "60-second guide", HelpPage);
            p.Toggle("Snapping (straight + grid)", () => BuildState.Snapping, v => { BuildState.Snapping = v; Settings.Save(); });
            p.Button("Close tablet", delegate { try { TabletUI.Close(); } catch { } });
            p.Footer = "ATTBuilding " + Mod.Version + " -- no grabbing, just point, trigger, build.";
            return p;
        }

        private static Page MovePage()
        {
            var p = new Page("MOVE", StatusText());
            p.Text("Controlling", SelectedLabel());
            if (_selected == null) { p.Text("Nothing selected", "finger-ray a spawned piece + trigger"); return p; }
            p.Choice("Step size", StepLabels, () => _stepIdx, v => _stepIdx = Mathf.Clamp(v, 0, StepLabels.Length - 1));
            string d = "+/- " + StepLabels[_stepIdx];
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
            p.Button("Bigger", "x1.25", delegate { SetScaleFactor(_scaleFactor * 1.25f); });
            p.Button("Smaller", "x0.8", delegate { SetScaleFactor(_scaleFactor * 0.8f); });
            p.Slider("Scale (fine)", () => _scaleFactor, v => SetScaleFactor(v), 0.1f, 5f, 0.05f);
            p.Button("Reset scale", "x1.00", delegate { SetScaleFactor(1f); });
            p.Toggle("Frozen (can't fall)", () => _frozen, v => SetFrozenSelected(v));
            return p;
        }

        private static Page PiecesPage()
        {
            var p = new Page("PIECES", StatusText());
            p.Text("Spawned pieces", TrackedRegistry.Count + " tracked");
            p.Text("Controlling", SelectedLabel());
            p.Button("Select next piece", delegate { CycleSelection(1); });
            p.Button("Select previous piece", delegate { CycleSelection(-1); });
            if (!string.IsNullOrEmpty(Spawner.LastSpawnName))
                p.Button("Spawn another", Spawner.LastSpawnName, delegate { Spawner.SpawnByName(Spawner.LastSpawnName, 1, delegate (string s) { Note(s); }); });
            else
                p.Text("Spawn another", "spawn once from the console first");
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
            p.Text("2. Open this tablet", "wrist wheel > Prefabulizer");
            p.Text("3. Build mode + ray on", "toggles on the home page");
            p.Text("4. Point at a piece", "its whole mesh glows amber");
            p.Text("5. Pull the trigger", "now the tablet controls it (green)");
            p.Text("6. Switch any time", "point + trigger a different piece");
            p.Text("7. Move / Rotate / Scale", "pages on the home screen");
            p.Text("Staying straight", "snapping on + build snapangle / build grid in the console");
            p.Text("Only your spawns", "world items are never selected");
            p.Footer = "No grabbing anywhere -- nothing to fight the hand physics.";
            return p;
        }

        // ---- frame tick ------------------------------------------------------
        internal static void Tick()
        {
            try { TabletUI.Tick(); } catch (Exception ex) { Mod.LogOnce("tablet tick: " + ex.Message); }
            if (!_registered) return;

            bool showing = false;
            try { showing = TabletUI.IsShowing(HomeFunc); } catch { }
            if (showing && !_wasShowing) SetBuildMode(true); // opening the tablet IS entering build mode
            _wasShowing = showing;

            ValidateSelection();
            if (!BuildState.BuildMode || !RayOn)
            {
                if (_hover != null) _hover = null;
                if (!BuildState.BuildMode) ClearLit(); else UpdateSelectedGlow();
                HideLine();
                _dwellId = 0;
                return;
            }
            UpdateRay();
        }

        private static void ValidateSelection()
        {
            if (_selected == null) { _selectedId = 0; return; } // == is Unity's fake-null: also catches destroyed
            if (!TrackedRegistry.Contains(_selectedId)) ClearSelection();
        }

        private static void ClearSelection()
        {
            if (_litRoot == _selected) ClearLit();
            _selected = null; _selectedId = 0; _scaleFactor = 1f;
        }

        // ---- finger ray --------------------------------------------------------
        private static Transform[] _hands = new Transform[0];
        private static Transform[] _tips = new Transform[0];
        private static int _handOwnerId;

        private static void AcquireHands()
        {
            try
            {
                var player = PlayerController.Current;
                if (player == null) { _hands = new Transform[0]; _tips = new Transform[0]; _handOwnerId = 0; return; }
                if (_handOwnerId == player.GetInstanceID() && _hands.Length > 0 && _hands[0] != null) return;
                var found = new List<Transform>();
                var tips = new List<Transform>();
                foreach (var interactor in player.GetComponentsInChildren<Interactor>())
                {
                    if (interactor == null || !interactor.IsLocal) continue;
                    found.Add(interactor.transform);
                    tips.Add(FindTip(interactor.transform));
                }
                _hands = found.ToArray(); _tips = tips.ToArray(); _handOwnerId = player.GetInstanceID();
                if (_hands.Length > 0) Diag.FileOnly("builder: finger ray hands=" + _hands.Length + " (tips found: " + CountNonNull(_tips) + ").");
            }
            catch (Exception ex) { Mod.LogOnce("builder hands: " + ex.Message); }
        }

        private static int CountNonNull(Transform[] arr) { int n = 0; foreach (var t in arr) if (t != null) n++; return n; }

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

        private static void UpdateRay()
        {
            if (_hands.Length == 0 || _hands[0] == null) AcquireHands();
            GameObject best = null; float bestDist = float.MaxValue;
            Vector3 bestOrigin = Vector3.zero, bestEnd = Vector3.zero; bool anyOrigin = false;
            for (int i = 0; i < _hands.Length; i++)
            {
                var hand = _hands[i];
                if (hand == null) { AcquireHands(); break; }
                Vector3 origin = (i < _tips.Length && _tips[i] != null) ? _tips[i].position : hand.position;
                Vector3 dir = hand.forward;
                if (!anyOrigin) { bestOrigin = origin; bestEnd = origin + dir * 30f; anyOrigin = true; }
                RaycastHit hit;
                if (!Physics.Raycast(origin, dir, out hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var root = TrackedRootOf(hit.collider == null ? null : hit.collider.transform);
                if (root == null) continue;
                if (hit.distance < bestDist) { bestDist = hit.distance; best = root; bestOrigin = origin; bestEnd = hit.point; }
            }
            if (best != _hover)
            {
                _hover = best;
                _dwellId = 0;
                if (best != null) Diag.FileOnly("builder: ray hovering '" + CleanName(best) + "' (trigger selects it).");
            }
            UpdateGlow();
            DrawLine(anyOrigin ? bestOrigin : Vector3.zero, bestEnd, anyOrigin, best != null);
            if (best != null) PollSelect(best);
        }

        // ---- selection trigger: XR trigger, dwell fallback ----------------------
        private static int _dwellId;
        private static float _dwellSince;

        private static void PollSelect(GameObject target)
        {
            if (TriggerInput.PressedEdge()) { Select(target); return; }
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

        // ---- mesh highlight (the actual prefab meshes, never a box) --------------
        private static GameObject _litRoot;
        private static readonly List<Renderer> _litRenderers = new List<Renderer>();
        private static readonly List<Material[]> _litOriginals = new List<Material[]>();
        private static readonly List<Material> _litClones = new List<Material>();
        private static readonly List<Color> _litBase = new List<Color>();
        private static readonly List<bool> _litEmissive = new List<bool>();
        private static bool _litPulse;
        private static Color _litColor;

        private static readonly Color HoverColor = new Color(1f, 0.72f, 0.12f);   // amber: "trigger will select this"
        private static readonly Color SelectedColor = new Color(0.25f, 1f, 0.45f); // green: "the tablet controls this"

        private static void UpdateGlow()
        {
            GameObject want = _hover != null ? _hover : _selected;
            ApplyLit(want, _hover != null ? HoverColor : SelectedColor, _hover != null);
        }

        private static void UpdateSelectedGlow()
        {
            ApplyLit(BuildState.BuildMode ? _selected : null, SelectedColor, false);
        }

        private static void ApplyLit(GameObject root, Color color, bool pulse)
        {
            if (root == _litRoot)
            {
                _litPulse = pulse;
                if (_litColor != color)
                {
                    _litColor = color;
                    for (int i = 0; i < _litClones.Count; i++)
                    {
                        _litBase[i] = color;
                        try { if (_litEmissive[i]) _litClones[i].SetColor("_EmissionColor", color); } catch { }
                    }
                }
                PulseLit();
                return;
            }
            ClearLit();
            if (root == null) return;
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;
                    var clones = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i];
                        if (src == null) { clones[i] = null; continue; }
                        var m = new Material(src);
                        clones[i] = m;
                        _litClones.Add(m);
                        if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", color); _litBase.Add(color); _litEmissive.Add(true); }
                        else if (m.HasProperty("_Color")) { m.SetColor("_Color", Color.Lerp(src.GetColor("_Color"), color, 0.6f)); _litBase.Add(color); _litEmissive.Add(false); }
                        else { _litBase.Add(color); _litEmissive.Add(false); }
                    }
                    _litRenderers.Add(r); _litOriginals.Add(mats);
                    r.sharedMaterials = clones;
                }
                _litRoot = root; _litPulse = pulse; _litColor = color;
                PulseLit();
            }
            catch (Exception ex) { Mod.LogOnce("builder highlight: " + ex.Message); ClearLit(); }
        }

        private static void PulseLit()
        {
            if (_litRoot == null || !_litPulse) return;
            float k = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
            for (int i = 0; i < _litClones.Count; i++)
            {
                if (!_litEmissive[i]) continue;
                try { _litClones[i].SetColor("_EmissionColor", _litBase[i] * k); } catch { }
            }
        }

        private static void ClearLit()
        {
            try
            {
                for (int i = 0; i < _litRenderers.Count; i++)
                {
                    var r = _litRenderers[i];
                    if (r != null) r.sharedMaterials = _litOriginals[i];
                }
            }
            catch { }
            foreach (var m in _litClones) { try { if (m != null) UnityEngine.Object.Destroy(m); } catch { } }
            _litRenderers.Clear(); _litOriginals.Clear(); _litClones.Clear(); _litBase.Clear(); _litEmissive.Clear();
            _litRoot = null;
        }

        // ---- ray visual ------------------------------------------------------------
        private static LineRenderer _line;
        private static bool _lineReady;

        private static void EnsureLine()
        {
            if (_lineReady) return;
            _lineReady = true;
            try
            {
                var go = new GameObject("ATTBuildingFingerRay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _line = go.AddComponent<LineRenderer>();
                if (_line == null) return;
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null) _line.material = new Material(shader);
                _line.startWidth = 0.004f; _line.endWidth = 0.0015f;
                _line.positionCount = 2;
                _line.enabled = false;
            }
            catch (Exception ex) { Mod.LogOnce("builder ray line: " + ex.Message); }
        }

        private static void DrawLine(Vector3 origin, Vector3 end, bool hasHand, bool onTarget)
        {
            EnsureLine();
            if (_line == null || !hasHand) { HideLine(); return; }
            try
            {
                _line.enabled = true;
                var c = onTarget ? SelectedColor : HoverColor;
                _line.startColor = c; _line.endColor = c;
                _line.SetPosition(0, origin);
                _line.SetPosition(1, end);
            }
            catch { }
        }

        private static void HideLine()
        {
            try { if (_line != null) _line.enabled = false; } catch { }
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
        }

        private static void Nudge(Vector3 right, Vector3 fwd, float sx, float sz)
        {
            if (_selected == null) return;
            float step = StepValues[_stepIdx];
            _selected.transform.position += (right * sx + fwd * sz) * step;
            SnapPositionIfOn();
            AfterEdit();
        }

        private static void NudgeWorld(Vector3 dir)
        {
            if (_selected == null) return;
            _selected.transform.position += dir * StepValues[_stepIdx];
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
            internal static bool Available { get { Ensure(); return _ok; } }

            private static bool _ok;
            private static float _nextTry = -1f;
            private static MethodInfo _getDevice;
            private static MethodInfo _tryGet;
            private static object _nodeLeft, _nodeRight, _usage;
            private static bool _prevL, _prevR;

            private static void Ensure()
            {
                if (_ok || Time.unscaledTime < _nextTry) return;
                _nextTry = Time.unscaledTime + 3f;
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

            internal static bool PressedEdge()
            {
                if (!Available) return false;
                bool edge = Read(_nodeRight, ref _prevR);
                edge = Read(_nodeLeft, ref _prevL) || edge;
                return edge;
            }

            private static bool Read(object node, ref bool prev)
            {
                try
                {
                    object device = _getDevice.Invoke(null, new object[] { node });
                    if (device == null) { prev = false; return false; }
                    var args = new object[] { _usage, false };
                    object okObj = _tryGet.Invoke(device, args);
                    bool down = okObj is bool && (bool)okObj && args[1] is bool && (bool)args[1];
                    bool edge = down && !prev;
                    prev = down;
                    return edge;
                }
                catch { prev = false; return false; }
            }
        }
    }
}
