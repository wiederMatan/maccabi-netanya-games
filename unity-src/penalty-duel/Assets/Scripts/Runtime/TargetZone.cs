using UnityEngine;

namespace PenaltyDuel
{
    /// <summary>
    /// One of the six spots marked out in the goal mouth. The shooter taps one to
    /// aim, then the keeper taps one to dive - the same board serves both, so a
    /// child only ever has to learn one control. MatchManager reads the taps.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TargetZone : MonoBehaviour
    {
        [SerializeField] int index;
        [SerializeField] Renderer panelRenderer;
        [SerializeField] TextMesh label;
        [SerializeField] Transform aimPoint;

        public int Index => index;

        Color idleColor;
        Color hoverColor;
        MaterialPropertyBlock block;
        bool interactable;

        /// <summary>Where a shot at this spot crosses the goal line.</summary>
        public Vector3 AimPoint => aimPoint != null ? aimPoint.position : transform.position;

        public void Configure(Color idle)
        {
            idleColor = idle;
            hoverColor = idle;
            block = new MaterialPropertyBlock();
            ApplyColor(idleColor);
        }

        /// <summary>Hover in the colour of whoever is choosing.</summary>
        public void SetInteractable(bool value, Color hover)
        {
            hoverColor = hover;
            SetInteractable(value);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!value) ApplyColor(idleColor);
        }

        /// <summary>Hide the board while the kick plays out, so nothing covers the keeper.</summary>
        public void SetVisible(bool visible)
        {
            // The panel, its number and the number's shadow.
            foreach (var part in GetComponentsInChildren<Renderer>()) part.enabled = visible;
        }

        public void Flash(Color color) => ApplyColor(color);

        public void ResetVisual() => ApplyColor(idleColor);

        // Hover is for a mouse only: on a touch screen the pointer stays wherever the
        // last tap was, and a spot would look picked before anyone chose it.
        // Picking itself is read by MatchManager.
        void OnMouseEnter()
        {
            if (interactable && !Input.touchSupported) ApplyColor(hoverColor);
        }

        void OnMouseExit()
        {
            if (interactable) ApplyColor(idleColor);
        }

        void ApplyColor(Color color)
        {
            if (panelRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            panelRenderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            panelRenderer.SetPropertyBlock(block);
        }

        public void Bind(int spot, Renderer panel, TextMesh text, Transform aim)
        {
            index = spot;
            panelRenderer = panel;
            label = text;
            aimPoint = aim;
        }
    }
}
