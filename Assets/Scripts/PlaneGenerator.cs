using UnityEngine;

public static class PlaneGenerator
{
    public static Mesh GeneratePlane(float planeWidth, float planeDepth, int widthSegments, int depthSegments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralPlane";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // --- Calcular Vértices y UVs ---
        int numVerticesX = widthSegments + 1;
        int numVerticesZ = depthSegments + 1;
        int totalVertices = numVerticesX * numVerticesZ;

        Vector3[] vertices = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];

        int vertexIndex = 0;
        for (int z = 0; z < numVerticesZ; z++)
        {
            for (int x = 0; x < numVerticesX; x++)
            {
                float xPos = ((float)x / widthSegments - 0.5f) * planeWidth;
                float zPos = ((float)z / depthSegments - 0.5f) * planeDepth;
                float yPos = 0;

                vertices[vertexIndex] = new Vector3(xPos, yPos, zPos);
                uvs[vertexIndex] = new Vector2((float)x / widthSegments, (float)z / depthSegments);

                vertexIndex++;
            }
        }

        // --- Calcular Triángulos ---
        int numQuadsX = widthSegments;
        int numQuadsZ = depthSegments;
        int totalTriangles = numQuadsX * numQuadsZ * 2;
        int[] triangles = new int[totalTriangles * 3];

        int triangleIndex = 0;
        for (int z = 0; z < numVerticesZ - 1; z++)
        {
            for (int x = 0; x < numVerticesX - 1; x++)
            {
                int topLeft = z * numVerticesX + x;
                int topRight = z * numVerticesX + x + 1;
                int bottomLeft = (z + 1) * numVerticesX + x;
                int bottomRight = (z + 1) * numVerticesX + x + 1;

                // Primer triángulo del quad
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = topLeft;

                // Segundo triángulo del quad
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = bottomRight;
                triangles[triangleIndex++] = topRight;
            }
        }

        // --- Asignar datos al Mesh ---
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}