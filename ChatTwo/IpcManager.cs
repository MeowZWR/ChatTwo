using System.Text.RegularExpressions;
using ChatTwo.Code;
using ChatTwo.Util;
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
            var chanLabel = string.IsNullOrEmpty(mareName) ? $"\uE044[{idx + 1}]" : $"\uE044[{mareName}]";
            var senderLabel = $"<{sender}>";

            var mareLink = PluginRef.MareChat?.OpenChatLinkPayload;
            var senderChunks = new List<Chunk>
            {
                new TextChunk(ChunkSource.Sender, mareLink, chanLabel) { FallbackColour = chatType },
                new TextChunk(ChunkSource.Sender, null, senderLabel) { FallbackColour = chatType },
                new TextChunk(ChunkSource.Sender, null, " ") { FallbackColour = chatType },
            };
            var contentChunks = BuildMareContentChunks(content, chatType);

            var code = new ChatCode((ushort)chatType);
            // Build sender SeString containing the same link payload so click handler can resolve it
            var senderSourcePayloads = new List<Payload>();
            if (mareLink != null)
            {
                senderSourcePayloads.Add(mareLink);
                senderSourcePayloads.Add(new TextPayload(chanLabel));
                senderSourcePayloads.Add(RawPayload.LinkTerminator);
            }
            else
            {
                senderSourcePayloads.Add(new TextPayload(chanLabel));
            }
            senderSourcePayloads.Add(new TextPayload(senderLabel));
            senderSourcePayloads.Add(new TextPayload(" "));
            var senderSource = new SeString(senderSourcePayloads);

            var message = new Message(PluginRef.MessageManager.CurrentContentId, 0, 0, code, senderChunks, contentChunks, senderSource, new SeString());

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

    // Parse Mare content to support AutoTranslate tags and keep compatibility with
    // downstream emote/URL processing in Message.CheckMessageContent.
    private static List<Chunk> BuildMareContentChunks(string content, ChatType chatType)
    {
        // Fast path: no auto-translate tag present
        if (content.IndexOf("<at:", StringComparison.Ordinal) < 0)
        {
            return new List<Chunk>
            {
                new TextChunk(ChunkSource.Content, null, content) { FallbackColour = chatType }
            };
        }

        var payloads = new List<Payload>();
        var regex = new Regex("<at:(\\d+),(\\d+)>", RegexOptions.Compiled);
        var lastIndex = 0;
        foreach (Match match in regex.Matches(content))
        {
            if (match.Index > lastIndex)
            {
                var textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                if (textBefore.Length > 0)
                    payloads.Add(new TextPayload(textBefore));
            }

            if (uint.TryParse(match.Groups[1].Value, out var group)
                && uint.TryParse(match.Groups[2].Value, out var row))
            {
                payloads.Add(new AutoTranslatePayload(group, row));
            }
            else
            {
                // If parsing fails, keep original text
                payloads.Add(new TextPayload(match.Value));
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
        {
            var tail = content.Substring(lastIndex);
            if (tail.Length > 0)
                payloads.Add(new TextPayload(tail));
        }

        var se = new SeString(payloads);
        return ChunkUtil.ToChunks(se, ChunkSource.Content, chatType).ToList();
    }
    }
