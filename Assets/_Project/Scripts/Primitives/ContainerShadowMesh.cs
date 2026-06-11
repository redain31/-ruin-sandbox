using System.Collections.Generic;
using UnityEngine;

namespace RuinApp.Primitives
{
    /// <summary>
    /// Builds and maintains a single mesh covering a container's claimed area.
    /// The mesh is the union of member cubes' claimed rectangles, emitted as one quad per cube
    /// into a single mesh. Because it's one mesh with one flat material, overlapping quads merge
    /// cleanly (no double-darkening) — the merge problem that plagued separate quads/decals is
    /// avoided by combining into a single draw.
    ///
    /// Attached to a Container. Rebuilt on demand (on recluster), not per-frame.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ContainerShadowMesh : MonoBehaviour
    {
        [SerializeField] private float meshY = 0.02f; // height above workspace surface

        private Mesh mesh;
        private MeshFilter meshFilter;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            mesh = new Mesh { name = "ContainerShadowMesh" };
            meshFilter.mesh = mesh;

            // Ensure this mesh never casts or receives real shadows (the quad lesson).
            MeshRenderer mr = GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// Rebuilds the mesh from the given claimed rectangles (each in XZ space as a Rect
        /// where Rect.x→worldX, Rect.y→worldZ). Reuses the same Mesh object (Clear + refill)
        /// to avoid allocating/leaking meshes.
        /// </summary>
        public void Rebuild(IReadOnlyList<Rect> claims)
        {
            mesh.Clear();

            if (claims == null || claims.Count == 0)
                return;

            // One quad per claim → 4 verts, 6 indices each.
            int quadCount = claims.Count;
            Vector3[] vertices = new Vector3[quadCount * 4];
            Vector2[] uvs = new Vector2[quadCount * 4];
            int[] triangles = new int[quadCount * 6];

            for (int i = 0; i < quadCount; i++)
            {
                Rect r = claims[i];

                // World-space corners of this claim rectangle, on the XZ plane at meshY.
                // Note: this component sits at the container's transform; we build the mesh
                // in WORLD space then place the transform at origin, OR build in local space.
                // For simplicity, we build in world space and keep the transform at identity.
                float x0 = r.xMin;
                float x1 = r.xMax;
                float z0 = r.yMin; // Rect.y maps to world Z
                float z1 = r.yMax;

                int v = i * 4;
                vertices[v + 0] = new Vector3(x0, meshY, z0);
                vertices[v + 1] = new Vector3(x1, meshY, z0);
                vertices[v + 2] = new Vector3(x1, meshY, z1);
                vertices[v + 3] = new Vector3(x0, meshY, z1);

                // UVs 0–1 per quad (for the soft-edge shader later).
                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                // Two triangles, wound so the face points UP (+Y), visible from the top-down camera.
                int t = i * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            // Normals not strictly needed for an unlit shadow, but set them upward for safety.
            Vector3[] normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
        }

        /// <summary>
        /// Clears the mesh (e.g., when the container drops below 2 members and should show nothing).
        /// </summary>
        public void ClearMesh()
        {
            mesh.Clear();
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}