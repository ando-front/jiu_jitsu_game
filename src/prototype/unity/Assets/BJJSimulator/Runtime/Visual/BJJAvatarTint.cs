// VISUAL — per-instance avatar color tint for role identification.
//
// Stage 2 ships two identical Y Bot meshes (Avatar_Bottom / Avatar_Top) so
// playtesters cannot tell roles apart at a glance. This component layers a
// MaterialPropertyBlock _Color / _BaseColor override onto every renderer in
// its hierarchy — no shared-material mutation, no new .mat asset bloat. Use
// red for Bottom and blue for Top by convention.
//
// [ExecuteAlways] is intentional: the tint must show up in Edit mode so we
// don't have to enter Play to confirm role differentiation in the Scene
// view. OnValidate keeps the Inspector responsive to live tweaks.
//
// Both _Color (Standard / Built-in) and _BaseColor (URP/Lit) are written
// because the Y Bot FBX-embedded materials use Standard while any URP
// replacement materials would use _BaseColor — writing both lets either
// shader path pick up the tint without per-shader branching.

using UnityEngine;

namespace BJJSimulator.Visual
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BJJAvatarTint : MonoBehaviour
    {
        [SerializeField] private Color tintColor = Color.white;
        [Tooltip("0 = no tint (white), 1 = full tintColor.")]
        [SerializeField, Range(0f, 1f)] private float tintStrength = 0.6f;

        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Reused across renderers each Apply() to avoid GC churn during
        // OnValidate spam from Inspector slider drags.
        private MaterialPropertyBlock _block;

        void OnEnable()   => Apply();
        void OnValidate() => Apply();

        private void Apply()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            Color tint = Color.Lerp(Color.white, tintColor, tintStrength);

            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(ColorId,     tint);
                _block.SetColor(BaseColorId, tint);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
