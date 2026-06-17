using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RuinApp.Primitives;
using RuinApp.Workspace;

namespace RuinApp.Manipulation
{
    /// <summary>
    /// STAGE 3 — the unified grab, with the bonding "magnet" preserved.
    ///
    /// One click resolves to ONE grabbed root transform, by what it hits:
    ///   - cube TOP face   -> split the bar here, grab the peeled bar      (depth: cube)
    ///   - cube SIDE face  -> grab the cube's whole bar                     (depth: bar)
    ///   - claimed area    -> grab the cube's whole container               (depth: container)
    ///
    /// The drag that follows is a SINGLE code path: translate the grabbed root in XZ. Because
    /// cube->bar->container is a real Transform parent chain, the same move covers all three
    /// depths and never reads a count. A lone cube and a cluster run the identical resolver.
    ///
    /// Preserved properties from before (behavior parity, cleaner internals):
    ///   - BONDING / magnet: on release, a moved bar whose left face is within bondTolerance of
    ///     another bar's right face snaps onto it (animated), up to MaxLength. Bars only.
    ///   - CONTAINER grouping by proximity: ContainerDetector.ReclusterAllBars on release.
    ///     (This recluster is the one borrowed bridge, to be replaced by local birth/death rules.)
    ///
    /// Replaces WorkspaceInputController. Keep ContainerDetector in the scene.
    /// </summary>
    public class GrabController : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private float groundY = 0f;
        [SerializeField] private float topFaceNormalThreshold = 0.7f;

        [Header("Bonding (magnet)")]
        [SerializeField] private float bondTolerance = 0.3f;
        [SerializeField] private float snapDurationSeconds = 0.2f;

        [Header("Wiring")]
        [SerializeField] private GrammarConfig config;
        [SerializeField] private ContainerDetector detector;

        [Header("Debug")]
        [Tooltip("Logs which layer each click resolved to (top/side/claim). Turn off before commit.")]
        [SerializeField] private bool debugLog = true;

        private Plane ground;
        private Transform grabbedRoot;   // the one thing the drag moves
        private Bar grabbedBar;          // set for cube/bar grabs, null for container grabs (drives bonding)
        private Container splitOrigin;   // container a cube was split out of; resolve reconsiders it on release
        private Vector3 grabOffset;       // XZ offset from grab point to root, frozen at grab

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (detector == null) detector = FindAnyObjectByType<ContainerDetector>();
            ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        }

        private void Update()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
                BeginGrab();
            else if (Mouse.current.leftButton.isPressed && grabbedRoot != null)
                Drag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame && grabbedRoot != null)
                Release();
        }

        // ---------------- the proven primitive (stage 1) ----------------

        private bool ScreenToGround(Vector2 screenPx, out Vector3 world)
        {
            world = default;
            if (cam == null) return false;
            if (screenPx.x < 0f || screenPx.y < 0f ||
                screenPx.x > cam.pixelWidth || screenPx.y > cam.pixelHeight) return false;

            Ray ray = cam.ScreenPointToRay(screenPx);
            if (!ground.Raycast(ray, out float enter)) return false;
            world = ray.GetPoint(enter);
            return true;
        }

        // ---------------- resolution: hit -> one grabbed root ----------------

        private void BeginGrab()
        {
            splitOrigin = null;
            Vector2 px = Mouse.current.position.ReadValue();
            if (!ScreenToGround(px, out Vector3 g)) return;

            // A hit only counts as a CUBE grab if it actually carries a Cube. Any other collider
            // (ground plane, shadow quad) is transparent: we fall through to the claim test, which
            // uses the projected ground point g and never depends on what the ray physically struck.
            Cube cube = null;
            RaycastHit hit = default;
            if (Physics.Raycast(cam.ScreenPointToRay(px), out hit, 100f))
                cube = hit.collider.GetComponent<Cube>();

            if (cube != null)
            {
                EnsureWrapped(cube); // a fresh cube pulled from the source becomes 1 cube / 1 bar

                if (hit.normal.y > topFaceNormalThreshold)
                {
                    splitOrigin = cube.Bar.Container;            // resolve reconsiders this on release
                    grabbedBar = DetachToNewBar(cube);           // TOP -> detach only, no teardown
                    grabbedRoot = grabbedBar.transform;
                    Log("top  -> split bar");
                }
                else
                {
                    grabbedBar = cube.Bar;                       // SIDE -> bar depth
                    grabbedRoot = grabbedBar.transform;
                    Log("side -> bar");
                }
            }
            else
            {
                Cube claimed = CubeWhoseClaimContains(new Vector2(g.x, g.z));
                if (claimed == null) { Log("empty -> no-op"); return; }

                EnsureWrapped(claimed);
                Container c = claimed.Bar != null ? claimed.Bar.Container : null;
                grabbedBar = null;                               // container grab does not bond
                grabbedRoot = (c != null) ? c.transform : claimed.Bar.transform; // CLAIM -> container
                Log(c != null ? "claim -> container" : "claim -> bar (no container yet)");
            }

            grabOffset = Flat(grabbedRoot.position) - Flat(g);   // freeze cursor-relative offset
        }

        // ---------------- drag: ONE path, no count ----------------

        private void Drag()
        {
            if (!ScreenToGround(Mouse.current.position.ReadValue(), out Vector3 g)) return; // off-view: hold

            grabbedRoot.position = new Vector3(
                g.x + grabOffset.x,
                grabbedRoot.position.y,   // preserve height; pure flat slide
                g.z + grabOffset.z
            );
        }

        // ---------------- release: bonding (magnet) + the borrowed recluster bridge ----------------

        private void Release()
        {
            Transform root = grabbedRoot;
            Bar released = grabbedBar;
            Container origin = splitOrigin;
            grabbedRoot = null;
            grabbedBar = null;
            splitOrigin = null;

            // Magnet: a moved bar snaps onto a neighbour's right face if within tolerance.
            if (released != null)
            {
                Bar target = FindBondCandidate(released);
                if (target != null)
                {
                    StartCoroutine(MergeBarsAnimated(target, released, origin)); // resolves (+cleans origin) when done
                    return;
                }
            }

            if (detector == null) return;

            if (released != null)
            {
                // bar/cube grab: one bar moved; origin (non-null only for a split) is reconsidered too
                detector.ResolveNeighborhood(new List<Bar> { released }, origin);
            }
            else
            {
                // container grab: the whole container moved rigidly — re-resolve its bars as a group
                Container moved = root != null ? root.GetComponent<Container>() : null;
                if (moved != null) detector.ResolveNeighborhood(new List<Bar>(moved.Members));
            }
        }

        // ---------------- bonding (preserved property) ----------------

        private Bar FindBondCandidate(Bar released)
        {
            Cube leftmost = released.GetLeftmostMember();
            if (leftmost == null) return null;
            Vector3 releasedLeftFace = leftmost.transform.position + new Vector3(-0.5f, 0f, 0f);

            Bar best = null;
            float bestDistance = float.MaxValue;

            foreach (Bar candidate in FindObjectsByType<Bar>(FindObjectsInactive.Exclude))
            {
                if (candidate == released) continue;
                if (candidate.Length + released.Length > Bar.MaxLength) continue;

                Cube rightmost = candidate.GetRightmostMember();
                if (rightmost == null) continue;

                Vector3 candidateRightFace = rightmost.transform.position + new Vector3(0.5f, 0f, 0f);
                float d = new Vector3(
                    releasedLeftFace.x - candidateRightFace.x, 0f,
                    releasedLeftFace.z - candidateRightFace.z).magnitude;

                if (d <= bondTolerance && d < bestDistance)
                {
                    best = candidate;
                    bestDistance = d;
                }
            }
            return best;
        }

        private IEnumerator MergeBarsAnimated(Bar target, Bar incoming, Container origin = null)
        {
            Cube targetRightmost = target.GetRightmostMember();
            Vector3 baseTarget = targetRightmost.transform.position + new Vector3(1f, 0f, 0f);

            List<Vector3> startPositions = new List<Vector3>();
            List<Vector3> endPositions = new List<Vector3>();
            for (int i = 0; i < incoming.Length; i++)
            {
                startPositions.Add(incoming.Members[i].transform.position);
                endPositions.Add(baseTarget + new Vector3(i * 1f, 0f, 0f));
            }

            float elapsed = 0f;
            while (elapsed < snapDurationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / snapDurationSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                for (int i = 0; i < incoming.Length; i++)
                    incoming.Members[i].transform.position = Vector3.Lerp(startPositions[i], endPositions[i], eased);
                yield return null;
            }
            for (int i = 0; i < incoming.Length; i++)
                incoming.Members[i].transform.position = endPositions[i];

            List<Cube> transfer = new List<Cube>(incoming.Members);
            incoming.RemoveFromIndex(0);
            foreach (Cube c in transfer) target.AddMember(c);

            if (incoming.Container != null) incoming.Container.RemoveMember(incoming);
            Destroy(incoming.gameObject);

            if (detector != null) detector.ResolveNeighborhood(new List<Bar> { target }, origin);
        }

        // ---------------- structural helpers ----------------

        /// <summary>Detach this cube (and everything right of it) into a new bar. Bar-level surgery
        /// only: the emptied leftover bar is destroyed (unambiguous garbage), but NO container is
        /// torn down here. Container lifecycle is owned entirely by ResolveNeighborhood, seeded with
        /// the origin container (splitOrigin) on release.</summary>
        private Bar DetachToNewBar(Cube cube)
        {
            Bar oldBar = cube.Bar;

            List<Cube> peeled = oldBar.RemoveFromIndex(oldBar.IndexOf(cube));
            Bar newBar = Bar.CreateForCube(peeled[0]);
            for (int i = 1; i < peeled.Count; i++) newBar.AddMember(peeled[i]);

            if (oldBar.Length == 0)
            {
                if (oldBar.Container != null) oldBar.Container.RemoveMember(oldBar);
                Destroy(oldBar.gameObject);
            }
            return newBar;
        }

        /// <summary>A cube fresh from the source has no bar yet; give it one and release the source.</summary>
        private void EnsureWrapped(Cube cube)
        {
            if (cube.Bar != null) return;

            CubeSource source = cube.GetComponentInParent<CubeSource>();
            if (source != null) cube.transform.SetParent(null);
            Bar.CreateForCube(cube);
            if (source != null) source.NotifyCubeLeftSource(cube);
        }

        private Cube CubeWhoseClaimContains(Vector2 point)
        {
            foreach (Cube cube in FindObjectsByType<Cube>(FindObjectsInactive.Exclude))
                if (cube.GetClaimedArea().Contains(point))
                    return cube;
            return null;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void Log(string what)
        {
            if (debugLog) Debug.Log($"[Grab] {what}");
        }
    }
}