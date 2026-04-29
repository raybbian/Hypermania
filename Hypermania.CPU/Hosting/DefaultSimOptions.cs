using System.IO;
using Game.Sim;
using MemoryPack;

namespace Hypermania.CPU.Hosting
{
    // Loads a SimOptions binary written by the Unity-side SimOptionsExporter.
    // The sim graph is fully [MemoryPackable] - nothing to translate, the
    // headless build's UnityEngine.Object shim makes new ScriptableObject()
    // a plain allocation so MemoryPack's deserialization runs unmodified.
    public static class DefaultSimOptions
    {
        public static SimOptions LoadPreset(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"sim-options preset not found at '{path}'. Generate one from the Unity " +
                    "editor menu 'Hypermania/Export Sim Options Preset...'.",
                    path
                );
            byte[] bytes = File.ReadAllBytes(path);
            return MemoryPackSerializer.Deserialize<SimOptions>(bytes);
        }
    }
}
