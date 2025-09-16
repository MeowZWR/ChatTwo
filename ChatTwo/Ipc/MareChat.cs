using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Ipc;

namespace ChatTwo.Ipc;

internal sealed class MareChat : IDisposable
{
    private Plugin Plugin { get; }

    private ICallGateSubscriber<Dictionary<int, string>>? ChannelInfosGate { get; }
    private ICallGateSubscriber<int, string, object?>? SendMessageGate { get; }

    private Dictionary<int, string> ChannelNames { get; set; } = new();
    internal DalamudLinkPayload? OpenChatLinkPayload { get; }

    internal MareChat(Plugin plugin)
    {
        Plugin = plugin;
        try
        {
            ChannelInfosGate = Plugin.Interface.GetIpcSubscriber<Dictionary<int, string>>("MareChat.ChannelInfos");
            SendMessageGate = Plugin.Interface.GetIpcSubscriber<int, string, object?>("MareChat.SendMessage");
            try
            {
                ChannelNames = ChannelInfosGate.InvokeFunc();
            }
            catch
            {
                // ignore if provider not present yet
            }

            // Register clickable chat link for Mare channel icon (\uE044)
            OpenChatLinkPayload = Plugin.ChatGui.AddChatLinkHandler(10001, OnOpenChatLinkClicked);
        }
        catch
        {
            // no-op; Mare not installed
        }
    }

    internal string? GetChannelName(int index)
    {
        if (ChannelInfosGate == null)
            return null;
        try
        {
            if (ChannelNames.Count == 0)
                ChannelNames = ChannelInfosGate.InvokeFunc();
            return ChannelNames.TryGetValue(index, out var name) ? name : null;
        }
        catch
        {
            return null;
        }
    }

    internal void SendMessage(int index, string message)
    {
        if (SendMessageGate == null)
            return;
        try
        {
            SendMessageGate.InvokeAction(index, message);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error sending MareChat message via IPC");
        }
    }

    public void Dispose()
    {
        // nothing to clean up; only subscribers
    }

    internal void InvalidateChannels()
    {
        ChannelNames.Clear();
    }

    private void OnOpenChatLinkClicked(uint cmdId, SeString se)
    {
        try
        {
            var label = string.Empty;
            var inLink = false;
            foreach (var p in se.Payloads)
            {
                if (p is DalamudLinkPayload)
                {
                    inLink = true;
                    continue;
                }
                if (!inLink)
                    continue;
                if (p is RawPayload raw && Equals(raw, RawPayload.LinkTerminator))
                    break;
                if (p is TextPayload tp)
                    label += tp.Text;
            }

            string arg = string.Empty;
            var open = label.IndexOf('[');
            var close = label.IndexOf(']', open + 1);
            if (open >= 0 && close > open)
            {
                var inner = label.Substring(open + 1, close - open - 1).Trim();
                var hasNonDigit = false;
                for (var i = 0; i < inner.Length; i++)
                    if (!char.IsDigit(inner[i])) { hasNonDigit = true; break; }
                if (hasNonDigit && inner.Length > 0)
                    arg = inner;
            }

            var cmd = string.IsNullOrEmpty(arg) ? "/mare chat" : $"/mare chat {arg}";
            ChatTwo.GameFunctions.ChatBox.SendMessage(cmd);
        }
        catch
        {
            // ignore
        }
    }
}


