using System.Collections.Generic;
using UnityEngine;
using RuinApp.Workspace;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A container is a logical group of one or more Bars arranged in proximity.
    /// Every Bar on the workspace belongs to exactly one Container.
    /// A container with 2+ members renders a shadow mesh covering the union of member claims.
    /// </summary>
    public class Container : MonoBehaviour
    {
        private List<Bar> members = new List<Bar>();

        public int Length => members.Count;
        public IReadOnlyList<Bar> Members => members;
        public bool ShouldVisualize => members.Count >= 2;

        private ContainerShadowMesh shadowMesh;
        private GrammarConfig config;

        public static Container CreateForBar(Bar bar, GrammarConfig config)
        {
            GameObject containerGO = new GameObject($"Container_{System.Guid.NewGuid().ToString().Substring(0, 8)}");
            containerGO.transform.position = bar.transform.position;

            Container container = containerGO.AddComponent<Container>();
            container.config = config;
            container.AddMember(bar);
            return container;
        }

        public void AddMember(Bar bar)
        {
            if (members.Contains(bar)) return;
            members.Add(bar);
            bar.transform.SetParent(transform);
            bar.SetContainer(this);
        }

        public void RemoveMember(Bar bar)
        {
            if (!members.Contains(bar)) return;
            members.Remove(bar);
            if (bar != null)
            {
                bar.transform.SetParent(null);
                bar.SetContainer(null);
            }
        }

        public Vector3 GetMembersCentroid()
        {
            if (members.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            foreach (Bar bar in members) sum += bar.transform.position;
            return sum / members.Count;
        }

        public int GetTotalCubeCount()
        {
            int count = 0;
            foreach (Bar bar in members) count += bar.Length;
            return count;
        }

        /// <summary>
        /// Rebuilds the shadow mesh from member cube claims.
        /// Shows the shadow only when the container has 2+ members.
        /// </summary>
        public void RefreshShadow()
        {
            if (shadowMesh == null)
            {
                GameObject shadowGO = new GameObject("ShadowMesh");
                shadowGO.transform.SetParent(transform);
                shadowGO.transform.localPosition = Vector3.zero;
                shadowMesh = shadowGO.AddComponent<ContainerShadowMesh>();

                MeshRenderer mr = shadowGO.GetComponent<MeshRenderer>();
                if (config != null && config.shadowMaterial != null)
                    mr.sharedMaterial = config.shadowMaterial;
            }

            if (!ShouldVisualize)
            {
                shadowMesh.ClearMesh();
                return;
            }

            List<Rect> claims = new List<Rect>();
            foreach (Bar bar in members)
            {
                foreach (Cube cube in bar.Members)
                {
                    claims.Add(cube.GetClaimedArea());
                }
            }
            shadowMesh.Rebuild(claims);
        }

        public void DestroyIfEmpty()
        {
            if (members.Count == 0)
                Destroy(gameObject);
        }
    }
}