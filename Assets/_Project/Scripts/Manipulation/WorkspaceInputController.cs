using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RuinApp.Primitives;

namespace RuinApp.Manipulation
{
    /// <summary>
    /// Centralized input handler for the workspace.
    /// Part 5B: grabs operate on bars; grab-from-top splits; bond-on-release merges bars
    /// while enforcing the five-cube cap. The grabbed cube serves as the drag anchor.
    /// </summary>
    public class WorkspaceInputController : MonoBehaviour
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

        private Bar heldBar;
        private Cube grabbedCube;
        private Vector3 grabOffset;
        private CubeSource sourceOfHeldBar;

        private Plane workspacePlane = new Plane(Vector3.up, Vector3.zero);

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
            else if (Mouse.current.leftButton.isPressed && heldBar != null)
                ContinueDrag();
            else if (Mouse.current.leftButton.wasReleasedThisFrame && heldBar != null)
                EndDrag();
        }

        // ---------------- DRAG START ----------------

        private void TryBeginDrag()
        {
            Vector2 pointerPos = Mouse.current.position.ReadValue();
            Ray ray = workspaceCamera.ScreenPointToRay(pointerPos);

            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, draggableLayerMask)) return;

            Cube hitCube = hit.collider.GetComponent<Cube>();
            if (hitCube == null) return;

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
                Vector3 pointerOnPlaneSrc = ComputePointerOnPlane(pointerPos);
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
            Vector3 pointerOnPlane = ComputePointerOnPlane(pointerPos);
            grabOffset = cubeOnPlane - pointerOnPlane;
        }

        // ---------------- DRAG CONTINUE ----------------

        private void ContinueDrag()
        {
            Vector2 pointerPos = Mouse.current.position.ReadValue();
            Ray ray = workspaceCamera.ScreenPointToRay(pointerPos);

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

        private Vector3 ComputePointerOnPlane(Vector2 pointerScreenPos)
        {
            Ray planeRay = workspaceCamera.ScreenPointToRay(pointerScreenPos);
            if (workspacePlane.Raycast(planeRay, out float enter))
            {
                Vector3 p = planeRay.GetPoint(enter);
                return new Vector3(p.x, 0f, p.z);
            }
            return Vector3.zero;
        }
    }
}