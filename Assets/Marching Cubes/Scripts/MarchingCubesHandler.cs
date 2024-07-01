using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class MarchingCubesHandler : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Material terrainMaterial;
    [SerializeField] private float radius;
    [SerializeField] private int chunksPerAxis;

    [Header("Noise")]
    [SerializeField] private int octaves;
    [SerializeField] private float lacunarity;
    [SerializeField] private float gain;
    [SerializeField] private float frequency;
    [SerializeField] private float amplitude;

    [Header("Shaders")]
    [SerializeField] private ComputeShader IsoLevelMarchingCubesShader;
    [SerializeField] private ComputeShader MarchingCubesShader;
    private ComputeBuffer IsoLevelCubesBufferOut, IsoLevelCubesBufferIn, MarchingCubesMeshOut;

    [Header("Chunk")]
    [SerializeField, Tooltip("The amount of cells per axis"), Range(2, 128)] private int dimensions;
    [SerializeField, Tooltip("The xyz length in metres"), Range(2, 128)] private int size;

    [Header("Gizmos")]
    [SerializeField] private bool drawCellBounding;
    [SerializeField] private bool drawCellCorners;
    [SerializeField] private bool drawChunkBorder;

    private int gridPointsTotal, gridPointsPerAxis;
    private float[] isoLevels;
    //private List<Vector3> vertices;
    private List<GameObject> chunks; 
    void RenderIsoLevel(Vector3 chunkPosition)
    {
        // Set values
        this.IsoLevelMarchingCubesShader.SetInt("PointsPerAxis_IN", this.gridPointsPerAxis);
        this.IsoLevelMarchingCubesShader.SetInt("ChunkSize_IN", this.size);
        this.IsoLevelMarchingCubesShader.SetFloat("CellScalar_IN", (float)size / (float)dimensions);
        this.IsoLevelMarchingCubesShader.SetVector("ChunkPosition_IN", chunkPosition);

        this.IsoLevelMarchingCubesShader.SetInt("Octaves_IN", octaves);
        this.IsoLevelMarchingCubesShader.SetVector("NoiseSettings_IN", new(this.lacunarity, this.gain, this.frequency, this.amplitude));

        // Buffers
        this.isoLevels = new float[this.gridPointsTotal];
        this.IsoLevelCubesBufferOut = new ComputeBuffer(isoLevels.Length, sizeof(float));
        this.IsoLevelMarchingCubesShader.SetBuffer(0, "Iso_OUT", this.IsoLevelCubesBufferOut);

        // Dispatch
        this.IsoLevelMarchingCubesShader.Dispatch(0, this.gridPointsPerAxis, this.gridPointsPerAxis, this.gridPointsPerAxis);

        // Get
        this.IsoLevelCubesBufferOut.GetData(isoLevels);
        
        // Dispose
        this.IsoLevelCubesBufferOut.Dispose();
    }

    void GenerateChunkMeshes()
    {
        float cellScalar = (float)this.size / (float)this.dimensions;
        int gridPointsPerAxis = (this.dimensions + 1);
        int gridPointsTotal = gridPointsPerAxis * gridPointsPerAxis * gridPointsPerAxis;

        /* Initialize buffers and non-changing shader-values */

        // Shared buffers
        float[] isoLevels = new float[gridPointsTotal];
        ComputeBuffer IsoLevelCubesBuffer = new ComputeBuffer(isoLevels.Length, sizeof(float));
        this.IsoLevelMarchingCubesShader.SetBuffer(0, "Iso_OUT", IsoLevelCubesBuffer);
        this.MarchingCubesShader.SetBuffer(0, "Iso_IN", IsoLevelCubesBuffer);
        
        // IsoLevelMarchingCubesShader
        this.IsoLevelMarchingCubesShader.SetInt("PointsPerAxis_IN", gridPointsPerAxis);
        this.IsoLevelMarchingCubesShader.SetInt("ChunkSize_IN", this.size);
        this.IsoLevelMarchingCubesShader.SetFloat("CellScalar_IN", cellScalar);

        this.IsoLevelMarchingCubesShader.SetInt("Octaves_IN", this.octaves);
        this.IsoLevelMarchingCubesShader.SetVector("NoiseSettings_IN", new(this.lacunarity, this.gain, this.frequency, this.amplitude));

        // MarchingCubesShader
        this.MarchingCubesShader.SetInt("ResolutionCells_IN", this.dimensions);
        this.MarchingCubesShader.SetFloat("SurfaceLevel_IN", this.radius);

        // verticesDataAmount = dimensions^3 * 3 components * 3 points per triangle * 5 triangles(which is the maximum)
        int verticesDataAmount = Mathf.FloorToInt(Mathf.Pow(this.dimensions, 3)) * 3 * 3 * 5;
        float[] verticesBuffer = new float[verticesDataAmount]; // The maximum amount of vertices
        ComputeBuffer MarchingCubesMeshOut = new ComputeBuffer(verticesBuffer.Length, sizeof(float));
        this.MarchingCubesShader.SetBuffer(0, "Mesh_OUT", MarchingCubesMeshOut);

        /* Generate each individual mesh */
        for (int i = 0; i < this.chunks.Count; i++)
        {
            /* Set per-chunk-changing shader values */
            this.IsoLevelMarchingCubesShader.SetVector("ChunkPosition_IN", this.chunks[i].transform.position);

            /* Dispatch shaders */

            this.IsoLevelMarchingCubesShader.Dispatch(0, gridPointsPerAxis, gridPointsPerAxis, gridPointsPerAxis);

            int meshThreads = Mathf.CeilToInt((float)this.dimensions / 8.0f);
            this.MarchingCubesShader.Dispatch(0, meshThreads, meshThreads, meshThreads);

            /* Retrieve data */

            MarchingCubesMeshOut.GetData(verticesBuffer);

            /* Create the mesh */
            List<Vector3> vertices = new List<Vector3>();
            for (int j = 0; j < verticesBuffer.Length; j+=3)
                if (verticesBuffer[j] >= 0) { vertices.Add(new Vector3(verticesBuffer[j], verticesBuffer[j + 1], verticesBuffer[j + 2]) * cellScalar); }

            int[] indices = new int[vertices.Count];
            for (int j = 0; j < vertices.Count; j++)
                indices[j] = j;

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            newMesh.vertices = vertices.ToArray();
            newMesh.triangles = indices;
            newMesh.RecalculateBounds();
            newMesh.RecalculateNormals();
            newMesh.RecalculateTangents();
            this.chunks[i].AddComponent<MeshFilter>().mesh = newMesh;

            this.chunks[i].AddComponent<MeshRenderer>().material = terrainMaterial;
        }

        /* Clean up */

        MarchingCubesMeshOut.Dispose();
        IsoLevelCubesBuffer.Dispose();

        isoLevels = new float[0];
        verticesBuffer = new float[0];
    }

    void RenderMesh()
    {
        // Set values
        this.MarchingCubesShader.SetInt("ResolutionCells_IN", dimensions);
        this.MarchingCubesShader.SetFloat("SurfaceLevel_IN", (float)radius);
        
        // Buffers
        this.IsoLevelCubesBufferIn = new ComputeBuffer(this.gridPointsTotal, sizeof(float));
        this.IsoLevelCubesBufferIn.SetData(this.isoLevels);
        this.MarchingCubesShader.SetBuffer(0, "Iso_IN", this.IsoLevelCubesBufferIn);

        int verticesDataAmount = Mathf.FloorToInt(Mathf.Pow(this.dimensions, 3)) * 3 * 3 * 5;
        float[] verticesBuffer = new float[verticesDataAmount]; // The maximum amount of vertices
        this.MarchingCubesMeshOut = new ComputeBuffer(verticesBuffer.Length, sizeof(float));
        this.MarchingCubesShader.SetBuffer(0, "Mesh_OUT", this.MarchingCubesMeshOut);

        // Dispatch
        int threads = Mathf.CeilToInt(this.dimensions / 8.0f);
        this.MarchingCubesShader.Dispatch(0, threads, threads, threads);

        // Get
        this.MarchingCubesMeshOut.GetData(verticesBuffer);

        float cellScalar = (float)size / (float)dimensions;
        //this.vertices = new List<Vector3>();
        for (int i = 0; i < verticesBuffer.Length; i+=3)
        {
            if (verticesBuffer[i] < 0) { continue; }
            //this.vertices.Add(new Vector3(verticesBuffer[i], verticesBuffer[i + 1], verticesBuffer[i + 2]) * cellScalar);
        }
        
        // Dispose
        this.MarchingCubesMeshOut.Dispose();
        this.IsoLevelCubesBufferIn.Dispose();
    }

    void GenerateChunks()
    {
        
        if (this.chunks != null) this.chunks.Clear();
        this.chunks = new List<GameObject>();

        while (this.transform.childCount > 0)
            DestroyImmediate(this.transform.GetChild(0).gameObject);

        for (int z = 0; z < this.chunksPerAxis; z++)
            for (int y = 0; y < this.chunksPerAxis; y++)
                for (int x = 0; x < this.chunksPerAxis; x++)
                {
                    GameObject newChunk = new GameObject();
                    newChunk.transform.parent = this.transform;
                    newChunk.transform.position = (new Vector3((x * size) - ((this.chunksPerAxis * this.size) * 0.5f), (y * size) - ((this.chunksPerAxis * this.size) * 0.5f), (z * size) - (this.chunksPerAxis * this.size) * 0.5f));
                    newChunk.name = x + " " + y + " " + z;

                    this.chunks.Add(newChunk);
                }
    }

    void OnEnable()
    {
        GenerateChunks();
        GenerateChunkMeshes();

        //DateTime startTime = DateTime.Now;



        //this.gridPointsPerAxis = (this.dimensions + 1);
        //this.gridPointsTotal = this.gridPointsPerAxis * this.gridPointsPerAxis * this.gridPointsPerAxis;

        //for (int z = 0; z < this.chunksPerAxis; z++)
        //    for (int y = 0; y < this.chunksPerAxis; y++)
        //        for (int x = 0; x < this.chunksPerAxis; x++)
        //        {
        //            GameObject newChunk = new GameObject();
        //            newChunk.transform.parent = transform;
        //            newChunk.transform.position = (new Vector3((x * size) - ((this.chunksPerAxis * this.size) * 0.5f), (y * size) - ((this.chunksPerAxis * this.size) * 0.5f), (z * size) - (this.chunksPerAxis * this.size) * 0.5f));
        //            newChunk.name = newChunk.transform.position.ToString();

        //            newChunk.AddComponent<MeshRenderer>().material = terraqinMaterial;

        //            RenderIsoLevel(newChunk.transform.position);
        //            RenderMesh();

        //            List<int> indices = new List<int>();
        //            for (int i = 0; i < vertices.Count; i++)
        //                indices.Add(i);

        //            Mesh chunkMesh = new Mesh();
        //            chunkMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        //            chunkMesh.vertices = vertices.ToArray();
        //            chunkMesh.triangles = indices.ToArray();
        //            chunkMesh.RecalculateNormals();

        //            newChunk.AddComponent<MeshFilter>().mesh = chunkMesh;
        //        }

        //DateTime endTime = DateTime.Now;

        //Debug.Log("Generated mesh in " + (endTime.Millisecond - startTime.Millisecond) + "ms");

        //this.vertices.Clear();
        //this.isoLevels = new float[0];
    }

    void DrawCellBounding()
    {
        if (!drawCellBounding) { return; }

        Color cellBoundingColor = Color.white;
        cellBoundingColor.a = 0.05f;
        Gizmos.color = cellBoundingColor;

        float cellScalar = (float)size/(float)dimensions;

        for (int z = 0; z < dimensions; z++)
            for (int y = 0; y < dimensions; y++)
                for (int x = 0; x < dimensions; x++)
                {
                    Vector3 size = new Vector3(1.0f, 1.0f, 1.0f) * cellScalar;
                    Vector3 center = new Vector3(x * size.x + (cellScalar * 0.5f), y * size.y + (cellScalar * 0.5f), z * size.z + (cellScalar * 0.5f));
                    Gizmos.DrawWireCube(center, size);
                }
    }

    void DrawCellCorners()
    {
        if (!drawCellCorners) { return; }

        for (int z = 0; z < gridPointsPerAxis; z++)
        {
            for (int y = 0; y < gridPointsPerAxis; y++)
            {
                for (int x = 0; x < gridPointsPerAxis; x++)
                {
                    // 0-1
                    int isoLevelIndex = x + (y * gridPointsPerAxis) + (z * gridPointsPerAxis * gridPointsPerAxis);
                    float level = (isoLevelIndex > isoLevels.Length ? 0 : isoLevels[isoLevelIndex]) / radius;
                    Color pointColor = Color.white;
                    pointColor.r = 1.0f * level;
                    pointColor.g = 1.0f * level;
                    pointColor.b = 1.0f * level;
                    Gizmos.color = pointColor;

                    float cellScalar = (float)size / (float)dimensions;
                    Vector3 centre = new Vector3(x, z, y) * cellScalar;

                    Gizmos.DrawCube(centre, new Vector3(0.05f, 0.05f, 0.05f));
                }
            }
        }
    }

    void DrawChunkBorder()
    {
        if (!drawChunkBorder) { return; }

        for (int i = 0; i < this.transform.childCount; i++)
        {
            Color chunkBorderColor = Color.white;
            chunkBorderColor.a = 0.1f; 
            Gizmos.color = chunkBorderColor;
            Vector3 center = (new Vector3(1, 1, 1) * this.size) * 0.5f + this.transform.GetChild(i).position;
            Vector3 size = new Vector3(this.size, this.size, this.size);
            Gizmos.DrawWireCube(center, size);
        }
    }

    void DrawMeterInCorner()
    {
        Color meter = Color.green;
        meter.a = 0.1f;
        Gizmos.color = meter;

        for (int i = 0; i < this.transform.childCount; i++)
        {
            Vector3 center = this.transform.GetChild(i).position;
            Gizmos.DrawLine(center, new(center.x + 1.0f, center.y, center.z));
            Gizmos.DrawLine(center, new(center.x, center.y + 1.0f, center.z));
            Gizmos.DrawLine(center, new(center.x, center.y, center.z + 1.0f));
        }
    }

    private void OnDrawGizmos()
    {
        DrawCellBounding();
        DrawCellCorners();
        DrawChunkBorder();
        DrawMeterInCorner();
    }
}