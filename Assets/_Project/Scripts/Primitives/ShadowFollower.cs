using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// Wires a shadow blob quad to follow a target's X/Z position while keeping a fixed ground Y.
    /// Not parented — the blob is an independent object whose horizontal position tracks the target.
    /// This keeps the blob flat on the ground even when the target cube lifts during a drag.
    /// </summary>
    public class ShadowFollower : MonoBehaviour
    {
        [SerializeField] private float groundY = 0.01f;

        private Transform target;

        /// <summary>
        /// Assigns the transform this blob should follow (typically a cube).
        /// </summary>
        public void SetTarget(Transform targetTransform)
        {
            target = targetTransform;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                // Target gone (e.g., cube destroyed) — destroy this blob too.
                Destroy(gameObject);
                return;
            }

            Vector3 targetPos = target.position;
            transform.position = new Vector3(targetPos.x, groundY, targetPos.z);
        }
    }
}