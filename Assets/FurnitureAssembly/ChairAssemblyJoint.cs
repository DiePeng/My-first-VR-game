using UnityEngine;

namespace FurnitureAssembly
{
    /// <summary>One physical mating location, expressed in the assembled chair's local frame.</summary>
    public sealed class ChairAssemblyJoint
    {
        public ChairAssemblyPart A { get; }
        public ChairAssemblyPart B { get; }
        public Vector3 Anchor { get; }
        public bool Connected => A.Group == B.Group;

        public ChairAssemblyJoint(ChairAssemblyPart a, ChairAssemblyPart b, Vector3 anchor)
        {
            A = a;
            B = b;
            Anchor = anchor;
        }

        public Vector3 PositionOn(ChairAssemblyGroup group) => group.transform.TransformPoint(Anchor);

        public bool Joins(ChairAssemblyGroup first, ChairAssemblyGroup second)
        {
            return (A.Group == first && B.Group == second) ||
                   (A.Group == second && B.Group == first);
        }
    }
}
