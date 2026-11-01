using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    public static class QuestBadge
    {
        static Texture2D circle;
        static Texture2D Circle
        {
            get
            {
                if (circle != null) return circle;
                circle = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var pixels = new Color32[32 * 32];
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        float dx = x - 15.5f, dy = y - 15.5f;
                        pixels[y * 32 + x] = dx * dx + dy * dy <= 15 * 15
                            ? new Color32(255, 218, 38, 255) : new Color32(0, 0, 0, 0);
                    }
                circle.SetPixels32(pixels); circle.Apply();
                return circle;
            }
        }
        public static void Draw(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color previous = GUI.color; TextAnchor anchor = Text.Anchor; GameFont font = Text.Font;
            GUI.color = Color.white; GUI.DrawTexture(rect, Circle);
            GUI.color = new Color(0.8f, 0.03f, 0.03f); Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Small;
            Widgets.Label(rect, "!");
            Text.Font = font; Text.Anchor = anchor; GUI.color = previous;
        }
    }

    public sealed class GrechkaButtonWorker : MainButtonWorker_ToggleTab
    {
        public override void DoButton(Rect rect)
        {
            base.DoButton(rect);
            var game = QuestGame.Current;
            if (game != null && game.HasClaimableQuest()) QuestBadge.Draw(new Rect(rect.xMax - 22, rect.y, 22, 22));
        }
    }
}
