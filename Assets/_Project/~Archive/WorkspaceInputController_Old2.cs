using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RuinApp.Primitives;

namespace RuinApp.Manipulation
{
    /// <summary>
    /// Centralized input handler for the workspace.
    /// Cascade on left-press:
    ///   1. ray hits a cube TOP face  -> split the bar at that cube
    ///   2. ray hits a cube SIDE face -> move the whole bar
    ///   3. ray misses cubes but the projected ground point lands in a container's
    ///      claimed area -> grab the whole container (rigid slide of all member bars)
    ///   4. nothing / off-view -> no-op
    ///
    /// All pointer->world projection goes through TryGetPointerRay, which remaps the
    /// pointer out of the OS/editor pixel space (Windows display scaling) into the
    /// camera's render-target pixel space. One remap, one place; identity on Android.
    /// </summary>
    public class WorkspaceInputController_Old2 : MonoBehaviour
    {
        [SerializeField] private Camera workspaceCamera;
        [SerializeField] private LayerMask draggableLayerMask = ~0;
        [SerializeField] private float dragHeight = 0.5f;

        [Header("Bonding")]
        [SerializeField] private float bondTolerance = 0.3f;
        [SerializeField] private float snapDurationSeconds = 0.2f;

        [Header("Face detection")]
        [SerializeField] private float topFaceNormalThreshold = 0.7f;
        [SerializeField] private RuinApp.Workspace.ContainerDetector containerDetector;

        [Header("Debug (turn OFF before commit)")]
        [Tooltip("On each press, logs the raw pointer / render-target sizes (the [DIAG2] readout) " +
                 "and draws the camera->ground ray + landing stalk. Use to confirm the pointer " +
                 "remap, then disable. Editor-only effect; defaults off, so a committed build is clean.")]
        [SerializeField] private bool debugPointer = false;

        private Container heldContainer;
        private List<Vector3> heldContainerMemberOffsets; // each member bar's offset from the pointer at grab time
        private Bar heldBar;
        private Cube grabbedCube;
        private Vector3 grabOffset;
        private CubeSource sourceOfHeldBar;

        private Plane workspacePlane = new Plane(Vector3.up, Vector3.zero);

        // --- Pointer space (the space Mouse.current.position is reported in) ---
        // DIAG2 decides which pair matches the mouse's max extent at the view edge.
        // Default: the display's rendering size. If DIAG2 shows the *system* size matches
        // instead, swap these two lines to Display.main.systemWidth / systemHeight.
        private float PointerSpaceWidth  => Display.main.renderingWidth;
        private float PointerSpaceHeight => Display.main.renderingHeight;

        private void Awake()
        {
            if (workspaceCamera == null)
                workspaceCamera = Camera.main;

            if (containerDetector == null)
                containerDetector = FindAnyObjectByType<RuinApp.Workspace.ContainerDetector>();
        }

        private void Update()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
                TryBeginDrag();
            else if (Mouse.current.leftButton.isPressed && (heldBar != null || heldContainer != null))
                ContinueDrag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame && (heldBar != null || heldContainer != null))
                EndDrag();
        }

        // ---------------- POINTER (single corrected projection path) ----------------

        /// <summary>
        /// Reads the pointer and builds the world ray through it, remapping from the
        /// OS/editor pixel space (where Mouse.current.position lives under Windows display
        /// scaling) into the camera's render-target pixel space. Returns false when the
        /// pointer is outside the render target -- which, with a maximized Game view, is
        /// the "click is over editor UI / off-view" guard (cascade case 4 no-op). On a
        /// device, mouse-space == render space, so this never rejects and the remap is identity.
        /// </summary>
        private bool TryGetPointerRay(out Ray ray)
        {
            ray = default;
            if (Mouse.current == null || workspaceCamera == null) return false;

            Vector2 p = Mouse.current.position.ReadValue();

            // Reject clicks outside the render target (editor: cursor over other panels).
            if (p.x < 0f || p.y < 0f || p.x > workspaceCamera.pixelWidth || p.y > workspaceCamera.pixelHeight)
                return false;

            ray = workspaceCamera.ScreenPointToRay(p);
            return true;
        }

        /// <summary>
        /// The pointer's landing point on the workspace plane (Y=0), flattened.
        /// Returns Vector3.zero if the pointer is off-view or the ray is parallel to the plane.
        /// </summary>
        private Vector3 ComputePointerOnPlane()
        {
            if (TryGetPointerRay(out Ray planeRay) && workspacePlane.Raycast(planeRay, out float enter))
            {
                Vector3 p = planeRay.GetPoint(enter);
                return new Vector3(p.x, 0f, p.z);
            }
            return Vector3.zero;
        }

        // ---------------- DRAG START ----------------

        private void TryBeginDrag()
        {
            if (debugPointer) LogPointerDiag();

            if (!TryGetPointerRay(out Ray ray)) return;   // off-view -> no-op (case 4)

            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, draggableLayerMask))
            {
                // Ray missed all cubes. Check if the pointer landed inside a container's claimed area.
                TryBeginContainerDrag();
                return;
            }

            Cube hitCube = hit.collider.GetComponent<Cube>();
            if (hitCube == null) return;

            Debug.Log($"COLLIDER HIT: {hit.collider.name}  normal.y={hit.normal.y:F2}");

            bool grabbedFromTop = hit.normal.y > topFaceNormalThreshold;

            // Case 1: cube is at the source via parenting (just-spawned, never been dragged).
            CubeSource source = hitCube.GetComponentInParent<CubeSource>();
            if (source != null)
            {
                hitCube.transform.SetParent(null);
                Bar newBar = Bar.CreateForCube(hitCube);
                heldBar = newBar;
                sourceOfHeldBar = source;
                grabbedCube = hitCube;

                Vector3 cubeOnPlaneSrc = new Vector3(grabbedCube.transform.position.x, 0f, grabbedCube.transform.position.z);
                Vector3 pointerOnPlaneSrc = ComputePointerOnPlane();
                grabOffset = cubeOnPlaneSrc - pointerOnPlaneSrc;
                return;
            }

            // Case 2: cube belongs to a bar already on the workspace.
            Bar existingBar = hitCube.Bar;
            if (existingBar == null)
            {
                Debug.LogWarning($"Cube {hitCube.CubeId} has no Bar — this shouldn't happen on the workspace.");
                return;
            }

            if (grabbedFromTop && existingBar.Length > 1)
            {
                int splitIndex = existingBar.IndexOf(hitCube);
                List<Cube> splitOff = existingBar.RemoveFromIndex(splitIndex);
                existingBar.DestroyIfEmpty();

                Bar newBar = Bar.CreateForCube(splitOff[0]);
                for (int i = 1; i < splitOff.Count; i++)
                {
                    newBar.AddMember(splitOff[i]);
                }

                heldBar = newBar;

                if (containerDetector != null)
                    containerDetector.ReclusterAllBars();
            }
            else
            {
                heldBar = existingBar;
            }

            grabbedCube = hitCube;

            // Check: is this cube currently sitting inside any source's disc?
            // If so, treat that source as the owner for source-exit notification.
            CubeSource enclosingSource = FindEnclosingSource(grabbedCube.transform.position);
            if (enclosingSource != null && heldBar.Length == 1)
            {
                sourceOfHeldBar = enclosingSource;
            }

            Vector3 cubeOnPlane = new Vector3(grabbedCube.transform.position.x, 0f, grabbedCube.transform.position.z);
            Vector3 pointerOnPlane = ComputePointerOnPlane();
            grabOffset = cubeOnPlane - pointerOnPlane;
        }

        // ---------------- DRAG CONTINUE ----------------

        private void ContinueDrag()
        {
            // Container drag: slide all member bars rigidly with the pointer (flat, no lift).
            if (heldContainer != null)
            {
                if (!TryGetPointerRay(out Ray cRay)) return;
                if (!workspacePlane.Raycast(cRay, out float cEnter)) return;

                Vector3 cPointerOnPlane = cRay.GetPoint(cEnter);
                Vector3 pointerFlat = new Vector3(cPointerOnPlane.x, 0f, cPointerOnPlane.z);

                var members = heldContainer.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    Bar bar = members[i];
                    Vector3 targetFlat = pointerFlat + heldContainerMemberOffsets[i];
                    Vector3 delta = targetFlat - new Vector3(bar.transform.position.x, 0f, bar.transform.position.z);
                    bar.transform.position += new Vector3(delta.x, 0f, delta.z);
                }
                return;
            }

            if (!TryGetPointerRay(out Ray ray)) return;
            if (!workspacePlane.Raycast(ray, out float enter)) return;

            Vector3 hitPoint = ray.GetPoint(enter);

            Vector3 targetForGrabbed = new Vector3(
                hitPoint.x + grabOffset.x,
                dragHeight,
                hitPoint.z + grabOffset.z
            );

            int grabbedIndex = heldBar.IndexOf(grabbedCube);
            if (grabbedIndex < 0) return;

            for (int i = 0; i < heldBar.Length; i++)
            {
                int offsetFromGrabbed = i - grabbedIndex;
                Cube c = heldBar.Members[i];
                c.transform.position = targetForGrabbed + new Vector3(offsetFromGrabbed * 1f, 0f, 0f);
            }

            if (sourceOfHeldBar != null && heldBar.Length == 1)
            {
                Vector3 cubePos = heldBar.Members[0].transform.position;
                if (!sourceOfHeldBar.IsPositionInsideSource(cubePos))
                {
                    sourceOfHeldBar.NotifyCubeLeftSource(heldBar.Members[0]);
                    sourceOfHeldBar = null;
                }
            }
        }

        // ---------------- DRAG END ----------------

        private void EndDrag()
        {
            // Container drag release: clear held container, recluster (membership may have changed).
            if (heldContainer != null)
            {
                heldContainer = null;
                heldContainerMemberOffsets = null;

                if (containerDetector != null)
                    containerDetector.ReclusterAllBars();
                return;
            }

            Bar releasedBar = heldBar;
            heldBar = null;
            sourceOfHeldBar = null;
            grabbedCube = null;

            for (int i = 0; i < releasedBar.Length; i++)
            {
                Cube c = releasedBar.Members[i];
                Vector3 p = c.transform.position;
                c.transform.position = new Vector3(p.x, 0.5f, p.z);
            }

            Bar bondTarget = FindBondCandidate(releasedBar);
            if (bondTarget != null)
            {
                StartCoroutine(MergeBarsAnimated(bondTarget, releasedBar));
            }
            else
            {
                if (containerDetector != null)
                    containerDetector.ReclusterAllBars();
            }
        }

        // ---------------- BOND DETECTION ----------------

        private Bar FindBondCandidate(Bar releasedBar)
        {
            Cube leftmostOfReleased = releasedBar.GetLeftmostMember();
            if (leftmostOfReleased == null) return null;

            Vector3 releasedLeftFace = leftmostOfReleased.transform.position + new Vector3(-0.5f, 0f, 0f);

            Bar[] allBars = FindObjectsByType<Bar>(FindObjectsInactive.Exclude);
            Bar best = null;
            float bestDistance = float.MaxValue;

            foreach (Bar candidate in allBars)
            {
                if (candidate == releasedBar) continue;
                if (candidate.Length + releasedBar.Length > Bar.MaxLength) continue;

                Cube rightmostOfCandidate = candidate.GetRightmostMember();
                if (rightmostOfCandidate == null) continue;

                Vector3 candidateRightFace = rightmostOfCandidate.transform.position + new Vector3(0.5f, 0f, 0f);
                Vector3 delta = new Vector3(
                    releasedLeftFace.x - candidateRightFace.x,
                    0f,
                    releasedLeftFace.z - candidateRightFace.z
                );

                float distance = delta.magnitude;
                if (distance <= bondTolerance && distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        // ---------------- BAR MERGE ----------------

        private IEnumerator MergeBarsAnimated(Bar target, Bar incoming)
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
                {
                    incoming.Members[i].transform.position = Vector3.Lerp(startPositions[i], endPositions[i], eased);
                }
                yield return null;
            }

            for (int i = 0; i < incoming.Length; i++)
            {
                incoming.Members[i].transform.position = endPositions[i];
            }

            List<Cube> cubesToTransfer = new List<Cube>(incoming.Members);
            incoming.RemoveFromIndex(0);
            foreach (Cube c in cubesToTransfer)
            {
                target.AddMember(c);
            }

            if (incoming.Container != null)
            {
                incoming.Container.RemoveMember(incoming);
            }

            Destroy(incoming.gameObject);

            if (containerDetector != null)
                containerDetector.ReclusterAllBars();
        }

        // ---------------- CONTAINER GRAB (cascade case 3) ----------------

        /// <summary>
        /// If the pointer (projected onto the workspace) lands within any container's claimed
        /// area but not on a cube, grab the whole container. Claims are tested independently,
        /// first hit wins (the "any" gate); the parent container then moves whole (the "all"
        /// response) rigidly via per-member offsets recorded at grab.
        /// </summary>
        private void TryBeginContainerDrag()
        {
            if (!TryGetPointerRay(out Ray ray)) return;
            if (!workspacePlane.Raycast(ray, out float enter)) return;

            Vector3 pointerOnPlane = ray.GetPoint(enter);

            if (debugPointer)
            {
                Debug.DrawLine(ray.origin, pointerOnPlane, Color.red, 2f);       // camera -> ground hit
                Debug.DrawRay(pointerOnPlane, Vector3.up * 2f, Color.yellow, 2f); // stalk at landing spot
            }

            Vector2 point2D = new Vector2(pointerOnPlane.x, pointerOnPlane.z);

            Debug.Log($"CONTAINER TEST point=({point2D.x:F2},{point2D.y:F2})  containers={FindObjectsByType<Container>(FindObjectsInactive.Exclude).Length}");
            foreach (var cu in FindObjectsByType<Cube>(FindObjectsInactive.Exclude))
            {
                Rect r = cu.GetClaimedArea();
                Debug.Log($"   claim {cu.name}: x[{r.xMin:F2},{r.xMax:F2}] z[{r.yMin:F2},{r.yMax:F2}]  contains={r.Contains(point2D)}");
            }

            Container[] containers = FindObjectsByType<Container>(FindObjectsInactive.Exclude);
            foreach (Container container in containers)
            {
                foreach (Bar bar in container.Members)
                {
                    foreach (Cube cube in bar.Members)
                    {
                        if (cube.GetClaimedArea().Contains(point2D))
                        {
                            BeginContainerDrag(container, pointerOnPlane);
                            return;
                        }
                    }
                }
            }
        }

        private void BeginContainerDrag(Container container, Vector3 pointerOnPlane)
        {
            heldContainer = container;

            // Record each member bar's offset from the pointer, so the group moves rigidly.
            heldContainerMemberOffsets = new List<Vector3>();
            foreach (Bar bar in container.Members)
            {
                Vector3 barOnPlane = new Vector3(bar.transform.position.x, 0f, bar.transform.position.z);
                heldContainerMemberOffsets.Add(barOnPlane - new Vector3(pointerOnPlane.x, 0f, pointerOnPlane.z));
            }
        }

        // ---------------- HELPERS ----------------

        private CubeSource FindEnclosingSource(Vector3 worldPosition)
        {
            CubeSource[] allSources = FindObjectsByType<CubeSource>(FindObjectsInactive.Exclude);
            foreach (CubeSource s in allSources)
            {
                if (s.IsPositionInsideSource(worldPosition))
                    return s;
            }
            return null;
        }

        /// <summary>
        /// [DIAG2] One-shot readout to confirm which pixel space the pointer lives in.
        /// Maximize the Game view, click near the top/right edge, and read which of
        /// rendering/system width&height matches the mouse's max extent — that's the denominator.
        /// </summary>
        private void LogPointerDiag()
        {
            Vector2 m = Mouse.current.position.ReadValue();
            Debug.Log($"[DIAG2] mouse=({m.x:F0},{m.y:F0}) Screen=({Screen.width}x{Screen.height}) " +
                      $"camPixel=({workspaceCamera.pixelWidth}x{workspaceCamera.pixelHeight}) " +
                      $"rendering=({Display.main.renderingWidth}x{Display.main.renderingHeight}) " +
                      $"system=({Display.main.systemWidth}x{Display.main.systemHeight})");
        }
    }
}
