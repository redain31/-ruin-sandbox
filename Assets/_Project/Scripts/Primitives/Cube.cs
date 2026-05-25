using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// A single 1×1 cube primitive in the iconic sandbox layer.
    /// Cubes are countable units that bond into bars via right-side magnetic contact.
    /// Every cube on the workspace belongs to exactly one Bar.
    /// </summary>
    public class Cube : MonoBehaviour
    {
        public string CubeId { get; private set; }

        /// <summary>
        /// Set by the input controller at grab time.
        /// True if the grab originated from the top face (intent: split / individual cube).
        /// False if from a side face (intent: move the whole bar).
        /// </summary>
        public bool GrabbedFromTop { get; set; }

        /// <summary>
        /// The Bar this cube currently belongs to.
        /// Null only transiently — e.g., while a cube is at the source before its bar is created,
        /// or during a re-parenting operation.
        /// </summary>
        public Bar Bar { get; private set; }

        private void Awake()
        {
            CubeId = System.Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Called by Bar when a cube is added or removed.
        /// Not for use by other systems — go through Bar.AddMember / RemoveFromIndex instead.
        /// </summary>
        public void SetBar(Bar bar)
        {
            Bar = bar;
        }
    }
}