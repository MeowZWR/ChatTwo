namespace ChatTwo.Ui.ChatLog;

public partial class ChatLog
{
    public void DrawChannelName(Tab activeTab, bool sendChannelSwitch = false)
    {
        var currentChannel = ReadChannelName(activeTab);
        if (sendChannelSwitch && !currentChannel.SequenceEqual(PreviousChannel))
        {
            PreviousChannel = currentChannel;
            Plugin.ServerCore.SendChannelSwitch(currentChannel);
        }

        InputHandler.ChunkHandler.DrawChunks(currentChannel);
    }
}