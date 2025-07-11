using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class Floater : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("The main WaterController script that manages the ocean waves.")]
    public WaterController waterController;

    [Header("Buoyancy Settings")]
    [Tooltip("The array of points where buoyancy forces will be applied. Create empty GameObjects as children and place them around the object's hull.")]
    public List<Transform> floatingPoints = new List<Transform>();

    [Tooltip("The upward force multiplier. Higher values make the object float higher or more aggressively.")]
    public float buoyancyForce = 15f;

    [Tooltip("The force multiplier that pushes the object along with the water's horizontal movement.")]
    public float waterDragForce = 1f;

    private Rigidbody _rigidbody;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (waterController == null)
        {
            Debug.LogError("Floater: WaterController not assigned!", this);
            enabled = false;
        }
    }

    void FixedUpdate()
    {
        if (waterController == null) return;

        int submergedPoints = 0;

        // Loop through each floating point to apply forces
        foreach (Transform point in floatingPoints)
        {
            // Get the world position of the floating point
            Vector3 pointPosition = point.position;

            // Get the water height at this point's horizontal location
            float waterHeight = waterController.GetWaterHeightAt(pointPosition.x, pointPosition.z);

            // Check if the point is underwater
            if (pointPosition.y < waterHeight)
            {
                submergedPoints++;
                // Calculate how deep the point is submerged
                float submergedDepth = waterHeight - pointPosition.y;

                // Apply the buoyant force upwards, proportional to the depth
                Vector3 buoyantForceVector = Vector3.up * buoyancyForce * submergedDepth;
                _rigidbody.AddForceAtPosition(buoyantForceVector, pointPosition, ForceMode.Force);

                // --- Apply Water Drag/Drifting Force ---
                // Get the horizontal velocity of the water at this point
                Vector2 waterVelocity2D = waterController.GetWaterHorizontalVelocityAt(pointPosition.x, pointPosition.z);
                Vector3 waterVelocity3D = new Vector3(waterVelocity2D.x, 0, waterVelocity2D.y);

                // Calculate the difference between the object's velocity and the water's velocity
                Vector3 velocityDifference = waterVelocity3D - _rigidbody.GetPointVelocity(pointPosition);

                // Apply a force to make the object match the water's velocity
                // We multiply by the submerged depth factor so it's only applied strongly on submerged parts
                Vector3 dragForce = velocityDifference * waterDragForce * submergedDepth;
                _rigidbody.AddForceAtPosition(dragForce, pointPosition, ForceMode.Force);
            }
        }
    }

    // Draw gizmos in the editor to make setting up the floating points easier
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (floatingPoints.Count == 0) return;

        Gizmos.color = Color.cyan;
        foreach (Transform point in floatingPoints)
        {
            if (point != null)
            {
                Gizmos.DrawSphere(point.position, 0.1f);
            }
        }
    }
#endif
}