using System.Collections.Generic;
using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A bar is an ordered, left-to-right list of bonded cubes.
    /// A free single cube is a bar of length one.
    /// Bars are capped at five members (medium-defining bound from the brief).
    /// The bar's GameObject parents its member cubes so they move as a unit.
    /// </summary>
    public class Bar : MonoBehaviour
    {
        public const int MaxLength = 5;

        // Members are stored in left-to-right order.
        // Index 0 is the leftmost cube; index Count-1 is the rightmost.
        private List<Cube> members = new List<Cube>();

        public int Length => members.Count;
        public bool IsFull => members.Count >= MaxLength;

        public IReadOnlyList<Cube> Members => members;

        /// <summary>
        /// Creates a new Bar GameObject containing a single starting cube.
        /// The cube is re-parented under the new bar.
        /// </summary>
        public static Bar CreateForCube(Cube cube)
        {
            GameObject barGO = new GameObject($"Bar_{cube.CubeId.Substring(0, 8)}");
            barGO.transform.position = cube.transform.position;

            Bar bar = barGO.AddComponent<Bar>();
            bar.AddMember(cube);
            return bar;
        }

        /// <summary>
        /// Adds a cube to the right end of this bar.
        /// The cube is re-parented under this bar and its position aligned.
        /// Caller is responsible for checking IsFull before calling.
        /// </summary>
        public void AddMember(Cube cube)
        {
            members.Add(cube);
            cube.transform.SetParent(transform);
            cube.SetBar(this);
        }

        /// <summary>
        /// Removes the given cube and all cubes to its right from this bar.
        /// Returns them as a list (caller decides what to do with them).
        /// The removed cubes are un-parented from this bar.
        /// </summary>
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

        /// <summary>
        /// Returns the index of the given cube within this bar, or -1 if not a member.
        /// </summary>
        public int IndexOf(Cube cube) => members.IndexOf(cube);

        /// <summary>
        /// Returns the world position where the next bonded cube would sit
        /// (one unit to the right of the rightmost member).
        /// </summary>
        public Vector3 GetNextBondPosition()
        {
            if (members.Count == 0) return transform.position;
            Cube rightmost = members[members.Count - 1];
            return rightmost.transform.position + new Vector3(1f, 0f, 0f);
        }

        /// <summary>
        /// Returns the rightmost cube in the bar (for bond-candidate matching).
        /// </summary>
        public Cube GetRightmostMember()
        {
            return members.Count > 0 ? members[members.Count - 1] : null;
        }

        /// <summary>
        /// Returns the leftmost cube in the bar (for grouping bars under their leftmost-cube position).
        /// </summary>
        public Cube GetLeftmostMember()
        {
            return members.Count > 0 ? members[0] : null;
        }

        /// <summary>
        /// If the bar is empty (after removal), destroy its GameObject.
        /// </summary>
        public void DestroyIfEmpty()
        {
            if (members.Count == 0)
                Destroy(gameObject);
        }
    }
}