using UnityEngine;

namespace PenaltyDuel
{
    /// <summary>
    /// Re-dresses a player in a team's colours at runtime. There is one striker and
    /// one keeper in the scene, and the two players swap roles every kick, so each
    /// kick paints them in the kits of whoever is shooting and saving.
    /// </summary>
    public static class KitPainter
    {
        static MaterialPropertyBlock block;

        public static void Paint(GameObject player, Color shirt, Color shorts)
        {
            if (player == null) return;
            block ??= new MaterialPropertyBlock();

            foreach (var renderer in player.GetComponentsInChildren<Renderer>())
            {
                Color colour;
                if (renderer.name == "Tops") colour = shirt;
                else if (renderer.name == "Bottoms") colour = shorts;
                else continue;

                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", colour);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
