using System;
using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(Rigidbody))]
public class PrometeoCarController : MonoBehaviour
{
    // ====== CRUISE & BOOST ======
    [Header("Cruise & Boost")]
    [Tooltip("Крейсерская скорость вперёд (км/ч) при зажатой W.")]
    [Range(5, 300)] public float cruiseSpeedKmh = 80f;

    [Tooltip("Крейсерская скорость назад (км/ч) при зажатой S.")]
    [Range(5, 120)] public float reverseCruiseSpeedKmh = 30f;

    [Tooltip("Множитель скорости во время буста (Shift).")]
    [Range(1.05f, 2.5f)] public float boostMultiplier = 2.35f;

    [Tooltip("Длительность буста (секунды).")]
    [Range(0.2f, 10f)] public float boostDuration = 2.5f;

    [Tooltip("Макс. заряды буста. 0 = бесконечно (работает кулдаун).")]
    public int maxBoostCharges = 8;

    [Tooltip("Кулдаун между бустами при бесконечных зарядах.")]
    public float boostCooldown = 3f;

    // ====== STEERING / HANDLING ======
    [Header("Steering & Handling")]
    [Range(5, 45)] public int maxSteeringAngle = 27;
    [Range(0.1f, 1f)] public float steeringSpeed = 0.5f;
    [Tooltip("Ослабление руля на высокой скорости (0..1).")]
    [Range(0.2f, 1f)] public float highSpeedTurnDamping = 0.6f;

    [Tooltip("Жёсткость боковой фрикции на низкой скорости.")]
    [Range(0.5f, 2f)] public float lateralFrictionBase = 1.0f;

    [Tooltip("Жёсткость боковой фрикции на высокой скорости.")]
    [Range(0.2f, 1.5f)] public float lateralFrictionAtSpeed = 0.8f;

    [Tooltip("«Сила двигателя» для удержания целевой скорости.")]
    public float motorForce = 1500f;

    [Tooltip("Базовый тормоз при превышении цели.")]
    public float brakeForce = 150f;

    [Tooltip("Анти-крен (0 = выкл).")]
    public float antiRoll = 4000f;

    [Header("Mass & COM")]
    public Vector3 bodyMassCenter = new Vector3(0, -0.3f, 0);

    // ====== WHEELS ======
    [Header("Wheels (Meshes & Colliders)")]
    public GameObject frontLeftMesh; public WheelCollider frontLeftCollider;
    public GameObject frontRightMesh; public WheelCollider frontRightCollider;
    public GameObject rearLeftMesh; public WheelCollider rearLeftCollider;
    public GameObject rearRightMesh; public WheelCollider rearRightCollider;

    // ====== EFFECTS / UI / SOUNDS ======
    [Header("VFX & UI (Optional)")]
    public bool useEffects = true;
    [Tooltip("Частицы выхлопа слева (включаются только на бусте).")]
    public ParticleSystem RLWParticleSystem;
    [Tooltip("Частицы выхлопа справа (включаются только на бусте).")]
    public ParticleSystem RRWParticleSystem;
    public TrailRenderer RLWTireSkid;
    public TrailRenderer RRWTireSkid;

    [Header("UI (TMP)")]
    public bool useUI = false;
    public TMP_Text carSpeedTextTMP;
    public TMP_Text boostStatusTextTMP;

    [Header("Sounds")]
    public bool useSounds = false;
    public AudioSource carEngineSound;
    public AudioSource tireScreechSound;
    float initialCarEngineSoundPitch;

    // ====== RUNTIME ======
    Rigidbody carRigidbody;

    float targetSpeedKmh = 0f;
    float forwardSpeedKmh = 0f;
    float speedKmh = 0f;
    float steeringAxis = 0f;

    // boost (Shift)
    bool boosting = false;
    int charges = 0;
    float lastBoostTime = -999f;

    // pickup boost (bonus)
    float pickupBoostTimer = 0f;
    float pickupBoostMultiplier = 1f;

    void Start()
    {
        carRigidbody = GetComponent<Rigidbody>();
        carRigidbody.centerOfMass = bodyMassCenter;
        targetSpeedKmh = 0f;

        charges = (maxBoostCharges > 0) ? maxBoostCharges : int.MaxValue;

        if (carEngineSound != null)
            initialCarEngineSoundPitch = carEngineSound.pitch;

        SetBoostVfx(false);

        if (useUI) InvokeRepeating(nameof(CarSpeedUI), 0f, 0.1f);
        if (useSounds) InvokeRepeating(nameof(CarSounds), 0f, 0.1f);
    }

    void Update()
    {
        // руление A/D
        float steerInput = 0f;
        if (Input.GetKey(KeyCode.A)) steerInput -= 1f;
        if (Input.GetKey(KeyCode.D)) steerInput += 1f;
        steeringAxis = Mathf.MoveTowards(steeringAxis, steerInput, Time.deltaTime * 10f * steeringSpeed);

        // скорость вперёд/назад
        forwardSpeedKmh = transform.InverseTransformDirection(carRigidbody.velocity).z * 3.6f;
        speedKmh = Mathf.Abs(forwardSpeedKmh);

        // движение
        bool forwardPressed = Input.GetKey(KeyCode.W);
        bool reversePressed = Input.GetKey(KeyCode.S);

        if (forwardPressed)
            targetSpeedKmh = cruiseSpeedKmh;
        else if (reversePressed)
            targetSpeedKmh = -reverseCruiseSpeedKmh;
        else
            targetSpeedKmh = 0f;

        // Shift = boost
        if (forwardPressed && Input.GetKeyDown(KeyCode.LeftShift))
            TryBoost();

        if (!forwardPressed && boosting)
        {
            SetBoostVfx(false);
            boosting = false;
        }

        // таймер бонусного буста
        if (pickupBoostTimer > 0f)
        {
            pickupBoostTimer -= Time.deltaTime;
            if (pickupBoostTimer <= 0f)
            {
                pickupBoostTimer = 0f;
                pickupBoostMultiplier = 1f;
                if (!boosting) SetBoostVfx(false);
            }
        }
    }

    void FixedUpdate()
    {
        float targetAbs = Mathf.Max(5f, Mathf.Abs(targetSpeedKmh));
        float speedAbs = Mathf.Abs(forwardSpeedKmh);
        float speedFactor = Mathf.InverseLerp(0f, targetAbs, speedAbs);

        float steer = maxSteeringAngle * steeringAxis * Mathf.Lerp(1f, highSpeedTurnDamping, speedFactor);
        frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steer, steeringSpeed);
        frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steer, steeringSpeed);

        float speedError = targetSpeedKmh - forwardSpeedKmh;
        float torque = motorForce * (speedError / Mathf.Max(10f, targetAbs));

        float slipDamp = Mathf.Lerp(1f, 0.7f, Mathf.Abs(steeringAxis) * speedFactor);
        torque *= slipDamp;

        float activeMul = 1f;
        if (targetSpeedKmh > 0f)
        {
            if (boosting) activeMul = Mathf.Max(activeMul, boostMultiplier);
            if (pickupBoostTimer > 0f) activeMul = Mathf.Max(activeMul, pickupBoostMultiplier);
        }

        if (activeMul > 1f && targetSpeedKmh > 0f)
        {
            float boostedTarget = cruiseSpeedKmh * activeMul;
            float boostedError = boostedTarget - forwardSpeedKmh;
            torque = motorForce * (boostedError / Mathf.Max(10f, boostedTarget));
            torque *= slipDamp;
        }

        ApplyMotorTorque(torque);
        ApplyBrake(speedError < -1f ? brakeForce : 0f);

        float lateralStiff = Mathf.Lerp(lateralFrictionBase, lateralFrictionAtSpeed,
                                        Mathf.Clamp01(speedFactor + Mathf.Abs(steeringAxis) * 0.35f));
        SetSidewaysStiffness(lateralStiff);

        if (antiRoll > 0f) ApplyAntiRoll(frontLeftCollider, frontRightCollider);
    }

    // ====== BOOST SHIFT ======
    void TryBoost()
    {
        if (boosting) return;

        if (maxBoostCharges > 0)
        {
            if (charges <= 0) return;
            charges--;
            StartCoroutine(BoostRoutine());
        }
        else
        {
            if (Time.time - lastBoostTime < boostCooldown) return;
            StartCoroutine(BoostRoutine());
        }
    }

    IEnumerator BoostRoutine()
    {
        boosting = true;
        lastBoostTime = Time.time;
        SetBoostVfx(true);

        float t = 0f;
        while (t < boostDuration && boosting)
        {
            t += Time.deltaTime;
            yield return null;
        }

        SetBoostVfx(false);
        boosting = false;
    }

    // ====== PICKUP BOOST ======
    public void PickupBoost(float multiplier, float duration)
    {
        pickupBoostMultiplier = Mathf.Max(pickupBoostMultiplier, multiplier);
        pickupBoostTimer = Mathf.Max(pickupBoostTimer, duration);
        SetBoostVfx(true);
    }

    // ====== HELPERS ======
    void SetBoostVfx(bool on)
    {
        if (!useEffects) return;
        if (RLWParticleSystem) { if (on) RLWParticleSystem.Play(); else RLWParticleSystem.Stop(); }
        if (RRWParticleSystem) { if (on) RRWParticleSystem.Play(); else RRWParticleSystem.Stop(); }
        if (RLWTireSkid) RLWTireSkid.emitting = on;
        if (RRWTireSkid) RRWTireSkid.emitting = on;
    }

    void ApplyMotorTorque(float torquePerWheel)
    {
        frontLeftCollider.motorTorque = torquePerWheel;
        frontRightCollider.motorTorque = torquePerWheel;
        rearLeftCollider.motorTorque = torquePerWheel;
        rearRightCollider.motorTorque = torquePerWheel;
    }

    void ApplyBrake(float brake)
    {
        frontLeftCollider.brakeTorque = brake;
        frontRightCollider.brakeTorque = brake;
        rearLeftCollider.brakeTorque = brake;
        rearRightCollider.brakeTorque = brake;
    }

    void SetSidewaysStiffness(float stiffness)
    {
        var fl = frontLeftCollider.sidewaysFriction; fl.stiffness = stiffness; frontLeftCollider.sidewaysFriction = fl;
        var fr = frontRightCollider.sidewaysFriction; fr.stiffness = stiffness; frontRightCollider.sidewaysFriction = fr;
        var rl = rearLeftCollider.sidewaysFriction; rl.stiffness = stiffness; rearLeftCollider.sidewaysFriction = rl;
        var rr = rearRightCollider.sidewaysFriction; rr.stiffness = stiffness; rearRightCollider.sidewaysFriction = rr;
    }

    void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        WheelHit hit;
        float travelL = 1.0f, travelR = 1.0f;
        bool groundedL = left.GetGroundHit(out hit);
        if (groundedL) travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;
        bool groundedR = right.GetGroundHit(out hit);
        if (groundedR) travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;

        float antiRollForce = (travelL - travelR) * antiRoll;
        if (groundedL) carRigidbody.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
        if (groundedR) carRigidbody.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
    }

    void LateUpdate()
    {
        SyncWheel(frontLeftCollider, frontLeftMesh);
        SyncWheel(frontRightCollider, frontRightMesh);
        SyncWheel(rearLeftCollider, rearLeftMesh);
        SyncWheel(rearRightCollider, rearRightMesh);
    }

    void SyncWheel(WheelCollider col, GameObject mesh)
    {
        if (!col || !mesh) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.transform.SetPositionAndRotation(pos, rot);
    }

    // ====== UI / SOUNDS ======
    public void CarSpeedUI()
    {
        if (!useUI) return;

        if (carSpeedTextTMP)
            carSpeedTextTMP.text = $"{Mathf.RoundToInt(speedKmh)} км/ч";

        if (boostStatusTextTMP)
        {
            if (boosting)
            {
                boostStatusTextTMP.text = "<color=#00FFFF>BOOST!</color>";
            }
            else if (maxBoostCharges > 0)
            {
                boostStatusTextTMP.text = $"BOOST: {charges}/{maxBoostCharges}";
            }
            else
            {
                float elapsed = Time.time - lastBoostTime;
                if (elapsed >= boostCooldown)
                    boostStatusTextTMP.text = "<color=lime>Boost Ready</color>";
                else
                    boostStatusTextTMP.text = $"<color=orange>Cooldown: {(boostCooldown - elapsed):0.0}s</color>";
            }
        }
    }

    public void CarSounds()
    {
        if (!useSounds)
        {
            if (carEngineSound != null && carEngineSound.isPlaying) carEngineSound.Stop();
            if (tireScreechSound != null && tireScreechSound.isPlaying) tireScreechSound.Stop();
            return;
        }

        try
        {
            if (carEngineSound != null)
            {
                float engineSoundPitch = initialCarEngineSoundPitch + (Mathf.Abs(carRigidbody.velocity.magnitude) / 25f);
                carEngineSound.pitch = engineSoundPitch;
                if (!carEngineSound.isPlaying) carEngineSound.Play();
            }

            if (tireScreechSound != null)
            {
                if ((boosting || pickupBoostTimer > 0f) && speedKmh > 12f)
                {
                    if (!tireScreechSound.isPlaying) tireScreechSound.Play();
                }
                else if (tireScreechSound.isPlaying)
                {
                    tireScreechSound.Stop();
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning(ex); }
    }
}
