using System.Numerics;

namespace MystieHook.Games.Example
{
    /// <summary>Config for the ExampleGame (ESP colors, toggles, etc.).</summary>
    public sealed class ExampleConfig
    {
        public bool DrawBoxes = true;
        public bool ShowName = true;
        public bool ShowDistance = true;

        public Vector4 BoxColor = new(0.3f, 0.8f, 1f, 1f);
        public Vector4 NameColor = new(1f, 1f, 1f, 1f);
        public Vector4 DistanceColor = new(0.7f, 1f, 0.7f, 1f);

        public float MaxDrawDistance = 1000f;
    }
}
