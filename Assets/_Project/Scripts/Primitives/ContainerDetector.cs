using System.Collections.Generic;
using UnityEngine;
using RuinApp.Primitives;

namespace RuinApp.Workspace
{
    /// <summary>
    /// Workspace-level service that reads all bars on the workspace and
    /// partitions them into containers based on spatial proximity.
    /// Containers form around clusters of bars within containerMembershipDistance.
    /// 
    /// Called on bar lifecycle events (creation, drag-end, merge, split) by the input controller.
    /// </summary>
    public class ContainerDetector : MonoBehaviour
    {
        [Header("Clustering")]
        [Tooltip("Multiplier on the bond tolerance. Two bars within (bondTolerance × this multiplier) of each other belong to the same container.")]
        [SerializeField] private float membershipDistanceMultiplier = 2.0f;
        [SerializeField] private float bondTolerance = 0.3f;

        private float ContainerMembershipDistance => bondTolerance * membershipDistanceMultiplier;

        /// <summary>
        /// Re-evaluate all containers on the workspace.
        /// Existing containers are dissolved; new containers are formed based on current bar positions.
        /// </summary>
        public void ReclusterAllBars()
        {
            Bar[] allBars = FindObjectsByType<Bar>(FindObjectsInactive.Exclude);
            if (allBars.Length == 0)
            {
                CleanupEmptyContainers();
                return;
            }

            // Detach all bars from their current containers — we'll re-cluster from scratch.
            // This is simpler than computing deltas, and the operation is cheap at our scale.
            DetachAllBarsFromContainers(allBars);

            // Build clusters by greedy union: for each bar, check whether it belongs to any
            // existing cluster (by proximity to any of that cluster's members). If yes, join it.
            // If it belongs to multiple, merge those clusters. If none, start a new cluster.
            List<List<Bar>> clusters = ClusterBars(allBars);

            // Form a Container GameObject for each cluster.
            foreach (List<Bar> cluster in clusters)
            {
                Container container = Container.CreateForBar(cluster[0]);
                for (int i = 1; i < cluster.Count; i++)
                {
                    container.AddMember(cluster[i]);
                }
            }

            CleanupEmptyContainers();
        }

        // ---------------- Cluster computation ----------------

        private List<List<Bar>> ClusterBars(Bar[] bars)
        {
            List<List<Bar>> clusters = new List<List<Bar>>();

            foreach (Bar bar in bars)
            {
                List<List<Bar>> clustersJoined = new List<List<Bar>>();

                // Find which existing clusters this bar is close to.
                foreach (List<Bar> cluster in clusters)
                {
                    foreach (Bar member in cluster)
                    {
                        if (BarsAreClose(bar, member))
                        {
                            clustersJoined.Add(cluster);
                            break;
                        }
                    }
                }

                if (clustersJoined.Count == 0)
                {
                    // New cluster of one.
                    clusters.Add(new List<Bar> { bar });
                }
                else if (clustersJoined.Count == 1)
                {
                    // Join the one cluster it's near.
                    clustersJoined[0].Add(bar);
                }
                else
                {
                    // Belongs to multiple clusters → merge them all into the first.
                    List<Bar> primary = clustersJoined[0];
                    primary.Add(bar);
                    for (int i = 1; i < clustersJoined.Count; i++)
                    {
                        primary.AddRange(clustersJoined[i]);
                        clusters.Remove(clustersJoined[i]);
                    }
                }
            }

            return clusters;
        }

        /// <summary>
        /// Returns true if the rectangular footprints of two bars are within
        /// ContainerMembershipDistance on the workspace plane.
        /// </summary>
        private bool BarsAreClose(Bar a, Bar b)
        {

            if (a.Length == 0 || b.Length == 0) return false;
            
            // Compute each bar's footprint rectangle on the XZ plane.
            Bounds aBounds = ComputeBarFootprint(a);
            Bounds bBounds = ComputeBarFootprint(b);

            float distance = RectangleDistance(aBounds, bBounds);
            return distance <= ContainerMembershipDistance;
        }

        private Bounds ComputeBarFootprint(Bar bar)
        {
            Cube leftmost = bar.GetLeftmostMember();
            Cube rightmost = bar.GetRightmostMember();

            if (leftmost == null || rightmost == null)
                return new Bounds(bar.transform.position, Vector3.zero);

            float minX = leftmost.transform.position.x - 0.5f;
            float maxX = rightmost.transform.position.x + 0.5f;
            float z = leftmost.transform.position.z;
            float minZ = z - 0.5f;
            float maxZ = z + 0.5f;

            Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            Vector3 size = new Vector3(maxX - minX, 0f, maxZ - minZ);
            return new Bounds(center, size);
        }

        /// <summary>
        /// 2D rectangle-to-rectangle distance on the XZ plane (Y is ignored).
        /// Returns 0 if rectangles overlap.
        /// </summary>
        private float RectangleDistance(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---------------- Bookkeeping ----------------

        private void DetachAllBarsFromContainers(Bar[] bars)
        {
            // Clear every container's members list, not just the ones reachable from bar.Container.
            // This is necessary because some bars may have a null Container reference while their
            // previous container still lists them (state desync during drag, split, etc.).
            Container[] allContainers = FindObjectsByType<Container>(FindObjectsInactive.Exclude);
            foreach (Container c in allContainers)
            {
                // Make a copy because RemoveMember modifies the list we're iterating
                List<Bar> currentMembers = new List<Bar>(c.Members);
                foreach (Bar bar in currentMembers)
                {
                    c.RemoveMember(bar);
                }
            }

            // Also clear the bar references defensively
            foreach (Bar bar in bars)
            {
                bar.SetContainer(null);
            }
        }
        private void CleanupEmptyContainers()
        {
            Container[] allContainers = FindObjectsByType<Container>(FindObjectsInactive.Exclude);
            foreach (Container c in allContainers)
            {
                c.DestroyIfEmpty();
            }
        }
    }
}