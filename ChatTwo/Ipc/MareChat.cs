using Dalamud.Plugin.Ipc;

namespace ChatTwo.Ipc;

internal sealed class MareChat : IDisposable
{
    private Plugin Plugin { get; }

    private ICallGateSubscriber<Dictionary<int, string>>? ChannelInfosGate { get; }
    private ICallGateSubscriber<int, string, object?>? SendMessageGate { get; }

    private Dictionary<int, string> ChannelNames { get; set; } = new();

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
}


