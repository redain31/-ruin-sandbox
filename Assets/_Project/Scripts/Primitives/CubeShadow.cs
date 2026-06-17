using System.Collections.Generic;
using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A per-cube opaque shadow quad, parented to its cube so it follows automatically.
    /// Sizes itself to the cube's claimed area. Resizes only on demand (spawn, or when the
    /// claim multiplier changes via GrammarConfig) — no per-frame cost.
    /// All live instances are tracked so a config change can propagate to every shadow at once.
    /// </summary>
    public class CubeShadow : MonoBehaviour
    {
        // All currently-live shadows, so a config change can resize them all.
        private static readonly List<CubeShadow> liveShadows = new List<CubeShadow>();

        private Cube cube;

        private void OnEnable()  { if (!liveShadows.Contains(this)) liveShadows.Add(this); }
        private void OnDisable() { liveShadows.Remove(this); }

        public void SetTarget(Transform cubeTransform)
        {
            cube = cubeTransform.GetComponent<Cube>();
            ApplySize();
        }

        public void ApplySize()
        {
            if (cube == null) return;
            Rect claim = cube.GetClaimedArea();
            transform.localScale = new Vector3(claim.width, claim.height, 1f);
        }

        /// <summary>
        /// Resizes every live shadow. Called when the claim multiplier changes.
        /// </summary>
        public static void RefreshAll()
        {
            for (int i = liveShadows.Count - 1; i >= 0; i--)
            {
                if (liveShadows[i] != null)
                    liveShadows[i].ApplySize();
                else
                    liveShadows.RemoveAt(i);
            }
        }
    }
}