using UnityEngine;

namespace RuinApp.Primitives
{
    public class Cube : MonoBehaviour
    {
        public string CubeId { get; private set; }
        public bool GrabbedFromTop { get; set; }
        public Bar Bar { get; private set; }

        [SerializeField] private GameObject shadowBlobPrefab;
        [SerializeField] private RuinApp.Workspace.GrammarConfig config;
        private void Awake()
        {
            CubeId = System.Guid.NewGuid().ToString();
            // SpawnShadowBlob();
        }

        private void SpawnShadowBlob()
        {
            if (shadowBlobPrefab == null) return;

            GameObject blob = Instantiate(shadowBlobPrefab);
            ShadowFollower follower = blob.GetComponent<ShadowFollower>();
            if (follower != null)
            {
                follower.SetTarget(transform);
            }
        }

        public void SetBar(Bar bar)
        {
            Bar = bar;
        }

        /// <summary>
        /// Returns the area this cube claims on the workspace plane (XZ), expressed as a Rect
        /// where Rect.x/y map to world X/Z. The claim is the cube's footprint expanded outward
        /// by the container claim margin on all sides. Cubes whose claims overlap share a container,
        /// and the union of claims defines the container's shadow region.
        /// </summary>
        public Rect GetClaimedArea()
        {
        float size = config != null ? config.cubeSize : 1.0f;
        float margin = config != null ? config.containerClaimMargin : 0.5f;

        float half = size * 0.5f + margin;
        Vector3 pos = transform.position;

        // Rect is defined by (x, y, width, height); we map x→worldX, y→worldZ.
        return new Rect(
            pos.x - half,
            pos.z - half,
            half * 2f,
            half * 2f
            );
        }
    }
}