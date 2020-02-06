using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

struct TerrainChunk {
    GameObject gameObject;
    Terrain terrain;

    public TerrainChunk(System.Action<Vector2, System.Action<MapData>> requestAction, Transform parentTransform, Vector2 position) {
        gameObject = new GameObject("Terrain Chunk");
        gameObject.transform.parent = parentTransform;
        gameObject.transform.position = new Vector3(position.x, 0.0f, position.y);
        terrain = gameObject.AddComponent<Terrain>();
        terrain.materialType = Terrain.MaterialType.Custom;
        terrain.materialTemplate = new Material(Shader.Find("Standard"));
        requestAction(position, OnReceiveTerrainData);
    }

    void OnReceiveTerrainData(MapData mapData) {
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = 257;
        terrainData.size = new Vector3(256, 10, 256);
        terrainData.SetHeights(0, 0, mapData.heightMap);
        terrain.terrainData = terrainData;

        Texture2D texture = new Texture2D(257, 257);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(mapData.colourMap);
        texture.Apply();
        terrain.materialTemplate.mainTexture = texture;
    }
}

// Struct to store the data for a terrain chunk
struct MapData {
    public float[,] heightMap;
    public Color[] colourMap;
}

[System.Serializable]
public struct TerrainType {
    public string name;
    public float height;
    public Color colour;
}

struct ThreadInfo<T> {
    public System.Action<T> callback;
    public T parameter;

    public ThreadInfo(System.Action<T> callback, T parameter) {
        this.callback = callback;
        this.parameter = parameter;
    }
}

public class EndlessTerrain : MonoBehaviour {
    const int terrainSize = 256;
    public int seed;
    public float scale;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;
    public Vector2 offset;
    public TerrainType[] regions;

    public Transform targetTransform;
    Vector2 targetPosition;
    Vector2 previousTargetPosition;

    Dictionary<Vector2, TerrainChunk> terrainChunksDictionary = new Dictionary<Vector2, TerrainChunk>();
    Queue<ThreadInfo<MapData>> terrainDataThreadInfoQueue = new Queue<ThreadInfo<MapData>>();

    void Start() {
        UpdateTerrainChunks();
    }

    void Update() {
        targetPosition = new Vector2(targetTransform.position.x, targetTransform.position.z);

        if ((previousTargetPosition - targetPosition).sqrMagnitude > 625.0f) {
            previousTargetPosition = targetPosition;
            UpdateTerrainChunks();
        }

        if (terrainDataThreadInfoQueue.Count > 0) {
            for (int i = 0; i < terrainDataThreadInfoQueue.Count; i++) {
                ThreadInfo<MapData> threadInfo = terrainDataThreadInfoQueue.Dequeue();
                threadInfo.callback(threadInfo.parameter);
            }
        }
    }

    void UpdateTerrainChunks() {
        int targetChunkCoordX = Mathf.RoundToInt(targetPosition.x / terrainSize);
        int targetChunkCoordY = Mathf.RoundToInt(targetPosition.y / terrainSize);

        for (int offsetY = -1; offsetY <= 1; offsetY++) {
            for (int offsetX = -1; offsetX <= 1; offsetX++) {
                Vector2 currentChunkCoord = new Vector2(targetChunkCoordX + offsetX, targetChunkCoordY + offsetY);
                Vector2 position = currentChunkCoord * terrainSize;

                if (!terrainChunksDictionary.ContainsKey(currentChunkCoord)) {
                    terrainChunksDictionary.Add(currentChunkCoord, new TerrainChunk(RequestTerrainData, transform, position));
                }
            }
        }
    }

    void RequestTerrainData(Vector2 terrainPosition, System.Action<MapData> callback) {
        ThreadStart threadStart = delegate {
            TerrainDataThread(terrainPosition, callback);
        };
        new Thread(threadStart).Start();
    }

    void TerrainDataThread(Vector2 terrainPosition, System.Action<MapData> callback) {
        MapData mapData = new MapData();

        mapData.heightMap = GenerateHeightMap(terrainSize + 1, terrainSize + 1, seed, scale, persistance, lacunarity, new Vector2(terrainPosition.y, -terrainPosition.x));
        mapData.colourMap = GenerateColourMap(mapData.heightMap);

        lock (terrainDataThreadInfoQueue) {
            terrainDataThreadInfoQueue.Enqueue(new ThreadInfo<MapData>(callback, mapData));
        }
    }

    float[,] GenerateHeightMap(int width, int height, int seed, float scale, float persistance, float lacunarity, Vector2 offset) {
        const int octaveCount = 5;
        
        System.Random randomNumberGenerator = new System.Random(seed);

        float[,] heightMap = new float[width, height];

        float amplitude = 1.0f;
        float frequency = 1.0f;

        float maxPossibleNoiseHeight = 0.0f;

        Vector2[] octaveOffsets = new Vector2[octaveCount];
        for (int i = 0; i < octaveCount; i++) {
            float offsetX = randomNumberGenerator.Next(-100000, 100000) + offset.x;
            float offsetY = randomNumberGenerator.Next(-100000, 100000) - offset.y;

            octaveOffsets[i] = new Vector2(offsetX, offsetY);
            maxPossibleNoiseHeight = maxPossibleNoiseHeight + amplitude;
            amplitude = amplitude * persistance;
        }

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                float noiseHeight = 0.0f;

                amplitude = 1.0f;
                frequency = 1.0f;

                for (int i = 0; i < octaveCount; i++) {
                    float sampleX = (x - (width / 2.0f) + octaveOffsets[i].x) / scale * frequency;
                    float sampleY = (y - (height / 2.0f) + octaveOffsets[i].y) / scale * frequency;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2.0f - 1.0f;
                    noiseHeight = noiseHeight + perlinValue * amplitude;
                    amplitude = amplitude * persistance;
                    frequency = frequency * lacunarity;
                }

                heightMap[x, y] = noiseHeight;
            }
        }

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                heightMap[x, y] = (heightMap[x, y] + 1.0f) / (2.0f * maxPossibleNoiseHeight / 1.75f);
            }
        }

        return heightMap;
    }

    Color[] GenerateColourMap(float[,] heightMap) {
        Color[] colourMap = new Color[(terrainSize + 1) * (terrainSize + 1)];

        for (int y = 0; y < (terrainSize + 1); y++) {
            for (int x = 0; x < (terrainSize + 1); x++) {
                float currentHeight = Mathf.Clamp01(heightMap[y, x]);

                for (int i = 0; i < regions.Length; i++) {
                    if (currentHeight <= regions[i].height) {
                        colourMap[y * (terrainSize + 1) + x] = regions[i].colour;
                        break;
                    }
                }
            }
        }

        return colourMap;
    }
}
