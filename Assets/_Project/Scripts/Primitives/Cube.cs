using UnityEngine;

namespace RuinApp.Primitives
{
    public class Cube : MonoBehaviour
    {
        public string CubeId { get; private set; }
        public bool GrabbedFromTop { get; set; }
        public Bar Bar { get; private set; }

        [SerializeField] private GameObject shadowBlobPrefab;

        private void Awake()
        {
            CubeId = System.Guid.NewGuid().ToString();
            SpawnShadowBlob();
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
    }
}