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
    public class ContainerDetector_Old : MonoBehaviour
    {
        [Header("Clustering")]
        [Tooltip("Multiplier on the bond tolerance. Two bars within (bondTolerance × this multiplier) of each other belong to the same container.")]
        [SerializeField] private GrammarConfig config;
        /// <summary>
        /// Re-evaluate all containers on the workspace.
        /// Existing containers are dissolved; new containers are formed based on current bar positions.
        /// </summary>
        public void ReclusterAllBars()
        {
            Bar[] found = FindObjectsByType<Bar>(FindObjectsInactive.Exclude);

            // Exclude empty/destroyed bars — they may be mid-destruction (Destroy is deferred),
            // and clustering them creates orphan empty containers.
            List<Bar> allBarsList = new List<Bar>();
            foreach (Bar b in found)
            {
                if (b != null && b.Length > 0)
                    allBarsList.Add(b);
            }
            Bar[] allBars = allBarsList.ToArray();

            if (allBars.Length == 0)
            {
                CleanupEmptyContainers();
                return;
            }

            DetachAllBarsFromContainers(allBars);
            List<List<Bar>> clusters = ClusterBars(allBars);

            foreach (List<Bar> cluster in clusters)
            {
                Container container = Container.CreateForBar(cluster[0], config);
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
        /// <summary>
        /// Two bars share a container if ANY cube's claimed area in one bar overlaps ANY cube's
        /// claimed area in the other. The claim comes from Cube.GetClaimedArea (driven by claimMultiplier),
        /// so the SAME value that sizes the shadow visual also defines grouping — unified by construction.
        /// </summary>
        private bool BarsAreClose(Bar a, Bar b)
        {
            if (a.Length == 0 || b.Length == 0) return false;

            foreach (Cube ca in a.Members)
            {
                Rect ra = ca.GetClaimedArea();
                foreach (Cube cb in b.Members)
                {
                    Rect rb = cb.GetClaimedArea();
                    if (ra.Overlaps(rb))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 2D rectangle-to-rectangle distance on the XZ plane (Y is ignored).
        /// Returns 0 if rectangles overlap.
        /// </summary>

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
            //Debug.Log($"[Cleanup] inspecting {allContainers.Length} containers");
   
            foreach (Container c in allContainers)
            {
                //Debug.Log($"[Cleanup] '{c.name}' Length={c.Length} childCount={c.transform.childCount}");
                c.DestroyIfEmpty();
            }
        }
    }
}