using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.XPilot;
using FlightCompanion.XPilot.Setup;

class FakeHttp : HttpMessageHandler {
  internal Queue<string> Responses=new Queue<string>();internal List<string> Methods=new List<string>(),Bodies=new List<string>();
  internal bool Fail;internal HttpStatusCode Status=HttpStatusCode.OK;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){
    Tests.Assert(request.RequestUri.Scheme=="https"&&request.RequestUri.Host=="api.telegram.org"&&request.Method==HttpMethod.Post);
    Methods.Add(request.RequestUri.Segments.Last());Bodies.Add(await request.Content.ReadAsStringAsync(cancel));
    if(Fail)throw new HttpRequestException("SECRET-URL-AND-TOKEN");
    return new HttpResponseMessage(Status){Content=new StringContent(Responses.Count>0?Responses.Dequeue():"{}")};
  }
}
class Tests {
  static int passed,failed;const string Token="123456:ABCDEFGHIJKLMNOPQRSTUVWX";
  internal static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
  static void Test(string name,Action body){try{body();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex.Message);failed++;}}
  static Exception Throws(Action body){try{body();}catch(Exception ex){return ex;}throw new Exception("Expected rejection");}
  static TelegramUpdate Update(Pairing pairing){return new TelegramUpdate{Id=1,ChatId=123,UserId=456,ChatType="private",UnixSeconds=100,Text=pairing.Phrase};}
  static JsonElement Json(string text){return JsonSerializer.Deserialize<JsonElement>(text);}
  static int Main(){
    Test("pairing nonce is random 128-bit text",()=>{var a=new Pairing(100,0);var b=new Pairing(100,0);Assert(a.Phrase.Length==37&&a.Phrase!=b.Phrase);});
    Test("pairing accepts one private chat and cannot be reused",()=>{var p=new Pairing(100,0);var u=Update(p);Assert(p.Accept(u,101));u.Id++;Assert(!p.Accept(u,102));});
    Test("pairing rejects groups, bots, forwards, replies and nonpositive IDs",()=>{for(int i=0;i<6;i++){var p=new Pairing(100,0);var u=Update(p);if(i==0)u.ChatType="group";if(i==1)u.IsBot=true;if(i==2)u.Forwarded=true;if(i==3)u.ReplyToId=9;if(i==4)u.ChatId=0;if(i==5)u.UserId=-1;Assert(!p.Accept(u,101));}});
    Test("pairing rejects stale, future and expired attempts",()=>{var p=new Pairing(100,0);var u=Update(p);u.UnixSeconds=99;Assert(!p.Accept(u,101));p=new Pairing(100,0);u=Update(p);u.UnixSeconds=110;Assert(!p.Accept(u,101));p=new Pairing(100,0);Assert(!p.Accept(Update(p),400));});
    Test("pairing enforces exact nonce and update replay watermark",()=>{var p=new Pairing(100,0);var u=Update(p);u.Text="wrong";Assert(!p.Accept(u,101));u.Text=p.Phrase;Assert(!p.Accept(u,102));u.Id++;Assert(p.Accept(u,102));});
    Test("pairing rejects startup backlog and invalid maximum update ID",()=>{var p=new Pairing(100,2);var u=Update(p);Assert(!p.Accept(u,101));u.Id=Int64.MaxValue;Assert(!p.Accept(u,101));});
    Test("configuration writes JSON token and explicit allowlists without overwriting",()=>{string path=Path.Combine(Path.GetTempPath(),"xpilot-config-test-"+Guid.NewGuid().ToString("N")+".json");try{var c=new PluginSettings{Enabled=true,BotToken=Token,AllowedChatId="123",AllowedUserId="456",AllowDisconnect=true};ConfigurationWriter.WriteNew(path,c);var loaded=PluginSettings.Load(path);Assert(loaded.BotToken==Token&&loaded.AllowedUserId=="456"&&loaded.AllowDisconnect);string before=File.ReadAllText(path);Throws(()=>ConfigurationWriter.WriteNew(path,c));Assert(before==File.ReadAllText(path));}finally{if(File.Exists(path))File.Delete(path);}});
    Test("invalid config is rejected before creating a file",()=>{string path=Path.Combine(Path.GetTempPath(),"xpilot-invalid-test-"+Guid.NewGuid().ToString("N")+".json");Throws(()=>ConfigurationWriter.WriteNew(path,new PluginSettings()));Assert(!File.Exists(path));});
    Test("bot verification reads identity and webhook without sending or mutating",()=>{var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":{\"is_bot\":true,\"username\":\"SyntheticBot\"}}");h.Responses.Enqueue("{\"ok\":true,\"result\":{\"url\":\"\"}}");using(var api=new TelegramApi(Token,"123",h)){Assert(api.VerifyBotAsync(default).GetAwaiter().GetResult()=="SyntheticBot");Assert(h.Methods.SequenceEqual(new[]{"getMe","getWebhookInfo"}));}});
    Test("existing webhook is refused without removal",()=>{var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":{\"is_bot\":true,\"username\":\"SyntheticBot\"}}");h.Responses.Enqueue("{\"ok\":true,\"result\":{\"url\":\"https://example.invalid/\"}}");using(var api=new TelegramApi(Token,"123",h)){Assert(Throws(()=>api.VerifyBotAsync(default).GetAwaiter().GetResult()).Message.Contains("webhook"));Assert(h.Methods.Count==2);}});
    Test("malformed bot identities fail closed",()=>{foreach(string body in new[]{"null","{\"is_bot\":false,\"username\":\"SyntheticBot\"}","{\"is_bot\":true,\"username\":\"bad name\"}"}){var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":"+body+"}");using(var api=new TelegramApi(Token,"123",h))Throws(()=>api.VerifyBotAsync(default).GetAwaiter().GetResult());Assert(h.Methods.Count==1);}});
    Test("poll request serializes offset, timeout and message filter",()=>{var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":[]}");using(var api=new TelegramApi(Token,"123",h))Assert(api.GetUpdatesAsync(42,25,default).GetAwaiter().GetResult().Count==0);var body=Json(h.Bodies.Single());Assert(h.Methods.Single()=="getUpdates"&&body.GetProperty("offset").GetInt64()==42&&body.GetProperty("timeout").GetInt32()==25&&body.GetProperty("allowed_updates")[0].GetString()=="message");});
    Test("send uses configured chat and maps returned message ID",()=>{var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":{\"message_id\":88}}");using(var api=new TelegramApi(Token,"123",h))Assert(api.SendTextAsync("Synthetic only",default).GetAwaiter().GetResult()==88);var body=Json(h.Bodies.Single());Assert(body.GetProperty("chat_id").GetString()=="123"&&body.GetProperty("text").GetString()=="Synthetic only");});
    Test("malformed send response never creates a reply binding",()=>{foreach(string result in new[]{"null","{}","{\"message_id\":\"88\"}","{\"message_id\":0}"}){var h=new FakeHttp();h.Responses.Enqueue("{\"ok\":true,\"result\":"+result+"}");using(var api=new TelegramApi(Token,"123",h))Throws(()=>api.SendTextAsync("Test",default).GetAwaiter().GetResult());}});
    Test("HTTP failures redact URLs, token and inner exceptions",()=>{var h=new FakeHttp{Fail=true};using(var api=new TelegramApi(Token,"123",h)){var error=Throws(()=>api.GetUpdatesAsync(0,0,default).GetAwaiter().GetResult());Assert(!error.ToString().Contains("SECRET")&&!error.ToString().Contains(Token)&&error.InnerException==null);}});
    Test("HTTP conflict and Telegram denial are failures without retry",()=>{foreach(bool conflict in new[]{true,false}){var h=new FakeHttp{Status=conflict?HttpStatusCode.Conflict:HttpStatusCode.OK};h.Responses.Enqueue("{\"ok\":false,\"description\":\"SECRET\"}");using(var api=new TelegramApi(Token,"123",h)){Assert(!Throws(()=>api.GetUpdatesAsync(0,0,default).GetAwaiter().GetResult()).ToString().Contains("SECRET"));Assert(h.Methods.Count==1);}}});
    Test("parser maps valid private update and rejects malformed authorization metadata",()=>{string good="{\"update_id\":11,\"message\":{\"chat\":{\"id\":123,\"type\":\"private\"},\"from\":{\"id\":456,\"is_bot\":false},\"date\":100,\"text\":\"/confirm\"}}";var u=TelegramApi.ParseUpdates(Json("["+good+"]")).Single();Assert(u.Text=="/confirm"&&u.ChatId==123&&u.UserId==456);foreach(string bad in new[]{good.Replace("\"is_bot\":false","\"is_bot\":\"false\""),good.Replace("\"date\":100","\"date\":null"),good.Replace("\"text\":\"/confirm\"","\"text\":\"/confirm\",\"reply_to_message\":{}")})Assert(TelegramApi.ParseUpdates(Json("["+bad+"]")).Single().Text==null);});
    Test("parser drops invalid IDs and retains valid unsupported IDs for watermark",()=>{var u=TelegramApi.ParseUpdates(Json("[null,{}, {\"update_id\":\"7\"}, {\"update_id\":8,\"edited_message\":{}}]"));Assert(u.Count==1&&u[0].Id==8&&u[0].Text==null);Throws(()=>TelegramApi.ParseUpdates(Json("{}")));});
    Console.WriteLine("RESULT: "+passed+" passed, "+failed+" failed. HTTP requests were mocked; no bot or flight client contacted.");return failed==0?0:1;
  }
}
