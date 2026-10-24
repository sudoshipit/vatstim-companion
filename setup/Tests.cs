using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FlightCompanionSetup;
class FakeHttp : HttpMessageHandler {
  internal readonly Queue<string> Responses=new Queue<string>();
  internal readonly List<string> Methods=new List<string>();
  internal bool Fail;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel) {
    if(Fail)throw new Exception("SYNTHETIC-SECRET-DO-NOT-LOG");
    if(request.RequestUri.Host!="api.telegram.org"||request.RequestUri.Scheme!="https")throw new Exception("Wrong destination");
    Methods.Add(request.RequestUri.Segments.Last());
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Responses.Dequeue())});
  }
}
class SetupTests {
  static int passed,failed;const string Token="123456:ABCDEFGHIJKLMNOPQRSTUVWX";
  static void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
  static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+" ("+e.GetType().Name+")");}}
  static object Update(Pairing pair,long id=1,string type="private",long chat=123,long user=456,bool bot=false,long date=1000,string text=null,bool forwarded=false) {
    var m=new Dictionary<string,object>{{"chat",new Dictionary<string,object>{{"id",chat},{"type",type}}},{"from",new Dictionary<string,object>{{"id",user},{"is_bot",bot}}},{"date",date},{"text",text??pair.Code}};
    if(forwarded)m["forward_origin"]=new {};return new object[]{new Dictionary<string,object>{{"update_id",id},{"message",m}}};
  }
  static Pairing Paired(){var p=new Pairing(1000);Check(p.Accept(Update(p),1000));return p;}
  static void Throws(Action action){bool thrown=false;try{action();}catch(SetupFailure){thrown=true;}Check(thrown);}
  static int Main() {
    Run("pairing codes are unpredictable 128-bit values",()=>{var a=new Pairing(1000);var b=new Pairing(1000);Check(a.Code.Length==37&&a.Code!=b.Code);});
    Run("private nonce captures exact chat and user IDs once",()=>{var p=Paired();Check(p.ChatId==123&&p.UserId==456);Check(!p.Accept(Update(p,id:2,chat:999),1000)&&p.ChatId==123);});
    Run("groups bots forwarded messages and wrong text cannot pair",()=>{
      foreach(int n in Enumerable.Range(0,6)){var p=new Pairing(1000);Check(!p.Accept(Update(p,type:n==0?"group":"private",bot:n==1,forwarded:n==2,text:n==3?"wrong":null,chat:n==4?-1:123,user:n==5?0:456),1000));}
    });
    Run("old future expired and pre-start messages cannot pair",()=>{
      var p=new Pairing(1000);Check(!p.Accept(Update(p,date:999),1000));
      p=new Pairing(1000);Check(!p.Accept(Update(p,date:1006),1000));
      p=new Pairing(1000);Check(!p.Accept(Update(p,date:1000),1061));
      p=new Pairing(1000);Check(!p.Accept(Update(p,date:1301),1301));
    });
    Run("unsupported updates advance offsets and replay cannot pair",()=>{var p=new Pairing(1000);Check(!p.Accept(new object[]{new Dictionary<string,object>{{"update_id",5}}},1000));Check(p.Offset==6);Check(!p.Accept(Update(p,id:5),1000));});
    Run("malformed and extreme update IDs are ignored safely",()=>{var p=new Pairing(1000);Check(!p.Accept(new object[]{new Dictionary<string,object>{{"update_id",Int64.MaxValue}},new Dictionary<string,object>{{"update_id","bad"}}},1000));Check(p.Offset==0);});
    Run("pairing is required before configuration",()=>Throws(()=>new Pairing(1000).Settings(false,false)));
    Run("allowlists and independent command opt-ins are retained",()=>{var p=Paired();var c=p.Settings(true,false);Check(c.Enabled&&c.AllowReplies&&!c.AllowDisconnect&&c.AllowedChatId=="123"&&c.AllowedUserId=="456");Check(p.Settings(false,true).AllowDisconnect);});
    Run("invalid token is rejected before network access",()=>Throws(()=>new BotClient("invalid",new FakeHttp())));
    Run("bot identity and absence of webhook are verified",()=>{
      var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":{\"is_bot\":true,\"username\":\"synthetic_bot\"}}");h.Responses.Enqueue("{\"ok\":true,\"result\":{\"url\":\"\"}}");
      using(var c=new BotClient(Token,h)){Check(c.Verify(CancellationToken.None).GetAwaiter().GetResult()=="synthetic_bot");Check(h.Methods.SequenceEqual(new[]{"getMe","getWebhookInfo"}));}
    });
    Run("existing webhook blocks setup without changing it",()=>{
      var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":{\"is_bot\":true,\"username\":\"synthetic_bot\"}}");h.Responses.Enqueue("{\"ok\":true,\"result\":{\"url\":\"https://example.test/hook\"}}");
      using(var c=new BotClient(Token,h))Throws(()=>c.Verify(CancellationToken.None).GetAwaiter().GetResult());Check(h.Methods.Count==2);
    });
    Run("setup cannot send messages or alter webhook settings",()=>{var h=new FakeHttp();using(var c=new BotClient(Token,h)){Throws(()=>c.Call("sendMessage",new {},CancellationToken.None).GetAwaiter().GetResult());Throws(()=>c.Call("deleteWebhook",new {},CancellationToken.None).GetAwaiter().GetResult());}Check(h.Methods.Count==0);});
    Run("transport errors never disclose token URL or inner exception",()=>{using(var c=new BotClient(Token,new FakeHttp{Fail=true})){try{c.Call("getMe",new {},CancellationToken.None).GetAwaiter().GetResult();Check(false);}catch(SetupFailure e){Check(!e.ToString().Contains(Token)&&!e.ToString().Contains("SYNTHETIC-SECRET")&&!e.ToString().Contains("api.telegram"));}}});
    Run("launch token goes only to child environment not arguments",()=>{using(var c=new BotClient(Token,new FakeHttp())){var info=c.LaunchInfo(Path.Combine(Path.GetTempPath(),"vPilot.exe"),"FC_SETUP_TEST_TOKEN");Check(info.EnvironmentVariables["FC_SETUP_TEST_TOKEN"]==Token&&!info.Arguments.Contains(Token)&&!info.UseShellExecute);Check(Environment.GetEnvironmentVariable("FC_SETUP_TEST_TOKEN")==null);}});
    Run("installation preserves unrelated files backups and token-free config",()=>{
      string root=Path.Combine(Path.GetTempPath(),"fc-setup-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
      string source=Path.Combine(root,"source.dll"),target=Path.Combine(root,"Plugins");Directory.CreateDirectory(target);
      File.WriteAllBytes(source,new byte[]{1,2,3});File.WriteAllText(Path.Combine(target,"unrelated.txt"),"preserve");
      File.WriteAllText(Path.Combine(target,"FlightCompanion.Plugin.dll"),"previous-dll");File.WriteAllText(Path.Combine(target,"FlightCompanion.Plugin.json"),"previous-config");
      string path=Installer.Install(source,target,Paired().Settings(false,false));string json=File.ReadAllText(path);
      Check(!json.Contains(Token)&&json.Contains("\"AllowedUserId\":\"456\"")&&File.ReadAllText(Path.Combine(target,"unrelated.txt"))=="preserve");
      Check(Directory.GetFiles(target,"*.backup-*").Length==2&&File.ReadAllBytes(Path.Combine(target,"FlightCompanion.Plugin.dll")).SequenceEqual(new byte[]{1,2,3}));
      // Leave the small synthetic fixture in TEMP; no real plugin directory is touched.
    });
    Console.WriteLine("RESULT: "+passed+" passed, "+failed+" failed. No live Telegram, vPilot or credential access.");return failed==0?0:1;
  }
}
