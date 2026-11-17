using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FurnitureAssembly.Editor
{
    /// <summary>Runs the actual XRI select/release path over successive Play Mode frames.</summary>
    public static class ChairAssemblyInteractionValidation
    {
        static readonly string[] InstallOrder =
        {
            "Backrest", "RearLeg_Right", "SeatSupport", "FrontLeg_Left", "Seat", "FrontLeg_Right"
        };

        static ChairAssemblyController controller;
        static ChairAssemblyGroup target;
        static XRDirectInteractor left;
        static XRDirectInteractor right;
        static int step;
        static int phase;
        static int assertions;
        static int completionEvents;
        static double nextCheck;
        static Vector3 beforeMovingGrip;

        [MenuItem("Tools/Furniture Assembly/Validate XR Release Assembly (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Enter Play Mode before validating XR assembly.");
            Cleanup();
            assertions = 0;
            completionEvents = 0;
            step = 0;
            phase = 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FurnitureAssembly/TargetChair/TargetChair_7Parts.prefab");
            var instance = UnityEngine.Object.Instantiate(prefab, new Vector3(100f, 3f, 100f), Quaternion.identity);
            instance.name = "XR Assembly Validation";
            controller = instance.GetComponent<ChairAssemblyController>();
            controller.AssemblyCompleted += OnCompleted;
            foreach (var group in controller.Groups)
            {
                group.Body.useGravity = false;
                group.Body.isKinematic = true;
            }
            target = controller.Groups.First(group => group.Parts[0].Name == "RearLeg_Left");
            target.transform.SetPositionAndRotation(new Vector3(100f, 3f, 100f), Quaternion.Euler(0f, 37f, 0f));
            right = CreateInteractor("Validation right grip", target.Parts[0].Transform.position);
            left = CreateInteractor("Validation left grip", target.Parts[0].Transform.position);
            right.StartManualInteraction((IXRSelectInteractable)target.Grab);
            Check(target.Grab.isSelected, "The stationary combination must be held.");
            nextCheck = EditorApplication.timeSinceStartup + 0.1;
            EditorApplication.update += Tick;
        }

        static XRDirectInteractor CreateInteractor(string name, Vector3 position)
        {
            var hand = new GameObject(name);
            hand.transform.position = position;
            var trigger = hand.AddComponent<SphereCollider>();
            trigger.radius = 0.025f;
            trigger.isTrigger = true;
            return hand.AddComponent<XRDirectInteractor>();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || controller == null)
            {
                Cleanup();
                return;
            }
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            try
            {
                switch (phase)
                {
                    case 0:
                        HoldNextPart(step == 0);
                        phase = step == 0 ? 1 : 3;
                        break;
                    case 1:
                        // First attempt is deliberately rotated beyond the allowed angle.
                        left.EndManualInteraction();
                        phase = 2;
                        break;
                    case 2:
                        Check(controller.Groups.Count == 7, "An incorrectly rotated release must not merge.");
                        HoldNextPart(false);
                        phase = 3;
                        break;
                    case 3:
                        Check(controller.GetComponentsInChildren<MeshRenderer>().Any(renderer =>
                            renderer.name.StartsWith("JointHint_", StringComparison.Ordinal) && renderer.gameObject.activeSelf),
                            "Matching interfaces should show visual hints while held.");
                        left.EndManualInteraction();
                        phase = 4;
                        break;
                    case 4:
                        Check(controller.Groups.Count == 6 - step, "A real XRI release must merge exactly two groups.");
                        Check(target.Parts.Count == step + 2, "All previously installed members must remain attached.");
                        Check(target.Grab.isSelected && right.hasSelection, "The other hand must retain its grip.");
                        Check(target.GetComponentsInChildren<Rigidbody>().Length == 1, "The combination has one body.");
                        Check(target.GetComponentsInChildren<XRGrabInteractable>().Length == 1, "The combination has one grab target.");
                        if (step == InstallOrder.Length - 1)
                        {
                            Check(controller.IsComplete && controller.Joints.All(joint => joint.Connected), "All joints complete.");
                            Check(completionEvents == 1, "Completion fires once.");
                            Check(target.GetComponentsInChildren<MeshRenderer>().All(renderer =>
                                renderer.sharedMaterial.IsKeywordEnabled("_EMISSION")), "The complete chair lights up.");
                            Check(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                                .Any(source => source.clip != null && source.clip.name == "Chair assembled"),
                                "Completion sound is played.");
                            beforeMovingGrip = target.transform.position;
                            right.transform.position += new Vector3(0.1f, 0.04f, 0f);
                            phase = 5;
                        }
                        else
                        {
                            step++;
                            phase = 0;
                        }
                        break;
                    case 5:
                        Check(Vector3.Distance(beforeMovingGrip, target.transform.position) > 0.05f,
                            "Moving the controller must move the whole completed chair.");
                        Debug.Log($"XR chair assembly validation passed: {assertions} assertions, six release snaps, " +
                            "invalid-angle rejection, retained grip, completion feedback and whole-chair movement.");
                        Cleanup();
                        return;
                }
                nextCheck = EditorApplication.timeSinceStartup + (phase == 4 ? 0.25 : 0.1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Cleanup();
            }
        }

        static void HoldNextPart(bool wrongAngle)
        {
            var moving = controller.Groups.First(group => group.Parts.Count == 1 &&
                group.Parts[0].Name == InstallOrder[step]);
            moving.transform.SetPositionAndRotation(target.transform.position + Vector3.right * 0.015f,
                target.transform.rotation * Quaternion.Euler(0f, wrongAngle ? 35f : 0f, 0f));
            if (!moving.Body.isKinematic)
            {
                moving.Body.linearVelocity = Vector3.zero;
                moving.Body.angularVelocity = Vector3.zero;
                moving.Body.isKinematic = true;
            }
            left.transform.SetPositionAndRotation(moving.Parts[0].Transform.position, moving.transform.rotation);
            left.StartManualInteraction((IXRSelectInteractable)moving.Grab);
            Check(moving.Grab.isSelected, "The moving part must be held before release.");
        }

        static void OnCompleted() => completionEvents++;

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            if (controller != null)
            {
                controller.enabled = false;
                controller.AssemblyCompleted -= OnCompleted;
                // Grabbed roots can be temporarily detached by XRI, so clean up every owned group.
                foreach (var group in controller.Groups.ToArray())
                {
                    group.Grab.enabled = false;
                    UnityEngine.Object.Destroy(group.gameObject);
                }
                UnityEngine.Object.Destroy(controller.gameObject);
            }
            if (left != null) UnityEngine.Object.Destroy(left.gameObject);
            if (right != null) UnityEngine.Object.Destroy(right.gameObject);
            controller = null;
            target = null;
            left = null;
            right = null;
        }
    }
}
