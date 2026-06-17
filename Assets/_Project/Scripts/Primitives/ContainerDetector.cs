using System.Collections.Generic;
using UnityEngine;
using RuinApp.Primitives;

namespace RuinApp.Workspace
{
    /// <summary>
    /// Local, event-driven container grouping. Replaces the old global ReclusterAllBars sweep:
    /// when bars move, only their NEIGHBOURHOOD is re-resolved; clusters elsewhere are left
    /// exactly as they are, and containers that empty out self-destruct (die-at-0).
    ///
    /// Bonding's regroup and claim-grab's regroup both call ResolveNeighborhood, so grouping has
    /// a single decision path. The grouping rule is unchanged: bars whose claimed areas overlap
    /// share a container.
    /// </summary>
    public class ContainerDetector : MonoBehaviour
    {
        [SerializeField] private GrammarConfig config;

        /// <summary>
        /// Re-resolve container membership around the bars that just moved. Gathers the affected
        /// set (each moved bar, its old cluster-mates, and the clusters of any bar it now overlaps),
        /// dissolves only those containers, re-clusters only that set, and lets emptied containers
        /// self-destruct (die-at-0). One level suffices: existing clusters are already internally
        /// valid, so only the moved bars introduce new adjacencies.
        ///
        /// 'alsoReconsider' force-includes one more container — the one a cube was just split out of —
        /// even if it is now empty, so its remainder re-clusters or it self-destructs. This is what
        /// lets resolve own ALL container lifecycle; the split path performs no teardown of its own.
        /// </summary>
        public void ResolveNeighborhood(List<Bar> movedBars, Container alsoReconsider = null)
        {
            HashSet<Bar> affected = new HashSet<Bar>();
            HashSet<Container> touched = new HashSet<Container>();

            void Include(Bar b)
            {
                if (b == null || b.Length == 0 || !affected.Add(b)) return;
                if (b.Container != null && touched.Add(b.Container))
                    foreach (Bar mate in b.Container.Members) Include(mate); // pull whole old cluster
            }

            Bar[] all = AllLiveBars();

            foreach (Bar moved in movedBars)
            {
                if (moved == null || moved.Length == 0) continue;
                Include(moved);
                foreach (Bar other in all)               // moved bar's new spatial neighbours...
                    if (other != moved && BarsAreClose(moved, other))
                        Include(other);                  // ...and their whole clusters
            }

            // The origin container of a split: reconsider it even if now empty.
            if (alsoReconsider != null && touched.Add(alsoReconsider))
                foreach (Bar mate in alsoReconsider.Members) Include(mate);

            if (affected.Count == 0 && touched.Count == 0) return;

            // Dissolve every touched container; all its bars are already in 'affected'.
            foreach (Container c in touched)
                foreach (Bar bar in new List<Bar>(c.Members))
                    c.RemoveMember(bar);

            // Re-cluster ONLY the affected set, rebuild containers.
            foreach (List<Bar> cluster in ClusterBars(new List<Bar>(affected).ToArray()))
            {
                Container container = Container.CreateForBar(cluster[0], config);
                for (int i = 1; i < cluster.Count; i++) container.AddMember(cluster[i]);
            }

            // Emptied dissolved containers (including a now-empty origin) self-destruct.
            foreach (Container c in touched)
                if (c != null) c.DestroyIfEmpty();
        }

        private Bar[] AllLiveBars()
        {
            Bar[] found = FindObjectsByType<Bar>(FindObjectsInactive.Exclude);
            List<Bar> live = new List<Bar>();
            foreach (Bar b in found) if (b != null && b.Length > 0) live.Add(b);
            return live.ToArray();
        }

        // ---- clustering (unchanged: bars whose claims overlap belong together) ----

        private List<List<Bar>> ClusterBars(Bar[] bars)
        {
            List<List<Bar>> clusters = new List<List<Bar>>();

            foreach (Bar bar in bars)
            {
                List<List<Bar>> joined = new List<List<Bar>>();
                foreach (List<Bar> cluster in clusters)
                    foreach (Bar member in cluster)
                        if (BarsAreClose(bar, member)) { joined.Add(cluster); break; }

                if (joined.Count == 0)
                {
                    clusters.Add(new List<Bar> { bar });
                }
                else if (joined.Count == 1)
                {
                    joined[0].Add(bar);
                }
                else
                {
                    List<Bar> primary = joined[0];
                    primary.Add(bar);
                    for (int i = 1; i < joined.Count; i++)
                    {
                        primary.AddRange(joined[i]);
                        clusters.Remove(joined[i]);
                    }
                }
            }
            return clusters;
        }

        /// <summary>Two bars share a container if ANY cube's claimed area in one overlaps ANY in the other.</summary>
        private bool BarsAreClose(Bar a, Bar b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            foreach (Cube ca in a.Members)
            {
                Rect ra = ca.GetClaimedArea();
                foreach (Cube cb in b.Members)
                    if (ra.Overlaps(cb.GetClaimedArea())) return true;
            }
            return false;
        }
    }
}