using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FlightCompanion;

namespace FlightCompanionSetup {
  internal sealed class SetupFailure : Exception { internal SetupFailure(string text):base(text){} }
  internal sealed class Pairing {
    internal readonly string Code;
    internal readonly long Started;
    internal string StartPayload {get{return "fc_"+Code.Substring(5);}}
    internal string Link(string username){return "https://t.me/"+username+"?start="+StartPayload;}
    internal long ChatId, UserId, Offset;
    internal Pairing(long started) {
      Started=started;var bytes=new byte[16];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
      Code="pair "+BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
    }
    internal bool Accept(object input,long now) {
      if(ChatId!=0||now<Started||now-Started>300)return false;
      var items=input as object[];if(items==null)throw new SetupFailure("Telegram returned an invalid update list.");
      foreach(object raw in items) {
        var u=raw as Dictionary<string,object>;if(u==null||!u.ContainsKey("update_id"))continue;
        long id;try{id=Convert.ToInt64(u["update_id"],CultureInfo.InvariantCulture);}catch{continue;}
        if(id<Offset||id<0||id==Int64.MaxValue)continue;Offset=id+1;
        try {
          var m=(Dictionary<string,object>)u["message"];
          var c=(Dictionary<string,object>)m["chat"];var f=(Dictionary<string,object>)m["from"];
          long date=Convert.ToInt64(m["date"]),chat=Convert.ToInt64(c["id"]),user=Convert.ToInt64(f["id"]);
          if((string)c["type"]!="private"||chat<=0||user<=0||Convert.ToBoolean(f["is_bot"])||
             m.ContainsKey("forward_origin")||m.ContainsKey("forward_date")||date<Started||date>now+5||
             now-date>60||(!String.Equals(m["text"] as string,Code,StringComparison.Ordinal)&&
             !String.Equals(m["text"] as string,"/start "+StartPayload,StringComparison.Ordinal)))continue;
          ChatId=chat;UserId=user;return true;
        }catch{continue;}
      }
      return false;
    }
    internal PluginSettings Settings(bool replies,bool disconnect) {
      if(ChatId<=0||UserId<=0)throw new SetupFailure("Pair your private chat first.");
      var settings=new PluginSettings {Enabled=true,AllowedChatId=ChatId.ToString(CultureInfo.InvariantCulture),
        AllowedUserId=UserId.ToString(CultureInfo.InvariantCulture),AllowReplies=replies,AllowDisconnect=disconnect};
      settings.Validate();return settings;
    }
  }
  internal sealed class BotClient : IDisposable {
    readonly string token;readonly HttpClient client;
    internal BotClient(string value,HttpMessageHandler handler=null) {
      if(!Regex.IsMatch(value??"",@"^[0-9]+:[A-Za-z0-9_-]{20,}$"))throw new SetupFailure("Enter your bot token from BotFather.");
      token=value;client=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=false,SslProtocols=SslProtocols.Tls12}) {Timeout=TimeSpan.FromSeconds(30)};
    }
    internal async Task<object> Call(string method,object body,CancellationToken cancel) {
      if(method!="getMe"&&method!="getWebhookInfo"&&method!="getUpdates")throw new SetupFailure("Unsupported setup operation.");
      try {
        using(var request=new HttpRequestMessage(HttpMethod.Post,"https://api.telegram.org/bot"+token+"/"+method)) {
          request.Content=new StringContent(new JavaScriptSerializer().Serialize(body),Encoding.UTF8,"application/json");
          using(var response=await client.SendAsync(request,cancel).ConfigureAwait(false)) {
            if(!response.IsSuccessStatusCode) {
              if((int)response.StatusCode==401)throw new SetupFailure("Telegram rejected this token. Check the token in BotFather and try again.");
              if((int)response.StatusCode==409)throw new SetupFailure("Another poller or webhook is using this bot. Stop that integration before pairing.");
              throw new SetupFailure("Telegram request failed. Check your connection and try again.");
            }
            var data=new JavaScriptSerializer().DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as Dictionary<string,object>;
            if(data==null||!data.ContainsKey("ok")||!(data["ok"] is bool)||!(bool)data["ok"]||!data.ContainsKey("result"))
              throw new SetupFailure("Telegram did not accept the setup request.");
            return data["result"];
          }
        }
      }catch(SetupFailure){throw;}catch(OperationCanceledException){throw;}catch {throw new SetupFailure("Could not reach Telegram. Check your connection; token details are hidden.");}
    }
    internal async Task<string> Verify(CancellationToken cancel) {
      var me=await Call("getMe",new {},cancel).ConfigureAwait(false) as Dictionary<string,object>;
      if(me==null||!me.ContainsKey("is_bot")||!Convert.ToBoolean(me["is_bot"])||!me.ContainsKey("username")||
         !Regex.IsMatch(me["username"] as string??"",@"^[A-Za-z0-9_]{5,32}$"))throw new SetupFailure("Telegram did not return a valid bot identity.");
      var webhook=await Call("getWebhookInfo",new {},cancel).ConfigureAwait(false) as Dictionary<string,object>;
      if(webhook==null||!webhook.ContainsKey("url"))throw new SetupFailure("Could not check the bot webhook state.");
      if(!String.IsNullOrEmpty(webhook["url"] as string))throw new SetupFailure("This bot has a webhook. Its owner must remove it or use a dedicated bot; setup made no change.");
      return (string)me["username"];
    }
    internal void SaveToken(PluginSettings settings){settings.BotToken="";settings.ProtectedBotToken=PluginSettings.ProtectToken(token);}
    public void Dispose(){client.Dispose();}
  }
  internal static class VpilotLocation {
    internal static ProcessStartInfo LaunchInfo(string folder) {
      return new ProcessStartInfo(Path.Combine(folder,"vPilot.exe")){UseShellExecute=false,WorkingDirectory=folder};
    }
    internal static string Validate(string location) {return Validate(location,path=>FileVersionInfo.GetVersionInfo(path).FileVersion);}
    internal static string Validate(string location,Func<string,string> readVersion) {
      if(String.IsNullOrWhiteSpace(location))throw new SetupFailure("Choose your vPilot.exe using Browse, or enter its installation folder.");
      string path;
      try {
        path=Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        if(!Regex.IsMatch(path,@"^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$))"))throw new ArgumentException();
        path=Path.GetFullPath(path);
      }catch {throw new SetupFailure("Enter a full vPilot installation path, or choose vPilot.exe using Browse.");}
      if(Directory.Exists(path))path=Path.Combine(path,"vPilot.exe");
      if(!String.Equals(Path.GetFileName(path),"vPilot.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))
        throw new SetupFailure("vPilot.exe was not found at this location. Use Browse to select your installed vPilot.exe.");
      string folder=Path.GetDirectoryName(path);
      if(!File.Exists(Path.Combine(folder,"RossCarlson.Vatsim.Vpilot.Plugins.dll")))
        throw new SetupFailure("The vPilot plugin SDK is missing beside vPilot.exe. Choose the complete vPilot installation.");
      if(readVersion(path)!="3.12.1.0")throw new SetupFailure("This setup supports vPilot 3.12.1.0. Verify compatibility before installing with another version.");
      return folder;
    }
  }
  internal static class Installer {
    internal static string InstallEmbedded(string targetFolder,PluginSettings config) {
      using(var input=typeof(Installer).Assembly.GetManifestResourceStream("FlightCompanion.Plugin.dll")) {
        if(input==null)throw new SetupFailure("The setup download is incomplete. Download FlightCompanion.Setup.exe again.");
        using(var buffer=new MemoryStream()){input.CopyTo(buffer);return InstallBytes(buffer.ToArray(),targetFolder,config);}
      }
    }
    internal static string Install(string sourceDll,string targetFolder,PluginSettings config) {
      if(!File.Exists(sourceDll))throw new SetupFailure("The compiled plugin is missing. Run Build.cmd first.");
      return InstallBytes(File.ReadAllBytes(sourceDll),targetFolder,config);
    }
    static string InstallBytes(byte[] source,string targetFolder,PluginSettings config) {
      config.Validate();
      string targetDll=Path.Combine(targetFolder,"FlightCompanion.Plugin.dll"),targetConfig=Path.Combine(targetFolder,"FlightCompanion.Plugin.json");
      Directory.CreateDirectory(targetFolder);
      string suffix=".backup-"+Guid.NewGuid().ToString("N");
      bool hadDll=File.Exists(targetDll),hadConfig=File.Exists(targetConfig);
      byte[] oldDll=hadDll?File.ReadAllBytes(targetDll):null,oldConfig=hadConfig?File.ReadAllBytes(targetConfig):null;
      // Only these two plugin files are replaced. Existing files get recoverable backups.
      if(hadDll)File.WriteAllBytes(targetDll+suffix,oldDll);
      if(hadConfig)File.WriteAllBytes(targetConfig+suffix,oldConfig);
      try {
        File.WriteAllBytes(targetDll,source);
        File.WriteAllText(targetConfig,new JavaScriptSerializer().Serialize(config),new UTF8Encoding(false));
        if(Hash(source)!=Hash(File.ReadAllBytes(targetDll)))throw new IOException();
        var check=PluginSettings.Load(targetConfig);check.Validate();
        if(check.AllowedChatId!=config.AllowedChatId||check.AllowedUserId!=config.AllowedUserId||!check.Enabled)throw new IOException();
      }catch {
        try {if(hadDll)File.WriteAllBytes(targetDll,oldDll);else if(File.Exists(targetDll))File.Delete(targetDll);
          if(hadConfig)File.WriteAllBytes(targetConfig,oldConfig);else if(File.Exists(targetConfig))File.Delete(targetConfig);}catch{}
        throw new SetupFailure("Installation failed. Existing files were backed up beside the plugin. Check file access and try again.");
      }
      return targetConfig;
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes));}
  }
}
