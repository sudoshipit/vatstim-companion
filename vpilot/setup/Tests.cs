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
using FlightCompanion;
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
  static string CustomInstallation() {
    string root=Path.Combine(Environment.GetEnvironmentVariable("FC_SETUP_TEST_ROOT")??Path.GetTempPath(),"fc-location-test-"+Guid.NewGuid().ToString("N"),"Flight tools","Custom vPilot");
    Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,"vPilot.exe"),new byte[]{1,2,3});
    File.WriteAllBytes(Path.Combine(root,"RossCarlson.Vatsim.Vpilot.Plugins.dll"),new byte[]{4,5,6});return root;
  }
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
    Run("normal launch needs no token in arguments or a setup environment",()=>{var info=VpilotLocation.LaunchInfo(Path.GetTempPath());Check(info.Arguments==""&&!info.UseShellExecute&&info.EnvironmentVariables["FC_SETUP_TEST_TOKEN"]==null);});
    Run("Start link pairs only the exact private nonce",()=>{
      var p=new Pairing(1000);Check(p.Accept(Update(p,text:"/start "+p.StartPayload),1000)&&p.ChatId==123&&p.UserId==456);
      p=new Pairing(1000);Check(!p.Accept(Update(p,text:"/start wrong"),1000));Check(!p.Accept(Update(p,id:2,text:"/start "+p.StartPayload,type:"group"),1000));
    });
    Run("Start link retains expiry and single-use pairing",()=>{
      var p=new Pairing(1000);Check(!p.Accept(Update(p,date:1301,text:"/start "+p.StartPayload),1301));
      p=new Pairing(1000);Check(p.Accept(Update(p,text:"/start "+p.StartPayload),1000));Check(!p.Accept(Update(p,id:2,user:999,text:"/start "+p.StartPayload),1000));
    });
    Run("pairing URL contains only bot name and one-time payload",()=>{var p=new Pairing(1000);Check(p.Link("synthetic_bot")=="https://t.me/synthetic_bot?start="+p.StartPayload&&p.StartPayload.Length==35&&!p.Link("synthetic_bot").Contains(Token));});
    Run("setup saves encrypted token with no plaintext in configuration",()=>{
      var settings=Paired().Settings(false,false);using(var client=new BotClient(Token,new FakeHttp()))client.SaveToken(settings);
      settings.Validate();Check(settings.ReadToken()==Token&&settings.BotToken==""&&!String.IsNullOrEmpty(settings.ProtectedBotToken));
      Check(!new JavaScriptSerializer().Serialize(settings).Contains(Token));
    });
    Run("embedded install works without a neighboring release folder",()=>{
      string root=CustomInstallation();var settings=Paired().Settings(false,false);using(var client=new BotClient(Token,new FakeHttp()))client.SaveToken(settings);
      string configPath=Installer.InstallEmbedded(Path.Combine(root,"Plugins"),settings);
      var loaded=PluginSettings.Load(configPath);Check(loaded.ReadToken()==Token&&!File.ReadAllText(configPath).Contains(Token));
      Check(System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(root,"Plugins","FlightCompanion.Plugin.dll")).Version.ToString()=="0.2.3.0");
    });
    Run("custom installation accepts a folder or quoted executable with spaces",()=>{
      string root=CustomInstallation();Check(VpilotLocation.Validate(root,p=>"3.12.1.0")==root);
      Check(VpilotLocation.Validate("\""+Path.Combine(root,"vPilot.exe")+"\"",p=>"3.12.1.0")==root);
    });
    Run("blank relative and missing installation paths fail before version lookup",()=>{
      foreach(string value in new[]{"", "vPilot", "vPilot.exe", "C:vPilot.exe", "\\vPilot.exe", Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"vPilot.exe")})
        Throws(()=>VpilotLocation.Validate(value,p=>{throw new Exception("Must not inspect version");}));
    });
    Run("another executable cannot be selected as vPilot",()=>{
      string root=CustomInstallation(),other=Path.Combine(root,"other.exe");File.WriteAllText(other,"fixture");Throws(()=>VpilotLocation.Validate(other,p=>"3.12.1.0"));
    });
    Run("incomplete installation without SDK is rejected",()=>{
      string root=CustomInstallation();File.Delete(Path.Combine(root,"RossCarlson.Vatsim.Vpilot.Plugins.dll"));Throws(()=>VpilotLocation.Validate(root,p=>"3.12.1.0"));
    });
    Run("version guard also applies to custom locations",()=>{
      string root=CustomInstallation();Throws(()=>VpilotLocation.Validate(root,p=>"4.0.0.0"));Throws(()=>VpilotLocation.Validate(root,p=>null));
    });
    Run("environment path expands without changing the chosen installation",()=>{
      string root=CustomInstallation(),name="FC_LOCATION_TEST_"+Guid.NewGuid().ToString("N");
      try {Environment.SetEnvironmentVariable(name,root);Check(VpilotLocation.Validate("%"+name+"%",p=>"3.12.1.0")==root);}finally{Environment.SetEnvironmentVariable(name,null);}
    });
    Run("custom installation creates Plugins and leaves a different installation untouched",()=>{
      string root=CustomInstallation(),other=CustomInstallation(),source=Path.Combine(root,"source.dll");
      File.WriteAllBytes(source,new byte[]{7,8,9});string selected=VpilotLocation.Validate(root,p=>"3.12.1.0");
      string result=Installer.Install(source,Path.Combine(selected,"Plugins"),Paired().Settings(false,false));
      Check(result==Path.Combine(root,"Plugins","FlightCompanion.Plugin.json")&&File.ReadAllBytes(Path.Combine(root,"Plugins","FlightCompanion.Plugin.dll")).SequenceEqual(new byte[]{7,8,9}));
      Check(!Directory.Exists(Path.Combine(other,"Plugins")));
    });
    Run("missing package DLL does not create destination directories",()=>{
      string root=CustomInstallation(),plugins=Path.Combine(root,"Plugins");Throws(()=>Installer.Install(Path.Combine(root,"missing.dll"),plugins,Paired().Settings(false,false)));Check(!Directory.Exists(plugins));
    });
    Run("launch uses the chosen executable and working directory including spaces",()=>{
      string root=CustomInstallation(),selected=VpilotLocation.Validate(root,p=>"3.12.1.0");
      var info=VpilotLocation.LaunchInfo(selected);Check(info.FileName==Path.Combine(root,"vPilot.exe")&&info.WorkingDirectory==root&&info.Arguments=="");
    });
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
