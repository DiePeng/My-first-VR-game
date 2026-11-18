namespace FurnitureAssembly.Editor
{
    public static class ChairAssemblyValidation
    {
        public static void ValidateAuthoredLayout()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
                "Assets/FurnitureAssembly/TargetChair/TargetChair_7Parts.prefab");
            var parent = new UnityEngine.GameObject("Authored layout validation");
            parent.SetActive(false);
            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab, parent.transform);
                var groups = instance.GetComponentsInChildren<ChairAssemblyGroup>(true);
                if (groups.Length != 7)
                    throw new System.Exception("The prefab must contain seven authored groups.");
                var positions = new UnityEngine.Vector3[groups.Length];
                var rotations = new UnityEngine.Quaternion[groups.Length];
                for (int i = 0; i < groups.Length; i++)
                {
                    groups[i].transform.localPosition += new UnityEngine.Vector3(i * 0.13f, 0.4f, 0.2f);
                    groups[i].transform.localRotation *= UnityEngine.Quaternion.Euler(13f, 27f, 19f);
                    positions[i] = groups[i].transform.localPosition;
                    rotations[i] = groups[i].transform.localRotation;
                    groups[i].GetComponent<UnityEngine.Rigidbody>().isKinematic = true;
                    if (groups[i].GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() == null)
                        throw new System.Exception("Every authored group must already have its grab component.");
                }
                parent.SetActive(true);
                var controller = instance.GetComponent<ChairAssemblyController>();
                if (controller.Groups.Count != 7 || instance.GetComponentsInChildren<UnityEngine.Rigidbody>().Length != 7)
                    throw new System.Exception("Startup must reuse authored groups and bodies.");
                for (int i = 0; i < groups.Length; i++)
                {
                    if (UnityEngine.Vector3.Distance(positions[i], groups[i].transform.localPosition) > 0.0001f ||
                        UnityEngine.Quaternion.Angle(rotations[i], groups[i].transform.localRotation) > 0.001f)
                        throw new System.Exception("Startup changed a manually authored part pose.");
                }
                UnityEngine.Debug.Log("Authored chair layout validation passed: seven preconfigured grab groups and all manual poses preserved at startup.");
            }
            finally
            {
                UnityEngine.Object.Destroy(parent);
            }
        }

        [UnityEditor.MenuItem("Tools/Furniture Assembly/Validate Free Assembly (Play Mode)")]
        public static void ValidateFreeAssembly()
        {
            if (!UnityEditor.EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Enter Play Mode before validating XR assembly.");
            
            ValidateAuthoredLayout();
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/FurnitureAssembly/TargetChair/TargetChair_7Parts.prefab");
            int assertions = 0;
            System.Action<bool,string> check = (ok,message) => { if (!ok) throw new System.Exception(message); assertions++; };
            var random = new System.Random(7127);
            var firstPairs = new System.Collections.Generic.HashSet<string>();
            for (int trial=0; trial<32; trial++) {
             var go = UnityEngine.Object.Instantiate(prefab, new UnityEngine.Vector3(100+trial*5,10,100), UnityEngine.Quaternion.identity);
             var c = go.GetComponent<FurnitureAssembly.ChairAssemblyController>();
             c.enabled=false;
             check(c.Groups.Count==7 && c.Joints.Count==14,"Seven parts and fourteen interfaces");
             var p = new UnityEngine.Vector3(100+trial*5,10,100);
             var q = UnityEngine.Quaternion.Euler(trial*7,trial*13,trial*3);
             foreach(var g in c.Groups) { g.Body.isKinematic=true; g.transform.SetPositionAndRotation(p,q); }
             var back = System.Linq.Enumerable.First(c.Groups,g=>g.Parts[0].Name=="Backrest");
             var seat = System.Linq.Enumerable.First(c.Groups,g=>g.Parts[0].Name=="Seat");
             var rear = System.Linq.Enumerable.First(c.Groups,g=>g.Parts[0].Name=="RearLeg_Left");
             float err;
             check(!c.CanJoin(back,back,out err),"Reject self");
             check(!c.CanJoin(back,seat,out err),"Reject unmatched interfaces");
             check(c.CanJoin(back,rear,out err),"Backrest can be first");
             back.transform.rotation=q*UnityEngine.Quaternion.Euler(0,30,0);
             check(!c.CanJoin(back,rear,out err),"Reject excessive angle");
             back.transform.SetPositionAndRotation(p+UnityEngine.Vector3.right*0.08f,q);
             check(!c.CanJoin(back,rear,out err),"Reject excessive distance");
             back.transform.SetPositionAndRotation(p,q);
             back.transform.RotateAround(back.transform.TransformPoint(new UnityEngine.Vector3(-0.161f,0.5825f,-0.18f)),back.transform.forward,10f);
             check(!c.CanJoin(back,rear,out err),"Both backrest rails must fit at once");
             back.transform.SetPositionAndRotation(p,q);
             int expected=7;
             while(c.Groups.Count>1) {
              var candidates = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(c.Joints,j=>!j.Connected));
              var edge=candidates[random.Next(candidates.Count)];
              var moving = random.Next(2)==0 ? edge.A.Group : edge.B.Group;
              var target = moving==edge.A.Group ? edge.B.Group : edge.A.Group;
              if(expected==7) firstPairs.Add(edge.A.Name+"+"+edge.B.Name);
              check(c.CanJoin(moving,target,out err),"Eligible clusters");
              int members=moving.Parts.Count+target.Parts.Count;
              c.MergeGroups(moving,target);
              target.Body.isKinematic=true;
              expected--;
              check(c.Groups.Count==expected && target.Parts.Count==members,"Union membership");
              check(target.GetComponentsInChildren<UnityEngine.Rigidbody>().Length==1,"One rigidbody per cluster");
              check(target.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>().Length==1,"One grab per cluster");
              int colliderCount=0;
              foreach(var part in target.Parts) {
               colliderCount+=part.Colliders.Length;
               check(part.Group==target && part.Transform.parent==target.transform,"Member ownership");
               check(UnityEngine.Vector3.Distance(part.Transform.localPosition,part.Position)<0.0001f,"Exact part pose");
               foreach(var col in part.Colliders) {
                UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable registered;
                check(target.Grab.interactionManager.TryGetInteractableForCollider(col,out registered) && object.ReferenceEquals(registered,target.Grab),"Grab any member collider");
               }
              }
              check(target.Grab.colliders.Count==colliderCount,"Complete collider union");
             }
             check(c.IsComplete && c.Groups[0].Parts.Count==7 && System.Linq.Enumerable.All(c.Joints,j=>j.Connected),"Completion requires all interfaces");
             UnityEngine.Object.Destroy(go);
            }
            UnityEngine.Debug.Log($"Chair assembly validation passed: {assertions} assertions, 32 orders, {firstPairs.Count} distinct first connections.");
        }
    }
}
