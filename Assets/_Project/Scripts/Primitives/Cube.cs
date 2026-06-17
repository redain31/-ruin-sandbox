using UnityEngine;

namespace RuinApp.Primitives
{
    public class Cube : MonoBehaviour
    {
        public string CubeId { get; private set; }
        public bool GrabbedFromTop { get; set; }
        public Bar Bar { get; private set; }

        [SerializeField] private GameObject cubeShadowPrefab;
        [SerializeField] private RuinApp.Workspace.GrammarConfig config;
        private void Awake()
        {
            CubeId = System.Guid.NewGuid().ToString();
            SpawnShadow();
        }

        private void SpawnShadow()
        {
            if (cubeShadowPrefab == null) return;

            GameObject shadow = Instantiate(cubeShadowPrefab, transform);  // parent = this cube
            shadow.transform.localPosition = new Vector3(0f, -0.48f, 0f);  // at the cube's base, near ground
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on XZ

            CubeShadow cs = shadow.GetComponent<CubeShadow>();
            //Debug.Log($"[SpawnShadow] shadow created, cs={(cs != null ? "OK" : "NULL")}");
            if (cs != null)
                cs.SetTarget(transform);  // still set target for sizing; following now via parenting
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
        /// <summary>
        /// Returns the area this cube claims on the workspace plane (XZ), as a Rect
        /// (Rect.x→worldX, Rect.y→worldZ). The claim is the cube footprint scaled by the
        /// config's claimMultiplier. This same claim drives both the shadow visual size and
        /// container membership (cubes whose claims overlap share a container).
        /// </summary>
        public Rect GetClaimedArea()
        {
            float size = config != null ? config.cubeSize : 1.0f;
            float mult = config != null ? config.claimMultiplier : 2.0f;
            //Debug.Log($"[Cube] config={(config != null ? "OK" : "NULL")} multiplier={mult}");

            float half = (size * mult) * 0.5f;
            Vector3 pos = transform.position;

            return new Rect(
                pos.x - half,
                pos.z - half,
                half * 2f,
                half * 2f
            );
        }
    }
}