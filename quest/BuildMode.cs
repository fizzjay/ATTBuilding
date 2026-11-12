
using System;
using HarmonyLib;
using ATTBuilding; // shared/ types (BuildState, CommandParser, TrackedRegistry, VectorSnap): sibling namespace, not visible unqualified without this
using Il2Cpp;
using Il2CppAlta.Networking;
using Il2CppAlta.Networking.Scripts.Player;
using Il2CppATT.Character.QuickAccessMenu;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ATTBuildingQuest
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
                    var addedComp = go.AddComponent(Il2CppType.Of<Pickup>()); pickup = addedComp == null ? null : addedComp.TryCast<Pickup>(); if (pickup == null) throw new InvalidOperationException("AddComponent<Pickup> returned null");
                    added = true;
                }
                FixupPickupFields(go, pickup);
                int points = 0;
                try { points = pickup.GrabPoints == null ? 0 : pickup.GrabPoints.Length; } catch { }
                if (points == 0) InstallSurfaceGrabPoint(go, pickup);
                try { points = pickup.GrabPoints == null ? 0 : pickup.GrabPoints.Length; } catch { }
                if (added)
                {
                    // Awake ran at AddComponent, but a network behaviour only
                    // becomes part of its entity in Initialize() -- which the
                    // game calls during entity setup, long before we added
                    // this. Run it now (guarded: if the entity refuses a
                    // late behaviour we learn that here, not silently).
                    try { pickup.Initialize(); Diag.Log("buildmode: Initialize() ran for added Pickup on '" + go.name + "'."); }
                    catch (Exception iex) { Diag.Log("buildmode: Initialize() FAILED for '" + go.name + "': " + iex.Message); }
                }
                try
                {
                    Diag.Log("buildmode: grabbable '" + go.name + "': pickup " + (added ? "ADDED" : "already present")
                        + ", grabPoints=" + points
                        + ", entityLinked=" + (pickup.Entity != null)
                        + ", rigidbodyField=" + (pickup.Rigidbody != null)
                        + ", collidersSet=" + pickup.Colliders.Count
                        + ", networkRigidbody=" + (go.GetComponent<NetworkRigidbody>() != null)
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
            // IL2CPP: grabPoints is an Il2CppReferenceArray<GrabPoint>, not a
            // managed GrabPoint[] -- a plain array fails ConvertTo with
            // 'Object must implement IConvertible' (the 0.4.7 failure).
            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GrabPoint>(1);
            arr[0] = gp;
            TrySet(pickup, "grabPoints", arr);
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
        }

        private static void Place(Pickup pickup)
        {
            try
            {
                if (pickup == null) return;
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


        private static readonly System.Collections.Generic.Dictionary<string, float> _diagSeen = new System.Collections.Generic.Dictionary<string, float>();

        internal static void NoteGrabCheck(Pickup pickup, string what, bool result)
        {
            try
            {
                if (pickup == null) return;
                int id = pickup.gameObject.GetInstanceID();
                if (!TrackedRegistry.Contains(id) && !TrackedRegistry.Contains(pickup.transform.root.gameObject.GetInstanceID())) return;
                string key = what + ":" + id;
                float now = UnityEngine.Time.time;
                float last;
                if (_diagSeen.TryGetValue(key, out last) && now - last < 1.5f) return;
                _diagSeen[key] = now;
                Diag.Log("grabdiag: " + what + " on '" + pickup.gameObject.name + "' -> " + result
                    + " (interactors=" + pickup.Interactors.Count + ", docked=" + pickup.IsDocked + ", rb=" + (pickup.Rigidbody != null) + ")");
            }
            catch { }
        }

        // The ShardClient Readout pattern: scan the local player's hands.
        private static Pickup FindHeldTracked()
        {
            var player = PlayerController.Current;
            if (player == null) return null;
            foreach (var interactor in player.GetComponentsInChildren<Interactor>())
            {
                if (interactor == null || !interactor.IsLocal || !interactor.IsInteracting) continue;
                var pickup = interactor.InteractingWith == null ? null : interactor.InteractingWith.TryCast<Pickup>();
                if (pickup == null) continue;
                if (TrackedRegistry.Contains(pickup.gameObject.GetInstanceID())
                    || TrackedRegistry.Contains(pickup.transform.root.gameObject.GetInstanceID()))
                    return pickup;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Pickup), "AssessScore")]
    internal static class GrabDiagAssess
    {
        [HarmonyPostfix]
        private static void Postfix(Pickup __instance, bool __result) { BuildMode.NoteGrabCheck(__instance, "AssessScore", __result); }
    }

    [HarmonyPatch(typeof(Pickup), "CheckInteractAllowedAndReplace")]
    internal static class GrabDiagAllowed
    {
        [HarmonyPostfix]
        private static void Postfix(Pickup __instance, bool __result) { BuildMode.NoteGrabCheck(__instance, "CheckInteractAllowed", __result); }
    }

    [HarmonyPatch(typeof(Pickup), "Interact", new System.Type[] { typeof(Interactor) })]
    internal static class GrabDiagInteract
    {
        [HarmonyPostfix]
        private static void Postfix(Pickup __instance) { BuildMode.NoteGrabCheck(__instance, "Interact()", true); }
    }
}
