using System;
using System.Collections.Generic;
using ChatTwo.Code;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Ipc;

namespace ChatTwo;

internal sealed class IpcManager : IDisposable
{
    private Plugin PluginRef { get; }
    private ICallGateProvider<string> RegisterGate { get; }
    private ICallGateProvider<string, object?> UnregisterGate { get; }
        private ICallGateProvider<(int major, int minor)> ApiVersionGate { get; }
    private ICallGateProvider<object?> AvailableGate { get; }
    private ICallGateProvider<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?> InvokeGate { get; }
    private ICallGateProvider<int, string, string, DateTime, object?> MarePushGate { get; }
    private ICallGateProvider<object?> MareChannelsUpdatedGate { get; }

    internal List<string> Registered { get; } = [];

    public IpcManager(Plugin plugin)
    {
        PluginRef = plugin;
        RegisterGate = Plugin.Interface.GetIpcProvider<string>("ChatTwo.Register");
        RegisterGate.RegisterFunc(Register);

            // Read-only API for availability/version checks
            ApiVersionGate = Plugin.Interface.GetIpcProvider<(int, int)>("ChatTwo.ApiVersion");
            ApiVersionGate.RegisterFunc(GetApiVersion);

        AvailableGate = Plugin.Interface.GetIpcProvider<object?>("ChatTwo.Available");

        UnregisterGate = Plugin.Interface.GetIpcProvider<string, object?>("ChatTwo.Unregister");
        UnregisterGate.RegisterAction(Unregister);

        InvokeGate = Plugin.Interface.GetIpcProvider<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>("ChatTwo.Invoke");

        // Provider for Mare to push messages into ChatTwo as MareLinkshell[i]
        MarePushGate = Plugin.Interface.GetIpcProvider<int, string, string, DateTime, object?>("ChatTwo.Mare.Push");
        MarePushGate.RegisterAction(MarePush);

        // Provider for Mare to notify channel info updates (join/leave/alias changes)
        MareChannelsUpdatedGate = Plugin.Interface.GetIpcProvider<object?>("ChatTwo.Mare.ChannelInfosUpdated");
        MareChannelsUpdatedGate.RegisterAction(MareChannelInfosUpdated);

        AvailableGate.SendMessage();
    }

    internal void Invoke(string id, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content)
    {
        InvokeGate.SendMessage(id, sender, contentId, payload, senderString, content);
    }

    private string Register()
    {
        var id = Guid.NewGuid().ToString();
        Registered.Add(id);
        return id;
    }

    private void Unregister(string id)
    {
        Registered.Remove(id);
    }

    public void Dispose()
    {
        UnregisterGate.UnregisterFunc();
        RegisterGate.UnregisterFunc();
            ApiVersionGate.UnregisterFunc();
        MarePushGate.UnregisterAction();
        MareChannelsUpdatedGate.UnregisterAction();
        Registered.Clear();
    }

    private void MarePush(int index, string sender, string content, DateTime timeUtc)
    {
        try
        {
            var input = index switch
            {
                0 => Code.InputChannel.MareLinkshell1,
                1 => Code.InputChannel.MareLinkshell2,
                2 => Code.InputChannel.MareLinkshell3,
                3 => Code.InputChannel.MareLinkshell4,
                4 => Code.InputChannel.MareLinkshell5,
                5 => Code.InputChannel.MareLinkshell6,
                6 => Code.InputChannel.MareLinkshell7,
                7 => Code.InputChannel.MareLinkshell8,
                _ => Code.InputChannel.MareLinkshell1,
            };

            var chatType = input.ToChatType();

            var idx = (int)input.LinkshellIndex();
            var mareName = PluginRef.MareChat?.GetChannelName(idx) ?? "";
            var chanLabel = string.IsNullOrEmpty(mareName) ? $"[\uE044{idx + 1}]" : $"[\uE044{mareName}]";
            var senderLabel = $"<{sender}>";

            var senderChunks = new List<Chunk>
            {
                new TextChunk(ChunkSource.Sender, null, chanLabel) { FallbackColour = chatType },
                new TextChunk(ChunkSource.Sender, null, senderLabel) { FallbackColour = chatType },
                new TextChunk(ChunkSource.Sender, null, " ") { FallbackColour = chatType },
            };
            var contentChunks = new List<Chunk> { new TextChunk(ChunkSource.Content, null, content) { FallbackColour = chatType } };

            var code = new ChatCode((ushort)chatType);
            var message = new Message(PluginRef.MessageManager.CurrentContentId, 0, 0, code, senderChunks, contentChunks, new SeString(), new SeString());

            // Overwrite time to server-provided value if possible (ignore if not settable)
            try
            {
                typeof(Message).GetProperty("Date")?.SetValue(message, new DateTimeOffset(timeUtc, TimeSpan.Zero));
            }
            catch
            {
                // ignore
            }

            // Persist to DB
            PluginRef.MessageManager.Store.UpsertMessage(message);

            // Insert into tabs and notify web
            var currentTabId = PluginRef.CurrentTab.Identifier;
            var currentMatches = PluginRef.CurrentTab.Matches(message);
            foreach (var tab in Plugin.Config.Tabs)
            {
                var unread = !(tab.UnreadMode == UnreadMode.Unseen && PluginRef.CurrentTab != tab && currentMatches);
                if (tab.Matches(message))
                {
                    tab.AddMessage(message, unread);
                    if (tab.Identifier == currentTabId)
                        PluginRef.ServerCore.SendNewMessage(message);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error handling ChatTwo.Mare.Push");
        }
    }

    private void MareChannelInfosUpdated()
    {
        try
        {
            PluginRef.MareChat?.InvalidateChannels();
        }
        catch
        {
            // ignore
        }
    }

    private static (int, int) GetApiVersion() => (1, 0);
    }
