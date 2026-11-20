using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAssembly
{
    public sealed class ChairAssemblyGuide : MonoBehaviour
    {
        [SerializeField] ChairAssemblyController assembly;
        [SerializeField] GameObject instructions;
        [SerializeField] GameObject completion;
        [SerializeField] TMP_Text progressLabel;
        [SerializeField] Image progressFill;
        TMP_Text alignmentText;
        string defaultAlignmentText;
        float nextHintRefresh;

        public void Configure(ChairAssemblyController controller, GameObject instructionContent,
            GameObject completionContent, TMP_Text label, Image fill)
        {
            if (isActiveAndEnabled && assembly != null)
                assembly.AssemblyChanged -= Refresh;
            assembly = controller;
            instructions = instructionContent;
            completion = completionContent;
            progressLabel = label;
            progressFill = fill;
            if (isActiveAndEnabled && assembly != null)
                assembly.AssemblyChanged += Refresh;
            Refresh();
        }

        void OnEnable()
        {
            if (assembly != null)
                assembly.AssemblyChanged += Refresh;
            Refresh();
        }

        void Start() => Refresh();

        void Update()
        {
            if (Time.unscaledTime < nextHintRefresh || assembly == null || assembly.IsComplete)
                return;
            nextHintRefresh = Time.unscaledTime + 0.1f;
            if (alignmentText == null && instructions != null)
            {
                foreach (var text in instructions.GetComponentsInChildren<TMP_Text>(true))
                    if (text.name == "ALIGN & RELEASE description")
                    {
                        alignmentText = text;
                        defaultAlignmentText = text.text;
                        break;
                    }
            }
            if (alignmentText != null)
                alignmentText.text = assembly.AlignmentHint() ?? defaultAlignmentText;
        }

        void OnDisable()
        {
            if (assembly != null)
                assembly.AssemblyChanged -= Refresh;
        }

        public void Refresh()
        {
            bool complete = assembly != null && assembly.IsComplete;
            int connections = assembly == null || assembly.Groups.Count == 0 ? 0 :
                Mathf.Clamp(7 - assembly.Groups.Count, 0, 6);
            if (instructions != null) instructions.SetActive(!complete);
            if (completion != null) completion.SetActive(complete);
            if (progressLabel != null)
                progressLabel.text = complete ? "ALL 7 PIECES CONNECTED" : $"CONNECTIONS  {connections} / 6";
            if (progressFill != null)
            {
                progressFill.fillAmount = connections / 6f;
                progressFill.rectTransform.localScale = new Vector3(connections / 6f, 1f, 1f);
            }
        }
    }
}
