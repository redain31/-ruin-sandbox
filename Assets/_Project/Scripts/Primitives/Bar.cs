using System.Collections.Generic;
using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A bar is an ordered, left-to-right list of bonded cubes.
    /// A free single cube is a bar of length one.
    /// Bars are capped at five members.
    /// Every bar on the workspace belongs to exactly one Container.
    /// </summary>
    public class Bar : MonoBehaviour
    {
        public const int MaxLength = 5;

        private List<Cube> members = new List<Cube>();

        public int Length => members.Count;
        public bool IsFull => members.Count >= MaxLength;
        public IReadOnlyList<Cube> Members => members;

        /// <summary>
        /// The Container this bar currently belongs to.
        /// Null only transiently — e.g., during re-parenting operations.
        /// </summary>
        public Container Container { get; private set; }

        public static Bar CreateForCube(Cube cube)
        {
            GameObject barGO = new GameObject($"Bar_{cube.CubeId.Substring(0, 8)}");
            barGO.transform.position = cube.transform.position;

            Bar bar = barGO.AddComponent<Bar>();
            bar.AddMember(cube);
            return bar;
        }

        public void AddMember(Cube cube)
        {
            members.Add(cube);
            cube.transform.SetParent(transform);
            cube.SetBar(this);
        }

        public List<Cube> RemoveFromIndex(int index)
        {
            List<Cube> removed = new List<Cube>();
            for (int i = members.Count - 1; i >= index; i--)
            {
                Cube c = members[i];
                c.transform.SetParent(null);
                c.SetBar(null);
                members.RemoveAt(i);
                removed.Insert(0, c);
            }
            return removed;
        }

        public int IndexOf(Cube cube) => members.IndexOf(cube);

        public Vector3 GetNextBondPosition()
        {
            if (members.Count == 0) return transform.position;
            Cube rightmost = members[members.Count - 1];
            return rightmost.transform.position + new Vector3(1f, 0f, 0f);
        }

        public Cube GetRightmostMember()
        {
            return members.Count > 0 ? members[members.Count - 1] : null;
        }

        public Cube GetLeftmostMember()
        {
            return members.Count > 0 ? members[0] : null;
        }

        /// <summary>
        /// Called by Container when this bar is added or removed.
        /// Not for use by other systems — go through Container.AddMember / RemoveMember instead.
        /// </summary>
        public void SetContainer(Container container)
        {
            Container = container;
        }

        public void DestroyIfEmpty()
        {
            if (members.Count == 0)
                Destroy(gameObject);
        }
    }
}