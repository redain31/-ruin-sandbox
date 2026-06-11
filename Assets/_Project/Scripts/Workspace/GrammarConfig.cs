using UnityEngine;

namespace RuinApp.Workspace
{
    /// <summary>
    /// Single source of truth for the cube grammar's shared spatial constants.
    /// Both the bonding/detection systems and the container shadow systems read from here,
    /// so spatial rules stay consistent by construction (no duplicated magic numbers).
    /// Create one asset via: Assets > Create > Ruin > Grammar Config.
    /// </summary>
    [CreateAssetMenu(fileName = "GrammarConfig", menuName = "Ruin/Grammar Config", order = 0)]
    public class GrammarConfig : ScriptableObject
    {
        [Header("Cube")]
        [Tooltip("World-unit size of one cube. The grammar's base length unit.")]
        public float cubeSize = 1.0f;

        [Header("Bonding")]
        [Tooltip("How close a cube's left face must be to another's right face to bond into a bar.")]
        public float bondTolerance = 0.3f;

        [Header("Container")]
        [Tooltip("How far beyond its footprint each cube 'claims' area. This single value defines BOTH " +
                 "container membership (cubes whose claims overlap are in the same container) AND " +
                 "the shadow region extension. Membership and shadow shape stay unified by construction.")]
        public float containerClaimMargin = 0.5f;
        
        [Header("Container Shadow")]
        [Tooltip("Material applied to the container shadow mesh.")]
        public Material shadowMaterial;
        /// <summary>
        /// The effective distance between two cube edges below which they share a container.
        /// Derived from the claim margin: two claims overlap when edge-to-edge distance < 2 * margin.
        /// </summary>
        public float ContainerMembershipDistance => containerClaimMargin * 2f;
    }
}