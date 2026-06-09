using System.Collections.Generic;
using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A container is a logical group of one or more Bars that have been arranged in proximity on the workspace.
    /// Every Bar on the workspace belongs to exactly one Container.
    /// A Container with a single Bar exists logically but renders no visible boundary (per Q2 in design).
    /// A Container with two or more Bars renders a soft merged-shadow visualization (Part 3).
    /// </summary>
    public class Container : MonoBehaviour
    {
        // Members are stored unordered — unlike Bar's ordered cube list,
        // a container is a set of bars; their spatial arrangement is their position in the world.
        private List<Bar> members = new List<Bar>();

        public int Length => members.Count;
        public IReadOnlyList<Bar> Members => members;

        /// <summary>
        /// True when this container has multiple members and should render its visual boundary.
        /// </summary>
        public bool ShouldVisualize => members.Count >= 2;

        /// <summary>
        /// Creates a new Container GameObject containing a single starting Bar.
        /// The Bar is re-parented under the new Container.
        /// </summary>
        public static Container CreateForBar(Bar bar)
        {
            GameObject containerGO = new GameObject($"Container_{System.Guid.NewGuid().ToString().Substring(0, 8)}");
            containerGO.transform.position = bar.transform.position;

            Container container = containerGO.AddComponent<Container>();
            container.AddMember(bar);
            return container;
        }

        /// <summary>
        /// Adds a Bar to this container. The Bar is re-parented under this container's transform.
        /// </summary>
        public void AddMember(Bar bar)
        {
            if (members.Contains(bar)) return;
            members.Add(bar);
            bar.transform.SetParent(transform);
            bar.SetContainer(this);
        }

        /// <summary>
        /// Removes a Bar from this container. The Bar is un-parented (re-parented to null / scene root).
        /// Does not destroy the bar — the bar continues to exist independently.
        /// </summary>
        public void RemoveMember(Bar bar)
        {
            if (!members.Contains(bar)) return;
            members.Remove(bar);
            if (bar != null)  // Unity's null check works for destroyed objects
            {
                bar.transform.SetParent(null);
                bar.SetContainer(null);
            }
        }

        /// <summary>
        /// Returns the geometric center of all member bars' positions.
        /// Used for re-positioning the container's transform after membership changes.
        /// </summary>
        public Vector3 GetMembersCentroid()
        {
            if (members.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            foreach (Bar bar in members)
            {
                sum += bar.transform.position;
            }
            return sum / members.Count;
        }

        /// <summary>
        /// Returns the total number of cubes across all member bars.
        /// This is the container's "quantity reading" — what it represents.
        /// </summary>
        public int GetTotalCubeCount()
        {
            int count = 0;
            foreach (Bar bar in members)
            {
                count += bar.Length;
            }
            return count;
        }

        /// <summary>
        /// Destroys this container's GameObject if it has no members left.
        /// Called after removal operations to clean up empty containers.
        /// </summary>
        public void DestroyIfEmpty()
        {
            if (members.Count == 0)
                Destroy(gameObject);
        }
    }
}