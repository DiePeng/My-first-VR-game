using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FurnitureAssembly.Editor
{
    public static class ChairAssemblySymmetryValidation
    {
        [MenuItem("Tools/Furniture Assembly/Validate Symmetric Connections (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FurnitureAssembly/TargetChair/TargetChair_7Parts.prefab");
            int checks = 0;
            for (int scenario = 0; scenario < 8; scenario++)
            {
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var controller = instance.GetComponent<ChairAssemblyController>();
                    foreach (var group in controller.Groups)
                    {
                        group.Body.isKinematic = true;
                        group.Body.useGravity = false;
                        group.Body.interpolation = RigidbodyInterpolation.None;
                        group.transform.SetPositionAndRotation(new Vector3(100f, 10f, 100f), Quaternion.identity);
                    }
                    Func<string, ChairAssemblyGroup> find = name => controller.Groups.First(group =>
                        group.Parts.Any(part => part.Name == name));
                    var front = find("FrontLeg_Left");
                    var savedPose = new Pose(front.transform.position, front.transform.rotation);
                    front.transform.RotateAround(front.transform.TransformPoint(front.Parts[0].Position),
                        Vector3.up, 180f);
                    float rejectedError;
                    Check(!controller.CanJoin(front, find("Seat"), out rejectedError),
                        "An asymmetric individual leg must not gain a false 180-degree connection.");
                    front.transform.SetPositionAndRotation(savedPose.position, savedPose.rotation);
                    var support = find("SeatSupport");
                    savedPose = new Pose(support.transform.position, support.transform.rotation);
                    support.transform.RotateAround(support.transform.TransformPoint(support.Parts[0].Position),
                        Vector3.right, 180f);
                    Check(!controller.CanJoin(support, find("Seat"), out rejectedError),
                        "Upside-down support tenons must not be treated as equivalent.");
                    support.transform.SetPositionAndRotation(savedPose.position, savedPose.rotation);
                    checks += 2;
                    ChairAssemblyGroup moving;
                    ChairAssemblyGroup target;
                    float centerZ;
                    switch (scenario % 4)
                    {
                        case 0:
                            moving = find("Backrest");
                            target = find("RearLeg_Left");
                            centerZ = -0.18f;
                            break;
                        case 1:
                            moving = find("SeatSupport");
                            target = find("Seat");
                            centerZ = -0.005f;
                            break;
                        default:
                            target = find("Seat");
                            controller.MergeGroups(find("SeatSupport"), target);
                            moving = find("RearLeg_Right");
                            controller.MergeGroups(find("Backrest"), moving);
                            controller.MergeGroups(find("RearLeg_Left"), moving);
                            moving.Body.isKinematic = true;
                            target.Body.isKinematic = true;
                            centerZ = -0.18f;
                            break;
                    }
                    moving.transform.RotateAround(moving.transform.TransformPoint(new Vector3(0f, 0f, centerZ)),
                        moving.transform.up, 180f);
                    float error;
                    Check(controller.CanJoin(moving, target, out error), "Symmetric pose should connect.");
                    checks++;
                    var geometry = Geometry(moving);
                    geometry.AddRange(Geometry(target));
                    if (scenario >= 4)
                    {
                        var swap = moving;
                        moving = target;
                        target = swap;
                    }
                    controller.MergeGroups(moving, target);
                    target.Body.isKinematic = true;
                    var after = Geometry(target);
                    Check(geometry.All(point => after.Any(other => Vector3.Distance(point, other) < 0.0002f)) &&
                        after.All(point => geometry.Any(other => Vector3.Distance(point, other) < 0.0002f)), "Snapping must preserve the symmetric mesh placement.");
                    Check(target.GetComponentsInChildren<Rigidbody>().Length == 1, "Merged physics body.");
                    Check(controller.Groups.SelectMany(group => group.Parts).Select(part => part.Name).Distinct().Count() == 7,
                        "Semantic slots must remain unique after swapping left and right.");
                    checks += 3;
                    // Finish the chair to verify future interfaces after the equivalent pose is accepted.
                    while (controller.Groups.Count > 1)
                    {
                        foreach (var group in controller.Groups)
                            group.transform.SetPositionAndRotation(new Vector3(100f, 10f, 100f), Quaternion.identity);
                        var joint = controller.Joints.First(item => !item.Connected);
                        var destination = joint.B.Group;
                        controller.MergeGroups(joint.A.Group, destination);
                        destination.Body.isKinematic = true;
                    }
                    Check(controller.IsComplete, "The symmetrically connected chair must remain completable.");
                    checks++;
                }
                finally
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }
            Debug.Log($"Chair symmetry validation passed: {checks} checks, eight connections in both merge directions.");
        }

        static List<Vector3> Geometry(ChairAssemblyGroup group)
        {
            var points = new List<Vector3>();
            foreach (var part in group.Parts)
                foreach (var vertex in part.Transform.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Vector3 world = part.Transform.TransformPoint(vertex);
                    points.Add(world);
                }
            return points;
        }

        static void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
