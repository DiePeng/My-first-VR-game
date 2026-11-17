using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FurnitureAssembly
{
    [DisallowMultipleComponent]
    public sealed class ChairAssemblyController : MonoBehaviour
    {
        [SerializeField, Min(0.005f)] float snapDistance = 0.05f;
        [SerializeField, Range(1f, 45f)] float snapAngle = 15f;
        [SerializeField, Min(0.01f)] float snapDuration = 0.12f;
        [SerializeField, Min(0.5f)] float scatterRadius = 1.05f;

        readonly List<ChairAssemblyGroup> groups = new List<ChairAssemblyGroup>();
        readonly List<ChairAssemblyJoint> joints = new List<ChairAssemblyJoint>();
        readonly Dictionary<string, ChairAssemblyPart> parts = new Dictionary<string, ChairAssemblyPart>();
        readonly List<ReleaseRequest> releases = new List<ReleaseRequest>();
        readonly List<GameObject> markers = new List<GameObject>();
        Material markerMaterial;
        MaterialPropertyBlock markerProperties;
        AudioClip snapSound;
        AudioClip completionSound;
        bool initialized;
        bool merging;

        public IReadOnlyList<ChairAssemblyGroup> Groups => groups;
        public IReadOnlyList<ChairAssemblyJoint> Joints => joints;
        public bool IsComplete { get; private set; }
        public event Action AssemblyCompleted;

        struct ReleaseRequest
        {
            public ChairAssemblyGroup Group;
            public IXRSelectInteractor Interactor;
            public int Frame;
        }

        void Awake() => InitializeAssembly(true);

        public void InitializeAssembly(bool scatter)
        {
            if (initialized)
                return;

            string[] names = { "Seat", "SeatSupport", "FrontLeg_Left", "FrontLeg_Right",
                "RearLeg_Left", "RearLeg_Right", "Backrest" };
            foreach (var partName in names)
            {
                var child = transform.Find(partName);
                if (child == null)
                    throw new InvalidOperationException($"TargetChair is missing {partName}.");
                parts.Add(partName, new ChairAssemblyPart
                {
                    Name = partName,
                    Transform = child,
                    Position = child.localPosition,
                    Rotation = child.localRotation,
                    Colliders = child.GetComponentsInChildren<Collider>()
                });
            }

            // Measured from the existing mortises, tenons and part transforms.
            // The frame has four leg connections, and four upward seat tenons.
            foreach (float side in new[] { -1f, 1f })
            {
                string suffix = side < 0 ? "Left" : "Right";
                AddJoint("SeatSupport", "FrontLeg_" + suffix, new Vector3(side * 0.161f, 0.43f, 0.17f));
                AddJoint("SeatSupport", "RearLeg_" + suffix, new Vector3(side * 0.161f, 0.43f, -0.18f));
                AddJoint("Seat", "FrontLeg_" + suffix, new Vector3(side * 0.175f, 0.4675f, 0.17f));
                AddJoint("Seat", "SeatSupport", new Vector3(side * 0.1f, 0.4675f, 0.17f));
                AddJoint("Seat", "SeatSupport", new Vector3(side * 0.1f, 0.4675f, -0.18f));
                // Both backrest rails must fit the same rear leg at once.
                AddJoint("Backrest", "RearLeg_" + suffix, new Vector3(side * 0.161f, 0.5825f, -0.18f));
                AddJoint("Backrest", "RearLeg_" + suffix, new Vector3(side * 0.161f, 0.925f, -0.18f));
            }

            for (int i = 0; i < names.Length; i++)
            {
                var part = parts[names[i]];
                var root = new GameObject("Assembly_" + part.Name);
                root.transform.SetParent(transform, false);
                var group = root.AddComponent<ChairAssemblyGroup>();
                group.Initialize(this);
                group.AddPart(part);
                groups.Add(group);
                if (scatter)
                    Scatter(group, i);
                group.RecoveryPosition = root.transform.position;
                group.RecoveryRotation = root.transform.rotation;
                group.RebuildGrab();
            }
            CreateFeedback();
            initialized = true;
        }

        void AddJoint(string first, string second, Vector3 anchor)
        {
            joints.Add(new ChairAssemblyJoint(parts[first], parts[second], anchor));
        }

        void Scatter(ChairAssemblyGroup group, int index)
        {
            // Lay long parts flat, distributed around the original chair footprint.
            float angle = (index - 1) * 60f;
            Vector3 offset = index == 0 ? Vector3.zero :
                Quaternion.Euler(0f, angle, 0f) * Vector3.forward * scatterRadius;
            var part = group.Parts[0];
            Quaternion restingRotation = Quaternion.identity;
            if (part.Name.StartsWith("FrontLeg_", StringComparison.Ordinal) ||
                part.Name.StartsWith("RearLeg_", StringComparison.Ordinal))
                restingRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (part.Name == "Backrest")
                restingRotation = Quaternion.Euler(90f, 0f, 0f);
            Quaternion rotation = transform.rotation * Quaternion.Euler(0f, angle + 15f, 0f) * restingRotation;
            Vector3 center = transform.TransformPoint(offset);
            float floor = transform.position.y;
            var hits = Physics.RaycastAll(center + Vector3.up * 0.2f, Vector3.down, 3f,
                ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.8f || hit.distance >= nearest)
                    continue;
                floor = hit.point.y;
                nearest = hit.distance;
            }
            // Offset by the rotated collision bounds, including the original part pivot.
            group.transform.rotation = rotation;
            group.transform.position = center - rotation * part.Position;
            Physics.SyncTransforms();
            float bottom = float.PositiveInfinity;
            foreach (var collider in part.Colliders)
                bottom = Mathf.Min(bottom, collider.bounds.min.y);
            group.transform.position += Vector3.up * (floor + 0.025f - bottom);
        }

        public bool CanJoin(ChairAssemblyGroup first, ChairAssemblyGroup second,
            out float error)
        {
            error = 0f;
            if (first == null || second == null || first == second ||
                !groups.Contains(first) || !groups.Contains(second) ||
                Quaternion.Angle(first.transform.rotation, second.transform.rotation) > snapAngle)
                return false;

            bool hasConnection = false;
            foreach (var joint in joints)
            {
                if (!joint.Joins(first, second))
                    continue;
                hasConnection = true;
                float distance = Vector3.Distance(joint.PositionOn(first), joint.PositionOn(second));
                error = Mathf.Max(error, distance);
                if (distance > snapDistance)
                    return false;
            }
            return hasConnection;
        }

        public void QueueRelease(ChairAssemblyGroup group, IXRSelectInteractor interactor)
        {
            if (!merging)
                releases.Add(new ReleaseRequest { Group = group, Interactor = interactor, Frame = Time.frameCount });
        }

        void Update()
        {
            if (!initialized || merging)
                return;
            // Process on the next frame, after XRI has finished its drop callbacks.
            for (int i = releases.Count - 1; i >= 0; i--)
            {
                var request = releases[i];
                if (request.Frame >= Time.frameCount)
                    continue;
                releases.RemoveAt(i);
                if (request.Group == null || !groups.Contains(request.Group) || request.Group.Grab.isSelected)
                    continue;
                var target = FindCandidate(request.Group);
                if (target == null)
                    continue;
                StartCoroutine(Snap(request.Group, target, request.Interactor));
                break;
            }
            UpdateMarkers();
            foreach (var group in groups)
            {
                if (!group.Grab.isSelected && group.transform.position.y < transform.position.y - 2f)
                {
                    group.Body.position = group.RecoveryPosition;
                    group.Body.rotation = group.RecoveryRotation;
                    group.Body.linearVelocity = Vector3.zero;
                    group.Body.angularVelocity = Vector3.zero;
                }
            }
        }

        ChairAssemblyGroup FindCandidate(ChairAssemblyGroup moving)
        {
            ChairAssemblyGroup best = null;
            float bestError = float.PositiveInfinity;
            foreach (var target in groups)
            {
                if (CanJoin(moving, target, out float error) && error < bestError)
                {
                    best = target;
                    bestError = error;
                }
            }
            return best;
        }

        IEnumerator Snap(ChairAssemblyGroup moving, ChairAssemblyGroup target, IXRSelectInteractor releasedBy)
        {
            merging = true;
            moving.Grab.enabled = false;
            moving.Body.isKinematic = true;
            Vector3 startPosition = moving.transform.position;
            Quaternion startRotation = moving.transform.rotation;
            float elapsed = 0f;
            while (elapsed < snapDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / snapDuration);
                moving.transform.SetPositionAndRotation(Vector3.Lerp(startPosition, target.transform.position, t),
                    Quaternion.Slerp(startRotation, target.transform.rotation, t));
                yield return null;
            }
            moving.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
            MergeGroups(moving, target);
            PlayConnectionFeedback(target, releasedBy);
            merging = false;
        }

        /// <summary>Called after alignment. Also used by the editor validation harness.</summary>
        public void MergeGroups(ChairAssemblyGroup moving, ChairAssemblyGroup target)
        {
            if (!CanJoin(moving, target, out _))
                throw new InvalidOperationException("Chair groups do not have matching aligned joints.");
            bool previousMerging = merging;
            merging = true;
            IXRSelectInteractor heldBy = target.Grab.isSelected ? target.Grab.interactorsSelecting[0] : null;
            moving.Grab.enabled = false;
            target.Grab.enabled = false;
            target.Body.isKinematic = true;
            moving.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
            foreach (var part in moving.Parts)
                target.AddPart(part);
            moving.Parts.Clear();
            groups.Remove(moving);
            target.RecoveryPosition = target.transform.position + Vector3.up * 0.15f;
            target.RecoveryRotation = target.transform.rotation;
            target.RebuildGrab();
            target.Body.linearVelocity = Vector3.zero;
            target.Body.angularVelocity = Vector3.zero;
            // Preserve the other hand's grip when attaching to a held combination.
            if (heldBy != null && heldBy.transform != null)
                target.Grab.interactionManager.SelectEnter(heldBy, target.Grab);
            if (Application.isPlaying)
                Destroy(moving.gameObject);
            else
                DestroyImmediate(moving.gameObject);
            merging = previousMerging;
            if (groups.Count == 1 && target.Parts.Count == 7 && joints.TrueForAll(joint => joint.Connected))
            {
                IsComplete = true;
                AssemblyCompleted?.Invoke();
            }
        }

        void CreateFeedback()
        {
            snapSound = CreateTone("Chair joint click", 700f, 0.09f);
            completionSound = CreateTone("Chair assembled", 1050f, 0.5f);
            // Reuse a referenced shader so build-time shader stripping cannot remove the hints.
            markerMaterial = new Material(parts["Seat"].Transform.GetComponent<MeshRenderer>().sharedMaterial);
            markerMaterial.EnableKeyword("_EMISSION");
            markerProperties = new MaterialPropertyBlock();
            foreach (var joint in joints)
            {
                for (int i = 0; i < 2; i++)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.name = "JointHint_" + joint.A.Name + "_" + joint.B.Name;
                    marker.transform.SetParent(transform, false);
                    marker.transform.localScale = Vector3.one * 0.018f;
                    var collider = marker.GetComponent<Collider>();
                    collider.enabled = false;
                    if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
                    var renderer = marker.GetComponent<Renderer>();
                    renderer.sharedMaterial = markerMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    marker.SetActive(false);
                    markers.Add(marker);
                }
            }
        }

        void UpdateMarkers()
        {
            for (int i = 0; i < joints.Count; i++)
            {
                var joint = joints[i];
                bool held = joint.A.Group.Grab.isSelected || joint.B.Group.Grab.isSelected;
                bool show = !joint.Connected && held &&
                    Vector3.Distance(joint.PositionOn(joint.A.Group), joint.PositionOn(joint.B.Group)) < 0.3f;
                bool aligned = show && CanJoin(joint.A.Group, joint.B.Group, out _);
                SetMarker(markers[i * 2], show, joint.PositionOn(joint.A.Group), aligned);
                SetMarker(markers[i * 2 + 1], show, joint.PositionOn(joint.B.Group), aligned);
            }
        }

        void SetMarker(GameObject marker, bool show, Vector3 position, bool aligned)
        {
            marker.SetActive(show);
            if (!show) return;
            marker.transform.position = position;
            Color color = aligned ? Color.green : new Color(1f, 0.65f, 0.1f);
            markerProperties.SetColor("_BaseColor", color);
            markerProperties.SetColor("_EmissionColor", color * 0.6f);
            marker.GetComponent<Renderer>().SetPropertyBlock(markerProperties);
        }

        void PlayConnectionFeedback(ChairAssemblyGroup target, IXRSelectInteractor releasedBy)
        {
            AudioSource.PlayClipAtPoint(IsComplete ? completionSound : snapSound,
                target.Body.worldCenterOfMass, 0.35f);
            if (releasedBy != null && releasedBy.transform != null)
            {
                var haptics = releasedBy.transform.GetComponentInParent<HapticImpulsePlayer>();
                if (haptics == null && releasedBy.transform.parent != null)
                    haptics = releasedBy.transform.parent.GetComponentInChildren<HapticImpulsePlayer>();
                if (haptics != null)
                    haptics.SendHapticImpulse(IsComplete ? 0.4f : 0.2f, 0.1f);
            }
            if (IsComplete)
                StartCoroutine(Glow(target));
        }

        IEnumerator Glow(ChairAssemblyGroup group)
        {
            var renderers = group.GetComponentsInChildren<MeshRenderer>();
            var originals = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterials;
                var glowing = renderers[i].materials;
                foreach (var material in glowing)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", new Color(0.15f, 0.8f, 0.3f) * 1.5f);
                }
            }
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var glowing = renderers[i].sharedMaterials;
                renderers[i].sharedMaterials = originals[i];
                foreach (var material in glowing) Destroy(material);
            }
        }

        static AudioClip CreateTone(string clipName, float frequency, float duration)
        {
            const int sampleRate = 22050;
            var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / sampleRate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-8f * t / duration) * 0.5f;
            }
            var clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void OnDestroy()
        {
            if (markerMaterial != null) Destroy(markerMaterial);
            if (snapSound != null) Destroy(snapSound);
            if (completionSound != null) Destroy(completionSound);
        }
    }
}
