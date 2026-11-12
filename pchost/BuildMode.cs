
using System;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using ATT.Character.QuickAccessMenu;
using UnityEngine;

namespace ATTBuilding
{
    // Build mode: only objects this console spawned (TrackedRegistry) become
    // grabbable. Most ATT prefabs already carry a Pickup (see the game's
    // Pickup class: Interact() undocks, creates the hand joint and treats the
    // object like an item); for prefabs that don't, we add Rigidbody+Pickup at
    // runtime on a best-effort basis. While a tracked object is held, snapping
    // quantises its rotation to the set angle every frame (straight, but any
    // direction); on release we also snap position to the grid, kill velocity,
    // and make sure the final transform is exactly snapped.
    internal static class BuildMode
    {
        private static int _heldId;
        private static Pickup _held;

        internal static void EnsureAllGrabbable()
        {
            foreach (int id in TrackedRegistry.Snapshot())
            {
                var go = Spawner.FindByInstanceId(id);
                if (go != null) EnsureGrabbable(go);
            }
        }

        internal static void EnsureGrabbable(GameObject go)
        {
            if (go == null) return;
            try
            {
                var pickup = go.GetComponentInChildren<Pickup>();
                bool added = false;
                if (pickup == null)
                {
                    // Prefab is not an item at all (e.g. stations like the
                    // Anvil): lend it a Rigidbody + Pickup. A runtime-added
                    // Pickup starts with its [GetComponent] fields EMPTY, so
                    // without the fixup below CheckInteractAllowed refuses
                    // every grab (that was 'cannot pick up the anvils').
                    var body0 = go.GetComponent<Rigidbody>();
                    if (body0 == null) go.AddComponent<Rigidbody>();
                    go.AddComponent<Pickup>(); pickup = go.GetComponent<Pickup>(); if (pickup == null) throw new InvalidOperationException("AddComponent<Pickup> did not stick");
                    added = true;
                }
                FixupPickupFields(go, pickup);
                int points = 0;
                try { points = pickup.GrabPoints == null ? 0 : pickup.GrabPoints.Length; } catch { }
                if (points == 0) InstallSurfaceGrabPoint(go, pickup);
                try { points = pickup.GrabPoints == null ? 0 : pickup.GrabPoints.Length; } catch { }
                if (added)
                {
                    // 0.4.10 field log: a runtime-added Pickup on a networked
                    // prefab starts DISABLED and entity-unlinked
                    // (enabled=False, entityLinked=False). A disabled
                    // Interactable is invisible to the hand's candidate
                    // scan, and Interact() dereferences Entity, so both
                    // must be fixed. The game's spawn pipeline links each
                    // behaviour to its NetworkEntity BEFORE Awake runs;
                    // our first Initialize() attempt NRE'd inside
                    // Pickup.Awake at add_Undocked -- most plausibly on
                    // that null Entity. Order matters: link, enable, init.
                    try
                    {
                        var netEntity = go.GetComponentInChildren<NetworkEntity>();
                        if (netEntity != null && pickup.Entity == null)
                        {
                            bool linked = LinkEntity(pickup, netEntity);
                            Diag.Log("buildmode: entity link for '" + go.name + "': linked=" + linked + ", entityLinked=" + (pickup.Entity != null));
                            if (pickup.Entity == null) DumpEntityMembers(pickup, netEntity);
                        }
                        else if (netEntity == null) Diag.Log("buildmode: '" + go.name + "' has NO NetworkEntity component to link a Pickup to.");
                    }
                    catch (Exception lex) { Diag.Log("buildmode: entity link failed for '" + go.name + "': " + lex.Message); }
                    try { pickup.enabled = true; } catch { }
                    try { pickup.Initialize(); Diag.Log("buildmode: Initialize() ran for added Pickup on '" + go.name + "' (entityLinked=" + (pickup.Entity != null) + ", enabled=" + pickup.enabled + ")."); }
                    catch (Exception iex) { Diag.Log("buildmode: Initialize() FAILED for '" + go.name + "': " + iex.Message); }
                    // Initialize may leave the behaviour disabled again.
                    try { if (!pickup.enabled) { pickup.enabled = true; Diag.Log("buildmode: re-enabled Pickup on '" + go.name + "' after Initialize."); } } catch { }
                }
                try
                {
                    Diag.Log("buildmode: grabbable '" + go.name + "': pickup " + (added ? "ADDED" : "already present")
                        + ", grabPoints=" + points
                        + ", entityLinked=" + (pickup.Entity != null)
                        + ", rigidbodyField=" + (pickup.Rigidbody != null)
                        + ", collidersSet=" + pickup.Colliders.Count
                        + ", networkRigidbody=" + (go.GetComponent<NetworkRigidbody>() != null)
                        + ", hasNetEntity=" + (go.GetComponentInChildren<NetworkEntity>() != null)
                        + ", enabled=" + pickup.enabled + " (grab it like an item).");
                }
                catch (Exception lex) { Diag.Log("buildmode: grabbable '" + go.name + "': pickup " + (added ? "ADDED" : "present") + ", grabPoints=" + points + " (state dump failed: " + lex.Message + ")"); }
            }
            catch (Exception ex) { Diag.Log("buildmode: grabbable fixup FAILED for '" + go.name + "': " + ex.Message); Mod.LogOnce("build mode grabbable: " + ex.Message); }
        }

        // Fill the fields the editor normally injects ([GetComponent] /
        // [GetComponentsInChildren]) so the game's own checks pass:
        // CheckInteractAllowedAndReplace requires the rigidbody field.
        private static bool TrySet(object target, string name, object value)
        {
            try { ConsoleHook.SetMember(target, name, value); return true; }
            catch (Exception ex) { Diag.Log("buildmode: set '" + name + "' FAILED: " + ex.Message); return false; }
        }

        private static void FixupPickupFields(GameObject go, Pickup pickup)
        {
            var body = go.GetComponent<Rigidbody>();
            if (body == null) body = pickup.gameObject.GetComponent<Rigidbody>();
            if (body != null) TrySet(pickup, "rigidbody", body);
            else Diag.Log("buildmode: NO rigidbody found for '" + go.name + "' (grabs will be refused).");
            var colliders = go.GetComponentsInChildren<Collider>();
            TrySet(pickup, "childrenColliders", colliders);
            // Pickup.Awake copies childrenColliders into its Colliders set --
            // but Awake ran at AddComponent, BEFORE this fixup, so the set is
            // still empty and anything consulting it sees no colliders.
            try { foreach (var c in colliders) { if (c != null) ConsoleHook.Call(pickup.Colliders, "Add", c); } }
            catch (Exception cex) { Diag.FileOnly("buildmode: filling Colliders set failed: " + cex.Message); }
            try { var nrb = go.GetComponent<NetworkRigidbody>(); if (nrb != null) TrySet(pickup, "networkRigidbody", nrb); } catch { }
        }

        // 'Grab the surface' (the ArmorSurfaceGrip idea, using the game's own
        // GrabPoint surface mode instead of Harmony): one GrabPoint whose
        // position is the object's bounds centre and whose poleForward is the
        // bounds extents -- CheckScore clamps the hand onto that box, so you
        // can grab the object anywhere on its surface, angle ignored.
        private static void InstallSurfaceGrabPoint(GameObject go, Pickup pickup)
        {
            Bounds bounds = LocalBounds(go, pickup.transform);
            var gp = new GrabPoint();
            gp.rotationMode = RotationMode.Surface;
            TrySet(gp, "position", bounds.center);
            var ext = bounds.extents;
            ext = new Vector3(Mathf.Max(ext.x, 0.03f), Mathf.Max(ext.y, 0.03f), Mathf.Max(ext.z, 0.03f));
            TrySet(gp, "poleForward", ext);
            TrySet(gp, "isIgnoringAngle", true);
            TrySet(pickup, "grabPoints", new GrabPoint[] { gp });
            Diag.FileOnly("buildmode: surface grab point on '" + go.name + "' centre=" + bounds.center + " extents=" + ext);
        }

        private static Bounds LocalBounds(GameObject go, Transform local)
        {
            Bounds world = new Bounds(); bool has = false;
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                if (c == null) continue;
                if (!has) { world = c.bounds; has = true; } else world.Encapsulate(c.bounds);
            }
            if (!has)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    if (!has) { world = r.bounds; has = true; } else world.Encapsulate(r.bounds);
                }
            }
            if (!has) return new Bounds(Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f));
            Vector3 center = local.InverseTransformPoint(world.center);
            Vector3 size = local.InverseTransformVector(world.size);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            if (size.sqrMagnitude < 0.0001f) size = new Vector3(0.4f, 0.4f, 0.4f);
            return new Bounds(center, size);
        }

        internal static void Tick()
        {
            if (!BuildState.BuildMode) { _held = null; _heldId = 0; return; }
            Pickup held = FindHeldTracked();
            if (held != null)
            {
                _held = held; _heldId = held.gameObject.GetInstanceID();
                BuildState.HeldInstanceId = _heldId;
                if (BuildState.Snapping)
                {
                    // Live angle snap while moving it: set angles only, every axis.
                    held.transform.rotation = VectorSnap.SnapRotation(held.transform.rotation, BuildState.AngleStepDegrees);
                    if (BuildState.LivePositionSnap && BuildState.GridMetres > 0f)
                        held.transform.position = VectorSnap.SnapPosition(held.transform.position, BuildState.GridMetres);
                }
            }
            else if (_held != null)
            {
                Place(_held); // just released
                _held = null; _heldId = 0; BuildState.HeldInstanceId = 0;
            }
            PollGrabDiag();
        }

        private static void Place(Pickup pickup)
        {
            try
            {
                if (pickup == null) return;  // Unity null check (destroyed objects compare == null)
                if (BuildState.Snapping)
                {
                    pickup.transform.rotation = VectorSnap.SnapRotation(pickup.transform.rotation, BuildState.AngleStepDegrees);
                    if (BuildState.GridMetres > 0f)
                        pickup.transform.position = VectorSnap.SnapPosition(pickup.transform.position, BuildState.GridMetres);
                }
                var body = pickup.Rigidbody;
                if (body != null) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            catch (Exception ex) { Mod.LogOnce("build mode place: " + ex.Message); }
        }


        // ---- grab diagnostics (POLLING ONLY -- do not Harmony-patch Pickup)
        // 0.4.9 added Harmony postfixes on Pickup.AssessScore /
        // CheckInteractAllowedAndReplace / Interact and CRASHED Quest ~5 s
        // after launch: AssessScore takes a `ref float` parameter and
        // Harmony's IL2CPP wrapper for ref-parameter methods emits invalid
        // IL (InvalidProgramException inside the DMD il2cpp->managed thunk).
        // So instead of patching anything, watch the observable state of
        // tracked pickups and log every transition + hand proximity. If a
        // grab attempt never changes interactors/docked while handDist
        // drops under ~0.3, the hand's candidate scan never selects the
        // object; if interactors flips but nothing holds, Interact refuses.
        private static readonly System.Collections.Generic.Dictionary<int, string> _diagState = new System.Collections.Generic.Dictionary<int, string>();
        private static float _diagNext;


        // SetMember silently no-ops when a member is missing and does not
        // walk base types for non-public members, so name-guessing the
        // Entity storage failed (0.4.11: 'attempted=True, entityLinked=
        // False'). Walk the whole hierarchy instead and try EVERY member
        // whose type is NetworkEntity, verifying by reading Entity back.
        private static bool LinkEntity(Pickup pickup, NetworkEntity netEntity)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            for (var type = pickup.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (field.FieldType != typeof(NetworkEntity) && !field.FieldType.IsAssignableFrom(typeof(NetworkEntity))) continue;
                    try
                    {
                        field.SetValue(pickup, netEntity);
                        if (pickup.Entity != null) { Diag.Log("buildmode: entity linked via field '" + field.Name + "' (" + type.Name + ")"); return true; }
                    }
                    catch (Exception ex) { Diag.FileOnly("buildmode: link field '" + field.Name + "': " + ex.Message); }
                }
                foreach (var prop in type.GetProperties(flags))
                {
                    if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                    if (prop.PropertyType != typeof(NetworkEntity) && !prop.PropertyType.IsAssignableFrom(typeof(NetworkEntity))) continue;
                    try
                    {
                        prop.SetValue(pickup, netEntity, null);
                        if (pickup.Entity != null) { Diag.Log("buildmode: entity linked via property '" + prop.Name + "' (" + type.Name + ")"); return true; }
                    }
                    catch (Exception ex) { Diag.FileOnly("buildmode: link property '" + prop.Name + "': " + ex.Message); }
                }
            }
            return false;
        }

        private static bool _membersDumped;

        // One-shot: if linking failed, list every member that could be the
        // real registration path (on Pickup's hierarchy and on the entity),
        // so the next step is reading a name from the log, not guessing.
        private static void DumpEntityMembers(Pickup pickup, NetworkEntity netEntity)
        {
            if (_membersDumped) return;
            _membersDumped = true;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            try
            {
                for (var type = pickup.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    foreach (var m in type.GetMembers(flags))
                    {
                        string n = m.Name;
                        if (n.IndexOf("ntity", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("ehaviour", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("egist", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("nitial", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Diag.Log("buildmode: member? " + type.Name + "." + n + " (" + m.MemberType + ")");
                    }
                }
                foreach (var m in netEntity.GetType().GetMembers(flags))
                {
                    string n = m.Name;
                    if (n.IndexOf("ehaviour", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("egist", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("dd", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("nitial", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Diag.Log("buildmode: entity member? " + m.Name + " (" + m.MemberType + ")");
                }
            }
            catch (Exception ex) { Diag.Log("buildmode: member dump failed: " + ex.Message); }
        }

        private static string Safe(System.Func<object> read)
        {
            try { var v = read(); return v == null ? "null" : v.ToString(); } catch { return "?"; }
        }

        private static void PollGrabDiag()
        {
            try
            {
                float now = Time.time;
                if (now < _diagNext) return;
                _diagNext = now + 0.5f;
                var player = PlayerController.Current;
                foreach (int id in TrackedRegistry.Snapshot())
                {
                    var go = Spawner.FindByInstanceId(id);
                    if (go == null) continue;
                    var pickup = go.GetComponentInChildren<Pickup>();
                    if (pickup == null) continue;
                    float nearest = -1f;
                    if (player != null)
                    {
                        foreach (var hand in player.GetComponentsInChildren<Interactor>())
                        {
                            if (hand == null || !hand.IsLocal) continue;
                            float d = Vector3.Distance(hand.transform.position, pickup.transform.position);
                            if (nearest < 0f || d < nearest) nearest = d;
                        }
                    }
                    var body = pickup.Rigidbody;
                    // Per-property guards: on a never-Awake'd Pickup some
                    // getters NRE (0.4.11: get_IsDocked), and one bad getter
                    // must not blind the whole poll.
                    string state = "interactors=" + Safe(() => pickup.Interactors.Count)
                        + " docked=" + Safe(() => pickup.IsDocked)
                        + " kinematic=" + Safe(() => body != null && body.isKinematic)
                        + " entity=" + Safe(() => pickup.Entity != null)
                        + " enabled=" + Safe(() => pickup.enabled)
                        + " handDist=" + (nearest < 0f ? "n/a" : (Mathf.Round(nearest * 4f) / 4f).ToString("0.00"));
                    string prev;
                    if (!_diagState.TryGetValue(id, out prev) || prev != state)
                    {
                        _diagState[id] = state;
                        Diag.Log("grabdiag: '" + go.name + "' " + state);
                    }
                }
            }
            catch (Exception ex) { Mod.LogOnce("grabdiag poll: " + ex.Message); }
        }

        // The ShardClient Readout pattern: scan the local player's hands.
        private static Pickup FindHeldTracked()
        {
            var player = PlayerController.Current;
            if (player == null) return null;
            foreach (var interactor in player.GetComponentsInChildren<Interactor>())
            {
                if (interactor == null || !interactor.IsLocal || !interactor.IsInteracting) continue;
                var pickup = interactor.InteractingWith == null ? null : interactor.InteractingWith as Pickup;
                if (pickup == null) continue;
                if (TrackedRegistry.Contains(pickup.gameObject.GetInstanceID())
                    || TrackedRegistry.Contains(pickup.transform.root.gameObject.GetInstanceID()))
                    return pickup;
            }
            return null;
        }
    }
}
