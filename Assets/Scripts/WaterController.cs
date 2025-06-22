using UnityEngine;

// Definición de una componente de ola Gerstner individual
[System.Serializable]
public struct GerstnerWaveComponent
{
    [Tooltip("Dirección de propagación de la ola.")]
    public Vector2 direction;
    [Tooltip("Longitud de onda en metros (distancia entre crestas).")]
    public float wavelength;
    [Tooltip("Amplitud en metros (altura desde el nivel medio del mar hasta la cresta).")]
    public float amplitude;
    [Tooltip("Agudeza de la cresta (0 = sinusoide, valores > 0 aumentan la agudeza).")]
    [Range(0f, 1f)]
    public float steepness;
    [Tooltip("Velocidad de propagación de la ola en m/s.")]
    public float speed;
    [Tooltip("Desfase inicial de la ola (en radianes, para variar el punto de inicio de cada ola).")]
    public float phaseOffset;
}

[RequireComponent(typeof(MeshFilter))]
public class WaterController : MonoBehaviour
{
    private Mesh mesh;
    private Vector3[] originalVertices;
    private Vector3[] displacedVertices;

    [Header("Base Ocean Waves")]
    [Tooltip("Conjunto de olas que componen el estado base del océano.")]
    public GerstnerWaveComponent[] oceanWaves;

    [Header("Tsunami Event")]
    public bool activateTsunami = false;
    [Tooltip("Configuración de la ola principal del tsunami. 'Phase Offset' también se puede usar aquí si se desea.")]
    public GerstnerWaveComponent tsunamiWaveSettings;
    public float tsunamiRampUpDuration = 30f;

    [Header("Perlin Noise Settings")]
    [Tooltip("Escala del ruido Perlin. Valores más pequeños = ruido más extendido.")]
    public float perlinNoiseScale = 0.1f;
    [Tooltip("Amplitud del ruido Perlin (cuánto afecta la altura).")]
    public float perlinNoiseAmplitude = 0.05f;
    [Tooltip("Velocidad a la que el patrón de ruido se mueve (X e Y para dos direcciones de scroll).")]
    public Vector2 perlinNoiseSpeed = new Vector2(0.03f, 0.02f);

    private bool tsunamiHasBeenActivated = false;
    private float tsunamiRampUpTimer = 0f;
    private float currentTsunamiAmplitude = 0f;

    void Start()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("WaterController: No se encontró MeshFilter.");
            enabled = false;
            return;
        }
        mesh = meshFilter.mesh;
        originalVertices = mesh.vertices;
        displacedVertices = new Vector3[originalVertices.Length];

        for (int i = 0; i < oceanWaves.Length; i++)
        {
            if (oceanWaves[i].direction.sqrMagnitude > 0)
                oceanWaves[i].direction.Normalize();
            else
                oceanWaves[i].direction = Vector2.right;
        }

        if (tsunamiWaveSettings.direction.sqrMagnitude > 0)
            tsunamiWaveSettings.direction.Normalize();
        else
            tsunamiWaveSettings.direction = Vector2.right;
    }

    void Update()
    {
        if (originalVertices == null || mesh == null) return;

        if (activateTsunami && !tsunamiHasBeenActivated)
        {
            tsunamiHasBeenActivated = true;
            tsunamiRampUpTimer = 0f;
            currentTsunamiAmplitude = 0f;
        }
        if (tsunamiHasBeenActivated && tsunamiRampUpTimer < tsunamiRampUpDuration)
        {
            tsunamiRampUpTimer += Time.deltaTime;
            float rampProgress = Mathf.Clamp01(tsunamiRampUpTimer / tsunamiRampUpDuration);
            currentTsunamiAmplitude = Mathf.Lerp(0f, tsunamiWaveSettings.amplitude, rampProgress);
        }
        else if (tsunamiHasBeenActivated)
        {
            currentTsunamiAmplitude = tsunamiWaveSettings.amplitude;
        }

        // --- Deformación del Mesh ---
        for (int i = 0; i < originalVertices.Length; i++)
        {
            Vector3 originalVertexPosition = originalVertices[i];
            Vector3 totalDisplacement = Vector3.zero;

            // 1. Sumar contribuciones de las olas base de Gerstner
            foreach (GerstnerWaveComponent wave in oceanWaves)
            {
                if (wave.amplitude == 0 || wave.wavelength == 0) continue;

                Vector2 normDir = wave.direction;
                float k = 2f * Mathf.PI / wave.wavelength;
                float omega = k * wave.speed;
                float dotProductDirection = originalVertexPosition.x * normDir.x + originalVertexPosition.z * normDir.y;
                float phase = dotProductDirection * k - omega * Time.time + wave.phaseOffset;

                float cosPhase = Mathf.Cos(phase);
                float sinPhase = Mathf.Sin(phase);

                totalDisplacement.y += wave.amplitude * sinPhase;
                float horizontalDisp = wave.steepness * wave.amplitude * cosPhase;
                totalDisplacement.x += horizontalDisp * normDir.x;
                totalDisplacement.z += horizontalDisp * normDir.y;
            }

            // 2. Sumar contribución de la ola del tsunami (Gerstner)
            if (tsunamiHasBeenActivated && currentTsunamiAmplitude > 0.001f && tsunamiWaveSettings.wavelength > 0)
            {
                GerstnerWaveComponent tsunami = tsunamiWaveSettings;
                Vector2 normDirTsunami = tsunami.direction;
                float kTsunami = 2f * Mathf.PI / tsunami.wavelength;
                float omegaTsunami = kTsunami * tsunami.speed;
                float dotProductDirectionTsunami = originalVertexPosition.x * normDirTsunami.x + originalVertexPosition.z * normDirTsunami.y;
                float phaseTsunami = dotProductDirectionTsunami * kTsunami - omegaTsunami * Time.time + tsunami.phaseOffset;

                float cosPhaseTsunami = Mathf.Cos(phaseTsunami);
                float sinPhaseTsunami = Mathf.Sin(phaseTsunami);

                totalDisplacement.y += currentTsunamiAmplitude * sinPhaseTsunami;
                float horizontalDispTsunami = tsunami.steepness * currentTsunamiAmplitude * cosPhaseTsunami;
                totalDisplacement.x += horizontalDispTsunami * normDirTsunami.x;
                totalDisplacement.z += horizontalDispTsunami * normDirTsunami.y;
            }

            // 3. Añadir contribución del Ruido Perlin (solo vertical)
            if (perlinNoiseAmplitude > 0 && perlinNoiseScale != 0)
            {
                float noiseCoordX = originalVertexPosition.x * perlinNoiseScale + Time.time * perlinNoiseSpeed.x;
                float noiseCoordZ = originalVertexPosition.z * perlinNoiseScale + Time.time * perlinNoiseSpeed.y;
                float noiseValue = Mathf.PerlinNoise(noiseCoordX, noiseCoordZ);
                float perlinVerticalOffset = (noiseValue * 2.0f - 1.0f) * perlinNoiseAmplitude;
                totalDisplacement.y += perlinVerticalOffset;
            }

            displacedVertices[i] = new Vector3(
                originalVertexPosition.x + totalDisplacement.x,
                originalVertexPosition.y + totalDisplacement.y,
                originalVertexPosition.z + totalDisplacement.z
            );
        }

        mesh.vertices = displacedVertices;
        mesh.RecalculateNormals();
    }

    public float GetWaterHeightAt(float worldX, float worldZ)
    {
        Vector3 localPoint = transform.InverseTransformPoint(new Vector3(worldX, 0f, worldZ));
        float totalVerticalOffset = 0f;

        // 1. Sumar alturas de las olas base de Gerstner
        foreach (GerstnerWaveComponent wave in oceanWaves)
        {
            if (wave.amplitude == 0 || wave.wavelength == 0) continue;
            Vector2 normDir = wave.direction;
            float k = 2f * Mathf.PI / wave.wavelength;
            float omega = k * wave.speed;
            float dotProductDirection = localPoint.x * normDir.x + localPoint.z * normDir.y;
            // AÑADIDO wave.phaseOffset
            float phase = dotProductDirection * k - omega * Time.time + wave.phaseOffset;
            totalVerticalOffset += wave.amplitude * Mathf.Sin(phase);
        }

        // 2. Sumar altura de la ola del tsunami (Gerstner)
        if (tsunamiHasBeenActivated && currentTsunamiAmplitude > 0.001f && tsunamiWaveSettings.wavelength > 0)
        {
            GerstnerWaveComponent tsunami = tsunamiWaveSettings;
            Vector2 normDirTsunami = tsunami.direction;
            float kTsunami = 2f * Mathf.PI / tsunami.wavelength;
            float omegaTsunami = kTsunami * tsunami.speed;
            float dotProductDirectionTsunami = localPoint.x * normDirTsunami.x + localPoint.z * normDirTsunami.y;
            float phaseTsunami = dotProductDirectionTsunami * kTsunami - omegaTsunami * Time.time + tsunami.phaseOffset;
            totalVerticalOffset += currentTsunamiAmplitude * Mathf.Sin(phaseTsunami);
        }

        // 3. Añadir contribución del Ruido Perlin a la altura
        if (perlinNoiseAmplitude > 0 && perlinNoiseScale != 0)
        {
            float noiseCoordX = localPoint.x * perlinNoiseScale + Time.time * perlinNoiseSpeed.x;
            float noiseCoordZ = localPoint.z * perlinNoiseScale + Time.time * perlinNoiseSpeed.y;
            float noiseValue = Mathf.PerlinNoise(noiseCoordX, noiseCoordZ);
            float perlinVerticalOffset = (noiseValue * 2.0f - 1.0f) * perlinNoiseAmplitude;
            totalVerticalOffset += perlinVerticalOffset;
        }

        Vector3 localWaveSurfacePoint = new Vector3(localPoint.x, totalVerticalOffset, localPoint.z);
        Vector3 worldWaveSurfacePoint = transform.TransformPoint(localWaveSurfacePoint);

        return worldWaveSurfacePoint.y;
    }

    public Vector2 GetWaterHorizontalVelocityAt(float worldX, float worldZ)
    {
        Vector3 localPoint = transform.InverseTransformPoint(new Vector3(worldX, 0f, worldZ));
        Vector2 totalHorizontalVelocity = Vector2.zero;

        // 1. Sumar velocidades de las olas base del océano
        foreach (GerstnerWaveComponent wave in oceanWaves)
        {
            if (wave.amplitude == 0 || wave.wavelength == 0 || wave.steepness == 0) continue;
            Vector2 normDir = wave.direction;
            float k = 2f * Mathf.PI / wave.wavelength;
            float omega = k * wave.speed;
            float dotProductDirection = localPoint.x * normDir.x + localPoint.z * normDir.y;
            float phase = dotProductDirection * k - omega * Time.time + wave.phaseOffset;
            float sinPhase = Mathf.Sin(phase);
            float velocityMagnitudeComponent = omega * wave.steepness * wave.amplitude * sinPhase;
            totalHorizontalVelocity.x += velocityMagnitudeComponent * normDir.x;
            totalHorizontalVelocity.y += velocityMagnitudeComponent * normDir.y;
        }

        // 2. Sumar velocidad de la ola del tsunami (si está activa)
        if (tsunamiHasBeenActivated && currentTsunamiAmplitude > 0.001f && tsunamiWaveSettings.wavelength > 0 && tsunamiWaveSettings.steepness > 0)
        {
            GerstnerWaveComponent tsunami = tsunamiWaveSettings;
            Vector2 normDirTsunami = tsunami.direction;
            float kTsunami = 2f * Mathf.PI / tsunami.wavelength;
            float omegaTsunami = kTsunami * tsunami.speed;
            float dotProductDirectionTsunami = localPoint.x * normDirTsunami.x + localPoint.z * normDirTsunami.y;
            float phaseTsunami = dotProductDirectionTsunami * kTsunami - omegaTsunami * Time.time + tsunami.phaseOffset;
            float sinPhaseTsunami = Mathf.Sin(phaseTsunami);
            float velocityMagnitudeComponentTsunami = omegaTsunami * tsunami.steepness * currentTsunamiAmplitude * sinPhaseTsunami;
            totalHorizontalVelocity.x += velocityMagnitudeComponentTsunami * normDirTsunami.x;
            totalHorizontalVelocity.y += velocityMagnitudeComponentTsunami * normDirTsunami.y;
        }
        return totalHorizontalVelocity;
    }
}