using UnityEngine;
using UnityEngine.InputSystem;

namespace RuinApp.Diagnostics
{
    /// <summary>
    /// STAGE 1 — the click→ground primitive, in isolation.
    ///
    /// Proves that a screen pixel maps to a world point on the Y=ground plane exactly,
    /// with ZERO dependence on cubes, bars, containers, or claims. Drop this on an empty
    /// GameObject, assign (or auto-find) the workspace camera, enter Play with the
    /// GAME VIEW MAXIMIZED, and read the overlay:
    ///
    ///   - The four corners (BL/BR/TL/TR) and CENTER show where each screen extreme lands
    ///     on the ground. Under a tilted camera the bottom edge is the NEAR ground (smaller Z)
    ///     and the top edge is the FAR ground (larger Z) — the tilt is already accounted for
    ///     by the ray↔plane intersection. These four world points ARE the virtual screen-space
    ///     corners projected onto the ground.
    ///   - MOUSE updates live; move the cursor and watch the world point track it 1:1.
    ///   - Click anywhere → logs the pixel→world mapping and draws a 2-unit yellow stalk in
    ///     the Scene view at the landing point.
    ///
    /// Success = the stalk plants under the cursor, the corners stay stable as you move, and
    /// resizing/maximizing does not change the corner world coords. Once trusted, delete this
    /// file; ScreenToGround is the only thing the real code needs to keep.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GroundProjectionProbe : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [Tooltip("World Y of the ground plane the click is projected onto.")]
        [SerializeField] private float groundY = 0f;

        private Plane ground;
        private Vector3 lastClickWorld;
        private bool hasClick;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        }

        /// <summary>
        /// THE primitive. Pixel (origin bottom-left, render-target space) → world point on the
        /// ground plane. Returns false ONLY when the pixel is outside the camera's render target
        /// (in the editor: cursor over the Game-view toolbar or another panel; on device: never).
        /// This is the single source of truth the whole input layer should route through.
        /// </summary>
        public bool ScreenToGround(Vector2 screenPx, out Vector3 world)
        {
            world = default;
            if (cam == null) return false;

            // Bounds guard — the only failure mode left once the view is maximized.
            if (screenPx.x < 0f || screenPx.y < 0f ||
                screenPx.x > cam.pixelWidth || screenPx.y > cam.pixelHeight)
                return false;

            Ray ray = cam.ScreenPointToRay(screenPx);
            if (!ground.Raycast(ray, out float enter)) return false; // ray parallel/above horizon

            world = ray.GetPoint(enter);
            return true;
        }

        private void Update()
        {
            DrawAllClaims();
            
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            Vector2 p = Mouse.current.position.ReadValue();
            if (ScreenToGround(p, out Vector3 w))
            {
                lastClickWorld = w;
                hasClick = true;
                Debug.Log($"[Probe] click px=({p.x:F0},{p.y:F0}) -> ground ({w.x:F2}, {w.z:F2})");
                Debug.DrawRay(w, Vector3.up * 2f, Color.yellow, 3f);
            }
            else
            {
                Debug.Log($"[Probe] click px=({p.x:F0},{p.y:F0}) OUTSIDE render target — ignored");
            }
        }

        private void OnGUI()
        {
            if (cam == null) return;

            float w = cam.pixelWidth;
            float h = cam.pixelHeight;
            GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };

            GUILayout.BeginArea(new Rect(10, 10, 580, 230), GUI.skin.box);
            GUILayout.Label($"render target : {w:F0} x {h:F0}", s);
            GUILayout.Label(Corner("BL    ", new Vector2(0f, 0f)), s);
            GUILayout.Label(Corner("BR    ", new Vector2(w, 0f)), s);
            GUILayout.Label(Corner("TL    ", new Vector2(0f, h)), s);
            GUILayout.Label(Corner("TR    ", new Vector2(w, h)), s);
            GUILayout.Label(Corner("CENTER", new Vector2(w * 0.5f, h * 0.5f)), s);

            Vector2 m = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            GUILayout.Label(
                ScreenToGround(m, out Vector3 mg)
                    ? $"MOUSE  px({m.x:F0},{m.y:F0}) -> world ({mg.x:F2}, {mg.z:F2})"
                    : $"MOUSE  px({m.x:F0},{m.y:F0}) -> outside render target", s);

            if (hasClick)
                GUILayout.Label($"LAST CLICK -> world ({lastClickWorld.x:F2}, {lastClickWorld.z:F2})", s);

            GUILayout.EndArea();
        }

        private string Corner(string name, Vector2 px)
        {
            return ScreenToGround(px, out Vector3 g)
                ? $"{name} px({px.x:F0},{px.y:F0}) -> world ({g.x:F2}, {g.z:F2})"
                : $"{name} px({px.x:F0},{px.y:F0}) -> no ground (above horizon)";
        }

        private void DrawAllClaims()
        {
            foreach (var cube in FindObjectsByType<RuinApp.Primitives.Cube>(FindObjectsInactive.Exclude))
            {
                Rect r = cube.GetClaimedArea();
                float y = groundY + 0.02f;
                Vector3 bl = new Vector3(r.xMin, y, r.yMin);
                Vector3 br = new Vector3(r.xMax, y, r.yMin);
                Vector3 tl = new Vector3(r.xMin, y, r.yMax);
                Vector3 tr = new Vector3(r.xMax, y, r.yMax);

                bool hit = hasClick &&
                        r.Contains(new Vector2(lastClickWorld.x, lastClickWorld.z));
                Color c = hit ? Color.green : Color.cyan;

                Debug.DrawLine(bl, br, c);
                Debug.DrawLine(br, tr, c);
                Debug.DrawLine(tr, tl, c);
                Debug.DrawLine(tl, bl, c);
            }
        }
    }
}
