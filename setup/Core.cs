using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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
             now-date>60||!String.Equals(m["text"] as string,Code,StringComparison.Ordinal))continue;
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
      if(!Regex.IsMatch(value??"",@"^[0-9]+:[A-Za-z0-9_-]{20,}$"))throw new SetupFailure("Enter the new token from BotFather.");
      token=value;client=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(30)};
    }
    internal async Task<object> Call(string method,object body,CancellationToken cancel) {
      if(method!="getMe"&&method!="getWebhookInfo"&&method!="getUpdates")throw new SetupFailure("Unsupported setup operation.");
      try {
        using(var request=new HttpRequestMessage(HttpMethod.Post,"https://api.telegram.org/bot"+token+"/"+method)) {
          request.Content=new StringContent(new JavaScriptSerializer().Serialize(body),Encoding.UTF8,"application/json");
          using(var response=await client.SendAsync(request,cancel).ConfigureAwait(false)) {
            if(!response.IsSuccessStatusCode) {
              if((int)response.StatusCode==401)throw new SetupFailure("Telegram rejected this token. Use the new token after revocation.");
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
    internal ProcessStartInfo LaunchInfo(string executable,string variable) {
      var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)};
      info.EnvironmentVariables[variable]=token;return info;
    }
    public void Dispose(){client.Dispose();}
  }
  internal static class Installer {
    internal static string Install(string sourceDll,string targetFolder,PluginSettings config) {
      config.Validate();
      string targetDll=Path.Combine(targetFolder,"FlightCompanion.Plugin.dll"),targetConfig=Path.Combine(targetFolder,"FlightCompanion.Plugin.json");
      if(!File.Exists(sourceDll))throw new SetupFailure("The compiled plugin is missing. Run Build.cmd first.");
      if(!Directory.Exists(targetFolder))throw new SetupFailure("The vPilot Plugins folder was not found.");
      string suffix=".backup-"+Guid.NewGuid().ToString("N");
      bool hadDll=File.Exists(targetDll),hadConfig=File.Exists(targetConfig);
      byte[] oldDll=hadDll?File.ReadAllBytes(targetDll):null,oldConfig=hadConfig?File.ReadAllBytes(targetConfig):null;
      // Only these two plugin files are replaced. Existing files get recoverable backups.
      if(hadDll)File.WriteAllBytes(targetDll+suffix,oldDll);
      if(hadConfig)File.WriteAllBytes(targetConfig+suffix,oldConfig);
      try {
        File.Copy(sourceDll,targetDll,true);
        File.WriteAllText(targetConfig,new JavaScriptSerializer().Serialize(config),new UTF8Encoding(false));
        if(Hash(sourceDll)!=Hash(targetDll))throw new IOException();
        var check=PluginSettings.Load(targetConfig);check.Validate();
        if(check.AllowedChatId!=config.AllowedChatId||check.AllowedUserId!=config.AllowedUserId||!check.Enabled)throw new IOException();
      }catch {
        try {if(hadDll)File.WriteAllBytes(targetDll,oldDll);else if(File.Exists(targetDll))File.Delete(targetDll);
          if(hadConfig)File.WriteAllBytes(targetConfig,oldConfig);else if(File.Exists(targetConfig))File.Delete(targetConfig);}catch{}
        throw new SetupFailure("Installation failed. Existing files were backed up beside the plugin; no token was saved.");
      }
      return targetConfig;
    }
    static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream));}
  }
}
