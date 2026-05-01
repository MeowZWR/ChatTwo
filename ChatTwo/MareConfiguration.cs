using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ChatTwo.Code;

namespace ChatTwo;

[Serializable]
internal class MareConfiguration
{
    public Dictionary<int, (ChatSource, ChatSource)> InactivityHideChannels = new();
    public Dictionary<int, uint> ChatColours = new();
    public List<Dictionary<int, (ChatSource, ChatSource)>> TabChatCodes = new();
    public bool DefaultChannelsInitialized;
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
        catch
        {
            return LoadLegacy();
        }
    }

    private static MareConfiguration? LoadLegacy()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return null;

            var root = JObject.Parse(File.ReadAllText(path));
            var config = new MareConfiguration
            {
                DefaultChannelsInitialized = root.Value<bool?>(nameof(DefaultChannelsInitialized)) ?? false,
                ReduceE044By2Pt = root.Value<bool?>(nameof(ReduceE044By2Pt)) ?? false,
                EmoteTooltipScale = root.Value<float?>(nameof(EmoteTooltipScale)) ?? 5.0f,
            };

            config.InactivityHideChannels = ReadChannelSources(root[nameof(InactivityHideChannels)] as JObject);
            config.ChatColours = root[nameof(ChatColours)]?.ToObject<Dictionary<int, uint>>() ?? new Dictionary<int, uint>();

            if (root[nameof(TabChatCodes)] is JArray tabs)
            {
                foreach (var tab in tabs)
                    config.TabChatCodes.Add(ReadChannelSources(tab as JObject));
            }

            return config;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<int, (ChatSource, ChatSource)> ReadChannelSources(JObject? obj)
    {
        var result = new Dictionary<int, (ChatSource, ChatSource)>();
        if (obj == null)
            return result;

        foreach (var prop in obj.Properties())
        {
            if (!int.TryParse(prop.Name, out var idx))
                continue;

            var source = ReadChannelSource(prop.Value);
            if (source != null)
                result[idx] = source.Value;
        }

        return result;
    }

    private static (ChatSource, ChatSource)? ReadChannelSource(JToken token)
    {
        if (token.Type == JTokenType.Integer)
        {
            var source = (ChatSource)token.Value<int>();
            return (source, source);
        }

        if (token is not JObject obj)
            return null;

        var sourceValue = obj["Source"] ?? obj["Item1"];
        var targetValue = obj["Target"] ?? obj["Item2"];
        if (sourceValue == null || targetValue == null)
            return null;

        return ((ChatSource)sourceValue.Value<int>(), (ChatSource)targetValue.Value<int>());
    }

    internal void EnsureDefaultChannels(int tabCount)
    {
        if (DefaultChannelsInitialized)
            return;

        while (TabChatCodes.Count < tabCount)
            TabChatCodes.Add(new Dictionary<int, (ChatSource, ChatSource)>());

        if (tabCount > 0 && TabChatCodes.All(tab => tab.Count == 0))
            for (var idx = 0; idx < 8; idx++)
                TabChatCodes[0][idx] = (ChatSourceExt.All, ChatSourceExt.All);

        DefaultChannelsInitialized = true;
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


