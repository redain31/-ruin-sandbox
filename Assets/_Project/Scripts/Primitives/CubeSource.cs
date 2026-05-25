using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// Inexhaustible source from which cubes are pulled into the workspace.
    /// The source is a circular zone (radius defined by sourceRadius);
    /// a cube is "in" the source while inside this disc, and is considered
    /// "taken" once it leaves the disc — at which point a replacement spawns.
    /// </summary>
    public class CubeSource : MonoBehaviour
    {
        [SerializeField] private GameObject cubePrefab;
        [SerializeField] private float sourceRadius = 1.0f;

        private Cube currentAvailableCube;

        private void Start()
        {
            SpawnAvailableCube();
        }

        public void SpawnAvailableCube()
        {
            GameObject instance = Instantiate(cubePrefab, transform.position, Quaternion.identity, transform);
            currentAvailableCube = instance.GetComponent<Cube>();
        }

        /// <summary>
        /// Called when a cube has been dragged across the source's boundary.
        /// The cube remains under whatever parent it now has (typically a Bar);
        /// the source just releases its claim and spawns a replacement.
        /// </summary>
        public void NotifyCubeLeftSource(Cube cube)
        {
            if (cube == currentAvailableCube)
            {
                currentAvailableCube = null;
                SpawnAvailableCube();
            }
        }

        /// <summary>
        /// True if the given world position lies within the source's circular zone
        /// (measured on the horizontal plane, ignoring vertical drag-lift).
        /// </summary>
        public bool IsPositionInsideSource(Vector3 worldPosition)
        {
            Vector3 horizontalDelta = new Vector3(
                worldPosition.x - transform.position.x,
                0f,
                worldPosition.z - transform.position.z
            );
            return horizontalDelta.magnitude <= sourceRadius;
        }

        /// <summary>
        /// Returns the source's center position.
        /// </summary>
        public Vector3 GetSourcePosition() => transform.position;
    }
}