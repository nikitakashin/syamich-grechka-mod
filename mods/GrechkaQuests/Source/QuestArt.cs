using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    internal static class QuestArt
    {
        static readonly HashSet<string> Chapters = new HashSet<string> {
            "ChapterArchotech", "ChapterGettingStarted", "ChapterProduction", "ChapterMechanites", "ChapterAutocrafting", "ChapterResources", "ChapterConstruction", "ChapterEquipment", "ChapterAgriculture", "ChapterGrechka", "ChapterThreats", "ChapterLogistics", "ChapterEnergy", "ChapterStorage", "ChapterBionics" };
        static readonly HashSet<string> Icons = new HashSet<string> { "ObjectiveManual", "ObjectiveHaveItems", "ObjectiveBuildings", "ObjectiveMine", "ObjectiveHarvest", "ObjectiveProduce", "QuestBook", "QuestUnlink", "QuestPreview", "RewardChoice", "QuestValidate", "QuestActive", "QuestRewardAdd", "QuestChapterAdd", "QuestLink", "QuestDuplicate", "QuestAdd", "RewardGuaranteed", "QuestDelete", "QuestFinish", "QuestApply", "QuestChapterLocked", "QuestRewardReady", "QuestCompleted", "QuestExport", "QuestSearch", "QuestClaimAll", "QuestObjectiveAdd", "QuestImport", "QuestClaimReward" };
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        public static IEnumerable<string> Ids => Chapters.Concat(Icons).OrderBy(id => id);
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id) || (!Chapters.Contains(id) && !Icons.Contains(id))) return null;
            if (!Cache.TryGetValue(id, out Texture2D image)) Cache[id] = image = ContentFinder<Texture2D>.Get("UI/" + (Chapters.Contains(id) ? "Chapters/" : "") + id, false);
            return image;
        }
        // Labelled native buttons retain their hit area and keyboard behavior. At narrow widths,
        // keep the full text instead of squeezing an icon over the label.
        public static bool Button(Rect rect, string label, string icon)
        {
            Texture2D image = Get(icon);
            float side = Mathf.Min(24, rect.height - 8);
            if (image == null || rect.width < Text.CalcSize(label).x + side + 18)
                return Widgets.ButtonText(rect, label);
            bool clicked = Widgets.ButtonText(rect, "");
            GUI.DrawTexture(new Rect(rect.x + 6, rect.y + (rect.height - side) / 2, side, side), image, ScaleMode.ScaleToFit);
            TextAnchor previous = Text.Anchor;
            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rect.x + side + 12, rect.y, rect.width - side - 18, rect.height), label);
            }
            finally { Text.Anchor = previous; }
            return clicked;
        }
        public static void Label(Rect rect, string label, string icon)
        {
            float side = Mathf.Min(24, rect.height - 4);
            if (!Draw(new Rect(rect.x, rect.y + 2, side, side), icon)) { Widgets.Label(rect, label); return; }
            Widgets.Label(new Rect(rect.x + side + 7, rect.y, Mathf.Max(0, rect.width - side - 7), rect.height), label);
        }
        public static bool Draw(Rect rect, string id)
        {
            Texture2D image = Get(id); if (image == null) return false;
            GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit); return true;
        }
    }
}
