using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ClientLedgerApp;

// Broadcast when a logical dataset has changed so listeners can refresh.
public class DataInvalidatedMessage : ValueChangedMessage<string>
{
    public DataInvalidatedMessage(string value) : base(value) { }
}