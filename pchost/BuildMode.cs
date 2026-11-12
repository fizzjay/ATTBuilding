
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
                if (added) HydratePickupGraph(pickup);
                if (added) DumpGrabSignatures(pickup);
                int points = 0;
                try { points = pickup.GrabPoints == null ? 0 : pickup.GrabPoints.Length; } catch { }
                if (points == 0) InstallSurfaceGrabPoints(go, pickup);
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
                    // 0.4.15: hydration now runs BEFORE this point and
                    // pre-creates exactly what Awake died on in 0.4.9-0.4.13
                    // (add_Undocked NRE: the docked/undocked
                    // PickupDockedEvent fields were null; 0.4.14 log shows
                    // them hydrated). With those in place and the entity
                    // linked, retry the real Initialize() -> Awake: if it
                    // completes, the Pickup gets genuine Awake state
                    // (docker / dockingChecker / pickupScaler are still
                    // null and only Awake creates them). If it still NREs,
                    // the replicated state stays and the grab probe below
                    // reports what the game's own scoring says.
                    try { pickup.Initialize(); Diag.Log("buildmode: Initialize() RAN for '" + go.name + "' after hydration (entityLinked=" + (pickup.Entity != null) + ", enabled=" + pickup.enabled + ")."); }
                    catch (Exception iex) { Diag.Log("buildmode: Initialize() still FAILED for '" + go.name + "': " + iex.Message); }
                    // Initialize may leave the behaviour disabled again.
                    try { if (!pickup.enabled) { pickup.enabled = true; Diag.Log("buildmode: re-enabled Pickup on '" + go.name + "' after Initialize."); } } catch { }
                    if (!_nullsDumped2) { _nullsDumped2 = true; DumpNullMembers(pickup, "still null post-init"); }
                }
                if (added) _addedIds.Add(go.GetInstanceID());
                if (added) FreezeBody(go, pickup);
                DumpObjectProfile(added ? "ours" : "native", go, pickup);
                if (!added) CaptureNativeTemplate(go, pickup);
                if (added) ApplyNativeTemplateTo(go, pickup);
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

        // Whole-mesh grabbing (user requirement: grab the spawned object
        // ANYWHERE on its mesh, not at one sweet spot). Instead of a single
        // GrabPoint, cover the collider bounds with surface GrabPoints --
        // centre, six face centres, twelve edge centres -- each in
        // RotationMode.Surface with angle checks ignored, each wired back
        // to its Pickup. The hand's scoring then always finds a point
        // within half an extent of wherever it touches the mesh.
        private static void InstallSurfaceGrabPoints(GameObject go, Pickup pickup)
        {
            Bounds bounds = LocalBounds(go, pickup.transform);
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            var ext = new Vector3(Mathf.Max(e.x, 0.03f), Mathf.Max(e.y, 0.03f), Mathf.Max(e.z, 0.03f));
            var positions = new System.Collections.Generic.List<Vector3>();
            positions.Add(c);
            positions.Add(c + new Vector3(ext.x, 0f, 0f));
            positions.Add(c - new Vector3(ext.x, 0f, 0f));
            positions.Add(c + new Vector3(0f, ext.y, 0f));
            positions.Add(c - new Vector3(0f, ext.y, 0f));
            positions.Add(c + new Vector3(0f, 0f, ext.z));
            positions.Add(c - new Vector3(0f, 0f, ext.z));
            float k = 0.7f;
            positions.Add(c + new Vector3(ext.x * k, ext.y * k, 0f));
            positions.Add(c + new Vector3(ext.x * k, -ext.y * k, 0f));
            positions.Add(c + new Vector3(-ext.x * k, ext.y * k, 0f));
            positions.Add(c + new Vector3(-ext.x * k, -ext.y * k, 0f));
            positions.Add(c + new Vector3(ext.x * k, 0f, ext.z * k));
            positions.Add(c + new Vector3(ext.x * k, 0f, -ext.z * k));
            positions.Add(c + new Vector3(-ext.x * k, 0f, ext.z * k));
            positions.Add(c + new Vector3(-ext.x * k, 0f, -ext.z * k));
            positions.Add(c + new Vector3(0f, ext.y * k, ext.z * k));
            positions.Add(c + new Vector3(0f, ext.y * k, -ext.z * k));
            positions.Add(c + new Vector3(0f, -ext.y * k, ext.z * k));
            positions.Add(c + new Vector3(0f, -ext.y * k, -ext.z * k));
            var made = new System.Collections.Generic.List<GrabPoint>();
            foreach (var p in positions)
            {
                var gp = new GrabPoint();
                // Start from a real point's design values when a native
                // template has been captured (0.4.16) -- editor defaults
                // we never saw may be load-bearing -- then impose the
                // whole-mesh layout on top.
                if (_gpTemplateValues != null) CopyGrabPointTemplate(gp);
                gp.rotationMode = RotationMode.Surface;
                // Reflective writes, NOT direct assignment: the Mono
                // (PC) GrabPoint has no 'position'/'poleForward'/
                // 'isIgnoringAngle' members under those names (0.4.16
                // PC build error) -- member naming differs per target,
                // which is why this whole file goes through walkers.
                TrySetMemberWalk(gp, "position", p);
                TrySetMemberWalk(gp, "poleForward", ext);
                TrySetMemberWalk(gp, "isIgnoringAngle", true);
                WireGrabPoint(gp, pickup);
                made.Add(gp);
            }
            TrySet(pickup, "grabPoints", made.ToArray());
            Diag.Log("buildmode: whole-mesh grab on '" + go.name + "': " + made.Count + " surface grab points, extents=" + ext);
        }

        private static bool _wireLogged;

        // A GrabPoint created in code differs from an editor-authored one:
        // nobody serialized its fields. The 0.4.14 signature dump shows
        // GrabPoint has NO owner back-reference member (CheckScore
        // receives the Pickup transform as a parameter instead), but it
        // DOES have a serialized 'transform' member editor points always
        // carry -- left null, anything dereferencing it fails silently
        // per point and the candidate is discarded. Wire transform +
        // local-space interpretation, and backfill null collections
        // (enabledFromDocks) the scoring path may enumerate.
        private static void WireGrabPoint(GrabPoint gp, Pickup pickup)
        {
            bool wired = TrySetMemberWalk(gp, "transform", pickup.transform);
            if (!wired) wired = TrySet(gp, "transform", pickup.transform);
            // A captured native template already dictated isUsingLocal;
            // only force local space while we are still guessing.
            if (_gpTemplateValues == null) TrySet(gp, "isUsingLocal", true);
            HydrateGraph(gp, HydrateSkip, null);
            if (!_wireLogged) { _wireLogged = true; Diag.Log("buildmode: GrabPoint transform wired=" + wired + " (19 surface points)"); }
        }

        // Hierarchy-walking, non-public-aware member set with a type check.
        // ConsoleHook.SetMember is declared-type/public-only and silently
        // no-ops on a miss, so owner/link wiring uses this instead.
        private static bool TrySetMemberWalk(object target, string name, object value)
        {
            if (target == null || value == null) return false;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            for (var type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                var field = type.GetField(name, flags);
                if (field != null && field.FieldType.IsInstanceOfType(value))
                {
                    try { field.SetValue(target, value); return true; } catch { return false; }
                }
                var prop = type.GetProperty(name, flags);
                if (prop != null && prop.GetIndexParameters().Length == 0 && prop.PropertyType.IsInstanceOfType(value))
                {
                    var setter = prop.GetSetMethod(true);
                    if (setter != null) { try { setter.Invoke(target, new object[] { value }); return true; } catch { return false; } }
                }
            }
            return false;
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
        private static bool _refDumped;



        // Pickup.Awake creates the Pickup's working collections (the sets
        // behind IsDocked and friends). A runtime-added Pickup never gets a
        // clean Awake -- Initialize() dies inside it at the add_Undocked
        // subscription -- so those getters NRE forever (0.4.12 poller:
        // docked=?) and the game's candidate evaluation likely throws on
        // this object and discards it. Replicate that part of Awake: fill
        // every null array/collection member with an empty instance.
        // 0.4.13 walked FIELDS only and logged nothing: on IL2CPP the
        // interop wrappers expose native fields as PROPERTIES, so the walk
        // saw an empty class. Enumerate both, over the full hierarchy.
        // Members legitimately wired elsewhere (entity link,
        // [GetComponent] injections, grabPoints) are skipped -- their own
        // steps set them.
        private static readonly string[] HydrateSkip =
        {
            "entity", "Entity", "transform", "gameObject", "rigidbody",
            "networkRigidbody", "grabPoints", "childrenColliders", "Colliders",
            "IsGrabbed", "IsDocked",
            // 0.4.16: null on a real Pickup until the game itself scores
            // a grab (0.4.15 refnull). Hydrating bestGrab installed an
            // empty GrabPoint sitting at the world origin -- exactly
            // where our probe scores (~827k) said the winning point
            // was. Never invent these.
            "bestGrab", "BestGrab", "dockedMemoryLeft", "dockedMemoryRight",
        };

        private static readonly object Unreadable = new object();

        private sealed class MemberSlot
        {
            public string Name;
            public Type MemberType;
            public string Declaring;
            public System.Reflection.FieldInfo Field;
            public System.Reflection.PropertyInfo Property;
            public object Read(object target)
            {
                try { return Field != null ? Field.GetValue(target) : Property.GetValue(target, null); }
                catch { return Unreadable; }
            }
            public bool Write(object target, object value)
            {
                try
                {
                    if (Field != null) { Field.SetValue(target, value); return true; }
                    var setter = Property.GetSetMethod(true);
                    if (setter == null) return false;
                    setter.Invoke(target, new object[] { value });
                    return true;
                }
                catch { return false; }
            }
        }

        private static System.Collections.Generic.List<MemberSlot> GraphMembers(Type root)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            var slots = new System.Collections.Generic.List<MemberSlot>();
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var type = root; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (!seen.Add(field.Name)) continue;
                    slots.Add(new MemberSlot { Name = field.Name, MemberType = field.FieldType, Declaring = type.Name, Field = field });
                }
                foreach (var prop in type.GetProperties(flags))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || !seen.Add(prop.Name)) continue;
                    slots.Add(new MemberSlot { Name = prop.Name, MemberType = prop.PropertyType, Declaring = type.Name, Property = prop });
                }
            }
            return slots;
        }

        private static int HydrateGraph(object target, string[] skip, string label)
        {
            int made = 0;
            var names = new System.Collections.Generic.List<string>();
            foreach (var slot in GraphMembers(target.GetType()))
            {
                if (skip != null && System.Array.IndexOf(skip, slot.Name) >= 0) continue;
                if (slot.MemberType.IsValueType) continue;
                var current = slot.Read(target);
                if (current == Unreadable || current != null) continue;
                var empty = TryMakeEmpty(slot.MemberType);
                if (empty == null) continue;
                if (slot.Write(target, empty)) { made++; if (label != null) names.Add(slot.Declaring + "." + slot.Name + " : " + slot.MemberType.Name); }
            }
            if (label != null)
            {
                if (made > 0) Diag.Log("buildmode: hydrated " + made + " empty " + label + " members (" + string.Join(", ", names.ToArray()) + ")");
                else Diag.FileOnly("buildmode: hydration found no empty " + label + " members to fill");
            }
            return made;
        }

        private static void HydratePickupGraph(Pickup pickup)
        {
            HydrateGraph(pickup, HydrateSkip, "Pickup");
            if (!_nullsDumped) { _nullsDumped = true; DumpNullMembers(pickup, "still null"); }
        }

        private static object TryMakeEmpty(Type type)
        {
            try
            {
                if (type == typeof(string)) return null;
                if (typeof(Delegate).IsAssignableFrom(type)) return null;
                if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;
                if (type.IsArray) return System.Array.CreateInstance(type.GetElementType(), 0);
                if (type.IsGenericType)
                {
                    var def = type.GetGenericTypeDefinition();
                    if (def == typeof(System.Collections.Generic.List<>)
                        || def == typeof(System.Collections.Generic.HashSet<>)
                        || def == typeof(System.Collections.Generic.Dictionary<,>)
                        || def == typeof(System.Collections.Generic.Queue<>)
                        || def == typeof(System.Collections.Generic.Stack<>))
                        return Activator.CreateInstance(type);
                }
                if (type.IsClass && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    return Activator.CreateInstance(type);
            }
            catch { }
            return null;
        }

        private static bool _nullsDumped;
        private static bool _nullsDumped2;

        // Whatever is STILL null after hydration is something Awake would
        // have wired to another object (a sibling component, a dock, ...).
        // Those names in the log tell us what to supply next. Run against
        // a real Pickup too ('refnull:') so normally-null optionals can be
        // told apart from Awake-created state we are missing.
        private static void DumpNullMembers(object target, string prefix)
        {
            try
            {
                var nulls = new System.Collections.Generic.List<string>();
                foreach (var slot in GraphMembers(target.GetType()))
                {
                    if (slot.MemberType.IsValueType) continue;
                    var v = slot.Read(target);
                    if (v != Unreadable && v == null) nulls.Add(slot.Declaring + "." + slot.Name + " : " + slot.MemberType.Name);
                }
                if (nulls.Count == 0) { Diag.Log("buildmode: " + prefix + ": none"); return; }
                int shown = 0;
                foreach (var n in nulls) { if (++shown > 40) break; Diag.Log("buildmode: " + prefix + ": " + n); }
                if (nulls.Count > 40) Diag.Log("buildmode: " + prefix + ": ... +" + (nulls.Count - 40) + " more");
            }
            catch (Exception ex) { Diag.Log("buildmode: null-member dump failed: " + ex.Message); }
        }

        private static bool _sigsDumped;

        // One-shot (debug file only): the exact grab-pipeline signatures the
        // game exposes, so the next step can invoke the game's own scoring
        // or wiring reflectively instead of patching (Harmony on the
        // ref-parameter AssessScore crashed Quest in 0.4.9 -- never re-add
        // that; reflective Invoke handles ref/out legally).
        private static void DumpGrabSignatures(Pickup pickup)
        {
            if (_sigsDumped) return;
            _sigsDumped = true;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var root in new[] { pickup.GetType(), typeof(GrabPoint) })
            {
                for (var type = root; type != null && type != typeof(object) && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
                {
                    try
                    {
                        foreach (var method in type.GetMethods(flags))
                        {
                            var n = method.Name;
                            bool interesting = n.IndexOf("Grab", StringComparison.Ordinal) >= 0
                                || n.IndexOf("Interact", StringComparison.Ordinal) >= 0
                                || n.IndexOf("Score", StringComparison.Ordinal) >= 0
                                || n.IndexOf("Dock", StringComparison.Ordinal) >= 0
                                || n.IndexOf("Assess", StringComparison.Ordinal) >= 0
                                || n.IndexOf("Ensure", StringComparison.Ordinal) >= 0;
                            if (!interesting) continue;
                            var ps = new System.Collections.Generic.List<string>();
                            foreach (var p in method.GetParameters())
                                ps.Add((p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : "")) + p.ParameterType.Name + " " + p.Name);
                            Diag.FileOnly("sig: " + type.Name + "." + method.Name + "(" + string.Join(", ", ps.ToArray()) + ") -> " + method.ReturnType.Name);
                        }
                        foreach (var prop in type.GetProperties(flags))
                            Diag.FileOnly("prop: " + type.Name + "." + prop.Name + " : " + prop.PropertyType.Name + (prop.CanWrite ? " (set)" : ""));
                    }
                    catch { }
                }
            }
        }

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
                    Interactor nearestHand = null;
                    if (player != null)
                    {
                        foreach (var hand in player.GetComponentsInChildren<Interactor>())
                        {
                            if (hand == null || !hand.IsLocal) continue;
                            float d = Vector3.Distance(hand.transform.position, pickup.transform.position);
                            if (nearest < 0f || d < nearest) { nearest = d; nearestHand = hand; }
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
                        + " points=" + Safe(() => pickup.GrabPoints.Length)
                        + " canInteract=" + SafeMember(pickup, "IsInteractable")
                        + " netwk=" + SafeMember(pickup, "IsNetworked")
                        + " chan=" + SafeMember(pickup, "Channels")
                        + "";
                    if (nearestHand != null && nearest >= 0f && nearest <= 0.8f && now >= _probeNext)
                    {
                        _probeNext = now + 2f;
                        ProbeGrab(pickup, nearestHand, "'" + go.name + "'");
                        if (_refPickup != null && _refPickup != pickup
                            && Vector3.Distance(nearestHand.transform.position, _refPickup.transform.position) <= 0.9f)
                            ProbeGrab(_refPickup, nearestHand, "ref");
                    }
                    string prev;
                    if (!_diagState.TryGetValue(id, out prev) || prev != state)
                    {
                        _diagState[id] = state;
                        Diag.Log("grabdiag: '" + go.name + "' " + state);
                    }
                    if (!_refDumped)
                    {
                        _refDumped = true;
                        // Ground truth: state of the nearest REAL (game-
                        // spawned) Pickup, for direct comparison with ours.
                        try
                        {
                            Pickup best = null; float bestD = 9f;
                            foreach (var other in UnityEngine.Object.FindObjectsOfType<Pickup>())
                            {
                                if (other == null || other == pickup) continue;
                                float d = Vector3.Distance(other.transform.position, pickup.transform.position);
                                if (d < bestD) { bestD = d; best = other; }
                            }
                            if (best != null)
                                Diag.Log("grabdiag ref: nearest real Pickup '" + best.gameObject.name + "' interactors=" + Safe(() => best.Interactors.Count)
                                    + " docked=" + Safe(() => best.IsDocked)
                                    + " kinematic=" + Safe(() => best.Rigidbody != null && best.Rigidbody.isKinematic)
                                    + " entity=" + Safe(() => best.Entity != null)
                                    + " enabled=" + Safe(() => best.enabled)
                                    + " points=" + Safe(() => best.GrabPoints.Length)
                                    + " canInteract=" + SafeMember(best, "IsInteractable")
                                    + " netwk=" + SafeMember(best, "IsNetworked")
                                    + " chan=" + SafeMember(best, "Channels")
                                    + " netRb=" + (best.gameObject.GetComponent<NetworkRigidbody>() != null));
                            if (best != null && !_refNullsDumped)
                            {
                                _refNullsDumped = true;
                                _refPickup = best;
                                DumpNullMembers(best, "refnull");
                            }
                            else Diag.Log("grabdiag ref: no other Pickup in scene to compare with.");
                        }
                        catch (Exception rex) { Diag.Log("grabdiag ref failed: " + rex.Message); }
                    }
                }
            }
            catch (Exception ex) { Mod.LogOnce("grabdiag poll: " + ex.Message); }
        }


        private static float _probeNext;
        private static bool _refNullsDumped;
        private static Pickup _refPickup;

        // Read a member by name over the hierarchy; "-" when absent, "?"
        // when its getter throws. Compile-safe on both targets (Mono and
        // IL2CPP member sets differ), unlike direct property access.
        private static string SafeMember(object target, string name)
        {
            if (target == null) return "-";
            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    var p = t.GetProperty(name, flags);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        var v = p.GetValue(target, null);
                        return v == null ? "null" : v.ToString();
                    }
                    var f = t.GetField(name, flags);
                    if (f != null)
                    {
                        var v = f.GetValue(target);
                        return v == null ? "null" : v.ToString();
                    }
                }
                return "-";
            }
            catch { return "?"; }
        }

        // Reflectively run the game's own grab pipeline for one real hand
        // and log the verdicts. (Invoking is legal where Harmony-patching
        // a ref/out method is not -- 0.4.9 crash.) If AssessScore returns
        // false/0 or throws, the candidate rejection is inside the game
        // and the exception names the missing state.
        private static void ProbeGrab(Pickup pickup, Interactor hand, string tag)
        {
            DumpInteractorMembers(hand);
            try
            {
                Diag.Log("grabprobe " + tag + ": allowed=" + InvokeProbe(pickup, "CheckInteractAllowedAndReplace", new object[] { hand })
                    + " assess=" + InvokeProbe(pickup, "AssessScore", new object[] { hand, 0f })
                    + " bestGrabSet=" + Safe(() => pickup.BestGrab != null));
            }
            catch (Exception ex) { Diag.Log("grabprobe " + tag + " failed: " + ex.Message); }
        }

        private static string InvokeProbe(object target, string name, object[] args)
        {
            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.Name != name) continue;
                        var ps = m.GetParameters();
                        if (ps.Length != args.Length) continue;
                        if (args.Length > 0 && args[0] != null && !ps[0].ParameterType.IsInstanceOfType(args[0])) continue;
                        object result = m.Invoke(target, args);
                        string extra = args.Length > 1 ? " score=" + args[1] : "";
                        return (result == null ? "void" : result.ToString()) + extra;
                    }
                }
                return "nomethod";
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return "EX " + inner.GetType().Name;
            }
        }


        // ---------------------------------------------------------------
        // 0.4.16 native template: the first tracked object whose Pickup
        // came WITH its prefab (e.g. a spawned Stick) is ground truth for
        // how a grabbable thing presents itself -- collider layers for
        // the hand's discovery scan, and a real GrabPoint's serialized
        // design values. Dump ours-vs-native profiles, capture that
        // template, and retrofit it onto every Pickup we added.
        private static readonly System.Collections.Generic.HashSet<int> _addedIds = new System.Collections.Generic.HashSet<int>();
        private static bool _templateCaptured;
        private static int _templateRootLayer = -1;
        private static int _templateColliderLayer = -1;
        private static System.Collections.Generic.Dictionary<string, object> _gpTemplateValues;
        private static bool _templateAppliedLogged;
        private static bool _ourProfileDumped;
        private static bool _nativeProfileDumped;
        private static bool _interactorDumped;

        private static readonly string[] GrabPointTemplateMembers =
        {
            "position", "rotationEuler", "linear", "poleForward", "offset",
            "rotationMode", "isUsingLocal", "isIgnoringAngle", "isDisabled",
            "thumbPosition", "refuseMultipleHands", "dockedRestriction",
            "nonDockedRestriction", "isSetupWithLeft",
        };

        private static string FormatValue(object v)
        {
            if (v == null) return "null";
            if (v is Vector3) { var t = (Vector3)v; return "(" + t.x.ToString("0.##") + "," + t.y.ToString("0.##") + "," + t.z.ToString("0.##") + ")"; }
            if (v is Quaternion) { var e = ((Quaternion)v).eulerAngles; return "euler(" + e.x.ToString("0.#") + "," + e.y.ToString("0.#") + "," + e.z.ToString("0.#") + ")"; }
            if (v is float) return ((float)v).ToString("0.###");
            if (v is string) return (string)v;
            if (v is System.Collections.IEnumerable) return "<" + v.GetType().Name + ">";
            if (v is UnityEngine.Object) return ((UnityEngine.Object)v).name + " <" + v.GetType().Name + ">";
            return v.ToString();
        }

        // One-shot per kind: how does this object present itself to the
        // physics/interaction world, ours (Pickup added by us) vs native
        // (Pickup shipped in the prefab)?
        private static void DumpObjectProfile(string tag, GameObject go, Pickup pickup)
        {
            if (tag == "ours") { if (_ourProfileDumped) return; _ourProfileDumped = true; }
            else { if (_nativeProfileDumped) return; _nativeProfileDumped = true; }
            try
            {
                Diag.Log("profile " + tag + ": '" + go.name + "' rootLayer=" + go.layer + " (" + LayerMask.LayerToName(go.layer) + ")"
                    + " rbKinematic=" + Safe(() => pickup.Rigidbody != null && pickup.Rigidbody.isKinematic)
                    + " rbDetect=" + Safe(() => pickup.Rigidbody == null ? "norb" : pickup.Rigidbody.detectCollisions.ToString())
                    + " isNetworked=" + SafeMember(pickup, "IsNetworked"));
                int shown = 0;
                foreach (var col in go.GetComponentsInChildren<Collider>())
                {
                    if (col == null) continue;
                    if (++shown > 10) break;
                    Diag.Log("profile " + tag + ": collider '" + col.name + "' layer=" + col.gameObject.layer + " (" + LayerMask.LayerToName(col.gameObject.layer) + ") trigger=" + col.isTrigger + " enabled=" + col.enabled);
                }
                var points = pickup.GrabPoints;
                if (points != null && points.Length > 0 && points[0] != null)
                {
                    foreach (var slot in GraphMembers(points[0].GetType()))
                    {
                        var v = slot.Read(points[0]);
                        if (v == Unreadable) continue;
                        Diag.Log("profile " + tag + ": gp0 " + slot.Declaring + "." + slot.Name + " = " + FormatValue(v));
                    }
                }
            }
            catch (Exception ex) { Diag.Log("profile " + tag + " failed: " + ex.Message); }
        }

        private static void CaptureNativeTemplate(GameObject go, Pickup pickup)
        {
            if (_templateCaptured) return;
            if (_addedIds.Contains(go.GetInstanceID())) return; // ours, misread as native on a re-run
            try
            {
                var colliders = go.GetComponentsInChildren<Collider>();
                if (colliders == null || colliders.Length == 0) return;
                var points = pickup.GrabPoints;
                if (points == null || points.Length == 0 || points[0] == null) return;
                _templateRootLayer = go.layer;
                _templateColliderLayer = colliders[0].gameObject.layer;
                var values = new System.Collections.Generic.Dictionary<string, object>();
                foreach (var slot in GraphMembers(points[0].GetType()))
                {
                    if (System.Array.IndexOf(GrabPointTemplateMembers, slot.Name) < 0) continue;
                    var v = slot.Read(points[0]);
                    if (v == Unreadable) continue;
                    values[slot.Name] = v;
                }
                _gpTemplateValues = values;
                _templateCaptured = true;
                Diag.Log("buildmode: native grab template captured from '" + go.name + "' (rootLayer=" + _templateRootLayer + " (" + LayerMask.LayerToName(_templateRootLayer) + ")"
                    + ", colliderLayer=" + _templateColliderLayer + " (" + LayerMask.LayerToName(_templateColliderLayer) + ")"
                    + ", gpMembers=" + values.Count + ") -- retrofitting our spawned pieces.");
                foreach (int id in TrackedRegistry.Snapshot())
                {
                    if (!_addedIds.Contains(id)) continue;
                    var other = Spawner.FindByInstanceId(id);
                    if (other == null) continue;
                    var otherPickup = other.GetComponentInChildren<Pickup>();
                    if (otherPickup == null) continue;
                    ApplyNativeTemplateTo(other, otherPickup);
                    InstallSurfaceGrabPoints(other, otherPickup);
                    Diag.Log("buildmode: retrofitted '" + other.name + "' with the native grab template.");
                }
            }
            catch (Exception ex) { Diag.Log("buildmode: native template capture failed: " + ex.Message); }
        }

        private static void CopyGrabPointTemplate(GrabPoint gp)
        {
            foreach (var slot in GraphMembers(gp.GetType()))
            {
                object v;
                if (!_gpTemplateValues.TryGetValue(slot.Name, out v)) continue;
                if (v == null && slot.MemberType.IsValueType) continue;
                if (v != null && !slot.MemberType.IsInstanceOfType(v)) continue;
                slot.Write(gp, v);
            }
        }

        private static void ApplyNativeTemplateTo(GameObject go, Pickup pickup)
        {
            if (!_templateCaptured) return;
            try
            {
                go.layer = _templateRootLayer;
                foreach (var col in go.GetComponentsInChildren<Collider>())
                    if (col != null) col.gameObject.layer = _templateColliderLayer;
                if (!_templateAppliedLogged) { _templateAppliedLogged = true; Diag.Log("buildmode: native layers applied to '" + go.name + "' (root=" + _templateRootLayer + ", colliders=" + _templateColliderLayer + ")."); }
            }
            catch { }
        }

        // Building pieces are placed, not physics toys: a dynamic station
        // board fell through the world in the 0.4.15 session. Freeze
        // pieces we lend a Pickup to; the game's grab machinery drives
        // kinematic bodies while held (the console has worked this way
        // since 0.4.2).
        private static void FreezeBody(GameObject go, Pickup pickup)
        {
            try
            {
                var rb = pickup.Rigidbody != null ? pickup.Rigidbody : go.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = true;
                    Diag.Log("buildmode: froze '" + go.name + "' in place (kinematic).");
                }
            }
            catch { }
        }

        // One-shot (debug file): where would an Interactor keep its
        // candidates? Names guide the next probe -- no guessing.
        private static void DumpInteractorMembers(Interactor hand)
        {
            if (_interactorDumped) return;
            _interactorDumped = true;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            int shown = 0;
            for (var t = hand.GetType(); t != null && t != typeof(object) && shown < 150; t = t.BaseType)
            {
                try
                {
                    foreach (var f in t.GetFields(flags)) { if (++shown > 150) break; Diag.FileOnly("imember: " + t.Name + "." + f.Name + " : " + f.FieldType.Name); }
                    foreach (var p in t.GetProperties(flags)) { if (++shown > 150) break; Diag.FileOnly("imember: " + t.Name + "." + p.Name + " : " + p.PropertyType.Name + " (prop)"); }
                }
                catch { }
            }
            Diag.Log("buildmode: Interactor member list dumped to the debug file (imember:).");
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
