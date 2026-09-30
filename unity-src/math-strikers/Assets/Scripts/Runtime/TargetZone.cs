using UnityEngine;

namespace MathStrikers
{
    /// <summary>
    /// One of the three aiming panels hanging in the goal mouth. Each carries a
    /// candidate answer; clicking it (or pressing its number key) is how the player
    /// commits to both an answer and a placement in a single action.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TargetZone : MonoBehaviour
    {
        public int LaneIndex { get; private set; }
        public int Value { get; private set; }

        [SerializeField] Renderer panelRenderer;
        [SerializeField] TextMesh label;
        [SerializeField] Transform aimPoint;

        Color idleColor;
        Color hoverColor;
        MaterialPropertyBlock block;
        bool interactable;

        public Vector3 AimPoint => aimPoint != null ? aimPoint.position : transform.position;

        public void Configure(int laneIndex, Color idle, Color hover)
        {
            LaneIndex = laneIndex;
            idleColor = idle;
            hoverColor = hover;
            block = new MaterialPropertyBlock();
            ApplyColor(idleColor);
        }

        public void SetValue(int value)
        {
            Value = value;
            if (label != null) label.text = value.ToString();
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!value) ApplyColor(idleColor);
        }

        /// <summary>Paint the panel for the outcome of a shot: green correct, red wrong.</summary>
        public void Flash(Color color)
        {
            ApplyColor(color);
        }

        public void ResetVisual()
        {
            ApplyColor(idleColor);
        }

        void OnMouseEnter()
        {
            if (interactable) ApplyColor(hoverColor);
        }

        void OnMouseExit()
        {
            if (interactable) ApplyColor(idleColor);
        }

        void OnMouseDown()
        {
            if (!interactable) return;
            MatchManager.Instance?.SubmitAnswer(this);
        }

        void ApplyColor(Color color)
        {
            if (panelRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            panelRenderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            panelRenderer.SetPropertyBlock(block);
        }

        public void Bind(Renderer panel, TextMesh text, Transform aim)
        {
            panelRenderer = panel;
            label = text;
            aimPoint = aim;
        }
    }
}
