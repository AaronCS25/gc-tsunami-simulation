using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralPlaneGenerator : MonoBehaviour
{
    [Header("Plane Dimensions")]
    public float planeWidth = 10f; // Ancho del plano
    public float planeDepth = 10f; // Profundidad (o largo) del plano

    [Header("Subdivisions")]
    [Min(1)]
    public int widthSegments = 10; // N�mero de segmentos a lo largo del ancho (X)
    [Min(1)]
    public int depthSegments = 10; // N�mero de segmentos a lo largo de la profundidad (Z)

    private MeshFilter meshFilter;

    void Awake() // Awake se llama antes que Start, asegurando que el mesh est� listo
    {
        meshFilter = GetComponent<MeshFilter>();
        GeneratePlane();
    }

    void GeneratePlane()
    {
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralPlane";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // --- Calcular V�rtices y UVs ---
        // N�mero de v�rtices = (segmentos + 1) en cada direcci�n
        int numVerticesX = widthSegments + 1;
        int numVerticesZ = depthSegments + 1;
        int totalVertices = numVerticesX * numVerticesZ;

        Vector3[] vertices = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];

        // Offset para centrar el plano en el origen
        float halfWidth = planeWidth / 2f;
        float halfDepth = planeDepth / 2f;

        int vertexIndex = 0;
        for (int z = 0; z < numVerticesZ; z++)
        {
            for (int x = 0; x < numVerticesX; x++)
            {
                // Posici�n del v�rtice
                // Normalizamos la posici�n (0 a 1) y luego escalamos y centramos
                float xPos = ((float)x / widthSegments - 0.5f) * planeWidth;
                float zPos = ((float)z / depthSegments - 0.5f) * planeDepth;
                float yPos = 0;

                vertices[vertexIndex] = new Vector3(xPos, yPos, zPos);

                // Coordenadas UV (para texturas)
                uvs[vertexIndex] = new Vector2((float)x / widthSegments, (float)z / depthSegments);

                vertexIndex++;
            }
        }

        // --- Calcular Tri�ngulos ---
        // N�mero de quads = segmentos en cada direcci�n
        // N�mero de tri�ngulos = n�mero de quads * 2
        // N�mero de �ndices de tri�ngulo = n�mero de tri�ngulos * 3
        int numQuadsX = widthSegments;
        int numQuadsZ = depthSegments;
        int totalTriangles = numQuadsX * numQuadsZ * 2;
        int[] triangles = new int[totalTriangles * 3];

        int triangleIndex = 0;
        for (int z = 0; z < numVerticesZ - 1; z++)
        {
            for (int x = 0; x < numVerticesX - 1; x++)
            {
                // V�rtices del quad actual (sentido horario desde abajo-izquierda)
                int topLeft = z * numVerticesX + x;
                int topRight = z * numVerticesX + x + 1;
                int bottomLeft = (z + 1) * numVerticesX + x;
                int bottomRight = (z + 1) * numVerticesX + x + 1;

                // Primer tri�ngulo del quad
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = topLeft;

                // Segundo tri�ngulo del quad
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = bottomRight;
                triangles[triangleIndex++] = topRight;
            }
        }

        // --- Asignar datos al Mesh ---
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        // --- Recalcular para el sombreado y colisiones ---
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Asignar el mesh generado al MeshFilter
        meshFilter.mesh = mesh;

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer.sharedMaterial == null)
        {            
            Material defaultMaterial = new Material(Shader.Find("Standard"));
            defaultMaterial.color = Color.gray;
            meshRenderer.sharedMaterial = defaultMaterial;
        }
    } 

#if UNITY_EDITOR
    void OnValidate()
    {
        // if (Application.isPlaying) return; // Solo en editor
        // if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        // if (meshFilter != null) GeneratePlane();
    }
#endif
}