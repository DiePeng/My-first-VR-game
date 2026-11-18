using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace FurnitureAssembly
{
    /// <summary>A connected set of chair parts with one physics body and one grab target.</summary>
    public sealed class ChairAssemblyGroup : MonoBehaviour
    {
        [SerializeField] List<ChairAssemblyPart> parts = new List<ChairAssemblyPart>();
        public List<ChairAssemblyPart> Parts => parts;
        public Rigidbody Body { get; private set; }
        public XRGrabInteractable Grab { get; private set; }
        public ChairAssemblyController Owner { get; private set; }
        public Vector3 RecoveryPosition { get; set; }
        public Quaternion RecoveryRotation { get; set; }

        public void Bind(ChairAssemblyController owner)
        {
            Owner = owner;
            Body = GetComponent<Rigidbody>();
            Grab = GetComponent<XRGrabInteractable>();
            if (Body == null || Grab == null)
                throw new System.InvalidOperationException($"{name} needs authored Rigidbody and XRGrabInteractable components.");
            Grab.selectExited.RemoveListener(OnReleased);
            Grab.selectExited.AddListener(OnReleased);
        }

#if UNITY_EDITOR
        public void Initialize(ChairAssemblyController owner)
        {
            Owner = owner;
            Body = gameObject.AddComponent<Rigidbody>();
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.linearDamping = 0.1f;
            Body.angularDamping = 0.5f;
            Grab = gameObject.AddComponent<XRGrabInteractable>();
            Grab.enabled = false;
            Grab.useDynamicAttach = true;
            Grab.matchAttachPosition = true;
            Grab.matchAttachRotation = true;
            Grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            Grab.throwOnDetach = false;
            Grab.selectExited.AddListener(OnReleased);
        }

#endif

        public void AddPart(ChairAssemblyPart part)
        {
            Parts.Add(part);
            part.Group = this;
            part.Transform.SetParent(transform, false);
            part.Transform.SetLocalPositionAndRotation(part.Position, part.Rotation);
        }

        public void RebuildGrab()
        {
            // Re-register the collider ownership map after merging two interactables.
            Grab.colliders.Clear();
            foreach (var part in Parts)
                Grab.colliders.AddRange(part.Colliders);
            Body.mass = Parts.Count * 0.6f;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
            Grab.enabled = true;
            Body.isKinematic = false;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (!args.isCanceled && !Grab.isSelected)
                Owner.QueueRelease(this, args.interactorObject);
        }
    }

    [System.Serializable]
    public sealed class ChairAssemblyPart
    {
        public string Name;
        public Transform Transform;
        public Vector3 Position;
        public Quaternion Rotation;
        public Collider[] Colliders;
        public ChairAssemblyGroup Group;
    }
}
