using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    sealed class FactoryOrderWindow : Window
    {
        readonly FactoryCraftMap manager;
        readonly Building_StorageController controller;
        readonly FactoryRecipe recipe;
        readonly bool maintain;
        int count = 1; string buffer = "1";
        public override Vector2 InitialSize => new Vector2(530, 250);
        public FactoryOrderWindow(FactoryCraftMap manager, Building_StorageController controller, FactoryRecipe recipe, bool maintain)
        { this.manager = manager; this.controller = controller; this.recipe = recipe; this.maintain = maintain; doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 50), recipe.Title);
            Widgets.Label(new Rect(0, 55, rect.width, 55), maintain ? StorageText.Get("GrechkaStorage_Text128") : recipe.Primary == null || recipe.NetOutput ? StorageText.Get("GrechkaStorage_Text129") : StorageText.Get("GrechkaStorage_Text130"));
            Widgets.TextFieldNumeric(new Rect(0, 110, rect.width, 32), ref count, ref buffer, 1, 1000000);
            if (Widgets.ButtonText(new Rect(rect.width - 140, 160, 140, 32), StorageText.Get("GrechkaStorage_Text131")))
            { if (controller.Spawned) manager.Add(controller, recipe, count, maintain); Close(); }
        }
    }
}
