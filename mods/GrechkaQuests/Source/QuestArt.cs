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
        static readonly HashSet<string> Icons = new HashSet<string> { "ObjectiveManual", "ObjectiveHaveItems", "ObjectiveBuildings", "ObjectiveMine", "ObjectiveHarvest", "ObjectiveProduce", "QuestBook" };
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        public static IEnumerable<string> Ids => Chapters.Concat(Icons).OrderBy(id => id);
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id) || (!Chapters.Contains(id) && !Icons.Contains(id))) return null;
            if (!Cache.TryGetValue(id, out Texture2D image)) Cache[id] = image = ContentFinder<Texture2D>.Get("UI/" + (Chapters.Contains(id) ? "Chapters/" : "") + id, false);
            return image;
        }
        public static bool Draw(Rect rect, string id)
        {
            Texture2D image = Get(id); if (image == null) return false;
            GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit); return true;
        }
    }
}
