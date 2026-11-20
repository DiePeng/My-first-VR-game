using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAssembly
{
    public struct ChairAssemblyAlignment
    {
        public bool FirstFlipped;
        public bool SecondFlipped;
        public float Distance;
        public float Nearest;
        public float Angle;
        public bool HasJoint;

        // These symmetries were checked against the mesh vertices, including the joinery.
        static bool SymmetryCenter(ChairAssemblyGroup group, out float z)
        {
            z = -0.18f;
            if (group.Parts.Count == 1)
            {
                if (group.Parts[0].Name == "Backrest") return true;
                z = -0.005f;
                return group.Parts[0].Name == "SeatSupport";
            }
            if (group.Parts.Count != 3) return false;
            bool left = false, right = false, back = false;
            foreach (var part in group.Parts)
            {
                left |= part.Name == "RearLeg_Left";
                right |= part.Name == "RearLeg_Right";
                back |= part.Name == "Backrest";
            }
            z = -0.18f;
            return left && right && back;
        }

        public static Pose Frame(ChairAssemblyGroup group, bool flipped)
        {
            if (!flipped) return new Pose(group.transform.position, group.transform.rotation);
            SymmetryCenter(group, out float z);
            return new Pose(group.transform.TransformPoint(new Vector3(0f, 0f, 2f * z)),
                group.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
        }

        public static Vector3 Point(ChairAssemblyGroup group, bool flipped, Vector3 anchor)
        {
            var frame = Frame(group, flipped);
            return frame.position + frame.rotation * anchor;
        }

        public static ChairAssemblyAlignment Find(ChairAssemblyGroup first, ChairAssemblyGroup second,
            IReadOnlyList<ChairAssemblyJoint> joints, float distanceLimit, float angleLimit)
        {
            var best = new ChairAssemblyAlignment { Distance = float.PositiveInfinity, Nearest = float.PositiveInfinity };
            float bestScore = float.PositiveInfinity;
            int firstCount = SymmetryCenter(first, out _) ? 2 : 1;
            int secondCount = SymmetryCenter(second, out _) ? 2 : 1;
            for (int a = 0; a < firstCount; a++)
            for (int b = 0; b < secondCount; b++)
            {
                var candidate = new ChairAssemblyAlignment
                {
                    FirstFlipped = a == 1, SecondFlipped = b == 1,
                    Nearest = float.PositiveInfinity,
                    Angle = Quaternion.Angle(Frame(first, a == 1).rotation, Frame(second, b == 1).rotation)
                };
                foreach (var joint in joints)
                {
                    if (!joint.Joins(first, second)) continue;
                    candidate.HasJoint = true;
                    float distance = Vector3.Distance(Point(first, a == 1, joint.Anchor), Point(second, b == 1, joint.Anchor));
                    candidate.Distance = Mathf.Max(candidate.Distance, distance);
                    candidate.Nearest = Mathf.Min(candidate.Nearest, distance);
                }
                if (!candidate.HasJoint) continue;
                bool valid = candidate.Distance <= distanceLimit && candidate.Angle <= angleLimit;
                float score = (valid ? 0f : 10000f) + candidate.Distance / distanceLimit + candidate.Angle / angleLimit;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            return best;
        }

        public static void Reframe(ChairAssemblyGroup group)
        {
            // Preserve every child's world pose, including XRI's dynamic attach transform.
            var children = new List<Transform>();
            var poses = new List<Pose>();
            foreach (Transform child in group.transform)
            {
                children.Add(child);
                poses.Add(new Pose(child.position, child.rotation));
            }
            var frame = Frame(group, true);
            group.transform.SetPositionAndRotation(frame.position, frame.rotation);
            group.Body.position = frame.position;
            group.Body.rotation = frame.rotation;
            for (int i = 0; i < children.Count; i++)
                children[i].SetPositionAndRotation(poses[i].position, poses[i].rotation);
            foreach (var part in group.Parts)
            {
                if (part.Name == "RearLeg_Left") part.Name = "RearLeg_Right";
                else if (part.Name == "RearLeg_Right") part.Name = "RearLeg_Left";
                part.Position = part.Transform.localPosition;
                part.Rotation = part.Transform.localRotation;
            }
        }
    }
}
