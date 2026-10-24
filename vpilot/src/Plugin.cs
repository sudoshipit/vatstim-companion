using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RossCarlson.Vatsim.Vpilot.Plugins;
using RossCarlson.Vatsim.Vpilot.Plugins.Events;

namespace FlightCompanion {
  public sealed class Plugin : IPlugin {
    public string Name {get{return "Flight Companion — Telegram (experimental)";}}
    IBroker broker;SynchronizationContext context;PluginSettings settings;ITelegramApi telegram;
    readonly CancellationTokenSource cancellation=new CancellationTokenSource();
    readonly Func<long> clock;readonly bool startWorkers;
    bool initialized,bootstrapped;volatile bool stopped;string lastLog;
    internal BridgeEngine Engine {get;private set;}
    public Plugin(){clock=BridgeEngine.UtcNow;startWorkers=true;}
    internal Plugin(PluginSettings config,ITelegramApi fakeTransport,Func<long> now,bool runWorkers){settings=config;telegram=fakeTransport;clock=now;startWorkers=runWorkers;}
    public void Initialize(IBroker instance) {
      if(initialized)return;initialized=true;broker=instance;context=SynchronizationContext.Current;
      try {
        if(settings==null)settings=PluginSettings.Load(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location),"FlightCompanion.Plugin.json"));
        if(!settings.Enabled){broker.PostDebugMessage("Flight Companion: disabled in configuration. No Telegram connection opened.");return;}
        settings.Validate();
        if(telegram==null)telegram=new TelegramApi(settings.ReadToken(),settings.AllowedChatId);
        Engine=new BridgeEngine(settings,context!=null,clock);
      } catch {
        broker.PostDebugMessage("Flight Companion: disabled; check FlightCompanion.Plugin.json, allowed private chat/user IDs, and BotToken or its named environment variable. No token or message contents are logged.");
        if(telegram!=null)telegram.Dispose();return;
      }
      broker.NetworkConnected+=Connected;broker.NetworkDisconnected+=Disconnected;broker.PrivateMessageReceived+=Private;
      broker.RadioMessageReceived+=Radio;broker.SelcalAlertReceived+=Selcal;broker.SessionEnded+=End;
      broker.PostDebugMessage("Flight Companion: Telegram plugin initialized. Load before connecting manually to VATSIM. No automatic reconnect.");
      if(context==null)broker.PostDebugMessage("Flight Companion: UI synchronization context unavailable. Notifications only; replies/disconnect disabled.");
      if(startWorkers){Task.Run((Func<Task>)PollLoop);Task.Run((Func<Task>)NotificationLoop);}
    }
    void Connected(object sender,NetworkConnectedEventArgs e){Engine.NetworkConnected(e.Callsign);}
    void Disconnected(object sender,EventArgs e){Engine.NetworkDisconnected();}
    void Private(object sender,PrivateMessageReceivedEventArgs e){Engine.Receive("private",e.From,e.Message);}
    void Radio(object sender,RadioMessageReceivedEventArgs e){Engine.Receive("radio",e.From,e.Message);}
    void Selcal(object sender,SelcalAlertReceivedEventArgs e){Engine.Receive("selcal",e.From,"SELCAL alert. Return to the cockpit and respond to ATC.");}
    void LogTransportError() {
      const string message="Flight Companion: Telegram request failed. Check connectivity/configuration. Failed sends are not automatically retried; /status includes dropped notifications.";
      if(stopped||lastLog==message)return;lastLog=message;
      if(context!=null)context.Post(unused=>{if(!stopped)broker.PostDebugMessage(message);},null);
    }
    internal async Task BootstrapAsync() {
      var backlog=await telegram.GetUpdatesAsync(-1,0,cancellation.Token).ConfigureAwait(false);
      Engine.SkipBacklog(backlog);bootstrapped=true;Engine.TransportSuccess();
    }
    internal async Task PollOnceAsync(int timeout) {
      if(!bootstrapped)await BootstrapAsync().ConfigureAwait(false);
      var updates=await telegram.GetUpdatesAsync(Engine.NextOffset,timeout,cancellation.Token).ConfigureAwait(false);
      Engine.TransportSuccess();
      foreach(var update in updates){if(stopped)break;await ProcessUpdateAsync(update).ConfigureAwait(false);}
    }
    async Task PollLoop() {
      while(!stopped) {
        bool retry=false;
        try {await PollOnceAsync(25).ConfigureAwait(false);}
        catch(OperationCanceledException){if(stopped)break;Engine.TransportFailure();LogTransportError();retry=true;}
        catch {if(stopped)break;Engine.TransportFailure();LogTransportError();retry=true;}
        if(retry)await Backoff().ConfigureAwait(false);
      }
    }
    async Task Backoff(){try{await Task.Delay(5000,cancellation.Token).ConfigureAwait(false);}catch(OperationCanceledException){}}
    internal async Task DrainNotificationsAsync() {
      for(int i=0;i<20&&!stopped;i++) {
        var notification=Engine.TakeNotification();if(notification==null)break;
        if(!Engine.NotificationCurrent(notification))continue;
        string text=notification.Kind.ToUpperInvariant()+" · "+notification.Sender+"\n"+notification.Text;
        if(notification.Kind=="private")text+=settings.AllowReplies?"\n\nReply to this notification within 5 minutes to reply privately to this sender.":"\n\nPrivate replies are disabled in plugin settings.";
        if(notification.Kind=="radio")text+="\n\nRadio notification only; radio replies are not supported.";
        try {long id=await telegram.SendTextAsync(text,cancellation.Token).ConfigureAwait(false);Engine.BindReply(id,notification);}
        catch {if(stopped)break;Engine.NotificationFailed();LogTransportError();}
      }
    }
    async Task NotificationLoop() {
      while(!stopped) {
        await DrainNotificationsAsync().ConfigureAwait(false);
        try {await Task.Delay(500,cancellation.Token).ConfigureAwait(false);}catch(OperationCanceledException){break;}
      }
    }
    internal async Task ProcessUpdateAsync(TelegramUpdate update) {
      var result=Engine.Handle(update);if(result==null||stopped)return;
      if(result.Action!=null){await ExecuteAsync(result.Action).ConfigureAwait(false);result.Text=Engine.ActionReport(result.Action);}
      try{await telegram.SendTextAsync(result.Text,cancellation.Token).ConfigureAwait(false);}
      catch {if(!stopped){Engine.TransportFailure();LogTransportError();}}
      // The update watermark has already advanced; a failed acknowledgement never replays the action.
    }
    internal async Task ExecuteAsync(BrokerAction action) {
      if(context==null||stopped){Engine.CancelQueued(action);return;}
      var completion=new TaskCompletionSource<bool>();
      try {
        context.Post(unused=>{
          try {
            if(stopped||!Engine.TryBegin(action)){completion.TrySetResult(false);return;}
            if(action.Kind=="reply")broker.SendPrivateMessage(action.Recipient,action.Text);
            else if(action.Kind=="disconnect")broker.RequestDisconnect();
            Engine.Complete(action,true);completion.TrySetResult(true);
          } catch {Engine.Complete(action,false);completion.TrySetResult(false);}
        },null);
      } catch {Engine.CancelQueued(action);return;}
      Task finished=await Task.WhenAny(completion.Task,Task.Delay(15000,cancellation.Token)).ConfigureAwait(false);
      if(finished!=completion.Task)Engine.CancelQueued(action);
    }
    void End(object sender,EventArgs e) {
      if(stopped)return;stopped=true;Engine.Close();cancellation.Cancel();
      broker.NetworkConnected-=Connected;broker.NetworkDisconnected-=Disconnected;broker.PrivateMessageReceived-=Private;
      broker.RadioMessageReceived-=Radio;broker.SelcalAlertReceived-=Selcal;broker.SessionEnded-=End;
      telegram.Dispose();
    }
  }
}
