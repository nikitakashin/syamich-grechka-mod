using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class Graphic_RoomCooler : Graphic_Single
    {
        private static readonly ConditionalWeakTable<Texture2D, Texture2D> blueTextures = new ConditionalWeakTable<Texture2D, Texture2D>();

        public override void Init(GraphicRequest request)
        {
            request.texture = Recolor(request.texture ?? ContentFinder<Texture2D>.Get(request.path));
            base.Init(request);
        }

        internal static Texture2D Recolor(Texture2D source) => blueTextures.GetValue(source, CreateBlueTexture);

        private static Texture2D CreateBlueTexture(Texture2D source)
        {
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a > 0 && pixel.r > pixel.g + 16 && pixel.r > pixel.b + 16)
                    pixels[i] = new Color32(pixel.b, pixel.g, pixel.r, pixel.a);
            }
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
            {
                name = "Grechka_RoomCooler_" + source.name,
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        public override Graphic GetColoredVersion(Shader shader, Color color, Color colorTwo)
            => GraphicDatabase.Get<Graphic_RoomCooler>(path, shader, drawSize, color, colorTwo, data);
    }
}
