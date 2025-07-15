using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

[RequireComponent(typeof(Rigidbody))]
public class Buoyancy : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Drag your OManager object here.")]
    public OManager oceanManager;

    [Header("Floating Points")]
    [Tooltip("Points on the hull for applying forces. Create a wide, stable base for best results.")]
    public List<Transform> floatingPoints = new List<Transform>();

    [Header("Physics Settings")]
    [Tooltip("The main upward force. Higher values make the object float higher.")]
    public float buoyancyForce = 30f;

    [Tooltip("The force that pushes the object along with the waves.")]
    public float waterDragForce = 2f;

    [Tooltip("Crucial for stability! Resists spinning. Increase if the boat is unstable.")]
    [Range(0.5f, 10f)]
    public float angularDrag = 2f;

    [Tooltip("Optional. Assign a child GameObject placed low on the boat to improve stability.")]
    public Transform centerOfMassObject;

    // --- Private Variables ---
    private Rigidbody _rigidbody;
    private NativeArray<float> _heightData;
    private NativeArray<float> _displacementXData;
    private NativeArray<float> _displacementZData;
    private bool _hasValidData = false;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (oceanManager == null)
        {
            Debug.LogError("Buoyancy: OManager not assigned! Please assign it in the Inspector.", this);
            enabled = false;
            return;
        }

        // Apply physics tuning from the Inspector
        _rigidbody.angularDamping = angularDrag;
        if (centerOfMassObject != null)
        {
            _rigidbody.centerOfMass = transform.InverseTransformPoint(centerOfMassObject.position);
        }
    }

    void OnEnable()
    {
        // Initialize the NativeArrays that will hold the GPU data
        int dataSize = oceanManager.N * oceanManager.N;
        _heightData = new NativeArray<float>(dataSize, Allocator.Persistent);
        _displacementXData = new NativeArray<float>(dataSize, Allocator.Persistent);
        _displacementZData = new NativeArray<float>(dataSize, Allocator.Persistent);

        // Start the coroutine that handles the continuous GPU readback
        StartCoroutine(GPUReadbackCoroutine());
    }

    void OnDisable()
    {
        // Clean up when the object is disabled or destroyed
        StopAllCoroutines();
        if (_heightData.IsCreated) _heightData.Dispose();
        if (_displacementXData.IsCreated) _displacementXData.Dispose();
        if (_displacementZData.IsCreated) _displacementZData.Dispose();
    }

    private IEnumerator GPUReadbackCoroutine()
    {
        while (true) // This loop will run as long as the component is enabled
        {
            yield return new WaitForEndOfFrame(); // Wait until the ocean simulation has finished for the frame

            bool yDone = false, xDone = false, zDone = false;

            // Request all three displacement maps from the GPU
            AsyncGPUReadback.Request(oceanManager.DisplacementMapY, 0, TextureFormat.RFloat, request => {
                if (request.done && !request.hasError) request.GetData<float>().CopyTo(_heightData);
                yDone = true;
            });
            AsyncGPUReadback.Request(oceanManager.DisplacementMapX, 0, TextureFormat.RFloat, request => {
                if (request.done && !request.hasError) request.GetData<float>().CopyTo(_displacementXData);
                xDone = true;
            });
            AsyncGPUReadback.Request(oceanManager.DisplacementMapZ, 0, TextureFormat.RFloat, request => {
                if (request.done && !request.hasError) request.GetData<float>().CopyTo(_displacementZData);
                zDone = true;
            });

            // Wait here until all three requests have completed
            yield return new WaitUntil(() => yDone && xDone && zDone);
            _hasValidData = true; // Signal that we have fresh data for the physics update
        }
    }

    void FixedUpdate()
    {
        // Physics calculations should only happen if we have valid data
        if (!_hasValidData || floatingPoints.Count == 0) return;

        // By dividing the force by the number of points, the total buoyancy remains consistent
        // regardless of how many points you use, making it easier to tune.
        float forcePerPoint = buoyancyForce / floatingPoints.Count;

        foreach (Transform point in floatingPoints)
        {
            Vector3 pointPosition = point.position;

            // Get all water data (height, horizontal displacement) at this point
            Vector3 waterData = GetWaterDataAt(pointPosition.x, pointPosition.z);
            float waterHeight = waterData.x;

            if (pointPosition.y < waterHeight)
            {
                float submergedDepth = waterHeight - pointPosition.y;

                // 1. Apply Buoyant Force (upwards)
                Vector3 buoyantForceVector = Vector3.up * forcePerPoint * submergedDepth;
                _rigidbody.AddForceAtPosition(buoyantForceVector, pointPosition, ForceMode.Force);

                // 2. Apply Water Drag/Drifting Force (horizontal)
                Vector3 waterDisplacement = new Vector3(waterData.y, 0, waterData.z);
                Vector3 dragForce = (waterDisplacement - _rigidbody.GetPointVelocity(pointPosition)) * waterDragForce * submergedDepth;
                _rigidbody.AddForceAtPosition(dragForce, pointPosition, ForceMode.Force);
            }
        }
    }

    private Vector3 GetWaterDataAt(float worldX, float worldZ)
    {
        float u = (worldX - oceanManager.transform.position.x) / oceanManager.L;
        float v = (worldZ - oceanManager.transform.position.z) / oceanManager.L;

        int x = Mathf.Clamp(Mathf.FloorToInt(u * oceanManager.N), 0, oceanManager.N - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(v * oceanManager.N), 0, oceanManager.N - 1);

        int index = y * oceanManager.N + x;

        if (index < 0 || index >= _heightData.Length)
        {
            return new Vector3(oceanManager.transform.position.y, 0, 0); // Safe default
        }

        // Sample the data from our CPU-side arrays
        float height = _heightData[index];
        float displacementX = _displacementXData[index];
        float displacementZ = _displacementZData[index];

        // Combine and scale the final data
        return new Vector3(
            oceanManager.transform.position.y + (height * oceanManager.displacementScale),
            displacementX * oceanManager.displacementScale,
            displacementZ * oceanManager.displacementScale
        );
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Draw gizmo for the center of mass
        if (_rigidbody != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.TransformPoint(_rigidbody.centerOfMass), 0.1f);
        }

        // Draw gizmos for the floating points
        if (floatingPoints.Count == 0) return;
        Gizmos.color = Color.cyan;
        foreach (Transform point in floatingPoints)
        {
            if (point != null)
            {
                Gizmos.DrawSphere(point.position, 0.3f);
            }
        }
    }
#endif
}
