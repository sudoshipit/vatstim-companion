using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FlightCompanion;
using RossCarlson.Vatsim.Vpilot.Plugins;
using RossCarlson.Vatsim.Vpilot.Plugins.Events;

// No real vPilot instance, SimConnect or network transport exists in this test executable.
class Clock {internal long Now=1800000000000;internal void Advance(long ms){Now+=ms;}}
class Sent {internal long Id;internal string Text;}
class FakeTelegram : ITelegramApi {
  internal List<Sent> Sent=new List<Sent>();internal List<long> Offsets=new List<long>();
  internal Queue<List<TelegramUpdate>> Batches=new Queue<List<TelegramUpdate>>();
  internal bool FailSend,FailRead,Disposed;internal int SendAttempts;long nextId=100;
  public Task<List<TelegramUpdate>> GetUpdatesAsync(long offset,int timeout,CancellationToken cancel){Offsets.Add(offset);if(FailRead)throw new Exception("SECRET-NEVER-LOG");return Task.FromResult(Batches.Count>0?Batches.Dequeue():new List<TelegramUpdate>());}
  public Task<long> SendTextAsync(string text,CancellationToken cancel){SendAttempts++;if(FailSend)throw new Exception("SECRET-NEVER-LOG");var sent=new Sent{Id=nextId++,Text=text};Sent.Add(sent);return Task.FromResult(sent.Id);}
  public void Dispose(){Disposed=true;}
}
class MockContext : SynchronizationContext {
  internal bool Immediate=true;readonly Queue<Action> callbacks=new Queue<Action>();
  public override void Post(SendOrPostCallback callback,object state){if(Immediate)callback(state);else callbacks.Enqueue(()=>callback(state));}
  internal void RunAll(){while(callbacks.Count>0)callbacks.Dequeue()();}
}
class FakeBroker : RealProxy {
  readonly Dictionary<string,Delegate> handlers=new Dictionary<string,Delegate>();
  internal List<string> Calls=new List<string>(),Logs=new List<string>();
  internal bool ThrowReply,ThrowDisconnect,AutoDisconnect;
  internal FakeBroker():base(typeof(IBroker)){}
  internal IBroker Proxy {get{return (IBroker)GetTransparentProxy();}}
  public override IMessage Invoke(IMessage input){
    var m=(IMethodCallMessage)input;
    try {
      if(m.MethodName.StartsWith("add_")){string name=m.MethodName.Substring(4);Delegate old;handlers.TryGetValue(name,out old);handlers[name]=Delegate.Combine(old,(Delegate)m.Args[0]);}
      else if(m.MethodName.StartsWith("remove_")){string name=m.MethodName.Substring(7);Delegate old;handlers.TryGetValue(name,out old);handlers[name]=Delegate.Remove(old,(Delegate)m.Args[0]);}
      else if(m.MethodName=="PostDebugMessage")Logs.Add((string)m.Args[0]);
      else if(m.MethodName=="SendPrivateMessage"){Calls.Add("reply|"+m.Args[0]+"|"+m.Args[1]);if(ThrowReply)throw new Exception("Broker outcome unknown");}
      else if(m.MethodName=="RequestDisconnect"){Calls.Add("disconnect");if(AutoDisconnect)Raise("NetworkDisconnected",EventArgs.Empty);if(ThrowDisconnect)throw new Exception("Broker outcome unknown");}
      else throw new Exception("Unexpected broker method: "+m.MethodName);
      return new ReturnMessage(null,null,0,m.LogicalCallContext,m);
    } catch(Exception ex){return new ReturnMessage(ex,m);}
  }
  internal void Raise(string name,EventArgs args){Delegate handler;if(handlers.TryGetValue(name,out handler)&&handler!=null)handler.DynamicInvoke(this,args);}
  internal void Connect(string callsign="TEST123"){Raise("NetworkConnected",new NetworkConnectedEventArgs("0",callsign,"C172","",false));}
  internal int HandlerCount {get{return handlers.Values.Where(v=>v!=null).Sum(v=>v.GetInvocationList().Length);}}
}
class Fixture : IDisposable {
  internal Clock Clock=new Clock();internal FakeTelegram Api=new FakeTelegram();internal FakeBroker Broker=new FakeBroker();
  internal PluginSettings Config;internal Plugin Plugin;internal MockContext Ui;long id=1;
  internal Fixture(PluginSettings config=null,bool ui=true){
    Config=config??Tests.Config();Ui=ui?new MockContext():null;
    var old=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(Ui);
    Plugin=new Plugin(Config,Api,()=>Clock.Now,false);Plugin.Initialize(Broker.Proxy);SynchronizationContext.SetSynchronizationContext(old);
  }
  internal TelegramUpdate Update(string text,long reply=0){return new TelegramUpdate{Id=id++,ChatId=123,UserId=456,ChatType="private",UnixSeconds=Clock.Now/1000,Text=text,ReplyToId=reply};}
  internal void Process(TelegramUpdate u){Plugin.ProcessUpdateAsync(u).GetAwaiter().GetResult();}
  internal long PrivateNotification(){Broker.Raise("PrivateMessageReceived",new PrivateMessageReceivedEventArgs("TEST_CTR","Synthetic message only"));Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();return Api.Sent.Last(s=>s.Text.StartsWith("PRIVATE")).Id;}
  internal void RequestDisconnect(){Process(Update("/disconnect"));Tests.Assert(Api.Sent.Last().Text.Contains("Send /confirm within 60 seconds"));}
  public void Dispose(){Broker.Raise("SessionEnded",EventArgs.Empty);}
}
class Tests {
  static int passed,failed;
  internal static PluginSettings Config(){return new PluginSettings{Enabled=true,AllowedChatId="123",AllowedUserId="456",AllowReplies=true,AllowDisconnect=true};}
  internal static void Assert(bool condition,string message="Assertion failed"){if(!condition)throw new Exception(message);}
  static void Test(string name,Action body){try{body();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex.Message);failed++;}}
  static void Throws(Action body){bool threw=false;try{body();}catch{threw=true;}Assert(threw,"Expected rejection");}
  static BridgeEngine Engine(Clock clock,PluginSettings config=null,bool context=true){var engine=new BridgeEngine(config??Config(),context,()=>clock.Now);engine.NetworkConnected("TEST123");return engine;}
  static TelegramUpdate Update(Clock clock,long id,string text,long reply=0){return new TelegramUpdate{Id=id,ChatId=123,UserId=456,ChatType="private",UnixSeconds=clock.Now/1000,Text=text,ReplyToId=reply};}
  static void Bind(BridgeEngine engine,long id=88){engine.Receive("private","TEST_CTR","Test private message");Notification n;while((n=engine.TakeNotification())!=null)if(n.Kind=="private"){engine.BindReply(id,n);return;}throw new Exception("No private notification");}
  static BrokerAction Disconnect(BridgeEngine engine,Clock clock,long id=1){engine.Handle(Update(clock,id,"/disconnect"));return engine.Handle(Update(clock,id+1,"/confirm")).Action;}
  static int Main(){
    Test("disabled configuration opens no transport and registers no handlers",()=>{using(var f=new Fixture(new PluginSettings())){Assert(f.Plugin.Engine==null);Assert(f.Api.Offsets.Count==0&&f.Api.SendAttempts==0);Assert(f.Broker.HandlerCount==0);}});
    Test("configuration requires explicit positive private-chat and user IDs",()=>{var c=Config();c.AllowedChatId="-123";Throws(c.Validate);c=Config();c.AllowedUserId="";Throws(c.Validate);c=Config();c.BotTokenEnvironmentVariable="BAD NAME";Throws(c.Validate);});
    Test("missing token fails closed without storing credentials",()=>{var c=Config();c.BotTokenEnvironmentVariable="FC_ABSENT_TEST_"+Guid.NewGuid().ToString("N");Throws(()=>c.ReadToken());});
    Test("JSON BotToken works without an environment variable",()=>{var c=Config();c.BotToken="123456:ABCDEFGHIJKLMNOPQRSTUVWX";c.BotTokenEnvironmentVariable="FC_ABSENT_TEST_"+Guid.NewGuid().ToString("N");c.Validate();Assert(c.ReadToken()==c.BotToken);});
    Test("explicit JSON token takes precedence over environment token",()=>{string name="FC_TOKEN_TEST_"+Guid.NewGuid().ToString("N");try{Environment.SetEnvironmentVariable(name,"987654:ZYXWVUTSRQPONMLKJIHGFEDC");var c=Config();c.BotTokenEnvironmentVariable=name;c.BotToken="123456:ABCDEFGHIJKLMNOPQRSTUVWX";Assert(c.ReadToken()==c.BotToken);}finally{Environment.SetEnvironmentVariable(name,null);}});
    Test("blank JSON token retains environment fallback",()=>{string name="FC_TOKEN_TEST_"+Guid.NewGuid().ToString("N");try{const string fake="123456:ABCDEFGHIJKLMNOPQRSTUVWX";Environment.SetEnvironmentVariable(name,fake);var c=Config();c.BotToken=" ";c.BotTokenEnvironmentVariable=name;c.Validate();Assert(c.ReadToken()==fake);}finally{Environment.SetEnvironmentVariable(name,null);}});
    Test("malformed explicit JSON token fails closed and is never disclosed",()=>{string name="FC_TOKEN_TEST_"+Guid.NewGuid().ToString("N");try{Environment.SetEnvironmentVariable(name,"123456:ABCDEFGHIJKLMNOPQRSTUVWX");var c=Config();c.BotTokenEnvironmentVariable=name;c.BotToken="INVALID-SECRET-NEVER-LOG";Throws(c.Validate);try{c.ReadToken();throw new Exception("Unexpected token acceptance");}catch(System.IO.InvalidDataException ex){Assert(!ex.ToString().Contains(c.BotToken));}}finally{Environment.SetEnvironmentVariable(name,null);}});
    Test("JSON token loads using actual settings deserialization",()=>{string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"fc-json-test-"+Guid.NewGuid().ToString("N")+".json");System.IO.File.WriteAllText(path,"{\"Enabled\":true,\"AllowedChatId\":\"123\",\"AllowedUserId\":\"456\",\"BotToken\":\" 123456:ABCDEFGHIJKLMNOPQRSTUVWX \",\"BotTokenEnvironmentVariable\":\"\"}");var c=PluginSettings.Load(path);c.Validate();Assert(c.ReadToken()=="123456:ABCDEFGHIJKLMNOPQRSTUVWX");});
    Test("token mistakenly placed in environment-variable name is rejected",()=>{var c=Config();c.BotTokenEnvironmentVariable="123456:ABCDEFGHIJKLMNOPQRSTUVWX";Throws(c.Validate);Throws(()=>c.ReadToken());});
    Test("initial connection is unknown until the SDK connection event",()=>{using(var f=new Fixture()){f.Process(f.Update("/disconnect"));Assert(f.Broker.Calls.Count==0);Assert(f.Api.Sent.Last().Text.Contains("no connection event"));}});
    Test("plain confirm requires a pending disconnect request",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.Count==0&&f.Api.Sent.Last().Text.Contains("invalid"));f.RequestDisconnect();f.Process(f.Update("/confirm unwanted-argument"));Assert(f.Broker.Calls.Count==0);f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.SequenceEqual(new[]{"disconnect"}));}});
    Test("confirmation predating a replacement request cannot disconnect",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();var oldConfirm=f.Update("/confirm");f.Clock.Advance(2000);f.RequestDisconnect();oldConfirm.Id=100;f.Process(oldConfirm);Assert(f.Broker.Calls.Count==0);f.Process(Update(f.Clock,101,"/confirm"));Assert(f.Broker.Calls.SequenceEqual(new[]{"disconnect"}));}});
    Test("disconnect can be requested again after a new connection",()=>{using(var f=new Fixture()){f.Broker.AutoDisconnect=true;f.Broker.Connect();f.RequestDisconnect();f.Process(f.Update("/confirm"));f.Clock.Advance(2000);f.Broker.Connect("NEXT123");f.RequestDisconnect();f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.Count==2&&f.Plugin.Engine.LastStatus=="disconnected");}});
    Test("compiled plugin forwards private event and binds reply to original sender",()=>{using(var f=new Fixture()){f.Broker.Connect();long reply=f.PrivateNotification();f.Process(f.Update("Wilco",reply));Assert(f.Broker.Calls.SequenceEqual(new[]{"reply|TEST_CTR|Wilco"}));Assert(f.Api.Sent.Last().Text.Contains("cannot confirm recipient delivery"));}});
    Test("SELCAL forwards without a reply target",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Broker.Raise("SelcalAlertReceived",new SelcalAlertReceivedEventArgs(new[]{23725},"TEST_CTR"));f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();var n=f.Api.Sent.Last();Assert(n.Text.StartsWith("SELCAL"));f.Process(f.Update("Wilco",n.Id));Assert(f.Broker.Calls.Count==0);}});
    Test("radio is opt-in, callsign filtered and always receive-only",()=>{var c=Config();c.ForwardRadioMessages=true;using(var f=new Fixture(c)){f.Broker.Connect();f.Broker.Raise("RadioMessageReceived",new RadioMessageReceivedEventArgs(new[]{23725},"TEST_CTR","OTHER123 hello"));f.Broker.Raise("RadioMessageReceived",new RadioMessageReceivedEventArgs(new[]{23725},"TEST_CTR","TEST1234 hello"));f.Broker.Raise("RadioMessageReceived",new RadioMessageReceivedEventArgs(new[]{23725},"TEST_CTR","TEST123 contact control"));f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();Assert(f.Api.Sent.Count(s=>s.Text.StartsWith("RADIO"))==1);long radio=f.Api.Sent.Last().Id;f.Process(f.Update("Wilco",radio));Assert(f.Broker.Calls.Count==0);}});
    Test("radio forwarding is disabled by default",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Broker.Raise("RadioMessageReceived",new RadioMessageReceivedEventArgs(new[]{23725},"TEST_CTR","TEST123 hello"));f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();Assert(!f.Api.Sent.Any(s=>s.Text.StartsWith("RADIO")));}});
    Test("unauthorized user/chat, groups, bots and forwarded messages are ignored",()=>{using(var f=new Fixture()){f.Broker.Connect();var u=f.Update("/disconnect");u.UserId=999;f.Process(u);u=f.Update("/disconnect");u.ChatId=999;f.Process(u);u=f.Update("/disconnect");u.ChatType="group";f.Process(u);u=f.Update("/disconnect");u.IsBot=true;f.Process(u);u=f.Update("/disconnect");u.Forwarded=true;f.Process(u);Assert(f.Api.Sent.Count==0&&f.Broker.Calls.Count==0);}});
    Test("stale, future and pre-start updates are ignored",()=>{using(var f=new Fixture()){f.Broker.Connect();var u=f.Update("/disconnect");u.UnixSeconds-=100;f.Process(u);u=f.Update("/disconnect");u.UnixSeconds+=60;f.Process(u);Assert(f.Api.Sent.Count==0);}});
    Test("update replay sends neither second acknowledgement nor second command",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();var u=f.Update("Wilco",target);f.Process(u);int sent=f.Api.Sent.Count;f.Process(u);Assert(f.Broker.Calls.Count==1&&f.Api.Sent.Count==sent);}});
    Test("bootstrap discards old backlog before accepting new updates",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Api.Batches.Enqueue(new List<TelegramUpdate>{Update(f.Clock,900,"/disconnect")});f.Api.Batches.Enqueue(new List<TelegramUpdate>{Update(f.Clock,901,"/status")});f.Plugin.PollOnceAsync(0).GetAwaiter().GetResult();Assert(f.Api.Offsets.SequenceEqual(new long[]{-1,901}));Assert(f.Api.Sent.Count==1&&f.Api.Sent[0].Text.StartsWith("vPilot:"));Assert(f.Broker.Calls.Count==0);}});
    Test("bootstrap failure retries bootstrap instead of executing backlog",()=>{using(var f=new Fixture()){f.Api.FailRead=true;Throws(()=>f.Plugin.PollOnceAsync(0).GetAwaiter().GetResult());f.Api.FailRead=false;f.Plugin.PollOnceAsync(0).GetAwaiter().GetResult();Assert(f.Api.Offsets.Take(2).SequenceEqual(new long[]{-1,-1}));Assert(f.Broker.Calls.Count==0);}});
    Test("reply mapping expires after five minutes",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();f.Clock.Advance(300001);f.Process(f.Update("Wilco",target));Assert(f.Broker.Calls.Count==0&&f.Api.Sent.Last().Text.Contains("expired"));}});
    Test("connection change invalidates old private reply routing",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();f.Broker.Connect("NEW456");f.Process(f.Update("Wilco",target));Assert(f.Broker.Calls.Count==0);}});
    Test("private replies enforce length and control-character validation",()=>{var clock=new Clock();var e=Engine(clock);Bind(e);Assert(e.Handle(Update(clock,1,new string('x',221),88)).Action==null);Assert(e.Handle(Update(clock,2,"hello\nworld",88)).Action==null);Assert(e.Handle(Update(clock,3," \t ",88))==null);});
    Test("arbitrary recipient/radio/flight-control commands never invoke broker",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Process(f.Update("/pm OTHER text"));f.Process(f.Update("/radio test"));f.Process(f.Update("/ident"));Assert(f.Broker.Calls.Count==0);}});
    Test("reply and disconnect opt-ins are separately enforced",()=>{var c=Config();c.AllowReplies=false;c.AllowDisconnect=false;using(var f=new Fixture(c)){f.Broker.Connect();long target=f.PrivateNotification();f.Process(f.Update("Wilco",target));Assert(f.Api.Sent.Last().Text.Contains("disabled"));f.Process(f.Update("/disconnect"));Assert(f.Api.Sent.Last().Text.Contains("disabled"));Assert(f.Broker.Calls.Count==0);}});
    Test("missing UI context permits notifications but denies all broker commands",()=>{using(var f=new Fixture(null,false)){f.Broker.Connect();long target=f.PrivateNotification();f.Process(f.Update("Wilco",target));f.Process(f.Update("/disconnect"));Assert(f.Broker.Calls.Count==0&&f.Api.Sent.Last().Text.Contains("synchronization context"));}});
    Test("disconnect needs confirmation and reports submitted until real event",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();Assert(f.Broker.Calls.Count==0);f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.SequenceEqual(new[]{"disconnect"}));Assert(f.Plugin.Engine.LastStatus=="submitted"&&f.Plugin.Engine.Connected);f.Broker.Raise("NetworkDisconnected",EventArgs.Empty);Assert(f.Plugin.Engine.LastStatus=="disconnected"&&!f.Plugin.Engine.Connected);f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();Assert(f.Api.Sent.Last().Text.Contains("connection event confirmed"));}});
    Test("synchronous disconnection event cannot be downgraded to submitted",()=>{using(var f=new Fixture()){f.Broker.AutoDisconnect=true;f.Broker.Connect();f.RequestDisconnect();f.Process(f.Update("/confirm"));Assert(f.Plugin.Engine.LastStatus=="disconnected");Assert(f.Api.Sent.Last().Text.Contains("confirmed by its connection event"));}});
    Test("disconnect confirmation is single-use and repeated requests are idempotent",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();f.Process(f.Update("/confirm"));f.Process(f.Update("/confirm"));f.Process(f.Update("/disconnect"));Assert(f.Broker.Calls.Count==1&&f.Api.Sent.Last().Text.Contains("already"));}});
    Test("disconnect confirmation rejects another actor and expires after sixty seconds",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();var wrong=f.Update("/confirm");wrong.UserId=999;f.Process(wrong);Assert(f.Broker.Calls.Count==0);f.Clock.Advance(60001);f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.Count==0&&f.Api.Sent.Last().Text.Contains("expired"));}});
    Test("disconnect confirmation is invalidated by reconnect/session change",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();f.Broker.Connect("OTHER");f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.Count==0);}});
    Test("cancel clears an unsubmitted disconnect confirmation",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();f.Process(f.Update("/cancel"));f.Process(f.Update("/confirm"));Assert(f.Broker.Calls.Count==0);}});
    Test("disconnect does not falsely succeed when no state event arrives",()=>{using(var f=new Fixture()){f.Broker.Connect();f.RequestDisconnect();f.Process(f.Update("/confirm"));f.Clock.Advance(16000);f.Process(f.Update("/status"));Assert(f.Plugin.Engine.LastStatus=="uncertain"&&f.Plugin.Engine.Connected);f.Process(f.Update("/disconnect"));Assert(f.Broker.Calls.Count==1);}});
    Test("broker failure is uncertain and never automatically retried",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Broker.ThrowDisconnect=true;f.RequestDisconnect();f.Process(f.Update("/confirm"));Assert(f.Plugin.Engine.LastStatus=="uncertain");f.Process(f.Update("/disconnect"));Assert(f.Broker.Calls.Count==1);}});
    Test("reply broker error remains uncertain without automatic resend",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Broker.ThrowReply=true;long target=f.PrivateNotification();var u=f.Update("Wilco",target);f.Process(u);f.Process(u);Assert(f.Plugin.Engine.LastStatus=="uncertain"&&f.Broker.Calls.Count==1);}});
    Test("failed Telegram acknowledgement cannot replay an executed command",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();f.Api.FailSend=true;var u=f.Update("Wilco",target);f.Process(u);f.Process(u);Assert(f.Broker.Calls.Count==1);Assert(!String.Join(" ",f.Broker.Logs).Contains("SECRET-NEVER-LOG"));}});
    Test("failed notifications are dropped once and do not create reply bindings",()=>{using(var f=new Fixture()){f.Broker.Connect();f.Api.FailSend=true;f.Broker.Raise("PrivateMessageReceived",new PrivateMessageReceivedEventArgs("TEST_CTR","Test"));f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();int attempts=f.Api.SendAttempts;f.Plugin.DrainNotificationsAsync().GetAwaiter().GetResult();Assert(f.Api.SendAttempts==attempts&&f.Plugin.Engine.Dropped==2);f.Api.FailSend=false;f.Process(f.Update("Wilco",100));Assert(f.Broker.Calls.Count==0);}});
    Test("stale buffered notifications are dropped before Telegram send",()=>{var clock=new Clock();var e=Engine(clock);e.Receive("private","TEST_CTR","Test");clock.Advance(300001);Assert(e.TakeNotification()==null&&e.Dropped==2);});
    Test("notification queue is bounded",()=>{var clock=new Clock();var e=Engine(clock);for(int i=0;i<150;i++)e.Receive("private","TEST_CTR","Test");int n=0;while(e.TakeNotification()!=null)n++;Assert(n==100&&e.Dropped==51);});
    Test("pending commands expire before a delayed UI callback executes",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();f.Ui.Immediate=false;var task=f.Plugin.ProcessUpdateAsync(f.Update("Wilco",target));f.Clock.Advance(16000);f.Ui.RunAll();task.GetAwaiter().GetResult();Assert(f.Broker.Calls.Count==0&&f.Plugin.Engine.LastStatus=="expired");}});
    Test("session change cancels command waiting on UI context",()=>{using(var f=new Fixture()){f.Broker.Connect();long target=f.PrivateNotification();f.Ui.Immediate=false;var task=f.Plugin.ProcessUpdateAsync(f.Update("Wilco",target));f.Broker.Connect("NEW123");f.Ui.RunAll();task.GetAwaiter().GetResult();Assert(f.Broker.Calls.Count==0&&f.Plugin.Engine.LastStatus=="expired");}});
    Test("command rate limit prevents bursts",()=>{var clock=new Clock();var e=Engine(clock);Bind(e);var first=e.Handle(Update(clock,1,"Wilco",88)).Action;Assert(e.TryBegin(first));e.Complete(first,true);Assert(e.Handle(Update(clock,2,"Again",88)).Action==null);clock.Advance(1500);Assert(e.Handle(Update(clock,3,"Again",88)).Action!=null);});
    Test("parser retains unsupported IDs but rejects malformed message bodies",()=>{var data=new JavaScriptSerializer().DeserializeObject("[{\"update_id\":77,\"edited_message\":{}},{\"update_id\":78,\"message\":{\"text\":\"/disconnect\"}}]");var updates=TelegramApi.ParseUpdates(data);Assert(updates.Count==2&&updates[0].Id==77&&updates[1].Text==null);});
    Test("session end unsubscribes handlers and disables pending execution",()=>{var f=new Fixture();f.Broker.Connect();Assert(f.Broker.HandlerCount==6);f.Dispose();Assert(f.Broker.HandlerCount==0&&f.Api.Disposed);f.Broker.Connect();Assert(!f.Plugin.Engine.Connected);});
    Console.WriteLine("RESULT: "+passed+" passed, "+failed+" failed. All Telegram and broker actions were mocked.");
    return failed==0?0:1;
  }
}
