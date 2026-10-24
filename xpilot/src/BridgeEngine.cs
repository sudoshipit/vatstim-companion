using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FlightCompanion.XPilot {
  internal sealed class Notification {
    internal string Session,Sender,Text,Kind;
    internal long CreatedAt;
  }
  internal sealed class BrokerAction {
    internal string Id,Session,Kind,Recipient,Text,Status;
    internal long CreatedAt,ExpiresAt;
  }
  internal sealed class UpdateResult {
    internal string Text;
    internal BrokerAction Action;
    internal UpdateResult(string text){Text=text;}
  }
  internal sealed class ReplyBinding {internal string Session,Sender;internal long ExpiresAt;}
  internal sealed class Confirmation {internal string Session,Actor;internal long ExpiresAt,RequestUpdateId,RequestedUnixSeconds;}

  internal sealed class BridgeEngine {
    readonly object gate=new object();readonly PluginSettings settings;readonly Func<long> clock;readonly bool commandsAvailable;
    readonly Queue<Notification> notifications=new Queue<Notification>();readonly Dictionary<long,ReplyBinding> replies=new Dictionary<long,ReplyBinding>();
    readonly long startedAt;
    string session=Guid.NewGuid().ToString("N"),callsign="",lastError="none";
    long nextOffset,sessionStartedAt,lastCommandAt;int dropped;
    bool connected,closed,disconnectIssued,healthy;
    Confirmation confirmation;BrokerAction lastAction;
    internal BridgeEngine(PluginSettings config,bool hasUiContext,Func<long> now) {
      settings=config;commandsAvailable=hasUiContext;clock=now;startedAt=now();settings.Validate();
    }
    internal static long UtcNow(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
    internal long NextOffset {get{lock(gate)return nextOffset;}}
    internal bool Connected {get{lock(gate)return connected;}}
    internal string LastStatus {get{lock(gate){Expire();return lastAction==null?"none":lastAction.Status;}}}
    internal string Session {get{lock(gate)return session;}}
    internal int Dropped {get{lock(gate)return dropped;}}
    internal void SkipBacklog(IEnumerable<TelegramUpdate> updates){lock(gate){foreach(var update in updates)if(update.Id>=nextOffset&&update.Id<Int64.MaxValue)nextOffset=update.Id+1;}}
    internal void TransportSuccess(){lock(gate)healthy=true;}
    internal void TransportFailure(){lock(gate){healthy=false;lastError="Telegram request failed; check connectivity, bot permissions and configuration. Failed sends are not retried.";}}
    internal void NotificationFailed(){lock(gate){dropped++;TransportFailure();}}
    void InvalidatePending() {
      confirmation=null;replies.Clear();notifications.Clear();
      if(lastAction!=null&&lastAction.Status=="queued")lastAction.Status="expired";
      else if(lastAction!=null&&lastAction.Status=="dispatched")lastAction.Status="uncertain";
    }
    internal void NetworkConnected(string value) {lock(gate){
      if(closed)return;InvalidatePending();session=Guid.NewGuid().ToString("N");sessionStartedAt=clock();
      callsign=value??"";connected=true;disconnectIssued=false;
      Enqueue("system","xPilot","Connected as "+callsign+". No automatic reconnection is enabled.");
    }}
    internal void NetworkDisconnected() {lock(gate){
      if(closed)return;
      if(lastAction!=null&&lastAction.Kind=="disconnect"&&lastAction.Session==session&&
        new[]{"dispatched","submitted","uncertain"}.Contains(lastAction.Status))lastAction.Status="disconnected";
      connected=false;InvalidatePending();
      Enqueue("system","xPilot","xPilot is disconnected (connection event confirmed). X-Plane remains running. Reconnect manually in xPilot when appropriate.");
    }}
    void Enqueue(string kind,string sender,string text) {
      if(notifications.Count>=100){dropped++;return;}
      notifications.Enqueue(new Notification{Kind=kind,Sender=sender,Text=text,Session=session,CreatedAt=clock()});
    }
    internal void Receive(string kind,string sender,string text){lock(gate){
      if(closed||!connected||String.IsNullOrWhiteSpace(text)||!Regex.IsMatch(sender??"",@"^[A-Za-z0-9_-]{1,16}$"))return;
      if(kind=="private"&&!settings.ForwardPrivateMessages)return;
      if(kind=="selcal"&&!settings.ForwardSelcal)return;
      if(kind=="radio"){
        if(!settings.ForwardRadioMessages)return;
        if(settings.RadioOnlyWhenCallsignMentioned&&(String.IsNullOrEmpty(callsign)||!Regex.IsMatch(text,@"(?<![A-Za-z0-9])"+Regex.Escape(callsign)+@"(?![A-Za-z0-9])",RegexOptions.IgnoreCase)))return;
      }
      if(kind!="private"&&kind!="radio"&&kind!="selcal")return;
      Enqueue(kind,sender.ToUpperInvariant(),text.Length>2000?text.Substring(0,2000):text);
    }}
    internal Notification TakeNotification(){lock(gate){
      while(notifications.Count>0){var n=notifications.Dequeue();if(!closed&&n.Session==session&&clock()-n.CreatedAt<300000&&(n.Kind=="system"||connected))return n;dropped++;}return null;
    }}
    internal bool NotificationCurrent(Notification n){lock(gate)return !closed&&n.Session==session&&clock()-n.CreatedAt<300000&&(n.Kind=="system"||connected);}
    internal void BindReply(long telegramId,Notification n){lock(gate){
      if(n.Kind!="private"||!connected||!NotificationCurrent(n))return;
      foreach(var id in replies.Where(p=>p.Value.ExpiresAt<=clock()).Select(p=>p.Key).ToArray())replies.Remove(id);
      if(replies.Count>=100)replies.Remove(replies.OrderBy(p=>p.Value.ExpiresAt).First().Key);
      replies[telegramId]=new ReplyBinding{Sender=n.Sender,Session=n.Session,ExpiresAt=n.CreatedAt+300000};
    }}
    void Expire(){
      if(confirmation!=null&&confirmation.ExpiresAt<=clock())confirmation=null;
      if(lastAction!=null&&clock()>lastAction.ExpiresAt){
        if(lastAction.Status=="queued")lastAction.Status="expired";
        else if(lastAction.Status=="dispatched"||(lastAction.Kind=="disconnect"&&lastAction.Status=="submitted"))lastAction.Status="uncertain";
      }
    }
    string Status(){
      Expire();return "xPilot: "+(connected?"connected as "+callsign:"disconnected")+
        "\nTelegram polling: "+(healthy?"healthy":"starting or unavailable")+
        "\nPrivate replies: "+(settings.AllowReplies&&commandsAvailable?"enabled":"disabled")+
        "\nRemote disconnect: "+(settings.AllowDisconnect&&commandsAvailable?"enabled":"disabled")+
        (commandsAvailable?"":"\nCommands unavailable: no xPilot UI synchronization context.")+
        "\nLast command: "+(lastAction==null?"none":lastAction.Kind+" — "+lastAction.Status)+
        "\nDropped notifications: "+dropped+"\nLast transport error: "+lastError+
        "\nNo automatic reconnect. Remain present and available to ATC.";
    }
    UpdateResult Denied(string kind) {
      if(!connected)return new UpdateResult("xPilot is disconnected. Connect manually in xPilot when appropriate.");
      if(!commandsAvailable)return new UpdateResult("Commands are unavailable because xPilot did not provide a UI synchronization context.");
      if(kind=="reply"&&!settings.AllowReplies)return new UpdateResult("Private replies are disabled in plugin settings.");
      if(kind=="disconnect"&&!settings.AllowDisconnect)return new UpdateResult("Remote disconnect is disabled in plugin settings.");
      return null;
    }
    UpdateResult QueueAction(string kind,string recipient,string text){
      Expire();
      if(lastAction!=null&&(lastAction.Status=="queued"||lastAction.Status=="dispatched"))return new UpdateResult("Another command is still pending. Check /status before trying again.");
      if(clock()-lastCommandAt<1500)return new UpdateResult("Please wait briefly before issuing another command.");
      lastCommandAt=clock();
      lastAction=new BrokerAction{Id=Guid.NewGuid().ToString("N"),Session=session,Kind=kind,Recipient=recipient,Text=text,Status="queued",CreatedAt=clock(),ExpiresAt=clock()+15000};
      return new UpdateResult("Command queued."){Action=lastAction};
    }
    internal UpdateResult Handle(TelegramUpdate u){lock(gate){
      if(closed||u==null||u.Id<nextOffset||u.Id<0||u.Id==Int64.MaxValue)return null;
      nextOffset=u.Id+1;Expire();
      if(u.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture)!=settings.AllowedChatId||u.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)!=settings.AllowedUserId||u.ChatType!="private"||u.IsBot||u.Forwarded||String.IsNullOrWhiteSpace(u.Text))return null;
      long seconds=clock()/1000;
      if(u.UnixSeconds<startedAt/1000||seconds-u.UnixSeconds>60||u.UnixSeconds-seconds>5)return null;
      string value=u.Text.Trim(),actor=u.ChatId+":"+u.UserId;
      if(value=="/status")return new UpdateResult(Status());
      if(value=="/cancel"){confirmation=null;return new UpdateResult("Pending disconnect confirmation cleared. Already dispatched commands cannot be cancelled.");}
      if(value=="/disconnect"){
        var denied=Denied("disconnect");if(denied!=null)return denied;
        if(u.UnixSeconds<sessionStartedAt/1000)return new UpdateResult("This request predates the current xPilot session.");
        if(disconnectIssued||(lastAction!=null&&lastAction.Kind=="disconnect"&&lastAction.Session==session&&lastAction.Status=="queued"))return new UpdateResult("Disconnect has already been requested for this session. Check /status; it will not be sent again.");
        confirmation=new Confirmation{Session=session,Actor=actor,ExpiresAt=clock()+60000,RequestUpdateId=u.Id,RequestedUnixSeconds=u.UnixSeconds};
        return new UpdateResult("Disconnect "+callsign+" from VATSIM? X-Plane stays running. Send /confirm within 60 seconds, or /cancel to cancel. No automatic reconnection.");
      }
      if(value=="/confirm"){
        var denied=Denied("disconnect");if(denied!=null)return denied;
        if(confirmation==null||confirmation.Actor!=actor||confirmation.Session!=session||confirmation.ExpiresAt<=clock()||u.Id<=confirmation.RequestUpdateId||u.UnixSeconds<confirmation.RequestedUnixSeconds)return new UpdateResult("Disconnect confirmation is invalid, expired or already used. Send /disconnect to start a new request.");
        confirmation=null;
        if(disconnectIssued)return new UpdateResult("Disconnect already requested; check /status.");
        return QueueAction("disconnect",null,null);
      }
      if(value.StartsWith("/",StringComparison.Ordinal))return new UpdateResult("Use /status, /disconnect, /confirm, or /cancel. Reply to a recent private-message notification to send a private reply. Radio sends and arbitrary recipient commands are not supported.");
      if(u.ReplyToId!=0){
        var denied=Denied("reply");if(denied!=null)return denied;
        ReplyBinding binding;
        if(!replies.TryGetValue(u.ReplyToId,out binding)||binding.Session!=session||binding.ExpiresAt<=clock())return new UpdateResult("This reply target is unknown or expired. Reply to a private-message notification from this connection within 5 minutes.");
        if(value.Length>220||value.Any(Char.IsControl))return new UpdateResult("Private replies must contain 1–220 characters on one line.");
        return QueueAction("reply",binding.Sender,value);
      }
      return new UpdateResult("Use /status or reply to a recent private notification. /disconnect requires an additional confirmation. Stay present and available to ATC.");
    }}
    internal bool TryBegin(BrokerAction action){lock(gate){
      Expire();
      if(closed||lastAction!=action||action.Status!="queued"||action.Session!=session||clock()>action.ExpiresAt||Denied(action.Kind)!=null){if(action.Status=="queued")action.Status="expired";return false;}
      if(action.Kind=="disconnect"&&disconnectIssued){action.Status="failed";return false;}
      action.Status="dispatched";if(action.Kind=="disconnect")disconnectIssued=true;return true;
    }}
    internal void Complete(BrokerAction action,bool ok){lock(gate){
      if(action.Status=="disconnected")return;
      if(action.Status=="dispatched")action.Status=ok?"submitted":"uncertain";
    }}
    internal void CancelQueued(BrokerAction action){lock(gate){if(action.Status=="queued")action.Status="expired";}}
    internal string ActionReport(BrokerAction action){lock(gate){
      Expire();
      if(action.Status=="disconnected")return "xPilot disconnected — confirmed by its connection event. No automatic reconnect.";
      if(action.Status=="submitted"&&action.Kind=="reply")return "Private reply submitted to xPilot for "+action.Recipient+". The SDK cannot confirm recipient delivery.";
      if(action.Status=="submitted")return "Disconnect submitted to xPilot. Waiting for its connection event; check /status for actual state.";
      return "Command outcome: "+action.Status+". Check /status and xPilot. It will not be retried automatically.";
    }}
    internal void Close(){lock(gate){closed=true;connected=false;InvalidatePending();}}
  }
}
