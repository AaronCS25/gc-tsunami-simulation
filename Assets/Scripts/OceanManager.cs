using UnityEngine;
using UnityEngine.Rendering;
using System; 

public class OManager : MonoBehaviour
{
    // --- Referencias a Shaders de Computo ---
    public ComputeShader spectrumGeneratorShader;
    public ComputeShader timeEvolutionShader;
    public ComputeShader butterflyTextureGeneratorShader;
    public ComputeShader ifftShader;
    public ComputeShader normalizeShader;

    // --- Parámetros de la Simulación ---
    [Header("Simulation Settings")]
    [Range(64, 1024)]
    public int N = 256;
    public float L = 100.0f;

    [Header("Phillips Spectrum Settings")]
    public float windSpeed = 10.0f;
    public Vector2 windDirection = new Vector2(1.0f, 0.0f);
    public float phillipsAmplitude = 0.0001f;
    public float gravity = 9.81f;
    public float seed = 0.0f;

    [Header("Time Evolution Settings")]
    public float simulationSpeed = 1.0f;

    [Header("Vertex Shader")]
    public Material waterMaterial;
    public float displacementScale = 1.0f;

    [Header("Debugging Textures")]
    public RenderTexture h0_spectrum_debug;
    public RenderTexture ht_spectrum_y_debug;
    public RenderTexture displacement_map_y_debug;
    public RenderTexture slope_map_x_debug;
    public RenderTexture slope_map_z_debug;

    // --- Texturas de Trabajo (Espectros) ---
    private RenderTexture _h0_spectrum;          // h0(k)
    private RenderTexture _h0_spectrum_conjugate; // h0(-k)
    private RenderTexture _ht_spectrum_y;        // h(k,t) para desplazamiento Y
    private RenderTexture _ht_spectrum_x;        // h(k,t) para desplazamiento X
    private RenderTexture _ht_spectrum_z;        // h(k,t) para desplazamiento Z

    // --- NUEVAS TEXTURAS DE ESPECTROS DE PENDIENTE ---
    private RenderTexture _ht_spectrum_slopeX; // Espectro d(h)/dx
    private RenderTexture _ht_spectrum_slopeZ; // Espectro d(h)/dz

    // --- Texturas y Buffers para la IFFT de referencia ---
    private RenderTexture _butterflyTexture;
    private ComputeBuffer _bitReversedIndicesBuffer;

    // Texturas para el ping-pong de la IFFT
    private RenderTexture _pingTexture;
    private RenderTexture _pongTexture;

    // Mapas de desplazamiento finales (dominio espacial)
    public RenderTexture DisplacementMapY { get; private set; }
    public RenderTexture DisplacementMapX { get; private set; }
    public RenderTexture DisplacementMapZ { get; private set; }

    public RenderTexture SlopeMapX { get; private set; }
    public RenderTexture SlopeMapZ { get; private set; }

    // --- Kernels IDs ---
    private int _spectrumGenKernelID;
    private int _timeEvolutionKernelID;
    private int _butterflyTextureGenKernelID;
    private int _ifftMainKernelID;
    private int _normalizeKernelID;

    // --- Mesh Generation ---
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _planeMesh;

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();

        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer == null) _meshRenderer = gameObject.AddComponent<MeshRenderer>();

        _planeMesh = PlaneGenerator.GeneratePlane(L, L, N - 1, N - 1);
        _meshFilter.mesh = _planeMesh;

        // Asignar el material de agua
        if (waterMaterial == null)
        {
            Debug.LogError("Water Material no asignado en OManager. Arrastra un material al Inspector.");
            _meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
        }
        else
        {
            _meshRenderer.sharedMaterial = waterMaterial;
        }

        // --- Inicializar Compute Shaders y Kernels ---
        _spectrumGenKernelID = spectrumGeneratorShader.FindKernel("CSMain");
        _timeEvolutionKernelID = timeEvolutionShader.FindKernel("CSMain");
        _butterflyTextureGenKernelID = butterflyTextureGeneratorShader.FindKernel("CSMain");
        _ifftMainKernelID = ifftShader.FindKernel("CSMain");
        _normalizeKernelID = normalizeShader.FindKernel("CSMain");

        InitializeRenderTextures();

        GenerateButterflyData();

        GenerateInitialSpectrums();
    }

    void InitializeRenderTextures()
    {
        RenderTextureFormat spectrumFormat = RenderTextureFormat.ARGBHalf;

        _h0_spectrum = CreateRenderTexture(N, N, spectrumFormat);
        _h0_spectrum_conjugate = CreateRenderTexture(N, N, spectrumFormat);

        _ht_spectrum_y = CreateRenderTexture(N, N, spectrumFormat);
        _ht_spectrum_x = CreateRenderTexture(N, N, spectrumFormat);
        _ht_spectrum_z = CreateRenderTexture(N, N, spectrumFormat);

        _ht_spectrum_slopeX = CreateRenderTexture(N, N, spectrumFormat);
        _ht_spectrum_slopeZ = CreateRenderTexture(N, N, spectrumFormat);

        // La textura mariposa tiene logN de alto por N de ancho, y es float4
        int logN_Value = (int)Mathf.Log(N, 2.0f);
        _butterflyTexture = CreateRenderTexture(logN_Value, N, spectrumFormat);

        // Texturas para el ping-pong de la IFFT (float4)
        _pingTexture = CreateRenderTexture(N, N, spectrumFormat);
        _pongTexture = CreateRenderTexture(N, N, spectrumFormat);

        DisplacementMapY = CreateRenderTexture(N, N, RenderTextureFormat.RHalf);
        DisplacementMapX = CreateRenderTexture(N, N, RenderTextureFormat.RHalf);
        DisplacementMapZ = CreateRenderTexture(N, N, RenderTextureFormat.RHalf);

        SlopeMapX = CreateRenderTexture(N, N, RenderTextureFormat.RHalf);
        SlopeMapZ = CreateRenderTexture(N, N, RenderTextureFormat.RHalf);
    }

    RenderTexture CreateRenderTexture(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, format);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    int[] GenerateBitReversedIndices(int size)
    {
        int[] indices = new int[size];
        int logN = (int)Mathf.Log(size, 2.0f);
        for (int i = 0; i < size; i++)
        {
            uint reversed = 0;
            uint x = (uint)i;
            for (int j = 0; j < logN; j++)
            {
                reversed = (reversed << 1) | (x & 1);
                x >>= 1;
            }
            indices[i] = (int)reversed;
        }
        return indices;
    }

    void GenerateButterflyData()
    {
        // 1. Generar el ComputeBuffer con los índices bit-invertidos
        int[] bitReversedIndicesArray = GenerateBitReversedIndices(N);
        _bitReversedIndicesBuffer = new ComputeBuffer(N, sizeof(int));
        _bitReversedIndicesBuffer.SetData(bitReversedIndicesArray);

        // 2. Ejecutar el ButterflyTextureGenerator shader
        butterflyTextureGeneratorShader.SetInt("N", N);
        butterflyTextureGeneratorShader.SetBuffer(_butterflyTextureGenKernelID, "_BitReversedIndices", _bitReversedIndicesBuffer);
        butterflyTextureGeneratorShader.SetTexture(_butterflyTextureGenKernelID, "_ButterflyTexture", _butterflyTexture);

        int logN_Value = (int)Mathf.Log(N, 2.0f);
        int threadGroupsX_Butterfly = logN_Value;
        int threadGroupsY_Butterfly = Mathf.CeilToInt(N / 16.0f);

        butterflyTextureGeneratorShader.Dispatch(_butterflyTextureGenKernelID, threadGroupsX_Butterfly, threadGroupsY_Butterfly, 1);
    }


    void GenerateInitialSpectrums()
    {
        spectrumGeneratorShader.SetInt("N", N);
        spectrumGeneratorShader.SetFloat("L", L);
        spectrumGeneratorShader.SetFloat("ws", windSpeed);
        spectrumGeneratorShader.SetVector("wd", windDirection.normalized);
        spectrumGeneratorShader.SetFloat("A", phillipsAmplitude);
        spectrumGeneratorShader.SetFloat("time_seed", seed);

        spectrumGeneratorShader.SetTexture(_spectrumGenKernelID, "H0_Spectrum", _h0_spectrum);
        spectrumGeneratorShader.SetTexture(_spectrumGenKernelID, "H0_Spectrum_Conjugate", _h0_spectrum_conjugate);

        int threadGroupsXY = Mathf.CeilToInt(N / 16.0f);
        spectrumGeneratorShader.Dispatch(_spectrumGenKernelID, threadGroupsXY, threadGroupsXY, 1);

        // Para depuración:
        if (h0_spectrum_debug != null) Graphics.Blit(_h0_spectrum, h0_spectrum_debug);
    }

    void Update()
    {
        float currentTime = Time.time * simulationSpeed;

        // --- 1. Evolucionar el Espectro en el Tiempo (Ht_Spectrum_Y, X, Z) ---
        timeEvolutionShader.SetInt("N", N);
        timeEvolutionShader.SetFloat("L", L);
        timeEvolutionShader.SetFloat("time", currentTime);

        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "H0_Spectrum", _h0_spectrum);
        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "H0_Spectrum_Conjugate", _h0_spectrum_conjugate);

        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "Ht_Spectrum_Y", _ht_spectrum_y);
        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "Ht_Spectrum_X", _ht_spectrum_x);
        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "Ht_Spectrum_Z", _ht_spectrum_z);

        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "Ht_Spectrum_SlopeX", _ht_spectrum_slopeX);
        timeEvolutionShader.SetTexture(_timeEvolutionKernelID, "Ht_Spectrum_SlopeZ", _ht_spectrum_slopeZ);

        int threadGroupsXY = Mathf.CeilToInt(N / 16.0f);
        timeEvolutionShader.Dispatch(_timeEvolutionKernelID, threadGroupsXY, threadGroupsXY, 1);

        // Para depuración:
        if (ht_spectrum_y_debug != null) Graphics.Blit(_ht_spectrum_y, ht_spectrum_y_debug);

        // --- 2. Aplicar IFFT a los 3 Espectros ---
        // (Ht_Spectrum_Y -> DisplacementMapY)
        RunIFFT(_ht_spectrum_y, DisplacementMapY);

        // (Ht_Spectrum_X -> DisplacementMapX)
        RunIFFT(_ht_spectrum_x, DisplacementMapX);

        // (Ht_Spectrum_Z -> DisplacementMapZ)
        RunIFFT(_ht_spectrum_z, DisplacementMapZ);

        // (Ht_Spectrum_SlopeX -> SlopeMapX)
        RunIFFT(_ht_spectrum_slopeX, SlopeMapX);

        // (Ht_Spectrum_SlopeZ -> SlopeMapZ)
        RunIFFT(_ht_spectrum_slopeZ, SlopeMapZ);

        _meshRenderer.sharedMaterial.SetTexture("_DisplacementMapY", DisplacementMapY);
        _meshRenderer.sharedMaterial.SetTexture("_DisplacementMapX", DisplacementMapX);
        _meshRenderer.sharedMaterial.SetTexture("_DisplacementMapZ", DisplacementMapZ);
        _meshRenderer.sharedMaterial.SetTexture("_SlopeMapX", SlopeMapX);
        _meshRenderer.sharedMaterial.SetTexture("_SlopeMapZ", SlopeMapZ);
        _meshRenderer.sharedMaterial.SetFloat("_DisplacementScale", displacementScale);

        // Para depuración
        if (displacement_map_y_debug != null) Graphics.Blit(DisplacementMapY, displacement_map_y_debug);
        if (slope_map_x_debug != null) Graphics.Blit(SlopeMapX, slope_map_x_debug);
        if (slope_map_z_debug != null) Graphics.Blit(SlopeMapZ, slope_map_z_debug);
    }

    // OManager.cs: En RunIFFT
    void RunIFFT(RenderTexture inputSpectrum, RenderTexture outputMap)
    {
        ifftShader.SetInt("N_SIZE", N);
        int logN_Value = (int)Mathf.Log(N, 2.0f);

        int threadGroupsX_IFFT = Mathf.CeilToInt(N / 16.0f);
        int threadGroupsY_IFFT = Mathf.CeilToInt(N / 16.0f);

        // --- PASO 1: Copiar el espectro de entrada a la primera textura de ping-pong ---
        Graphics.Blit(inputSpectrum, _pingTexture);

        // --- PASO 2: Pasos iterativos de la mariposa de la IFFT ---
        ifftShader.SetTexture(_ifftMainKernelID, "_ButterflyTexture", _butterflyTexture);

        // Paso Horizontal de la IFFT
        ifftShader.SetInt("_Direction", 0); // 0 = Horizontal
        for (int stage = 0; stage < logN_Value; stage++)
        {
            ifftShader.SetInt("_FFT_Stage", stage);

            // _PingPongState: 0 para (ping->pong), 1 para (pong->ping)
            int currentPingPongState = (stage % 2 == 0) ? 0 : 1;
            ifftShader.SetInt("_PingPongState", currentPingPongState);

            ifftShader.SetTexture(_ifftMainKernelID, "_PingPong0", _pingTexture);
            ifftShader.SetTexture(_ifftMainKernelID, "_PingPong1", _pongTexture);

            ifftShader.Dispatch(_ifftMainKernelID, threadGroupsX_IFFT, threadGroupsY_IFFT, 1);
        }

        // Determinar cuál textura contiene el resultado de la pasada Horizontal
        RenderTexture horizontalResultFinalTex = (logN_Value % 2 == 0) ? _pingTexture : _pongTexture;

        // --- Prepárate para el Paso Vertical ---
        Graphics.Blit(horizontalResultFinalTex, _pingTexture);

        // Paso Vertical de la IFFT
        ifftShader.SetInt("_Direction", 1); // 1 = Vertical
        // _ButterflyTexture ya está seteada
        for (int stage = 0; stage < logN_Value; stage++)
        {
            ifftShader.SetInt("_FFT_Stage", stage);

            int currentPingPongState = (stage % 2 == 0) ? 0 : 1;
            ifftShader.SetInt("_PingPongState", currentPingPongState);

            ifftShader.SetTexture(_ifftMainKernelID, "_PingPong0", _pingTexture);
            ifftShader.SetTexture(_ifftMainKernelID, "_PingPong1", _pongTexture);
            ifftShader.Dispatch(_ifftMainKernelID, threadGroupsX_IFFT, threadGroupsY_IFFT, 1);
        }

        // Determinar cuál textura contiene el resultado final de la IFFT
        RenderTexture finalIFFTResultTex = (logN_Value % 2 == 0) ? _pingTexture : _pongTexture;

        // --- PASO 3: Normalización y Permutación de Signos ---
        NormalizeIFFT(finalIFFTResultTex, outputMap);
    }

    void NormalizeIFFT(RenderTexture ifftResultTex, RenderTexture outputMap)
    {
        normalizeShader.SetInt("N_SIZE", N);

        int logN_Value = (int)Mathf.Log(N, 2.0f);
        int finalPingPongState = (logN_Value % 2 == 0) ? 0 : 1;

        normalizeShader.SetInt("_FinalPingPongState", finalPingPongState);

        normalizeShader.SetTexture(_normalizeKernelID, "InputData_PingPong0", _pingTexture);
        normalizeShader.SetTexture(_normalizeKernelID, "InputData_PingPong1", _pongTexture);
        normalizeShader.SetTexture(_normalizeKernelID, "OutputData", outputMap);

        int groups = Mathf.CeilToInt(N / 16.0f);
        normalizeShader.Dispatch(_normalizeKernelID, groups, groups, 1);
    }

    void OnDisable()
    {
        if (_h0_spectrum != null) _h0_spectrum.Release();
        if (_h0_spectrum_conjugate != null) _h0_spectrum_conjugate.Release();
        if (_ht_spectrum_y != null) _ht_spectrum_y.Release();
        if (_ht_spectrum_x != null) _ht_spectrum_x.Release();
        if (_ht_spectrum_z != null) _ht_spectrum_z.Release();
        if (_ht_spectrum_slopeX != null) _ht_spectrum_slopeX.Release();
        if (_ht_spectrum_slopeZ != null) _ht_spectrum_slopeZ.Release();

        if (_butterflyTexture != null) _butterflyTexture.Release();
        if (_bitReversedIndicesBuffer != null) _bitReversedIndicesBuffer.Release();
        if (_pingTexture != null) _pingTexture.Release();
        if (_pongTexture != null) _pongTexture.Release();
        if (DisplacementMapY != null) DisplacementMapY.Release();
        if (DisplacementMapX != null) DisplacementMapX.Release();
        if (DisplacementMapZ != null) DisplacementMapZ.Release();
        if (SlopeMapX != null) SlopeMapX.Release();
        if (SlopeMapZ != null) SlopeMapZ.Release();

        if (_planeMesh != null)
        {
            Destroy(_planeMesh);
            _meshFilter.mesh = null;
        }
    }
}