using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ChatTwo.Code;

namespace ChatTwo;

[Serializable]
internal class MareConfiguration
{
    public Dictionary<int, (ChatSource, ChatSource)> InactivityHideChannels = new();
    public Dictionary<int, uint> ChatColours = new();
    public List<Dictionary<int, (ChatSource, ChatSource)>> TabChatCodes = new();
    public bool ReduceE044By2Pt;
    public float EmoteTooltipScale = 5.0f;

    internal static string GetPath() => Path.Combine(Plugin.Interface.ConfigDirectory.FullName, "MareChat.json");

    internal static MareConfiguration? Load()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<MareConfiguration>(json);
        }
        catch { return null; }
    }

    internal void Save()
    {
        try
        {
            var path = GetPath();
            Directory.CreateDirectory(Plugin.Interface.ConfigDirectory.FullName);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch { }
    }
}


